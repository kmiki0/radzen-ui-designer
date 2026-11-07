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
        Assert.HasCount(22, keys);

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
