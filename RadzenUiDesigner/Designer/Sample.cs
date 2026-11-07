namespace RadzenUiDesigner.Designer;

/// <summary>初めて開いたときと「サンプル」で読み込む画面（会員登録フォーム）。</summary>
public static class Sample
{
    public static DesignNode Create() => new()
    {
        Id = "root",
        Kind = ComponentCatalog.RootKind,
        Children =
        [
            N("Card", [],
                N("Stack", [("Gap", "1rem")],
                    N("Text", [("Text", "会員登録"), ("TextStyle", "H5")]),
                    N("Row", [("Gap", "1rem")],
                        N("Column", [("Size", "12"), ("SizeMD", "6")],
                            N("FormField", [("Text", "氏名"), ("Style", "width: 100%")],
                                N("TextBox", [(DesignNode.VarKey, "name"), ("Placeholder", "山田 太郎")]))),
                        N("Column", [("Size", "12"), ("SizeMD", "6")],
                            N("FormField", [("Text", "フリガナ"), ("Style", "width: 100%")],
                                N("TextBox", [(DesignNode.VarKey, "kana"), ("Placeholder", "ヤマダ タロウ")])))),
                    N("FormField", [("Text", "都道府県")],
                        N("DropDown", [(DesignNode.VarKey, "prefecture"), (DesignNode.ItemsKey, "東京都\n大阪府\n愛知県\n福岡県")])),
                    N("FormField", [("Text", "生年月日")],
                        N("DatePicker", [(DesignNode.VarKey, "birthday"), ("DateFormat", "yyyy/MM/dd")])),
                    N("Stack", [("Gap", "0.25rem")],
                        N("Label", [("Text", "プラン")]),
                        N("RadioButtonList", [(DesignNode.VarKey, "plan"), (DesignNode.ItemsKey, "無料\nスタンダード\nプレミアム")])),
                    N("Stack", [("Orientation", "Horizontal"), ("AlignItems", "Center"), ("Gap", "0.5rem")],
                        N("CheckBox", [(DesignNode.VarKey, "agree"), ("Name", "agree")]),
                        N("Label", [("Text", "利用規約に同意する"), ("Component", "agree")])),
                    N("Stack", [("Orientation", "Horizontal"), ("JustifyContent", "End"), ("Gap", "0.5rem")],
                        N("Button", [("Text", "キャンセル"), ("Variant", "Text"), ("ButtonStyle", "Base")]),
                        N("Button", [("Text", "登録"), ("Icon", "check"), (DesignNode.ClickKey, "OnRegisterClick")])))),
        ],
    };

    static DesignNode N(string kind, (string Key, string Value)[] props, params DesignNode[] children) => new()
    {
        Kind = kind,
        Props = props.ToDictionary(p => p.Key, p => p.Value),
        Children = children.ToList(),
    };
}
