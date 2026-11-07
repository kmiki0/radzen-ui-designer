using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;

namespace RadzenUiDesigner.Designer;

/// <summary>デザイナーで使える部品 1 種類の定義。</summary>
public sealed class ComponentDef
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Category { get; init; }
    public required string Icon { get; init; }

    /// <summary>
    /// キャンバスに表示する型。ジェネリックの部品は型引数を決めた型にする。
    /// パラメーターをリフレクションで読み、部品として描くので、公開用のビルドで削られないように印を付ける。
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public required Type Type { get; init; }

    /// <summary>コードに出す TValue（ジェネリックの部品だけ）。</summary>
    public string? TValue { get; init; }

    /// <summary>子の部品を入れられるか（ChildContent を持つか）。</summary>
    public bool IsContainer { get; init; }

    /// <summary>パラメーター欄で先に出すもの。ほかは「その他」に入る。</summary>
    public string[] MainProps { get; init; } = [];

    /// <summary>置いたときの初期値。</summary>
    public Dictionary<string, string> Initial { get; init; } = new();

    /// <summary>@bind-Value で使う変数の型。null なら値を持たない部品。</summary>
    public string? FieldType { get; init; }

    /// <summary>選択肢を持つか（DropDown、RadioButtonList）。</summary>
    public bool HasItems { get; init; }

    /// <summary>Click のメソッド名を指定できるか（Button）。</summary>
    public bool HasClick { get; init; }

    public string TagName => Type.IsGenericType ? Type.Name[..Type.Name.IndexOf('`')] : Type.Name;

    /// <summary>"Radzen" を除いた名前（RadzenButton → Button）。部品の一覧やパンくずに出す。</summary>
    public string ShortName => TagName.StartsWith("Radzen") ? TagName[6..] : TagName;

    IReadOnlyList<PropDef>? _props;
    public IReadOnlyList<PropDef> Props => _props ??= PropDef.Discover(this);
}

/// <summary>
/// 部品のパラメーター 1 つ。[Parameter] の付いたプロパティから、文字列・数値・bool・列挙型のものを拾う。
/// </summary>
public sealed class PropDef
{
    public required string Name { get; init; }
    public required Type Type { get; init; }

    /// <summary>既定値を文字列にしたもの（部品を実際に作って読んだ値）。</summary>
    public string? Default { get; init; }

    /// <summary>既定値そのもの。キャンバスで、指定のないパラメーターに渡す。</summary>
    public object? DefaultValue { get; init; }

    /// <summary>null を渡せる型か（参照型か Nullable）。</summary>
    public bool AcceptsNull => !Type.IsValueType || Nullable.GetUnderlyingType(Type) is not null;

    public bool IsMain { get; init; }

    public Type BaseType => Nullable.GetUnderlyingType(Type) ?? Type;
    public bool IsBool => BaseType == typeof(bool);
    public bool IsNumber => NumberTypes.Contains(BaseType);
    public string[]? EnumNames => BaseType.IsEnum ? Enum.GetNames(BaseType) : null;
    public string Label => Labels.GetValueOrDefault(Name, Name);

    static readonly Type[] NumberTypes = [typeof(int), typeof(long), typeof(double), typeof(float), typeof(decimal)];

    // 選択肢はデザイナー側で扱う。ほかは画面に出しても意味がないもの
    static readonly HashSet<string> Excluded = ["Data", "TextProperty", "ValueProperty", "DisabledProperty", "VisibleProperty", "Count"];

    static bool IsSimple(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u == typeof(string) || u == typeof(bool) || u.IsEnum || NumberTypes.Contains(u);
    }

    internal static IReadOnlyList<PropDef> Discover(ComponentDef def)
    {
        object? instance = null;
        try { instance = Activator.CreateInstance(def.Type); } catch { /* 作れなければ既定値は空欄 */ }

        var props = def.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && p.GetCustomAttribute<ParameterAttribute>() is { CaptureUnmatchedValues: false })
            .Where(p => p.GetCustomAttribute<ObsoleteAttribute>() is null && IsSimple(p.PropertyType) && !Excluded.Contains(p.Name))
            // 入力部品の値は @bind-Value の変数で持つので、パラメーター欄には出さない
            .Where(p => !(def.FieldType is not null && p.Name == "Value"))
            // new で隠したプロパティは派生側だけを使う
            .GroupBy(p => p.Name)
            .Select(g => g.OrderByDescending(p => Depth(p.DeclaringType)).First());

