using System.Text.Json.Serialization;

namespace RadzenUiDesigner.Designer;

/// <summary>
/// デザイナーのキャンバスに置いた部品 1 つ。JSON にしてブラウザに保存する。
/// </summary>
public sealed class DesignNode
{
    // Props のうち、Radzen のパラメーターではなくデザイナーが使うもの
    public const string VarKey = "@var";       // @bind-Value の変数名
    public const string ItemsKey = "@items";   // 選択肢（1 行に 1 つ）
    public const string ClickKey = "@click";   // Click で呼ぶメソッド名

    public string Id { get; set; } = NewId();

    /// <summary>ComponentCatalog のキー（"Button" など）。ルートは "Root"。</summary>
    public string Kind { get; set; } = "";

    /// <summary>パラメーターの値。型にかかわらず文字列で持ち、表示とコード生成のときに変換する。</summary>
    public Dictionary<string, string> Props { get; set; } = new();

    public List<DesignNode> Children { get; set; } = new();

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>子孫も含めて複製する。Id は新しく振る。</summary>
    public DesignNode Clone() => new()
    {
        Kind = Kind,
        Props = new(Props),
        Children = Children.Select(c => c.Clone()).ToList(),
    };

    public IEnumerable<DesignNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }

    public DesignNode? Find(string id) => SelfAndDescendants().FirstOrDefault(n => n.Id == id);

    public DesignNode? FindParent(string id) =>
        SelfAndDescendants().FirstOrDefault(n => n.Children.Any(c => c.Id == id));

    [JsonIgnore]
    public IReadOnlyList<string> Items =>
        Props.GetValueOrDefault(ItemsKey, "")
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
}
