using RadzenUiDesigner.Designer;

namespace RadzenUiDesigner.Tests;

[TestClass]
public sealed class RazorWriterTests
{
    static DesignNode Root(params DesignNode[] children) =>
        new() { Id = "root", Kind = ComponentCatalog.RootKind, Children = children.ToList() };

    static DesignNode Node(string kind, (string Key, string Value)[]? props = null, params DesignNode[] children) => new()
    {
        Kind = kind,
        Props = (props ?? []).ToDictionary(p => p.Key, p => p.Value),
        Children = children.ToList(),
    };

    static string[] Lines(GeneratedCode code) => code.Text.TrimEnd('\n').Split('\n');

    [TestMethod]
    public void サンプルのコードに_部品と_code_の変数とメソッドが出る()
    {
        var text = RazorWriter.Write(Sample.Create()).Text;
        StringAssert.StartsWith(text, "<RadzenCard>\n");
        StringAssert.Contains(text, """<RadzenText Text="会員登録" TextStyle="TextStyle.H5" />""");
        StringAssert.Contains(text, """<RadzenTextBox @bind-Value="name" Placeholder="山田 太郎" />""");
        StringAssert.Contains(text, """<RadzenDropDown @bind-Value="prefecture" TValue="string" Data="@prefectureItems" />""");
        StringAssert.Contains(text, """<RadzenButton Text="登録" Icon="check" Click="OnRegisterClick" />""");
        StringAssert.Contains(text, "@code {\n");
        StringAssert.Contains(text, "    string? name;\n");
        StringAssert.Contains(text, """    string[] prefectureItems = ["東京都", "大阪府", "愛知県", "福岡県"];""");
        StringAssert.Contains(text, "    DateTime? birthday;\n");
        StringAssert.Contains(text, "    void OnRegisterClick()\n");
        StringAssert.EndsWith(text, "}\n");
    }

    [TestMethod]
    public void 色分けの行とコピー用の文字は同じ中身になる()
    {
        var code = RazorWriter.Write(Sample.Create());
        var joined = string.Join("\n", code.Lines.Select(l => string.Concat(l.Select(t => t.Text)))) + "\n";
        Assert.AreEqual(code.Text, joined);
        Assert.HasCount(code.Lines.Count, code.Owners);
    }

    [TestMethod]
    public void 予約語の変数名やメソッド名は使わない()
    {
        var root = Root(
            Node("TextBox", [(DesignNode.VarKey, "class")]),
            Node("Button", [(DesignNode.ClickKey, "void")]),
            Node("Button", [(DesignNode.ClickKey, "1abc")]));
        var text = RazorWriter.Write(root).Text;
        StringAssert.Contains(text, """@bind-Value="textBox" """.TrimEnd());
        Assert.DoesNotContain("class;", text);
        Assert.DoesNotContain("Click=", text, "使えない名前のメソッドは Click に出さない");
        Assert.DoesNotContain("void void", text);
    }

    [TestMethod]
    public void 選択肢の配列やメソッドの名前が変数とぶつかれば番号を付ける()
    {
        var root = Root(
            Node("TextBox", [(DesignNode.VarKey, "prefItems")]),
            Node("DropDown", [(DesignNode.VarKey, "pref"), (DesignNode.ItemsKey, "a\nb")]),
            Node("Button", [(DesignNode.ClickKey, "pref")]));
        var lines = Lines(RazorWriter.Write(root));

        CollectionAssert.Contains(lines, "    string? prefItems;");
        CollectionAssert.Contains(lines, "    string? pref;");
        CollectionAssert.Contains(lines, """    string[] prefItems2 = ["a", "b"];""");
        CollectionAssert.Contains(lines, "    void pref2()");
        Assert.IsTrue(lines.Any(l => l.Contains("""Data="@prefItems2" """.TrimEnd())), string.Join("\n", lines));
        Assert.IsTrue(lines.Any(l => l.Contains("""Click="pref2" """.TrimEnd())), string.Join("\n", lines));
    }