        var list = props.Select(p =>
        {
            var raw = ReadDefault(p, instance);
            return new PropDef
            {
                Name = p.Name,
                Type = p.PropertyType,
                Default = Format(raw),
                DefaultValue = raw,
                IsMain = def.MainProps.Contains(p.Name),
            };
        }).ToList();

        return list
            .OrderBy(p => p.IsMain ? Array.IndexOf(def.MainProps, p.Name) : int.MaxValue)
            .ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToList();
    }

    static int Depth(Type? t)
    {
        var d = 0;
        for (; t is not null; t = t.BaseType) d++;
        return d;
    }

    static object? ReadDefault(PropertyInfo p, object? instance)
    {
        if (instance is null) return null;
        try { return p.GetValue(instance); } catch { return null; }
    }

    public static string? Format(object? v) => v switch
    {
        null => null,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    /// <summary>文字列で持っている値を、パラメーターの型に変える。</summary>
    public bool TryConvert(string s, out object? value)
    {
        value = null;
        var u = BaseType;
        if (u == typeof(string)) { value = s; return true; }
        if (u.IsEnum) return Enum.TryParse(u, s, out value);
        if (u == typeof(bool) && bool.TryParse(s, out var b)) { value = b; return true; }
        if (u == typeof(int) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { value = i; return true; }
        if (u == typeof(long) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) { value = l; return true; }
        if (u == typeof(double) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) { value = d; return true; }
        if (u == typeof(float) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) { value = f; return true; }
        if (u == typeof(decimal) && decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m)) { value = m; return true; }
        return false;
    }

    static readonly Dictionary<string, string> Labels = new()
    {
        ["Text"] = "テキスト",
        ["Title"] = "タイトル",
        ["Gap"] = "間隔",
        ["RowGap"] = "行の間隔",
        ["Orientation"] = "並べる向き",
        ["AlignItems"] = "揃え（並びと直角の向き）",
        ["JustifyContent"] = "揃え（並ぶ向き）",
        ["Wrap"] = "折り返し",
        ["Reverse"] = "逆順に並べる",
        ["Size"] = "サイズ",
        ["SizeXS"] = "幅（XS 以上）",
        ["SizeSM"] = "幅（SM 以上）",
        ["SizeMD"] = "幅（MD 以上）",
        ["SizeLG"] = "幅（LG 以上）",
        ["SizeXL"] = "幅（XL 以上）",
        ["SizeXX"] = "幅（XX 以上）",
        ["Offset"] = "左に空ける列数",
        ["Variant"] = "見た目",
        ["ButtonStyle"] = "色",
        ["BadgeStyle"] = "色",
        ["AlertStyle"] = "色",
        ["ProgressBarStyle"] = "色",
        ["Shade"] = "濃さ",
        ["Icon"] = "アイコン",
        ["IconColor"] = "アイコンの色",
        ["IconStyle"] = "アイコンの色の種類",
        ["Disabled"] = "無効にする",
        ["ReadOnly"] = "読み取り専用",
        ["Placeholder"] = "プレースホルダー",
        ["Visible"] = "表示する",
        ["Style"] = "スタイル（CSS）",
        ["TextStyle"] = "文字の種類",
        ["TagName"] = "タグ",
        ["TextAlign"] = "文字の揃え",
        ["IsPill"] = "角を丸くする",
        ["ShowIcon"] = "アイコンを出す",
        ["AllowClose"] = "閉じるボタンを出す",
        ["Path"] = "リンク先",
        ["Target"] = "開く先（target）",
        ["Max"] = "最大値",
        ["Min"] = "最小値",
        ["Step"] = "増減の幅",
        ["ShowUpDown"] = "増減ボタンを出す",
        ["Mode"] = "モード",
        ["ShowValue"] = "値を出す",
        ["Unit"] = "単位",
        ["Rows"] = "行数",
        ["Cols"] = "列数",
        ["AllowClear"] = "クリアボタンを出す",
        ["AllowFiltering"] = "絞り込みを出す",
        ["Multiple"] = "複数選択",
        ["DateFormat"] = "日付の書式",
        ["ShowTime"] = "時刻も選ぶ",
        ["AllowFloatingLabel"] = "ラベルを枠の上に浮かせる",
        ["Component"] = "対象の部品（Name）",
        ["AllowCollapse"] = "折りたためるようにする",
        ["Collapsed"] = "折りたたんでおく",
        ["Name"] = "名前（Label の Component から指す）",
        ["TabIndex"] = "タブ順",
        ["Immediate"] = "入力のたびに値を反映",
        ["Trim"] = "前後の空白を削る",
        ["Format"] = "書式",
    };
}

