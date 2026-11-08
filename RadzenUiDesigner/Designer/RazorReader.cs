using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RadzenUiDesigner.Designer;

/// <summary>コードを読めなかった所。Line は 1 から数えた行番号。</summary>
public sealed record ReadError(int Line, string Message);

/// <summary>コードを読んだ結果。読めない所があれば Root は null で、Errors にその所が入る。</summary>
public sealed record ReadResult(DesignNode? Root, IReadOnlyList<ReadError> Errors);

/// <summary>
/// コード欄で書き換えた .razor のコードを読み、DesignNode の木に組み直す（RazorWriter の逆）。
/// 読めるのは、ComponentCatalog にある部品とそのパラメーター、@bind-Value・Click・選択肢だけ。
/// HTML のタグや @if などが入っていれば、その行を Errors に入れて、木は作らない。
/// @code の中は、Data に渡す選択肢の配列だけを読む。フィールドとメソッドは、RazorWriter が部品から作り直す。
/// </summary>
public static partial class RazorReader
{
    const int MaxErrors = 20;

    /// <summary>タグ名から部品の定義を引く（RadzenButton → Button）。</summary>
    static readonly Dictionary<string, ComponentDef> ByTag = ComponentCatalog.All.ToDictionary(d => d.TagName);

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    public static ReadResult Read(string text) => new Parser(text).Run();

    /// <summary>中身に何を書けるか。</summary>
    enum RegionKind
    {
        Components, // 部品（ページ・入れ物の部品の中）
        Items,      // 選択肢の部品（RadioButtonList の中など）
        Empty,      // 何も書けない（ボタンの中など）
    }

    /// <summary>開きタグから閉じタグまでの中身。</summary>
    sealed class Region
    {
        public required DesignNode Node { get; init; }
        public ComponentDef? Def { get; init; }      // Node の部品の定義（ページなら null）
        public string? Close { get; init; }          // この中身を閉じるタグ。null なら文字の終わりまで
        public int OpenAt { get; init; }             // 開きタグの位置（閉じタグがないときに知らせる）
        public RegionKind Kind { get; init; }
        public string? Wrapper { get; init; }        // 中身を 1 回だけ包んでよいタグ（<Tabs>・<Items>・<ChildContent>）
        public string Owner { get; init; } = "";     // 知らせるときに使うタグ名
        public List<string> Items { get; init; } = [];
    }

    readonly record struct Attr(string Name, string? Value, int At);

    /// <summary>読んでいる途中で、続きを読めなくなったとき。</summary>
    sealed class Abort : Exception;

    sealed class Parser(string text)
    {
        readonly string s = text.Replace("\r\n", "\n").Replace('\r', '\n');
        int pos;
        readonly List<ReadError> errors = [];
        readonly List<(DesignNode Node, string Field, int At)> dataRefs = [];
        readonly List<(string Name, int At)> bound = [];
        readonly StringBuilder code = new();

        public ReadResult Run()
        {
            var root = DesignDocument.NewRoot();
            try
            {
                Content(new Region { Node = root, Kind = RegionKind.Components });
                ResolveData();
                CheckVars();
            }
            catch (Abort) { }
            if (errors.Count > 0) return new(null, errors.OrderBy(e => e.Line).ToList());
            NameVars(root);
            return new(root, []);
        }

        // ---- 知らせる
        void Error(int at, string message)
        {
            errors.Add(new(LineAt(at), message));
            if (errors.Count >= MaxErrors) throw new Abort();
        }

        /// <summary>続きを読めない間違い。知らせて読むのをやめる（throw Fatal(...) と書く）。</summary>
        Abort Fatal(int at, string message)
        {
            errors.Add(new(LineAt(at), message));
            return new Abort();
        }

        int LineAt(int at)
        {
            var line = 1;
            for (var i = 0; i < at && i < s.Length; i++)
                if (s[i] == '\n') line++;
            return line;
        }

