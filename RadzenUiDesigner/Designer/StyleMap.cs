namespace RadzenUiDesigner.Designer;

/// <summary>
/// Style パラメーターの文字列（"width: 100%; margin: 8px"）を、CSS のプロパティごとに読み書きする。
/// スタイル欄の各入力は、この中の 1 つのプロパティだけを書き換える。
/// </summary>
public sealed class StyleMap
{
    readonly List<KeyValuePair<string, string>> _items = new();

    public static StyleMap Parse(string? style)
    {
        var map = new StyleMap();
        foreach (var part in (style ?? "").Split(';'))
        {
            var i = part.IndexOf(':');
            if (i <= 0) continue;
            var name = part[..i].Trim().ToLowerInvariant();
            var value = part[(i + 1)..].Trim();
            if (name.Length > 0 && value.Length > 0) map.Set(name, value);
        }
        return map;
    }

    public string? Get(string name) =>
        _items.FirstOrDefault(kv => kv.Key == name).Value;

    public void Set(string name, string? value)
    {
        var i = _items.FindIndex(kv => kv.Key == name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (i >= 0) _items.RemoveAt(i);
            return;
        }
        var item = new KeyValuePair<string, string>(name, value.Trim());
        if (i >= 0) _items[i] = item;
        else _items.Add(item);
    }

    public override string ToString() => string.Join("; ", _items.Select(kv => $"{kv.Key}: {kv.Value}"));
}