    [TestMethod]
    public void Items_の選択肢は_Items_の中に部品として並べる()
    {
        var root = Root(Node("CheckBoxList", [(DesignNode.VarKey, "tags"), (DesignNode.ItemsKey, "A\nB")]));
        var lines = Lines(RazorWriter.Write(root));
        CollectionAssert.AreEqual(new[]
        {
            """<RadzenCheckBoxList @bind-Value="tags" TValue="string">""",
            "    <Items>",
            """        <RadzenCheckBoxListItem Text="A" Value="@("A")" />""",
            """        <RadzenCheckBoxListItem Text="B" Value="@("B")" />""",
            "    </Items>",
            "</RadzenCheckBoxList>",
        }, lines[..6]);
        CollectionAssert.Contains(lines, "    IEnumerable<string>? tags;");
    }

    [TestMethod]
    public void ChildItems_の選択肢は_タグの中にそのまま並べる()
    {
        var root = Root(Node("SplitButton", [("Text", "保存"), (DesignNode.ItemsKey, "下書き\n名前を付けて"), (DesignNode.ClickKey, "OnSave")]));
        var lines = Lines(RazorWriter.Write(root));
        CollectionAssert.AreEqual(new[]
        {
            """<RadzenSplitButton Text="保存" Click="OnSave">""",
            """    <RadzenSplitButtonItem Text="下書き" Value="@("下書き")" />""",
            """    <RadzenSplitButtonItem Text="名前を付けて" Value="@("名前を付けて")" />""",
            "</RadzenSplitButton>",
        }, lines[..4]);
        CollectionAssert.Contains(lines, "    void OnSave()");
    }

    [TestMethod]
    public void Data_に渡す選択肢は_DropDown_以外も配列にする()
    {
        var root = Root(Node("ListBox", [(DesignNode.VarKey, "city"), (DesignNode.ItemsKey, "a\nb")]));
        var lines = Lines(RazorWriter.Write(root));
        Assert.AreEqual("""<RadzenListBox @bind-Value="city" TValue="string" Data="@cityItems" />""", lines[0]);
        CollectionAssert.Contains(lines, """    string[] cityItems = ["a", "b"];""");
    }

    [TestMethod]
    public void ChildContent_以外で子を受け取る部品は_そのパラメーターのタグで包む()
    {
        var root = Root(Node("Tabs", null,
            Node("TabsItem", [("Text", "タブ1")], Node("Button", [("Text", "OK")])),
            Node("TabsItem", [("Text", "タブ2")])));
        CollectionAssert.AreEqual(new[]
        {
            "<RadzenTabs>",
            "    <Tabs>",
            """        <RadzenTabsItem Text="タブ1">""",
            """            <RadzenButton Text="OK" />""",
            "        </RadzenTabsItem>",
            """        <RadzenTabsItem Text="タブ2" />""",
            "    </Tabs>",
            "</RadzenTabs>",
        }, Lines(RazorWriter.Write(root)));
    }

    [TestMethod]
    public void メニューの項目はタグの中に入れ子で並べる()
    {
        var root = Root(Node("Menu", null,
            Node("MenuItem", [("Text", "製品")], Node("MenuItem", [("Text", "一覧")]))));
        CollectionAssert.AreEqual(new[]
        {
            "<RadzenMenu>",
            """    <RadzenMenuItem Text="製品">""",
            """        <RadzenMenuItem Text="一覧" />""",
            "    </RadzenMenuItem>",
            "</RadzenMenu>",
        }, Lines(RazorWriter.Write(root)));
    }

    [TestMethod]
    public void キャンバス用の仮の値はコードに出さない()
    {
        var root = Root(Node("Image", [("AlternateText", "画像")]));
        Assert.AreEqual("""<RadzenImage AlternateText="画像" />""", Lines(RazorWriter.Write(root))[0]);
    }

    [TestMethod]
    public void ジェネリックの型は_型引数も色を分ける()
    {
        var code = RazorWriter.Write(Root(Node("CheckBoxList", [(DesignNode.VarKey, "tags")])));
        var field = code.Lines.Single(l => string.Concat(l.Select(t => t.Text)) == "    IEnumerable<string>? tags;");
        CollectionAssert.Contains(field.ToList(), new Token(TokenKind.Type, "IEnumerable"));
        CollectionAssert.Contains(field.ToList(), new Token(TokenKind.Keyword, "string"));
    }

