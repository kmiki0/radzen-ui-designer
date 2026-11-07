using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Radzen.Blazor;

namespace RadzenUiDesigner.Designer;

/// <summary>
/// DesignNode の木を、本物の Radzen の部品としてキャンバスに描く。
/// 選択とドラッグ＆ドロップは designer.js が data-dz-* 属性を手がかりに行う。
/// Radzen の部品は知らない属性を一番外側の要素にそのまま付けるので、包む要素を足さずに済む。
/// </summary>
public static class CanvasRenderer
{
    public static RenderFragment Children(DesignNode parent, string? selectedId) => b =>
    {
        foreach (var child in parent.Children)
            Node(b, child, selectedId);
    };

    static void Node(RenderTreeBuilder b, DesignNode node, string? selectedId)
    {
        if (!ComponentCatalog.TryGet(node.Kind, out var def)) return;

        b.OpenComponent(0, def.Type);
        b.SetKey(node.Id);

        foreach (var p in def.Props)
        {
            if (node.Props.TryGetValue(p.Name, out var s) && p.TryConvert(s, out var value))
                b.AddComponentParameter(1, p.Name, value);
            // 指定のないパラメーターにも既定値を渡す。Blazor の部品は、渡されなかったパラメーターの
            // 前の値を持ち続けるので、渡さないと「既定に戻す」や「元に戻す」が画面に出ない
            else if (p.DefaultValue is not null || p.AcceptsNull)
                b.AddComponentParameter(1, p.Name, p.DefaultValue);
        }

        b.AddAttribute(2, "data-dz-id", node.Id);
        b.AddAttribute(3, "data-dz-label", def.Label);
        b.AddAttribute(3, "data-dz-tag", def.TagName);
        if (def.IsContainer) b.AddAttribute(4, "data-dz-container", "1");
        if (node.Id == selectedId) b.AddAttribute(5, "data-dz-selected", "1");

        if (def.HasItems)
        {
            var items = node.Items;
            if (def.Key == "DropDown")
                b.AddComponentParameter(6, "Data", items);
            else
                b.AddComponentParameter(7, "Items", (RenderFragment)(ib =>
                {
                    foreach (var item in items)
                    {
                        ib.OpenComponent<RadzenRadioButtonListItem<string>>(0);
                        ib.AddComponentParameter(1, "Text", item);
                        ib.AddComponentParameter(2, "Value", item);
                        ib.CloseComponent();
                    }
                }));
        }

        if (def.IsContainer)
            b.AddComponentParameter(8, "ChildContent", (RenderFragment)(cb =>
            {
                if (node.Children.Count == 0)
                {
                    cb.OpenElement(0, "div");
                    cb.AddAttribute(1, "class", "dz-empty");
                    cb.AddContent(2, "ここにドロップ");
                    cb.CloseElement();
                    return;
                }
                foreach (var child in node.Children)
                    Node(cb, child, selectedId);
            }));

        b.CloseComponent();
    }
}
