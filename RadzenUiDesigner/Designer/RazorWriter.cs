using System.Text;
using System.Text.RegularExpressions;

namespace RadzenUiDesigner.Designer;

/// <summary>コードの 1 片の種類。コード欄ではこれで色を分ける。</summary>
public enum TokenKind
{
    Plain,      // 括弧・空白など
    Punct,      // < /> などのタグの記号
    Tag,        // 部品のタグ名（RadzenButton）
    Param,      // 子のタグになるパラメーター（<Items>）
    Attr,       // 属性名（Text=）
    Razor,      // @ と @code、@bind-Value
    Str,        // 文字列
    Keyword,    // C# のキーワード（void、true、int、string など）
    Type,       // 型名（DateTime、ButtonStyle）
    Member,     // 列挙型の値（Primary）
    Var,        // 変数・フィールド
    Method,     // メソッド
    Number,     // 数値
}

public readonly record struct Token(TokenKind Kind, string Text);

/// <summary>コードの行の範囲（0 から数えた行番号。End も含む）。</summary>
public readonly record struct LineRange(int Start, int End)
{
    public bool Contains(int line) => line >= Start && line <= End;
}

/// <summary>
/// 生成したコード。Text はコピー用、Lines は色分けした表示用。
/// Ranges は部品の Id から、その部品の開始タグから終了タグまでの行。
/// Owners は行ごとに、その行を書いた一番内側の部品の Id（@code の行は null）。
/// キャンバスで選んだ部品のコードに色を付けるのと、コードの行から部品を選ぶのに使う。
/// </summary>
public sealed record GeneratedCode(
    string Text,
    IReadOnlyList<IReadOnlyList<Token>> Lines,
    IReadOnlyDictionary<string, LineRange> Ranges,
    IReadOnlyList<string?> Owners)
{
    public static readonly GeneratedCode Empty = new("", [], new Dictionary<string, LineRange>(), []);
}