        // ---- 中身
        void Content(Region r)
        {
            while (true)
            {
                SkipSpaceAndComments();
                if (pos >= s.Length)
                {
                    if (r.Close is not null) throw Fatal(r.OpenAt, $"<{r.Close}> の閉じタグ </{r.Close}> がありません。");
                    return;
                }
                if (At("</"))
                {
                    var at = pos;
                    pos += 2;
                    var name = Name();
                    SkipSpace();
                    if (!At(">")) throw Fatal(at, $"閉じタグ </{name}> の > がありません。");
                    pos++;
                    if (r.Close is null) throw Fatal(at, $"閉じタグ </{name}> に合う開きタグがありません。");
                    if (name != r.Close) throw Fatal(at, $"閉じタグ </{name}> が、開きタグ <{r.Close}> と合っていません。");
                    return;
                }
                if (At("<"))
                {
                    Element(r);
                    continue;
                }
                if (r.Def is null && r.Close is null && AtWord("@code"))
                {
                    CodeBlock();
                    continue;
                }
                if (At("@"))
                    throw Fatal(pos, "@ で始まる書き方（@if・@foreach・@page など）は読めません。デザイナーにある部品だけを書けます。");

                var start = pos;
                while (pos < s.Length && s[pos] != '<' && s[pos] != '@') pos++;
                var words = Shorten(s[start..pos].Trim());
                Error(start, r.Def is null
                    ? $"部品の外に文字は書けません（「{words}」）。文字は RadzenText などの部品の Text に書きます。"
                    : $"<{r.Owner}> の中に文字は書けません（「{words}」）。文字は Text などのパラメーターに書きます。");
            }
        }

        void Element(Region r)
        {
            var at = pos;
            pos++;
            var name = Name();
            if (name.Length == 0) throw Fatal(at, "< の後にタグの名前がありません。");
            var (attrs, selfClosing) = Attributes(name, at);

            if (r.Kind == RegionKind.Empty)
            {
                Error(at, $"<{r.Owner}> の中には何も書けません。");
                if (!selfClosing) Skip(name, at);
                return;
            }

            // 中身を包むタグ（<Tabs>・<Items>・<ChildContent>）
            if (r.Wrapper is not null && name == r.Wrapper)
            {
                foreach (var a in attrs) Error(a.At, $"<{name}> には属性を書けません。");
                if (!selfClosing)
                    Content(new Region
                    {
                        Node = r.Node, Def = r.Def, Close = name, OpenAt = at, Kind = r.Kind, Owner = r.Owner, Items = r.Items,
                    });
                return;
            }

            // 選択肢 1 つ（<RadzenRadioButtonListItem Text="A" Value="@("A")" />）
            if (r.Kind == RegionKind.Items && name == r.Def?.ItemTag)
            {
                Item(r, name, attrs, at);
                if (!selfClosing)
                    Content(new Region { Node = r.Node, Def = r.Def, Close = name, OpenAt = at, Kind = RegionKind.Empty, Owner = name });
                return;
            }

            // 部品（Radzen.Blazor.RadzenButton のように名前空間を付けてもよい）
            if (!ByTag.TryGetValue(name[(name.LastIndexOf('.') + 1)..], out var def))
            {
                Error(at, r.Kind == RegionKind.Items
                    ? $"<{name}> は読めません。<{r.Owner}> の中には <{r.Def!.ItemTag}> だけを書けます。"
                    : char.IsLower(name[0])
                        ? $"<{name}> は読めません。HTML のタグは使えず、デザイナーにある Radzen の部品だけを書けます。"
                        : $"<{name}> は読めません。デザイナーにある Radzen の部品だけを書けます。");
                if (!selfClosing) Skip(name, at);
                return;
            }

            var node = new DesignNode { Kind = def.Key };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in attrs) Attribute(node, def, a, seen);

