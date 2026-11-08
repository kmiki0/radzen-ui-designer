using RadzenUiDesigner.Designer;

namespace RadzenUiDesigner.Tests;

[TestClass]
public sealed class RazorReaderTests
{
    static DesignNode ReadOk(string text)
    {
        var result = RazorReader.Read(text);
        Assert.IsNotNull(result.Root, string.Join("\n", result.Errors.Select(e => $"{e.Line} 行目: {e.Message}")));
        return result.Root;
    }

    static IReadOnlyList<ReadError> ReadNg(string text)
    {
        var result = RazorReader.Read(text);
        Assert.IsNull(result.Root, "読めないはずのコードを読めてしまった");
        Assert.IsNotEmpty(result.Errors);
        return result.Errors;
    }

    [TestMethod]
    public void サンプルのコードを読んで書き直すと_同じコードになる()
    {
        var text = RazorWriter.Write(Sample.Create()).Text;
        Assert.AreEqual(text, RazorWriter.Write(ReadOk(text)).Text);
    }

    [TestMethod]
    public void すべての部品を置いたコードを読んで書き直すと_同じコードになる()
    {
        var doc = new DesignDocument();
        foreach (var def in ComponentCatalog.All.Where(d => !d.IsItem))
        {
            doc.Select(null);
            doc.Add(def.Key);
        }
        // タブの中の部品・サブメニュー・Razor が読み違える文字を含む文字列も入れる
        doc.Select(doc.Root.SelfAndDescendants().First(n => n.Kind == "TabsItem").Id);
        doc.Add("Button");
        doc.Select(doc.Root.SelfAndDescendants().First(n => n.Kind == "MenuItem").Id);
        doc.AddItem(inside: true);
        doc.Root.SelfAndDescendants().First(n => n.Kind == "Text").Props["Text"] = " \"引用\" と @mail <b> & \\ ";

        var text = RazorWriter.Write(doc.Root).Text;
        Assert.AreEqual(text, RazorWriter.Write(ReadOk(text)).Text);
    }

    [TestMethod]
    public void 属性を書き換えると_部品のパラメーターが変わる()
    {
        var root = ReadOk("""<RadzenButton Text="送信" ButtonStyle="ButtonStyle.Success" Disabled="true" Click="OnSend" />""");
        var button = root.Children.Single();
        Assert.AreEqual("Button", button.Kind);
        Assert.AreEqual("送信", button.Props["Text"]);
        Assert.AreEqual("Success", button.Props["ButtonStyle"]);
        Assert.AreEqual("true", button.Props["Disabled"]);
        Assert.AreEqual("OnSend", button.Props[DesignNode.ClickKey]);
    }

    [TestMethod]
    public void 入れ子と_子を受け取るタグと_選択肢を読む()
    {
        var root = ReadOk("""
            <RadzenTabs>
                <Tabs>
                    <RadzenTabsItem Text="基本">
                        <RadzenTextBox @bind-Value="name" />
                    </RadzenTabsItem>
                </Tabs>
            </RadzenTabs>
            <RadzenRadioButtonList @bind-Value="plan" TValue="string">
                <Items>
                    <RadzenRadioButtonListItem Text="無料" Value="@("無料")" />
                    <RadzenRadioButtonListItem Text="有料" Value="@("有料")" />
                </Items>
            </RadzenRadioButtonList>
            <RadzenDropDown @bind-Value="pref" TValue="string" Data="@prefItems" />

            @code {
                string? name;
                // 選択肢
                string[] prefItems = ["東京都", "大阪府"];
            }
            """);
        var tabs = root.Children[0];
        Assert.AreEqual("Tabs", tabs.Kind);
        var tab = tabs.Children.Single();
        Assert.AreEqual("基本", tab.Props["Text"]);
        Assert.AreEqual("name", tab.Children.Single().Props[DesignNode.VarKey]);
        Assert.AreEqual("無料\n有料", root.Children[1].Props[DesignNode.ItemsKey]);
        Assert.AreEqual("東京都\n大阪府", root.Children[2].Props[DesignNode.ItemsKey]);
    }

    [TestMethod]
    public void 書き方が少し違っても読む()
    {
        // 列挙型の型名を省く・@ を付ける・@("…") の中のエスケープ・@@・単引用符・名前空間付きのタグ・コメント・開きタグと閉じタグ
        var root = ReadOk("""
            <!-- HTML のコメント -->
            @* Razor のコメント *@
            <RadzenButton Text="@("a \"b\"\nc")" ButtonStyle="Danger" Disabled="@true" />
            <Radzen.Blazor.RadzenLink Text="mail@@example.com" Path="a@b.jp" />
            <RadzenText Text='単引用符' TextStyle="@TextStyle.H3"></RadzenText>
            <RadzenStack><ChildContent><RadzenIcon Icon="home" /></ChildContent></RadzenStack>
            """);
        Assert.HasCount(4, root.Children);
        Assert.AreEqual("a \"b\"\nc", root.Children[0].Props["Text"]);
        Assert.AreEqual("Danger", root.Children[0].Props["ButtonStyle"]);
        Assert.AreEqual("true", root.Children[0].Props["Disabled"]);
        Assert.AreEqual("mail@example.com", root.Children[1].Props["Text"]);
        Assert.AreEqual("a@b.jp", root.Children[1].Props["Path"]);
        Assert.AreEqual("単引用符", root.Children[2].Props["Text"]);
        Assert.AreEqual("H3", root.Children[2].Props["TextStyle"]);
        Assert.AreEqual("Icon", root.Children[3].Children.Single().Kind);
    }

