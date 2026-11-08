using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace RadzenUiDesigner.Designer;

/// <summary>
/// DesignNode の木を、本物の Radzen の部品としてキャンバスに描く。
/// 選択とドラッグ＆ドロップは designer.js が data-dz-* 属性を手がかりに行う。
/// Radzen の部品は知らない属性を一番外側の要素にそのまま付けるので、ふつうは包む要素を足さずに済む。
/// タブの中身のように、子を自分の要素の外に描く部品だけは、子を div で包んでその div を入れ物にする。
/// </summary>
public static class CanvasRenderer
{
    public static RenderFragment Children(DesignNode root, string? selectedId) => b =>
    {
        var path = SelectionPath(root, selectedId);
        foreach (var child in root.Children)
            Node(b, child, path);
    };

    /// <summary>選んでいる部品と、その親たちの Id。選んだ部品を含むタブを開いて見せるのに使う。</summary>
    static HashSet<string> SelectionPath(DesignNode root, string? id)
    {
        var path = new HashSet<string>();
        for (var n = id is null ? null : root.Find(id); n is not null && n != root; n = root.FindParent(n.Id))
            path.Add(n.Id);
        return path;
    }

    static void Node(RenderTreeBuilder b, DesignNode node, HashSet<string> path)
    {
        if (!ComponentCatalog.TryGet(node.Kind, out var def)) return;

        // 選んでいる部品が見えるように、それを含むタブやステップを開き、メニューを広げる
        var overrides = new Dictionary<string, object>();
        if (def.SelectedIndexParam is { } indexParam && node.Children.FindIndex(c => path.Contains(c.Id)) is var i and >= 0)
            overrides[indexParam] = i;
        // メニューは子があるときだけ広げる。アコーディオンの項目は、空でもドロップできるように開く
        if (def.ExpandParam is { } expandParam && path.Contains(node.Id) && (node.Children.Count > 0 || def.WrapChildren))
            overrides[expandParam] = true;

        b.OpenComponent(0, def.Type);
        b.SetKey(node.Id);

        foreach (var p in def.Props)
        {
            if (overrides.TryGetValue(p.Name, out var forced))
                b.AddComponentParameter(1, p.Name, forced);
            else if (node.Props.TryGetValue(p.Name, out var s) && p.TryConvert(s, out var value))
                b.AddComponentParameter(1, p.Name, value);
            // 指定がなければ、キャンバスだけで使う仮の値を渡す（Path のない画像に、仮の画像を出す）
            else if (def.CanvasFallback.TryGetValue(p.Name, out var fallback))
                b.AddComponentParameter(1, p.Name, fallback);
            // 指定のないパラメーターにも既定値を渡す。Blazor の部品は、渡されなかったパラメーターの
            // 前の値を持ち続けるので、渡さないと「既定に戻す」や「元に戻す」が画面に出ない
            else if (p.DefaultValue is not null || p.AcceptsNull)
                b.AddComponentParameter(1, p.Name, p.DefaultValue);
        }

        Marks(b, 2, node, def);
        if (def.IsContainer && !def.WrapChildren)
        {
            b.AddAttribute(6, "data-dz-container", "1");
            if (def.ChildKinds is { } kinds) b.AddAttribute(7, "data-dz-accept", string.Join(",", kinds));
        }

        switch (def.ItemsMode)
        {
            case ItemsMode.Data:
                b.AddComponentParameter(9, "Data", node.Items);
                break;
            case ItemsMode.Items:
                b.AddComponentParameter(10, "Items", ItemList(def, node.Items));
                break;
            case ItemsMode.ChildItems:
                b.AddComponentParameter(10, "ChildContent", ItemList(def, node.Items));
                break;
        }

        if (def.IsContainer)
        {
            // 子の種類が決まった入れ物（メニューなど）が空なら、子を渡さない。空の ChildContent を渡すと、
            // サブメニューがあるものとして矢印が出る。前の値が残らないように null を渡す
            var empty = node.Children.Count == 0 && def.ChildKinds is not null && !def.WrapChildren;
            b.AddComponentParameter(11, def.ChildSlot, empty ? null : (RenderFragment)(cb =>
            {
                if (def.WrapChildren)
                {
                    cb.OpenElement(0, "div");
                    cb.AddAttribute(1, "class", "dz-slot");
                    Marks(cb, 2, node, def);
                    cb.AddAttribute(6, "data-dz-container", "1");
                    cb.OpenRegion(7);
                    Contents(cb, node, def, path);
                    cb.CloseRegion();
                    cb.CloseElement();
                }
                else
                {
                    Contents(cb, node, def, path);
                }
            }));
        }

        b.CloseComponent();
    }

    /// <summary>部品を見分けるための属性（data-dz-id など）。4 つの連番を使う。</summary>
    static void Marks(RenderTreeBuilder b, int seq, DesignNode node, ComponentDef def)
    {
        b.AddAttribute(seq, "data-dz-id", node.Id);
        b.AddAttribute(seq + 1, "data-dz-kind", node.Kind);
        b.AddAttribute(seq + 2, "data-dz-tag", def.TagName);
        // タブなどの決まった子の部品。designer.js は、決まった親の中にだけドロップできるようにする
        if (def.IsItem) b.AddAttribute(seq + 3, "data-dz-item", "1");
    }

    /// <summary>入れ物の中身。空なら「ここにドロップ」を出す（子の種類が決まった入れ物では出さない）。</summary>
    static void Contents(RenderTreeBuilder b, DesignNode node, ComponentDef def, HashSet<string> path)
    {
        if (node.Children.Count == 0)
        {
            if (def.ChildKinds is not null) return;
            b.OpenElement(0, "div");
            b.AddAttribute(1, "class", "dz-empty");
            b.AddContent(2, "ここにドロップ");
            b.CloseElement();
            return;
        }
        foreach (var child in node.Children)
            Node(b, child, path);
    }

    /// <summary>選択肢 1 つずつを、選択肢の部品（RadzenRadioButtonListItem など）にする。Text と Value はどちらも選択肢の文字列。</summary>
    static RenderFragment ItemList(ComponentDef def, IReadOnlyList<string> items) => b =>
    {
        foreach (var item in items)
        {
            b.OpenComponent(0, def.ItemType!);
            b.AddComponentParameter(1, "Text", item);
            b.AddComponentParameter(2, "Value", item);
            b.CloseComponent();
        }
    };
}