/// <summary>
/// DesignNode の木から .razor のマークアップと @code ブロックを作る。
/// 入力部品は @bind-Value の変数を、ボタンは Click のメソッドを @code に出すので、貼り付ければそのまま動く。
/// 各部分が型・変数・メソッドのどれかを、作るときに記録しておく（コード欄の色分けに使う）。
/// </summary>
public static partial class RazorWriter
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();

    static readonly HashSet<string> TypeKeywords = ["string", "int", "bool", "decimal", "double", "float", "long", "object"];

    /// <summary>C# の予約語。変数名やメソッド名にすると、貼り付けたコードがビルドできない。</summary>
    static readonly HashSet<string> CSharpKeywords =
    [
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    ];

    /// <summary>C# の名前として使えるか（英字か _ で始まり、予約語ではない）。</summary>
    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && Identifier().IsMatch(name) && !CSharpKeywords.Contains(name);

    sealed class Builder
    {
        readonly List<List<Token>> _lines = [[]];
        public Builder Add(TokenKind kind, string text) { if (text.Length > 0) _lines[^1].Add(new(kind, text)); return this; }
        public Builder Plain(string text) => Add(TokenKind.Plain, text);
        public Builder NewLine() { _lines.Add([]); return this; }

        /// <summary>いま書いている行の番号（0 から）。</summary>
        public int Line => _lines.Count - 1;

        public GeneratedCode Build(Context ctx)
        {
            if (_lines.Count > 1 && _lines[^1].Count == 0) _lines.RemoveAt(_lines.Count - 1);
            var sb = new StringBuilder();
            foreach (var line in _lines)
            {
                foreach (var t in line) sb.Append(t.Text);
                sb.Append('\n');
            }

            // 外側の部品から順に行の持ち主を書き、内側の部品で上書きする
            var owners = new string?[_lines.Count];
            foreach (var (id, depth, range) in ctx.Ranges.OrderBy(r => r.Depth))
                for (var i = range.Start; i <= range.End && i < owners.Length; i++)
                    owners[i] = id;

            return new(sb.ToString(), _lines, ctx.Ranges.ToDictionary(r => r.Id, r => r.Range), owners);
        }
    }

    sealed class Context
    {
        public readonly Dictionary<string, string> Vars = new();        // ノードの Id → @bind-Value の変数名
        public readonly Dictionary<string, string> ItemsFields = new(); // ノードの Id → 選択肢の配列の名前（DropDown）
        public readonly Dictionary<string, string> MethodOf = new();    // ノードの Id → Click のメソッド名
        public readonly List<(string Type, string Name, IReadOnlyList<string>? Items)> Fields = new();
        public readonly List<string> Methods = new();
        public readonly List<(string Id, int Depth, LineRange Range)> Ranges = new();
    }

    public static GeneratedCode Write(DesignNode root)
    {
        var ctx = new Context();
        // フィールドとメソッドは同じクラスに並ぶので、名前は 1 つの集合で重ならないようにする
        var used = new HashSet<string>();

        foreach (var n in root.SelfAndDescendants())
        {
            if (!ComponentCatalog.TryGet(n.Kind, out var def) || def.FieldType is null) continue;
            var name = Unique(Sanitize(n.Props.GetValueOrDefault(DesignNode.VarKey), LowerFirst(def.Key)), used);
            ctx.Vars[n.Id] = name;
            ctx.Fields.Add((def.FieldType, name, null));
            if (def.Key == "DropDown")
            {
                var items = Unique(name + "Items", used);
                ctx.ItemsFields[n.Id] = items;
                ctx.Fields.Add(("string[]", items, n.Items));
            }
        }

        // メソッドはフィールドの後で名前を決める。同じ名前を指定したボタンは、1 つのメソッドを共有する
        var methodNames = new Dictionary<string, string>();
        foreach (var n in root.SelfAndDescendants())
        {
            if (!ComponentCatalog.TryGet(n.Kind, out var def) || !def.HasClick || Method(n) is not { } raw) continue;
            if (!methodNames.TryGetValue(raw, out var name))
            {
                name = Unique(raw, used);
                methodNames[raw] = name;
                ctx.Methods.Add(name);
            }
            ctx.MethodOf[n.Id] = name;
        }

        var b = new Builder();
        foreach (var child in root.Children)
            WriteNode(b, child, 0, ctx);

        if (ctx.Fields.Count > 0 || ctx.Methods.Count > 0)
        {
            b.NewLine();
            b.Add(TokenKind.Razor, "@code").Plain(" {").NewLine();
            foreach (var (type, name, items) in ctx.Fields)
            {
                b.Plain("    ");
                TypeName(b, type);
                b.Plain(" ").Add(TokenKind.Var, name);
                if (items is not null)
                {
                    b.Plain(" = [");
                    for (var i = 0; i < items.Count; i++)
                    {
                        if (i > 0) b.Plain(", ");
                        b.Add(TokenKind.Str, CsString(items[i]));
                    }
                    b.Plain("]");
                }
                b.Plain(";").NewLine();
            }
            for (var i = 0; i < ctx.Methods.Count; i++)
            {
                if (i > 0 || ctx.Fields.Count > 0) b.NewLine();
                b.Plain("    ").Add(TokenKind.Keyword, "void").Plain(" ").Add(TokenKind.Method, ctx.Methods[i]).Plain("()").NewLine();
                b.Plain("    {").NewLine();
                b.Plain("    }").NewLine();
            }
            b.Plain("}").NewLine();
        }
        return b.Build(ctx);
    }

    static void WriteNode(Builder b, DesignNode n, int depth, Context ctx)
    {
        if (!ComponentCatalog.TryGet(n.Kind, out var def)) return;
        var pad = new string(' ', depth * 4);
        var start = b.Line;

        b.Plain(pad).Add(TokenKind.Punct, "<").Add(TokenKind.Tag, def.TagName);

        var bound = ctx.Vars.GetValueOrDefault(n.Id);
        if (bound is { } bv)
            Attr(b, TokenKind.Razor, "@bind-Value", () => b.Add(TokenKind.Var, bv));
        if (def.TValue is { } tvalue)
            Attr(b, TokenKind.Attr, "TValue", () => TypeName(b, tvalue));
        if (ctx.ItemsFields.GetValueOrDefault(n.Id) is { } itemsField)
            Attr(b, TokenKind.Attr, "Data", () => b.Add(TokenKind.Razor, "@").Add(TokenKind.Var, itemsField));

        foreach (var p in def.Props)
            if (n.Props.TryGetValue(p.Name, out var s) && s != p.Default && p.TryConvert(s, out _))
                Attr(b, TokenKind.Attr, p.Name, () => Value(b, p, s));

        if (ctx.MethodOf.GetValueOrDefault(n.Id) is { } m)
            Attr(b, TokenKind.Attr, "Click", () => b.Add(TokenKind.Method, m));

        IReadOnlyList<string> items = def.Key == "RadioButtonList" ? n.Items : [];

        if (def.IsContainer && n.Children.Count > 0)
        {
            b.Add(TokenKind.Punct, ">").NewLine();
            foreach (var c in n.Children) WriteNode(b, c, depth + 1, ctx);
            Close(b, pad, TokenKind.Tag, def.TagName);
        }
        else if (items.Count > 0)
        {
            b.Add(TokenKind.Punct, ">").NewLine();
            b.Plain(pad + "    ").Add(TokenKind.Punct, "<").Add(TokenKind.Param, "Items").Add(TokenKind.Punct, ">").NewLine();
            foreach (var item in items)
            {
                b.Plain(pad + "        ").Add(TokenKind.Punct, "<").Add(TokenKind.Tag, "RadzenRadioButtonListItem");
                Attr(b, TokenKind.Attr, "Text", () => StringValue(b, item));
                Attr(b, TokenKind.Attr, "Value", () => b.Add(TokenKind.Razor, "@").Plain("(").Add(TokenKind.Str, CsString(item)).Plain(")"));
                b.Add(TokenKind.Punct, " />").NewLine();
            }
            Close(b, pad + "    ", TokenKind.Param, "Items");
            Close(b, pad, TokenKind.Tag, def.TagName);
        }
        else
        {
            b.Add(TokenKind.Punct, " />").NewLine();
        }

        // 最後に NewLine したので、この部品の最後の行は 1 つ前
        ctx.Ranges.Add((n.Id, depth, new LineRange(start, b.Line - 1)));
    }

    static void Close(Builder b, string pad, TokenKind kind, string name) =>
        b.Plain(pad).Add(TokenKind.Punct, "</").Add(kind, name).Add(TokenKind.Punct, ">").NewLine();

    /// <summary>属性 1 つ（ Name="値"）。値の中身は write が書く。</summary>
    static void Attr(Builder b, TokenKind nameKind, string name, Action write)
    {
        b.Plain(" ").Add(nameKind, name).Plain("=\"");
        write();
        b.Plain("\"");
    }

    static void Value(Builder b, PropDef p, string s)
    {
        if (p.BaseType.IsEnum) b.Add(TokenKind.Type, p.BaseType.Name).Plain(".").Add(TokenKind.Member, s);
        else if (p.IsBool) b.Add(TokenKind.Keyword, s);
        else if (p.IsNumber) b.Add(TokenKind.Number, s);
        else StringValue(b, s);
    }

    /// <summary>文字列の値。そのまま書くと Razor が読み違える文字を含むときは、C# の式にして渡す。</summary>
    static void StringValue(Builder b, string s)
    {
        if (s.IndexOfAny(['"', '@', '<', '>', '&', '\n']) >= 0 || s != s.Trim())
            b.Add(TokenKind.Razor, "@").Plain("(").Add(TokenKind.Str, CsString(s)).Plain(")");
        else
            b.Add(TokenKind.Str, s);
    }

    /// <summary>型名（"string?"、"DateTime?"、"string[]"）。C# のキーワードの型とそれ以外で色を分ける。</summary>
    static void TypeName(Builder b, string type)
    {
        var core = type.TrimEnd('?', '[', ']');
        b.Add(TypeKeywords.Contains(core) ? TokenKind.Keyword : TokenKind.Type, core).Plain(type[core.Length..]);
    }

    static string? Method(DesignNode n)
    {
        var raw = n.Props.GetValueOrDefault(DesignNode.ClickKey)?.Trim();
        return IsValidName(raw) ? raw : null;
    }

    static string CsString(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n") + "\"";

    static string Sanitize(string? name, string fallback)
    {
        name = name?.Trim();
        return IsValidName(name) ? name! : fallback;
    }

    static string Unique(string name, HashSet<string> used)
    {
        var result = name;
        for (var i = 2; !used.Add(result); i++) result = name + i;
        return result;
    }

    public static string LowerFirst(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
