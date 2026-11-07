using RadzenUiDesigner.Designer;

namespace RadzenUiDesigner.Tests;

[TestClass]
public sealed class DesignDocumentTests
{
    static DesignDocument NewDoc(DesignNode? root = null)
    {
        var doc = new DesignDocument();
        doc.Load(root ?? DesignDocument.NewRoot());
        return doc;
    }

    static DesignNode Node(string kind, params DesignNode[] children) => new() { Kind = kind, Children = children.ToList() };

    [TestMethod]
    public void Add_何も選んでいなければページの最後に入れて選ぶ()
    {
        var doc = NewDoc();
        var a = doc.Add("Button")!;
        var b = doc.Add("Text")!;
        // Text はボタン（入れ物ではない）の後ろに入る
        CollectionAssert.AreEqual(new[] { a.Id, b.Id }, doc.Root.Children.Select(c => c.Id).ToArray());
        Assert.AreEqual(b.Id, doc.SelectedId);
    }

    [TestMethod]
    public void Add_入れ物を選んでいればその中の最後に入れる()
    {
        var doc = NewDoc();
        var stack = doc.Add("Stack")!;
        var button = doc.Add("Button")!;
        Assert.AreSame(stack, doc.Root.FindParent(button.Id));
    }

    [TestMethod]
    public void Add_入力部品には重ならない変数名を付ける()
    {
        var doc = NewDoc();
        var a = doc.Add("TextBox")!;
        doc.Select(null);
        var b = doc.Add("TextBox")!;
        Assert.AreEqual("textBox1", a.Props[DesignNode.VarKey]);
        Assert.AreEqual("textBox2", b.Props[DesignNode.VarKey]);
    }

    [TestMethod]
    public void Add_知らない種類は追加しない()
    {
        var doc = NewDoc();
        Assert.IsNull(doc.Add("NoSuchComponent"));
        Assert.AreEqual(0, doc.NodeCount);
        Assert.IsFalse(doc.CanUndo);
    }

    [TestMethod]
    public void Drop_部品の一覧からなら新しく作って指定の位置に入れる()
    {
        var doc = NewDoc(Sample.Create());
        var card = doc.Root.Children[0];
        var stack = card.Children[0];
        Assert.IsTrue(doc.Drop("palette", "Alert", stack.Id, 1));
        Assert.AreEqual("Alert", stack.Children[1].Kind);
        Assert.AreEqual(stack.Children[1].Id, doc.SelectedId);
    }

    [TestMethod]
    public void Drop_自分の子孫の中には動かせない()
    {
        var inner = Node("Stack");
        var outer = Node("Card", inner);
        var doc = NewDoc(new DesignNode { Id = "root", Kind = ComponentCatalog.RootKind, Children = [outer] });
        Assert.IsFalse(doc.Drop("node", outer.Id, inner.Id, 0));
        Assert.IsFalse(doc.Drop("node", outer.Id, outer.Id, 0));
        Assert.AreSame(outer, doc.Root.Children[0]);
        Assert.IsFalse(doc.CanUndo);
    }

    [TestMethod]
    public void Drop_同じ親の中で順番を入れ替える()
    {
        var (a, b, c) = (Node("Text"), Node("Button"), Node("Badge"));
        var doc = NewDoc(new DesignNode { Id = "root", Kind = ComponentCatalog.RootKind, Children = [a, b, c] });
        // designer.js は、動かす部品を除いた並びでの位置を渡す
        Assert.IsTrue(doc.Drop("node", a.Id, "root", 2));
        CollectionAssert.AreEqual(new[] { b, c, a }, doc.Root.Children);
    }

    [TestMethod]
    public void Drop_入れ物でない部品の中には入れない()
    {
        var button = Node("Button");
        var doc = NewDoc(new DesignNode { Id = "root", Kind = ComponentCatalog.RootKind, Children = [button] });
        Assert.IsFalse(doc.Drop("palette", "Text", button.Id, 0));
        Assert.AreEqual(1, doc.NodeCount);
    }

