using System.Text.Json;

namespace RadzenUiDesigner.Designer;

/// <summary>
/// デザイナーで編集している 1 画面。部品の木・選択・元に戻す／やり直す・生成したコードを持つ。
/// 画面（Razor）からは切り離してあり、編集の処理はすべてここを通す。
/// </summary>
public sealed class DesignDocument
{
    const int MaxUndo = 100;

    // 元に戻す・やり直すは、木全体の JSON を積んでおく
    readonly List<string> _undo = new();
    readonly List<string> _redo = new();
    string? _lastEditKey;   // 同じ欄への続けての入力は 1 回分にまとめる

    public DesignNode Root { get; private set; } = NewRoot();
    public string? SelectedId { get; private set; }
    public DesignNode? Selected => SelectedId is null ? null : Root.Find(SelectedId);
    public GeneratedCode Code { get; private set; } = RazorWriter.Write(NewRoot());

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>ルートを除いた部品の数。</summary>
    public int NodeCount => Root.SelfAndDescendants().Count() - 1;

    /// <summary>
    /// 変わったときに呼ばれる。引数は中身が変わったか（true なら保存し直す。false は選択だけが変わった）。
    /// </summary>
    public event Action<bool>? Changed;

    public static DesignNode NewRoot() => new() { Id = "root", Kind = ComponentCatalog.RootKind };

    // ---- 読み込み・保存
    /// <summary>保存してあった画面を読み込む。元に戻す履歴は空にする。</summary>
    public void Load(DesignNode root)
    {
        Root = root;
        SelectedId = null;
        _undo.Clear();
        _redo.Clear();
        _lastEditKey = null;
        Code = RazorWriter.Write(Root);
        Changed?.Invoke(false);
    }

    /// <summary>画面を丸ごと入れ替える（サンプルの読み込み・全部消す）。元に戻せる。</summary>
    public void Replace(DesignNode root)
    {
        Snapshot();
        Root = root;
        SelectedId = null;
        Commit();
    }

    public string Serialize() => Serialize(Root);

    static string Serialize(DesignNode node) => JsonSerializer.Serialize(node);