/// <summary>デザイナーで使える部品の一覧。</summary>
public static class ComponentCatalog
{
    public const string RootKind = "Root";

    public static readonly IReadOnlyList<ComponentDef> All =
    [
        // ---- レイアウト
        new() { Key = "Stack", Label = "スタック", Category = "レイアウト", Icon = "view_agenda", Type = typeof(RadzenStack), IsContainer = true,
                MainProps = ["Orientation", "Gap", "AlignItems", "JustifyContent", "Wrap"], Initial = new() { ["Gap"] = "1rem" } },
        new() { Key = "Row", Label = "行（12 分割）", Category = "レイアウト", Icon = "view_column", Type = typeof(RadzenRow), IsContainer = true,
                MainProps = ["Gap", "AlignItems", "JustifyContent"], Initial = new() { ["Gap"] = "1rem" } },
        new() { Key = "Column", Label = "列", Category = "レイアウト", Icon = "crop_portrait", Type = typeof(RadzenColumn), IsContainer = true,
                MainProps = ["Size", "SizeSM", "SizeMD", "SizeLG"], Initial = new() { ["Size"] = "6" } },
        new() { Key = "Card", Label = "カード", Category = "レイアウト", Icon = "crop_square", Type = typeof(RadzenCard), IsContainer = true,
                MainProps = ["Variant"] },
        new() { Key = "Fieldset", Label = "グループ枠", Category = "レイアウト", Icon = "select_all", Type = typeof(RadzenFieldset), IsContainer = true,
                MainProps = ["Text", "Icon", "AllowCollapse"], Initial = new() { ["Text"] = "グループ" } },
        new() { Key = "FormField", Label = "入力欄の枠", Category = "レイアウト", Icon = "input", Type = typeof(RadzenFormField), IsContainer = true,
                MainProps = ["Text", "Variant", "AllowFloatingLabel"], Initial = new() { ["Text"] = "項目名" } },

        // ---- 表示
        new() { Key = "Text", Label = "テキスト", Category = "表示", Icon = "title", Type = typeof(RadzenText),
                MainProps = ["Text", "TextStyle", "TagName", "TextAlign"], Initial = new() { ["Text"] = "テキスト" } },
        new() { Key = "Label", Label = "ラベル", Category = "表示", Icon = "label", Type = typeof(RadzenLabel),
                MainProps = ["Text", "Component"], Initial = new() { ["Text"] = "ラベル" } },
        new() { Key = "Icon", Label = "アイコン", Category = "表示", Icon = "star", Type = typeof(RadzenIcon),
                MainProps = ["Icon", "IconColor", "IconStyle"], Initial = new() { ["Icon"] = "home" } },
        new() { Key = "Badge", Label = "バッジ", Category = "表示", Icon = "new_releases", Type = typeof(RadzenBadge),
                MainProps = ["Text", "BadgeStyle", "Variant", "Shade", "IsPill"], Initial = new() { ["Text"] = "バッジ" } },
        new() { Key = "Alert", Label = "お知らせ", Category = "表示", Icon = "info", Type = typeof(RadzenAlert),
                MainProps = ["Title", "Text", "AlertStyle", "Variant", "Shade", "Size", "ShowIcon", "AllowClose"],
                Initial = new() { ["Title"] = "お知らせ", ["Text"] = "メッセージ", ["AllowClose"] = "false" } },
        new() { Key = "Link", Label = "リンク", Category = "表示", Icon = "link", Type = typeof(RadzenLink),
                MainProps = ["Text", "Path", "Icon", "Target"], Initial = new() { ["Text"] = "リンク", ["Path"] = "/" } },
        new() { Key = "ProgressBar", Label = "進み具合", Category = "表示", Icon = "linear_scale", Type = typeof(RadzenProgressBar),
                MainProps = ["Value", "ProgressBarStyle", "Mode", "ShowValue", "Max"], Initial = new() { ["Value"] = "40" } },

        // ---- 入力
        new() { Key = "Button", Label = "ボタン", Category = "入力", Icon = "smart_button", Type = typeof(RadzenButton), HasClick = true,
                MainProps = ["Text", "Icon", "ButtonStyle", "Variant", "Shade", "Size", "Disabled"], Initial = new() { ["Text"] = "ボタン" } },
        new() { Key = "TextBox", Label = "テキスト入力", Category = "入力", Icon = "text_fields", Type = typeof(RadzenTextBox), FieldType = "string?",
                MainProps = ["Placeholder", "Disabled", "ReadOnly"] },
        new() { Key = "TextArea", Label = "複数行の入力", Category = "入力", Icon = "notes", Type = typeof(RadzenTextArea), FieldType = "string?",
                MainProps = ["Placeholder", "Rows", "Disabled", "ReadOnly"] },
        new() { Key = "Numeric", Label = "数値入力", Category = "入力", Icon = "pin", Type = typeof(RadzenNumeric<int>), TValue = "int", FieldType = "int",
                MainProps = ["Placeholder", "Min", "Max", "Step", "ShowUpDown", "Disabled"] },
        new() { Key = "CheckBox", Label = "チェックボックス", Category = "入力", Icon = "check_box", Type = typeof(RadzenCheckBox<bool>), TValue = "bool", FieldType = "bool",
                MainProps = ["Name", "Disabled"] },
        new() { Key = "Switch", Label = "スイッチ", Category = "入力", Icon = "toggle_on", Type = typeof(RadzenSwitch), FieldType = "bool",
                MainProps = ["Name", "Disabled"] },
        new() { Key = "DropDown", Label = "ドロップダウン", Category = "入力", Icon = "arrow_drop_down_circle", Type = typeof(RadzenDropDown<string>), TValue = "string", FieldType = "string?", HasItems = true,
                MainProps = ["Placeholder", "AllowClear", "AllowFiltering", "Disabled"],
                Initial = new() { [DesignNode.ItemsKey] = "選択肢1\n選択肢2\n選択肢3", ["Placeholder"] = "選択してください" } },
        new() { Key = "DatePicker", Label = "日付", Category = "入力", Icon = "event", Type = typeof(RadzenDatePicker<DateTime?>), TValue = "DateTime?", FieldType = "DateTime?",
                MainProps = ["DateFormat", "ShowTime", "AllowClear", "Disabled"] },
        new() { Key = "RadioButtonList", Label = "ラジオボタン", Category = "入力", Icon = "radio_button_checked", Type = typeof(RadzenRadioButtonList<string>), TValue = "string", FieldType = "string?", HasItems = true,
                MainProps = ["Orientation", "Gap", "Disabled"], Initial = new() { [DesignNode.ItemsKey] = "A\nB\nC" } },
    ];

    static readonly Dictionary<string, ComponentDef> ByKey = All.ToDictionary(d => d.Key);

    public static bool TryGet(string kind, [MaybeNullWhen(false)] out ComponentDef def) => ByKey.TryGetValue(kind, out def);

    public static bool IsContainer(DesignNode node) =>
        node.Kind == RootKind || (TryGet(node.Kind, out var def) && def.IsContainer);
}