    [TestMethod]
    public void 同じ変数名の入力部品は番号を付けて分ける()
    {
        var root = Root(
            Node("TextBox", [(DesignNode.VarKey, "name")]),
            Node("TextBox", [(DesignNode.VarKey, "name")]));
        var lines = Lines(RazorWriter.Write(root));
        CollectionAssert.Contains(lines, "    string? name;");
        CollectionAssert.Contains(lines, "    string? name2;");
    }

    [TestMethod]
    public void 同じメソッド名のボタンは_1つのメソッドを共有する()
    {
        var root = Root(
            Node("Button", [(DesignNode.ClickKey, "OnSave")]),
            Node("Button", [(DesignNode.ClickKey, "OnSave")]));
        var lines = Lines(RazorWriter.Write(root));
        Assert.AreEqual(1, lines.Count(l => l == "    void OnSave()"));
        Assert.AreEqual(2, lines.Count(l => l.Contains("""Click="OnSave" """.TrimEnd())));
    }

    [TestMethod]
    public void Razor_が読み違える文字を含む文字列は_C_の式にする()
    {
        var root = Root(Node("Text", [("Text", "a \"quote\" @mail <b>")]));
        var text = RazorWriter.Write(root).Text;
        StringAssert.Contains(text, """Text="@("a \"quote\" @mail <b>")" """.TrimEnd());
    }

    [TestMethod]
    public void 部品ごとの行の範囲と_行の持ち主()
    {
        var text = Node("Text", [("Text", "見出し")]);
        var button = Node("Button");
        var stack = Node("Stack", null, text, button);
        var card = Node("Card", null, stack);
        var code = RazorWriter.Write(Root(card));
        var lines = Lines(code);

        // <RadzenCard> から </RadzenCard> まで
        Assert.AreEqual(new LineRange(0, 5), code.Ranges[card.Id]);
        Assert.AreEqual("<RadzenCard>", lines[0]);
        Assert.AreEqual("</RadzenCard>", lines[5]);
        Assert.AreEqual(new LineRange(1, 4), code.Ranges[stack.Id]);
        Assert.AreEqual(new LineRange(2, 2), code.Ranges[text.Id]);
        Assert.AreEqual(new LineRange(3, 3), code.Ranges[button.Id]);

        // 行の持ち主は、その行を書いた一番内側の部品
        CollectionAssert.AreEqual(new[] { card.Id, stack.Id, text.Id, button.Id, stack.Id, card.Id }, code.Owners.ToArray());
    }

    [TestMethod]
    public void code_の行には持ち主がない()
    {
        var box = Node("TextBox", [(DesignNode.VarKey, "name")]);
        var code = RazorWriter.Write(Root(box));
        var lines = Lines(code);
        var codeStart = Array.IndexOf(lines, "@code {");
        Assert.IsGreaterThan(0, codeStart);
        for (var i = codeStart; i < lines.Length; i++)
            Assert.IsNull(code.Owners[i], $"{i} 行目: {lines[i]}");
    }

    [TestMethod]
    public void 既定値と同じパラメーターはコードに出さない()
    {
        ComponentCatalog.TryGet("Button", out var def);
        var defaultStyle = def!.Props.First(p => p.Name == "ButtonStyle").Default!;
        var root = Root(Node("Button", [("ButtonStyle", defaultStyle)]));
        Assert.DoesNotContain("ButtonStyle=", RazorWriter.Write(root).Text);
    }

    [TestMethod]
    public void 空の画面なら空のコード()
    {
        var code = RazorWriter.Write(DesignDocument.NewRoot());
        Assert.AreEqual("\n", code.Text);
        Assert.IsEmpty(code.Ranges);
    }

    [TestMethod]
    public void IsValidName()
    {
        Assert.IsTrue(RazorWriter.IsValidName("userName"));
        Assert.IsTrue(RazorWriter.IsValidName("_x1"));
        Assert.IsFalse(RazorWriter.IsValidName(null));
        Assert.IsFalse(RazorWriter.IsValidName(""));
        Assert.IsFalse(RazorWriter.IsValidName("1x"));
        Assert.IsFalse(RazorWriter.IsValidName("user name"));
        Assert.IsFalse(RazorWriter.IsValidName("class"));
        Assert.IsFalse(RazorWriter.IsValidName("string"));
    }
}