    /// <summary>保存した JSON を読む。壊れていたり、ルートでなかったりすれば null。</summary>
    public static DesignNode? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var node = JsonSerializer.Deserialize<DesignNode>(json);
            return node is { Kind: ComponentCatalog.RootKind } ? node : null;
        }
        catch (JsonException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    // ---- 選択
    /// <summary>部品を選ぶ。"root" や見つからない Id なら選択を外す。</summary>
    public void Select(string? id)
    {
        SelectedId = id is not null && id != "root" && Root.Find(id) is not null ? id : null;
        _lastEditKey = null;
        Changed?.Invoke(false);
    }

    public void SelectParent()
    {
        if (Selected is { } sel && Root.FindParent(sel.Id) is { } parent)
            Select(parent.Id);
    }

    /// <summary>ルートより下の親を、外側から順に返す（パンくず用）。</summary>
    public IReadOnlyList<DesignNode> Ancestors(DesignNode node)
    {
        var chain = new List<DesignNode>();
        for (var p = Root.FindParent(node.Id); p is not null && p != Root; p = Root.FindParent(p.Id))
            chain.Insert(0, p);
        return chain;
    }

    // ---- 部品の追加・移動・削除
    /// <summary>
    /// 部品を追加する。選んでいる部品が入れ物ならその中の最後に、そうでなければその後ろに入れる。
    /// </summary>
    public DesignNode? Add(string kind)
    {
        if (!ComponentCatalog.TryGet(kind, out _)) return null;
        DesignNode parent = Root;
        var index = Root.Children.Count;
        if (Selected is { } sel)
        {
            if (ComponentCatalog.IsContainer(sel)) { parent = sel; index = sel.Children.Count; }
            else if (Root.FindParent(sel.Id) is { } p) { parent = p; index = p.Children.IndexOf(sel) + 1; }
        }
        Snapshot();
        var node = CreateNode(kind);
        parent.Children.Insert(index, node);
        SelectedId = node.Id;
        Commit();
        return node;
    }

    /// <summary>
    /// ドラッグ＆ドロップ。source が "palette" なら key の種類の部品を新しく作り、
    /// "node" なら key の Id の部品を動かす。自分自身や自分の子孫の中には動かせない。
    /// </summary>
    public bool Drop(string source, string key, string parentId, int index)
    {
        var parent = Root.Find(parentId);
        if (parent is null || !ComponentCatalog.IsContainer(parent)) return false;

        DesignNode node;
        if (source == "palette")
        {
            if (!ComponentCatalog.TryGet(key, out _)) return false;
            Snapshot();
            node = CreateNode(key);
        }
        else
        {
            var moving = Root.Find(key);
            var oldParent = Root.FindParent(key);
            if (moving is null || oldParent is null || moving.SelfAndDescendants().Contains(parent)) return false;
            Snapshot();
            oldParent.Children.Remove(moving);
            node = moving;
        }
        parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), node);
        SelectedId = node.Id;
        Commit();
        return true;
    }

    /// <summary>選んでいる部品を消し、隣（なければ親）を選ぶ。</summary>
    public bool DeleteSelected()
    {
        if (Selected is not { } sel || Root.FindParent(sel.Id) is not { } parent) return false;
        Snapshot();
        var i = parent.Children.IndexOf(sel);
        parent.Children.RemoveAt(i);
        SelectedId = parent.Children.Count > 0 ? parent.Children[Math.Min(i, parent.Children.Count - 1)].Id
                   : parent == Root ? null : parent.Id;
        Commit();
        return true;
    }

    /// <summary>選んでいる部品を、子孫ごと後ろに複製する。変数名はぶつからないように振り直す。</summary>
    public bool DuplicateSelected()
    {
        if (Selected is not { } sel || Root.FindParent(sel.Id) is not { } parent) return false;
        Snapshot();
        var copy = sel.Clone();
        parent.Children.Insert(parent.Children.IndexOf(sel) + 1, copy);
        foreach (var n in copy.SelfAndDescendants().Where(n => n.Props.ContainsKey(DesignNode.VarKey)))
            n.Props[DesignNode.VarKey] = NewVar(n.Kind);
        SelectedId = copy.Id;
        Commit();
        return true;
    }

    /// <summary>選んでいる部品を、同じ親の中で前（-1）か後ろ（+1）に動かす。</summary>
    public bool MoveSelected(int delta)
    {
        if (Selected is not { } sel || Root.FindParent(sel.Id) is not { } parent) return false;
        var i = parent.Children.IndexOf(sel);
        var j = i + delta;
        if (j < 0 || j >= parent.Children.Count) return false;
        Snapshot();
        (parent.Children[i], parent.Children[j]) = (parent.Children[j], parent.Children[i]);
        Commit();
        return true;
    }

    // ---- パラメーターの変更
    /// <summary>パラメーターを変える。空か既定値なら消す（コードに出さない）。</summary>
    public void SetProp(DesignNode node, PropDef p, string? value)
    {
        Snapshot(node.Id + "/" + p.Name);
        if (string.IsNullOrEmpty(value) || value == p.Default) node.Props.Remove(p.Name);
        else node.Props[p.Name] = value;
        Commit();
    }

    /// <summary>そのまま値を書く（変数名・選択肢・Click・Style の直接入力）。空なら消す。</summary>
    public void SetRaw(DesignNode node, string key, string? value)
    {
        Snapshot(node.Id + "/" + key);
        if (string.IsNullOrEmpty(value)) node.Props.Remove(key);
        else node.Props[key] = value;
        Commit();
    }

    /// <summary>Style の中の CSS プロパティ 1 つを書き換える。</summary>
    public void SetStyle(DesignNode node, string css, string? value)
    {
        Snapshot(node.Id + "/style/" + css);
        var map = StyleMap.Parse(node.Props.GetValueOrDefault("Style"));
        map.Set(css, value);
        SetStyleString(node, map.ToString());
        Commit();
    }

    /// <summary>選択枠のつまみで大きさを変えたとき。Style の width / height に px で書く。</summary>
    public bool Resize(string id, double? width, double? height)
    {
        if (Root.Find(id) is not { } node || (width is null && height is null)) return false;
        Snapshot();
        var map = StyleMap.Parse(node.Props.GetValueOrDefault("Style"));
        if (width is { } w) map.Set("width", $"{Math.Round(w)}px");
        if (height is { } h) map.Set("height", $"{Math.Round(h)}px");
        SetStyleString(node, map.ToString());
        Commit();
        return true;
    }

    static void SetStyleString(DesignNode node, string style)
    {
        if (style.Length == 0) node.Props.Remove("Style");
        else node.Props["Style"] = style;
    }

    // ---- 元に戻す・やり直す
    public bool Undo() => Restore(_undo, _redo);

    public bool Redo() => Restore(_redo, _undo);

    bool Restore(List<string> from, List<string> to)
    {
        if (from.Count == 0) return false;
        to.Add(Serialize(Root));
        Root = Deserialize(from[^1]) ?? NewRoot();
        from.RemoveAt(from.Count - 1);
        _lastEditKey = null;
        if (SelectedId is not null && Root.Find(SelectedId) is null) SelectedId = null;
        Commit();
        return true;
    }

    /// <summary>変える前の状態を履歴に積む。editKey が前回と同じなら積まない（続けての入力をまとめる）。</summary>
    void Snapshot(string? editKey = null)
    {
        if (editKey is not null && editKey == _lastEditKey) return;
        _lastEditKey = editKey;
        _undo.Add(Serialize(Root));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>変えたあとに呼ぶ。コードを作り直して知らせる。</summary>
    void Commit()
    {
        Code = RazorWriter.Write(Root);
        Changed?.Invoke(true);
    }

    // ---- 部品を作る
    DesignNode CreateNode(string kind)
    {
        ComponentCatalog.TryGet(kind, out var def);
        var node = new DesignNode { Kind = kind, Props = new(def!.Initial) };
        if (def.FieldType is not null) node.Props[DesignNode.VarKey] = NewVar(kind);
        return node;
    }

    /// <summary>部品の種類から、まだ使っていない変数名（textBox1 など）を作る。</summary>
    string NewVar(string kind)
    {
        var used = Root.SelfAndDescendants().Select(n => n.Props.GetValueOrDefault(DesignNode.VarKey)).ToHashSet();
        var baseName = RazorWriter.LowerFirst(kind);
        var i = 1;
        while (used.Contains(baseName + i)) i++;
        return baseName + i;
    }
}