            if (r.Kind == RegionKind.Items) Error(at, $"<{r.Owner}> の中には <{r.Def!.ItemTag}> だけを書けます。");
            else if (!ComponentCatalog.CanContain(r.Node, def.Key)) Error(at, Placement(r, def));
            else r.Node.Children.Add(node);

            var inner = RegionFor(node, def, name, at);
            if (!selfClosing) Content(inner);
            if (inner.Items.Count > 0) node.Props[DesignNode.ItemsKey] = string.Join("\n", inner.Items);
        }

        static Region RegionFor(DesignNode node, ComponentDef def, string tag, int at)
        {
            var (kind, wrapper) = def switch
            {
                { IsContainer: true } => (RegionKind.Components, def.ChildSlot),
                { ItemsMode: ItemsMode.Items } => (RegionKind.Items, "Items"),
                { ItemsMode: ItemsMode.ChildItems } => (RegionKind.Items, "ChildContent"),
                _ => (RegionKind.Empty, (string?)null),
            };
            return new Region { Node = node, Def = def, Close = tag, OpenAt = at, Kind = kind, Wrapper = wrapper, Owner = def.TagName };
        }

        static string Placement(Region r, ComponentDef child)
        {
            if (child.ParentKinds is { } parents && (r.Def is null || !parents.Contains(r.Def.Key)))
                return $"<{child.TagName}> は {Tags(parents)} の中にだけ置けます。";
            if (r.Def is { ChildKinds: { } kinds })
                return $"<{r.Def.TagName}> の中には {Tags(kinds)} だけを置けます。";
            return r.Def is null ? $"<{child.TagName}> はここに置けません。" : $"<{r.Def.TagName}> の中には部品を入れられません。";
        }

        static string Tags(IEnumerable<string> kinds) =>
            string.Join("・", kinds.Select(k => ComponentCatalog.TryGet(k, out var d) ? $"<{d.TagName}>" : k));

        /// <summary>読めない部品を、閉じタグまで読み飛ばす。</summary>
        void Skip(string name, int at)
        {
            var depth = 1;
            while (true)
            {
                var next = s.IndexOf('<', pos);
                if (next < 0) throw Fatal(at, $"<{name}> の閉じタグ </{name}> がありません。");
                pos = next;
                if (At("<!--"))
                {
                    SkipSpaceAndComments();
                    continue;
                }
                if (At("</"))
                {
                    pos += 2;
                    var closing = Name();
                    SkipSpace();
                    if (At(">")) pos++;
                    if (closing == name && --depth == 0) return;
                    continue;
                }
                var tagAt = pos;
                pos++;
                var opening = Name();
                if (opening.Length == 0) continue;
                var (_, selfClosing) = Attributes(opening, tagAt);
                if (opening == name && !selfClosing) depth++;
            }
        }

        // ---- 属性
        (List<Attr> Attrs, bool SelfClosing) Attributes(string tag, int tagAt)
        {
            var list = new List<Attr>();
            while (true)
            {
                SkipSpace();
                if (pos >= s.Length) throw Fatal(tagAt, $"<{tag}> の > がありません。");
                if (At("/>")) { pos += 2; return (list, true); }
                if (At(">")) { pos++; return (list, false); }
                var at = pos;
                var name = AttrName();
                if (name.Length == 0) throw Fatal(at, $"<{tag}> の中に読めない文字「{s[pos]}」があります。");
                SkipSpace();
                string? value = null;
                if (At("="))
                {
                    pos++;
                    SkipSpace();
                    value = AttrValue(name, at);
                }
                list.Add(new(name, value, at));
            }
        }