    [TestMethod]
    public void DeleteSelected_隣を選び_最後の1つなら親を選ぶ()
    {
        var (a, b) = (Node("Text"), Node("Button"));
        var stack = Node("Stack", a, b);
        var doc = NewDoc(new DesignNode { Id = "root", Kind = ComponentCatalog.RootKind, Children = [stack] });
        doc.Select(b.Id);
        Assert.IsTrue(doc.DeleteSelected());
        Assert.AreEqual(a.Id, doc.SelectedId);
        Assert.IsTrue(doc.DeleteSelected());
        Assert.AreEqual(stack.Id, doc.SelectedId);
        Assert.IsTrue(doc.DeleteSelected());
        Assert.IsNull(doc.SelectedId);
        Assert.IsFalse(doc.DeleteSelected());
    }

    [TestMethod]
    public void DuplicateSelected_子孫ごと複製し_Id_と変数名を振り直す()
    {
        var doc = NewDoc(Sample.Create());
        var column = doc.Root.Children[0].Children[0].Children[1].Children[0];   // Card > Stack > Row > Column
        doc.Select(column.Id);
        Assert.IsTrue(doc.DuplicateSelected());

        var copy = doc.Selected!;
        Assert.AreNotEqual(column.Id, copy.Id);
        var ids = doc.Root.SelfAndDescendants().Select(n => n.Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), "Id が重なっている");
        var vars = doc.Root.SelfAndDescendants().Select(n => n.Props.GetValueOrDefault(DesignNode.VarKey)).OfType<string>().ToList();
        Assert.AreEqual(vars.Count, vars.Distinct().Count(), "変数名が重なっている");
    }

    [TestMethod]
    public void MoveSelected_前後に動かし_端では動かさない()
    {
        var (a, b) = (Node("Text"), Node("Button"));
        var doc = NewDoc(new DesignNode { Id = "root", Kind = ComponentCatalog.RootKind, Children = [a, b] });
        doc.Select(a.Id);
        Assert.IsFalse(doc.MoveSelected(-1));
        Assert.IsTrue(doc.MoveSelected(1));
        CollectionAssert.AreEqual(new[] { b, a }, doc.Root.Children);
        Assert.IsFalse(doc.MoveSelected(1));
    }

    [TestMethod]
    public void SetProp_既定値や空にすると消す()
    {
        var doc = NewDoc();
        var button = doc.Add("Button")!;
        ComponentCatalog.TryGet("Button", out var def);
        var style = def!.Props.First(p => p.Name == "ButtonStyle");

        doc.SetProp(button, style, "Success");
        Assert.AreEqual("Success", button.Props["ButtonStyle"]);
        doc.SetProp(button, style, style.Default);
        Assert.IsFalse(button.Props.ContainsKey("ButtonStyle"));
        doc.SetProp(button, style, "Danger");
        doc.SetProp(button, style, null);
        Assert.IsFalse(button.Props.ContainsKey("ButtonStyle"));
    }

    [TestMethod]
    public void SetRaw_同じ欄への続けての入力は_元に戻す1回で戻る()
    {
        var doc = NewDoc();
        var box = doc.Add("TextBox")!;
        foreach (var text in new[] { "u", "us", "use", "user" })
            doc.SetRaw(box, DesignNode.VarKey, text);
        Assert.AreEqual("user", box.Props[DesignNode.VarKey]);

        Assert.IsTrue(doc.Undo());
        var restored = doc.Root.Find(box.Id)!;
        Assert.AreEqual("textBox1", restored.Props[DesignNode.VarKey]);
    }

    [TestMethod]
    public void Undo_Redo_で行き来でき_新しい編集でやり直しは消える()
    {
        var doc = NewDoc();
        doc.Add("Button");
        doc.Add("Text");
        Assert.AreEqual(2, doc.NodeCount);

        Assert.IsTrue(doc.Undo());
        Assert.AreEqual(1, doc.NodeCount);
        Assert.IsTrue(doc.Redo());
        Assert.AreEqual(2, doc.NodeCount);

        doc.Undo();
        doc.Add("Badge");
        Assert.IsFalse(doc.CanRedo);
        Assert.IsFalse(doc.Redo());
    }

    [TestMethod]
    public void Undo_で選んでいた部品がなくなれば選択を外す()
    {
        var doc = NewDoc();
        var button = doc.Add("Button")!;
        Assert.AreEqual(button.Id, doc.SelectedId);
        doc.Undo();
        Assert.IsNull(doc.SelectedId);
        Assert.IsNull(doc.Selected);
    }

    [TestMethod]
    public void Replace_は元に戻せる()
    {
        var doc = NewDoc(Sample.Create());
        var count = doc.NodeCount;
        doc.Replace(DesignDocument.NewRoot());
        Assert.AreEqual(0, doc.NodeCount);
        doc.Undo();
        Assert.AreEqual(count, doc.NodeCount);
    }

    [TestMethod]
    public void Resize_は_Style_の幅と高さだけを_px_で書き換える()
    {
        var doc = NewDoc();
        var card = doc.Add("Card")!;
        doc.SetRaw(card, "Style", "margin: 8px; width: 100%");
        Assert.IsTrue(doc.Resize(card.Id, 320.4, null));
        Assert.AreEqual("margin: 8px; width: 320px", card.Props["Style"]);
        Assert.IsTrue(doc.Resize(card.Id, null, 99.6));
        Assert.AreEqual("margin: 8px; width: 320px; height: 100px", card.Props["Style"]);
        Assert.IsFalse(doc.Resize(card.Id, null, null));
        Assert.IsFalse(doc.Resize("no-such-id", 10, 10));
    }

    [TestMethod]
    public void SetStyle_最後の1つを消すと_Style_ごと消える()
    {
        var doc = NewDoc();
        var card = doc.Add("Card")!;
        doc.SetStyle(card, "padding", "8px");
        Assert.AreEqual("padding: 8px", card.Props["Style"]);
        doc.SetStyle(card, "padding", "");
        Assert.IsFalse(card.Props.ContainsKey("Style"));
    }

    [TestMethod]
    public void Select_ルートや見つからない_Id_なら選択を外す()
    {
        var doc = NewDoc(Sample.Create());
        var card = doc.Root.Children[0];
        doc.Select(card.Id);
        Assert.AreEqual(card.Id, doc.SelectedId);
        doc.Select("root");
        Assert.IsNull(doc.SelectedId);
        doc.Select("no-such-id");
        Assert.IsNull(doc.SelectedId);
    }

    [TestMethod]
    public void Changed_は中身が変わったかを知らせる()
    {
        var doc = NewDoc();
        var events = new List<bool>();
        doc.Changed += events.Add;
        var button = doc.Add("Button")!;   // 中身が変わる
        doc.Select(null);                  // 選択だけ
        doc.Select(button.Id);             // 選択だけ
        doc.Undo();                        // 中身が変わる
        CollectionAssert.AreEqual(new[] { true, false, false, true }, events);
    }

    [TestMethod]
    public void Serialize_と_Deserialize_で元に戻る()
    {
        var doc = NewDoc(Sample.Create());
        var copy = NewDoc(DesignDocument.Deserialize(doc.Serialize())!);
        Assert.AreEqual(doc.Serialize(), copy.Serialize());
        Assert.AreEqual(doc.Code.Text, copy.Code.Text);
    }

    [TestMethod]
    public void Deserialize_壊れた保存やルートでないものは_null()
    {
        Assert.IsNull(DesignDocument.Deserialize(null));
        Assert.IsNull(DesignDocument.Deserialize(""));
        Assert.IsNull(DesignDocument.Deserialize("{not json"));
        Assert.IsNull(DesignDocument.Deserialize("[1,2,3]"));
        Assert.IsNull(DesignDocument.Deserialize("""{"Id":"x","Kind":"Button"}"""));
    }
}
