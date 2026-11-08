using RadzenUiDesigner.Designer;

namespace RadzenUiDesigner.Tests;

[TestClass]
public sealed class CatalogAndStyleTests
{
    [TestMethod]
    public void 部品の一覧_キーが重ならず_どれも作れて_パラメーターを読める()
    {
        var keys = ComponentCatalog.All.Select(d => d.Key).ToList();
        Assert.AreEqual(keys.Count, keys.Distinct().Count());
        Assert.HasCount(62, keys);

        foreach (var def in ComponentCatalog.All)
        {
            Assert.IsNotNull(Activator.CreateInstance(def.Type), def.Key);
            Assert.IsNotEmpty(def.Props, def.Key);
            // 置いたときの初期値は、どれもパラメーターとして読める
            foreach (var (name, value) in def.Initial.Where(kv => !kv.Key.StartsWith('@')))
            {
                var p = def.Props.FirstOrDefault(p => p.Name == name);
                Assert.IsNotNull(p, $"{def.Key}.{name}");
                Assert.IsTrue(p.TryConvert(value, out _), $"{def.Key}.{name} = {value}");
            }
        }
    }

    [TestMethod]
    public void キャンバスに渡す既定値_null_を渡せない型には値がある()
    {
        // CanvasRenderer は、指定のないパラメーターに既定値を渡す（渡さないと、前の値が画面に残る）
        foreach (var def in ComponentCatalog.All)
            foreach (var p in def.Props.Where(p => !p.AcceptsNull))
                Assert.IsNotNull(p.DefaultValue, $"{def.Key}.{p.Name}");
    }

    [TestMethod]
    public void 選択肢の渡し方に合った定義になっている()
    {
        foreach (var def in ComponentCatalog.All)
        {
            switch (def.ItemsMode)
            {
                case ItemsMode.Data:
                    // 選択肢の配列の名前は、@bind-Value の変数名から作る
                    Assert.IsNotNull(def.FieldType, def.Key);
                    Assert.IsTrue(def.Type.GetProperties().Any(p => p.Name == "Data"), def.Key);
                    Assert.IsNull(def.ItemType, def.Key);
                    break;
                case ItemsMode.Items:
                case ItemsMode.ChildItems:
                    Assert.IsNotNull(def.ItemType, def.Key);
                    Assert.IsNotNull(Activator.CreateInstance(def.ItemType), def.Key);
                    Assert.IsTrue(def.Type.GetProperties().Any(p => p.Name == (def.ItemsMode == ItemsMode.Items ? "Items" : "ChildContent")), def.Key);
                    Assert.IsFalse(def.IsContainer, def.Key);
                    break;
                default:
                    Assert.IsNull(def.ItemType, def.Key);
                    break;
            }
        }
    }

    [TestMethod]
    public void 親子の決まりが食い違っていない()
    {
        foreach (var def in ComponentCatalog.All)
        {
            // 子を受け取るパラメーターがある
            if (def.IsContainer)
                Assert.IsTrue(def.Type.GetProperties().Any(p => p.Name == def.ChildSlot), $"{def.Key}.{def.ChildSlot}");
            // 子に決めた種類は、その親を自分の親として認めている
            foreach (var kind in def.ChildKinds ?? [])
            {
                Assert.IsTrue(ComponentCatalog.TryGet(kind, out var child), kind);
                CollectionAssert.Contains(child!.ParentKinds, def.Key, $"{def.Key} → {kind}");
            }
            // 決まった親を持つ部品は、その親の子に決めてある
            foreach (var parent in def.ParentKinds ?? [])
            {
                Assert.IsTrue(ComponentCatalog.TryGet(parent, out var p), parent);
                CollectionAssert.Contains(p!.ChildKinds, def.Key, $"{parent} → {def.Key}");
            }
            // 一緒に作る子は、入れられる種類で、見出しはパラメーターとして読める
            var self = new DesignNode { Kind = def.Key };
            foreach (var (kind, props) in def.InitialChildren)
            {
                Assert.IsTrue(ComponentCatalog.CanContain(self, kind), $"{def.Key} → {kind}");
                ComponentCatalog.TryGet(kind, out var child);
                foreach (var name in props.Keys)
                    Assert.IsTrue(child!.Props.Any(p => p.Name == name), $"{kind}.{name}");
            }
            foreach (var name in new[] { def.SelectedIndexParam, def.ExpandParam }.OfType<string>())
                Assert.IsTrue(def.Props.Any(p => p.Name == name), $"{def.Key}.{name}");
        }
    }