        string AttrName()
        {
            var start = pos;
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] is not ('=' or '>' or '/' or '"' or '\'' or '<')) pos++;
            return s[start..pos];
        }

        string AttrValue(string name, int at)
        {
            if (At("\"") || At("'"))
            {
                var q = s[pos++];
                var start = pos;
                // @("...") の中の " で値が終わったと読み違えないように、式は括弧の対応を見て読む
                if (At("@(")) { pos++; Expression(at); }
                var end = s.IndexOf(q, pos);
                if (end < 0) throw Fatal(at, $"{name} の値の {q} が閉じていません。");
                pos = end + 1;
                return s[start..end];
            }
            var st = pos;
            if (At("@(")) { pos++; Expression(at); return s[st..pos]; }
            while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '>' && !At("/>")) pos++;
            return s[st..pos];
        }

        /// <summary>( から対応する ) までを読み飛ばす。中の C# の文字列も読み飛ばす。</summary>
        void Expression(int at)
        {
            var depth = 0;
            while (pos < s.Length)
            {
                var c = s[pos];
                if (c is '"' or '\'') { SkipCsString(at); continue; }
                if (c == '@' && pos + 1 < s.Length && s[pos + 1] == '"') { pos++; SkipVerbatim(at); continue; }
                if (c == '(') depth++;
                else if (c == ')' && --depth == 0) { pos++; return; }
                pos++;
            }
            throw Fatal(at, "( に合う ) がありません。");
        }

        void Attribute(DesignNode node, ComponentDef def, Attr a, HashSet<string> seen)
        {
            var tag = def.TagName;
            if (!seen.Add(a.Name)) { Error(a.At, $"<{tag}> に {a.Name} が 2 回書かれています。"); return; }
            if (a.Value is not { } v) { Error(a.At, $"{a.Name} に値がありません（例: {a.Name}=\"…\"）。"); return; }

            if (a.Name.Equals("@bind-Value", StringComparison.OrdinalIgnoreCase))
            {
                var name = Ident(v);
                if (def.FieldType is null) Error(a.At, $"<{tag}> は値を持たないので、@bind-Value は使えません。");
                else if (!RazorWriter.IsValidName(name)) Error(a.At, $"@bind-Value には変数の名前を書きます（「{v}」は名前に使えません）。");
                else { node.Props[DesignNode.VarKey] = name; bound.Add((name, a.At)); }
                return;
            }
            if (a.Name.StartsWith('@'))
            {
                Error(a.At, $"{a.Name} は読めません。@ で始まる属性は @bind-Value だけを書けます。");
                return;
            }
            if (a.Name.Equals("TValue", StringComparison.OrdinalIgnoreCase) && def.TValue is not null)
            {
                if (!string.Equals(v.Replace(" ", ""), def.TValue, StringComparison.Ordinal))
                    Error(a.At, $"<{tag}> の TValue は {def.TValue} です（ほかの型にはできません）。");
                return;
            }
            if (a.Name.Equals("Data", StringComparison.OrdinalIgnoreCase) && def.ItemsMode == ItemsMode.Data)
            {
                var field = Ident(v);
                if (Identifier().IsMatch(field)) dataRefs.Add((node, field, a.At));
                else Error(a.At, $"Data には @code の配列の名前を書きます（例: Data=\"@items\"）。「{v}」は読めません。");
                return;
            }
            if (a.Name.Equals("Click", StringComparison.OrdinalIgnoreCase))
            {
                var method = Ident(v);
                if (!def.HasClick) Error(a.At, $"<{tag}> の Click は読めません。Click を書けるのは、ボタンとメニューだけです。");
                else if (!RazorWriter.IsValidName(method)) Error(a.At, $"Click にはメソッドの名前だけを書けます（「{v}」は読めません）。");
                else node.Props[DesignNode.ClickKey] = method;
                return;
            }

            var p = def.Props.FirstOrDefault(x => x.Name.Equals(a.Name, StringComparison.OrdinalIgnoreCase));
            if (p is null)
            {
                Error(a.At, def.FieldType is not null && a.Name.Equals("Value", StringComparison.OrdinalIgnoreCase)
                    ? $"<{tag}> の値は @bind-Value の変数で持つので、Value は書けません。"
                    : $"<{tag}> の {a.Name} は読めません。デザイナーで扱えるパラメーターではありません。");
                return;
            }
            // 空か既定値と同じなら持たない（右のパネルで空にしたときや、既定に戻したときと同じ）
            if (Convert(p, v, a.At) is { Length: > 0 } value && value != p.Default) node.Props[p.Name] = value;
        }

        /// <summary>属性の値を、パラメーターの型に合わせて読む。読めなければ知らせて null を返す。</summary>
        string? Convert(PropDef p, string raw, int at)
        {
            if (p.BaseType == typeof(string)) return StringValue(raw, p.Name, at);

            var v = Unwrap(raw);
            if (p.BaseType.IsEnum)
            {
                var typeName = p.BaseType.Name;
                var dot = v.LastIndexOf('.');
                var prefix = dot < 0 ? null : v[..dot];
                var member = v[(dot + 1)..];
                var names = Enum.GetNames(p.BaseType);
                var found = names.FirstOrDefault(n => n.Equals(member, StringComparison.OrdinalIgnoreCase));
                if ((prefix is null || prefix == typeName || prefix.EndsWith("." + typeName, StringComparison.Ordinal)) && found is not null)
                    return found;
                Error(at, $"{p.Name} に「{raw}」は使えません。{typeName}.{names[0]} のように書きます（使える値: {string.Join("・", names.Take(10))}{(names.Length > 10 ? " など" : "")}）。");
                return null;
            }
            if (p.IsBool)
            {
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase)) return "true";
                if (v.Equals("false", StringComparison.OrdinalIgnoreCase)) return "false";
                Error(at, $"{p.Name} は true か false で書きます（「{raw}」は読めません）。");
                return null;
            }
            if (p.IsNumber && p.TryConvert(v, out _)) return v;
            Error(at, $"{p.Name} は数で書きます（「{raw}」は読めません）。");
            return null;
        }

        /// <summary>文字列の値。Text="保存" と Text="@("保存")" の両方を読む。@@ は @ 1 つ。</summary>
        string? StringValue(string raw, string name, int at)
        {
            var t = raw.Trim();
            if (t.StartsWith("@(", StringComparison.Ordinal))
            {
                if (t.EndsWith(')') && TryCsString(t[2..^1].Trim(), out var str)) return str;
            }
            else if (!(t.StartsWith('@') && !t.StartsWith("@@", StringComparison.Ordinal)))
            {
                return raw.Replace("@@", "@");
            }
            Error(at, $"{name} の値の式は読めません。文字は \"…\" か @(\"…\") で書きます。");
            return null;
        }

        void Item(Region r, string tag, List<Attr> attrs, int at)
        {
            string? text = null, value = null;
            foreach (var a in attrs)
            {
                if (a.Value is null) Error(a.At, $"{a.Name} に値がありません（例: {a.Name}=\"…\"）。");
                else if (a.Name.Equals("Text", StringComparison.OrdinalIgnoreCase)) text = StringValue(a.Value, "Text", a.At);
                else if (a.Name.Equals("Value", StringComparison.OrdinalIgnoreCase)) value = StringValue(a.Value, "Value", a.At);
                else Error(a.At, $"<{tag}> には Text と Value だけを書けます（{a.Name} は読めません）。");
            }
            // デザイナーは Text と Value に同じ文字を使う。Text がなければ Value を使う
            var item = (text ?? value)?.Replace('\n', ' ').Trim();
            if (string.IsNullOrEmpty(item)) Error(at, $"<{tag}> に Text がありません。");
            else r.Items.Add(item);
        }

        // ---- @code
        void CodeBlock()
        {
            var at = pos;
            pos += "@code".Length;
            SkipSpace();
            if (!At("{")) throw Fatal(at, "@code の後に { がありません。");
            var start = pos + 1;
            var depth = 0;
            while (pos < s.Length)
            {
                var c = s[pos];
                if (c is '"' or '\'') { SkipCsString(at); continue; }
                if (c == '@' && pos + 1 < s.Length && s[pos + 1] == '"') { pos++; SkipVerbatim(at); continue; }
                if (At("//")) { var nl = s.IndexOf('\n', pos); pos = nl < 0 ? s.Length : nl; continue; }
                if (At("/*"))
                {
                    var end = s.IndexOf("*/", pos + 2, StringComparison.Ordinal);
                    if (end < 0) throw Fatal(pos, "/* に合う */ がありません。");
                    pos = end + 2;
                    continue;
                }
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    code.Append(s, start, pos - start).Append('\n');
                    pos++;
                    return;
                }
                pos++;
            }
            throw Fatal(at, "@code の { に合う } がありません。");
        }

        /// <summary>Data に書いた配列を @code から探し、選択肢にする。</summary>
        void ResolveData()
        {
            var src = code.ToString();
            foreach (var (node, field, at) in dataRefs)
            {
                // field = [...] か field = new ...{ ... } を探す（== や => は除く）
                var assigns = Regex.Matches(src, @"(?<![\w.])" + Regex.Escape(field) + @"\s*=(?![=>])\s*");
                if (assigns.Count == 0)
                {
                    Error(at, $"Data に書いた {field} が @code にありません。@code に string[] {field} = [\"…\", \"…\"]; のように書きます。");
                    continue;
                }
                var items = assigns.Select(m => ReadArray(src, m.Index + m.Length)).FirstOrDefault(x => x is not null);
                if (items is null)
                    Error(at, $"@code の {field} を読めません。[\"…\", \"…\"] のように、文字列だけを並べて書きます。");
                else if (items.Count > 0)
                    node.Props[DesignNode.ItemsKey] = string.Join("\n", items);
            }
        }

        /// <summary>i から [...] か new ...{ ... } を読み、中の文字列を返す。文字列以外があれば null。</summary>
        static List<string>? ReadArray(string src, int i)
        {
            char close;
            if (i < src.Length && src[i] == '[') close = ']';
            else if (src.AsSpan(i).StartsWith("new"))
            {
                i = src.IndexOf('{', i);
                if (i < 0) return null;
                close = '}';
            }
            else return null;

            var items = new List<string>();
            i++;
            while (true)
            {
                while (i < src.Length && (char.IsWhiteSpace(src[i]) || src[i] == ',')) i++;
                if (i >= src.Length) return null;
                if (src[i] == close) return items;
                if (!TryReadCsString(src, ref i, out var item)) return null;
                item = item.Replace('\n', ' ').Trim();
                if (item.Length > 0) items.Add(item);
            }
        }

        /// <summary>@bind-Value の変数を、2 つの部品で使っていないか。</summary>
        void CheckVars()
        {
            foreach (var g in bound.GroupBy(b => b.Name).Where(g => g.Count() > 1))
                foreach (var (name, at) in g.Skip(1))
                    Error(at, $"@bind-Value の変数 {name} を、ほかの部品でも使っています。部品ごとに別の名前にしてください。");
        }

        /// <summary>@bind-Value を書かなかった入力部品に、変数名（textBox1 など）を付ける。</summary>
        static void NameVars(DesignNode root)
        {
            var used = root.SelfAndDescendants().Select(n => n.Props.GetValueOrDefault(DesignNode.VarKey)).OfType<string>().ToHashSet();
            foreach (var n in root.SelfAndDescendants())
            {
                if (!ComponentCatalog.TryGet(n.Kind, out var def) || def.FieldType is null || n.Props.ContainsKey(DesignNode.VarKey)) continue;
                var baseName = RazorWriter.LowerFirst(def.Key);
                var i = 1;
                while (used.Contains(baseName + i)) i++;
                n.Props[DesignNode.VarKey] = baseName + i;
                used.Add(baseName + i);
            }
        }

        // ---- 文字の読み進め
        bool At(string x) => s.AsSpan(pos).StartsWith(x);

        bool AtWord(string x) => At(x) && (pos + x.Length >= s.Length || !(char.IsLetterOrDigit(s[pos + x.Length]) || s[pos + x.Length] == '_'));

        void SkipSpace()
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        /// <summary>空白と、HTML のコメント（&lt;!-- --&gt;）・Razor のコメント（@* *@）を読み飛ばす。</summary>
        void SkipSpaceAndComments()
        {
            while (true)
            {
                SkipSpace();
                var (open, close) = At("<!--") ? ("<!--", "-->") : At("@*") ? ("@*", "*@") : (null, null);
                if (open is null) return;
                var end = s.IndexOf(close!, pos + open.Length, StringComparison.Ordinal);
                if (end < 0) throw Fatal(pos, $"{open} に合う {close} がありません。");
                pos = end + close!.Length;
            }
        }

        string Name()
        {
            var start = pos;
            while (pos < s.Length && (char.IsLetterOrDigit(s[pos]) || s[pos] is '_' or '.' or ':' or '-')) pos++;
            return s[start..pos];
        }

        void SkipCsString(int at)
        {
            var q = s[pos++];
            while (pos < s.Length)
            {
                var c = s[pos];
                if (c == '\\') { pos += 2; continue; }
                if (c == '\n') break;
                pos++;
                if (c == q) return;
            }
            throw Fatal(at, $"{q} で始めた C# の文字列が閉じていません。");
        }

        void SkipVerbatim(int at)
        {
            pos++; // "
            while (pos < s.Length)
            {
                if (s[pos] == '"')
                {
                    if (pos + 1 < s.Length && s[pos + 1] == '"') { pos += 2; continue; }
                    pos++;
                    return;
                }
                pos++;
            }
            throw Fatal(at, "@\" で始めた C# の文字列が閉じていません。");
        }
    }

    /// <summary>値の前後の @ と括弧を外す（@true・@(ButtonStyle.Base)・@OnClick）。</summary>
    static string Unwrap(string raw)
    {
        var v = raw.Trim();
        if (v.StartsWith('@')) v = v[1..].Trim();
        if (v.StartsWith('(') && v.EndsWith(')')) v = v[1..^1].Trim();
        return v;
    }

    static string Ident(string raw) => Unwrap(raw);

    static string Shorten(string text) => text.Length <= 20 ? text : text[..20] + "…";

    /// <summary>text 全体が C# の文字列 1 つ（"…" か @"…"）なら、その中身を返す。</summary>
    static bool TryCsString(string text, out string value)
    {
        var i = 0;
        return TryReadCsString(text, ref i, out value) && i == text.Length;
    }

    /// <summary>i から C# の文字列（"…" か @"…"）を 1 つ読む。読めたら i をその後ろに進める。</summary>
    static bool TryReadCsString(string t, ref int i, out string value)
    {
        value = "";
        var sb = new StringBuilder();
        if (i + 1 < t.Length && t[i] == '@' && t[i + 1] == '"')
        {
            for (var j = i + 2; j < t.Length; j++)
            {
                if (t[j] != '"') { sb.Append(t[j]); continue; }
                if (j + 1 < t.Length && t[j + 1] == '"') { sb.Append('"'); j++; continue; }
                i = j + 1;
                value = sb.ToString();
                return true;
            }
            return false;
        }
        if (i >= t.Length || t[i] != '"') return false;
        for (var j = i + 1; j < t.Length; j++)
        {
            var c = t[j];
            if (c == '"')
            {
                i = j + 1;
                value = sb.ToString();
                return true;
            }
            if (c == '\n') return false;
            if (c != '\\') { sb.Append(c); continue; }
            if (++j >= t.Length) return false;
            switch (t[j])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '0': sb.Append('\0'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case 'u' when j + 4 < t.Length
                              && ushort.TryParse(t.AsSpan(j + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var u):
                    sb.Append((char)u);
                    j += 4;
                    break;
                default: return false;
            }
        }
        return false;
    }
}