    [TestMethod]
    public void 既定値と同じ値や空の値は持たない()
    {
        var button = ReadOk("""<RadzenButton Text="" Disabled="false" Size="ButtonSize.Medium" />""").Children.Single();
        Assert.IsEmpty(button.Props);
    }

    [TestMethod]
    public void bind_Valueを書かない入力部品には_使っていない変数名を付ける()
    {
        var root = ReadOk("""
            <RadzenTextBox />
            <RadzenTextBox @bind-Value="textBox1" />
            """);
        Assert.AreEqual("textBox2", root.Children[0].Props[DesignNode.VarKey]);
        Assert.AreEqual("textBox1", root.Children[1].Props[DesignNode.VarKey]);
    }

    [TestMethod]
    public void 読めない所は_行番号と一緒に知らせて_木は作らない()
    {
        var errors = ReadNg("""
            <RadzenStack>
                <div>あ</div>
                <RadzenButton Foo="1" ButtonStyle="ButtonStyle.Nope" />
            </RadzenStack>
            """);
        CollectionAssert.AreEqual(new[] { 2, 3, 3 }, errors.Select(e => e.Line).ToArray());
        StringAssert.Contains(errors[0].Message, "<div>");
        StringAssert.Contains(errors[1].Message, "Foo");
        StringAssert.Contains(errors[2].Message, "ButtonStyle.Nope");
    }

    [TestMethod]
    public void 置けない場所の部品を知らせる()
    {
        var errors = ReadNg("""
            <RadzenTabsItem Text="a" />
            <RadzenTabs><Tabs><RadzenButton Text="b" /></Tabs></RadzenTabs>
            <RadzenButton Text="c"><RadzenIcon /></RadzenButton>
            <RadzenRadioButtonList><Items><RadzenButton /></Items></RadzenRadioButtonList>
            """);
        Assert.HasCount(4, errors);
        StringAssert.Contains(errors[0].Message, "<RadzenTabs> の中にだけ");
        StringAssert.Contains(errors[1].Message, "<RadzenTabsItem> だけ");
        StringAssert.Contains(errors[2].Message, "何も書けません");
        StringAssert.Contains(errors[3].Message, "<RadzenRadioButtonListItem> だけ");
    }

    [TestMethod]
    public void タグの対応が崩れていれば知らせる()
    {
        var unclosed = ReadNg("<RadzenStack>\n    <RadzenButton />\n");
        Assert.AreEqual(1, unclosed[0].Line);
        StringAssert.Contains(unclosed[0].Message, "</RadzenStack>");

        var mismatched = ReadNg("<RadzenStack>\n</RadzenCard>");
        Assert.AreEqual(2, mismatched[0].Line);

        var quote = ReadNg("<RadzenButton Text=\"a />");
        StringAssert.Contains(quote[0].Message, "閉じていません");
    }

    [TestMethod]
    public void at_ifなどのRazorの書き方と_部品の外の文字は読めない()
    {
        var razor = ReadNg("<RadzenStack>\n@if (show)\n{\n<RadzenButton />\n}\n</RadzenStack>");
        Assert.AreEqual(2, razor[0].Line);
        StringAssert.Contains(razor[0].Message, "@if");

        var text = ReadNg("<RadzenStack>\n    保存する\n</RadzenStack>");
        Assert.AreEqual(2, text[0].Line);
        StringAssert.Contains(text[0].Message, "保存する");
    }

    [TestMethod]
    public void Dataの配列が_codeになければ知らせる()
    {
        var missing = ReadNg("""<RadzenDropDown @bind-Value="a" TValue="string" Data="@aItems" />""");
        StringAssert.Contains(missing[0].Message, "aItems");

        var notStrings = ReadNg("<RadzenDropDown @bind-Value=\"a\" Data=\"@aItems\" />\n@code {\n    string[] aItems = [GetName()];\n}");
        StringAssert.Contains(notStrings[0].Message, "読めません");
    }

    [TestMethod]
    public void 型や値を持たない部品の_bind_Valueと_TValueの違いを知らせる()
    {
        var errors = ReadNg("""
            <RadzenButton @bind-Value="x" />
            <RadzenNumeric TValue="decimal" />
            <RadzenTextBox Value="a" />
            """);
        Assert.HasCount(3, errors);
        StringAssert.Contains(errors[0].Message, "@bind-Value");
        StringAssert.Contains(errors[1].Message, "int");
        StringAssert.Contains(errors[2].Message, "@bind-Value の変数");
    }

    [TestMethod]
    public void 同じ変数を2つの部品で使えば知らせる()
    {
        var errors = ReadNg("""
            <RadzenTextBox @bind-Value="name" />
            <RadzenTextBox @bind-Value="name" />
            """);
        Assert.AreEqual(2, errors.Single().Line);
    }

    [TestMethod]
    public void 読んだ木で画面を入れ替えると_元に戻せる()
    {
        var doc = new DesignDocument();
        doc.Load(Sample.Create());
        var before = doc.Code.Text;
        doc.Replace(ReadOk("""<RadzenButton Text="だけ" />"""));
        Assert.AreEqual(1, doc.NodeCount);
        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(before, doc.Code.Text);
    }
}