    [TestMethod]
    public void CanContain_普通の入れ物とページには_決まった子の部品を入れない()
    {
        var root = DesignDocument.NewRoot();
        var stack = new DesignNode { Kind = "Stack" };
        var menuItem = new DesignNode { Kind = "MenuItem" };
        Assert.IsTrue(ComponentCatalog.CanContain(root, "Tabs"));
        Assert.IsFalse(ComponentCatalog.CanContain(root, "TabsItem"));
        Assert.IsFalse(ComponentCatalog.CanContain(stack, "TabsItem"));
        Assert.IsTrue(ComponentCatalog.CanContain(menuItem, "MenuItem"));
        Assert.IsFalse(ComponentCatalog.CanContain(menuItem, "Button"));
        Assert.IsFalse(ComponentCatalog.CanContain(new DesignNode { Kind = "Button" }, "Text"));
    }

    [TestMethod]
    public void Data_の項目用のパラメーターは_Data_を持つ部品でだけ隠す()
    {
        ComponentCatalog.TryGet("DropDown", out var dropDown);
        Assert.IsFalse(dropDown!.Props.Any(p => p.Name is "Count" or "TextProperty"));
        // 確認コードの Count は桁数なので出す
        ComponentCatalog.TryGet("SecurityCode", out var code);
        Assert.AreEqual("桁数", code!.Props.Single(p => p.Name == "Count").Label);
    }

    [TestMethod]
    public void パラメーターの名前は部品ごとに変えられる()
    {
        ComponentCatalog.TryGet("Image", out var image);
        ComponentCatalog.TryGet("Link", out var link);
        Assert.AreEqual("画像の場所（URL）", image!.Props.Single(p => p.Name == "Path").Label);
        Assert.AreEqual("リンク先", link!.Props.Single(p => p.Name == "Path").Label);
    }

    [TestMethod]
    public void サンプルの部品はどれも一覧にある()
    {
        foreach (var n in Sample.Create().SelfAndDescendants().Skip(1))
            Assert.IsTrue(ComponentCatalog.TryGet(n.Kind, out _), n.Kind);
    }

    [TestMethod]
    public void ShortName_は_Radzen_を除く()
    {
        ComponentCatalog.TryGet("DropDown", out var def);
        Assert.AreEqual("RadzenDropDown", def!.TagName);
        Assert.AreEqual("DropDown", def.ShortName);
    }

    [TestMethod]
    public void StyleMap_読み書きと順番()
    {
        var map = StyleMap.Parse(" width : 100% ; margin:0 0 8px;; color: red ");
        Assert.AreEqual("100%", map.Get("width"));
        Assert.AreEqual("0 0 8px", map.Get("margin"));
        Assert.AreEqual("width: 100%; margin: 0 0 8px; color: red", map.ToString());

        map.Set("margin", "4px");          // 位置はそのまま
        map.Set("color", "  ");            // 空なら消す
        map.Set("height", "20px");         // 新しいものは最後
        Assert.AreEqual("width: 100%; margin: 4px; height: 20px", map.ToString());
        Assert.IsNull(map.Get("color"));
    }

    [TestMethod]
    public void StyleMap_プロパティ名は小文字にそろえ_値のないものは捨てる()
    {
        var map = StyleMap.Parse("WIDTH: 10px; height:; :bad; color");
        Assert.AreEqual("width: 10px", map.ToString());
        Assert.AreEqual("", StyleMap.Parse(null).ToString());
    }
}
