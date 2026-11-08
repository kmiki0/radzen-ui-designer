using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Radzen.Blazor;

namespace RadzenUiDesigner.Designer;

/// <summary>選択肢（1 行に 1 つ書いた文字列）を部品に渡す方法。</summary>
public enum ItemsMode
{
    /// <summary>選択肢を持たない。</summary>
    None,

    /// <summary>Data に文字列の配列を渡す（DropDown・ListBox・AutoComplete）。</summary>
    Data,

    /// <summary>Items の中に、選択肢の部品を 1 つずつ置く（RadioButtonList・CheckBoxList・SelectBar）。</summary>
    Items,

    /// <summary>子として、選択肢の部品を 1 つずつ置く（SplitButton）。</summary>
    ChildItems,
}

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

    /// <summary>子の部品を渡すパラメーター。たいていは ChildContent で、Tabs は Tabs、Accordion は Items。</summary>
    public string ChildSlot { get; init; } = "ChildContent";

    /// <summary>中に入れられる部品の種類（Tabs なら TabsItem だけ）。null なら、決まった親を持たない部品なら何でも入る。</summary>
    public string[]? ChildKinds { get; init; }

    /// <summary>この部品を入れられる親の種類（TabsItem なら Tabs）。null なら、どの入れ物にも入る。</summary>
    public string[]? ParentKinds { get; init; }

    /// <summary>決まった親の中にだけ置く部品（タブなど）か。部品の一覧には出さず、親のプロパティ欄から足す。</summary>
    public bool IsItem => ParentKinds is not null;

    /// <summary>
    /// 子の部品を、Radzen が自分の要素の外に描くか（タブの中身は、タブの見出しの外に描かれる）。
    /// そのときはキャンバスで子を div で包み、その div を入れ物にする。designer.js は DOM の入れ子で入れ物を見分けるため。
    /// </summary>
    public bool WrapChildren { get; init; }

    /// <summary>中の子が選ばれているとき、その子の番号を渡すパラメーター（Tabs の SelectedIndex）。選んだ部品を見せるのに使う。</summary>
    public string? SelectedIndexParam { get; init; }

    /// <summary>自分か子孫が選ばれているとき、true を渡すパラメーター（PanelMenuItem の Expanded）。</summary>
    public string? ExpandParam { get; init; }

    /// <summary>置いたときに一緒に作る子の部品（タブ 2 つなど）。</summary>
    public (string Kind, Dictionary<string, string> Props)[] InitialChildren { get; init; } = [];

    /// <summary>パラメーター欄で先に出すもの。ほかは「その他」に入る。</summary>
    public string[] MainProps { get; init; } = [];

    /// <summary>置いたときの初期値。</summary>
    public Dictionary<string, string> Initial { get; init; } = new();

    /// <summary>@bind-Value で使う変数の型。null なら値を持たない部品。</summary>
    public string? FieldType { get; init; }

    /// <summary>選択肢の渡し方。</summary>
    public ItemsMode ItemsMode { get; init; }

    /// <summary>
    /// 選択肢 1 つを表す部品の型（ItemsMode が Items か ChildItems のとき）。Text と Value に選択肢の文字列を渡す。
    /// キャンバスで部品として描くので、Type と同じく公開用のビルドで削られないように印を付ける。
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? ItemType { get; init; }

    /// <summary>プロパティ欄で、選択肢の欄に付ける名前。</summary>
    public string ItemsLabel { get; init; } = "選択肢";

    public bool HasItems => ItemsMode != ItemsMode.None;

    /// <summary>Click のメソッド名を指定できるか（Button など）。</summary>
    public bool HasClick { get; init; }

    /// <summary>複数行で入力するパラメーター（Markdown の本文など）。</summary>
    public string[] MultilineProps { get; init; } = [];

    /// <summary>この部品だけで使うパラメーターの名前。同じ名前のパラメーターでも、部品によって意味が違うときに使う。</summary>
    public Dictionary<string, string> PropLabels { get; init; } = new();

    /// <summary>キャンバスでだけ使う仮の値。指定のないパラメーターに渡し、コードには出さない（画像の Path など）。</summary>
    public Dictionary<string, object> CanvasFallback { get; init; } = new();

    public string TagName => TagOf(Type);

    public string? ItemTag => ItemType is null ? null : TagOf(ItemType);

    static string TagOf(Type t) => t.IsGenericType ? t.Name[..t.Name.IndexOf('`')] : t.Name;

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

    /// <summary>複数行の入力欄で編集するか。</summary>
    public bool IsMultiline { get; init; }

    /// <summary>プロパティ欄に出す名前。</summary>
    public string Label { get; init; } = "";

    public Type BaseType => Nullable.GetUnderlyingType(Type) ?? Type;
    public bool IsBool => BaseType == typeof(bool);
    public bool IsNumber => NumberTypes.Contains(BaseType);
    public string[]? EnumNames => BaseType.IsEnum ? Enum.GetNames(BaseType) : null;

    static readonly Type[] NumberTypes = [typeof(int), typeof(long), typeof(double), typeof(float), typeof(decimal)];

    // Data を持つ部品の、Data に渡す項目を読むためのパラメーター。選択肢はデザイナー側で扱うので、画面に出しても意味がない
    static readonly HashSet<string> DataParams = ["Data", "TextProperty", "ValueProperty", "DisabledProperty", "VisibleProperty", "ReadOnlyProperty", "Count"];

    static bool IsSimple(Type t)
    {
        var u = Nullable.GetUnderlyingType(t) ?? t;
        return u == typeof(string) || u == typeof(bool) || u.IsEnum || NumberTypes.Contains(u);
    }

    internal static IReadOnlyList<PropDef> Discover(ComponentDef def)
    {
        object? instance = null;
        try { instance = Activator.CreateInstance(def.Type); } catch { /* 作れなければ既定値は空欄 */ }

        var all = def.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var hasData = all.Any(p => p.Name == "Data");
        var props = all
            .Where(p => p.CanWrite && p.GetCustomAttribute<ParameterAttribute>() is { CaptureUnmatchedValues: false })
            .Where(p => p.GetCustomAttribute<ObsoleteAttribute>() is null && IsSimple(p.PropertyType))
            .Where(p => !(hasData && DataParams.Contains(p.Name)))
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
                IsMultiline = def.MultilineProps.Contains(p.Name),
                Label = def.PropLabels.GetValueOrDefault(p.Name) ?? Labels.GetValueOrDefault(p.Name, p.Name),
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
        ["Value"] = "値",
        ["Width"] = "幅",
        ["Height"] = "高さ",
        ["ChipStyle"] = "色",
        ["ToggleButtonStyle"] = "オンのときの色",
        ["AlternateText"] = "代わりの文字（alt）",
        ["Animation"] = "動き",
        ["Foreground"] = "色",
        ["Background"] = "背景色",
        ["Type"] = "種類",
        ["Mask"] = "書式（* が入力する文字）",
        ["CharacterPattern"] = "入力できる文字（正規表現）",
        ["Pattern"] = "取り除く文字（正規表現）",
        ["Stars"] = "星の数",
        ["Count"] = "桁数",
        ["MinLength"] = "候補を出し始める文字数",
        ["FilterOperator"] = "候補の選び方",
        ["OpenOnFocus"] = "フォーカスで候補を出す",
        ["AllowSelectAll"] = "「すべて選ぶ」を出す",
        ["TimeSpanFormat"] = "表示の書式",
        ["FieldPrecision"] = "入力する最小の単位",
        ["ShowButton"] = "OK ボタンを出す",
        ["ShowColors"] = "決まった色の一覧を出す",
        ["ShowHSV"] = "色を選ぶ面を出す",
        ["ShowRGBA"] = "RGBA の入力欄を出す",
        ["Accept"] = "受け付けるファイル（accept）",
        ["ChooseText"] = "選ぶボタンの文字",
        ["MaxFileSize"] = "最大のファイルサイズ（バイト）",
        ["Url"] = "送り先（URL）",
        ["Auto"] = "選んだらすぐ送る",
        ["ShowToolbar"] = "ツールバーを出す",
        ["StrokeColor"] = "線の色",
        ["StrokeWidth"] = "線の太さ",
        ["TabPosition"] = "タブの位置",
        ["SelectedIndex"] = "最初に開く番号（0 から）",
        ["ShowStepsButtons"] = "前へ・次へボタンを出す",
        ["AllowStepSelect"] = "見出しで移動できる",
        ["NextText"] = "次へボタンの文字",
        ["PreviousText"] = "前へボタンの文字",
        ["Responsive"] = "狭い画面で畳む",
        ["ClickToOpen"] = "クリックで開く",
        ["DisplayStyle"] = "表示のしかた",
        ["ShowArrow"] = "矢印を出す",
        ["Expanded"] = "開いておく",
        ["Collapsible"] = "畳めるようにする",
        ["Resizable"] = "大きさを変えられる",
    };
}

/// <summary>デザイナーで使える部品の一覧。</summary>
public static class ComponentCatalog
{
    public const string RootKind = "Root";

    /// <summary>画像の Path を指定していないとき、キャンバスに出す仮の画像。</summary>
    static readonly string PlaceholderImage = "data:image/svg+xml," + Uri.EscapeDataString(
        "<svg xmlns='http://www.w3.org/2000/svg' width='240' height='150' viewBox='0 0 240 150'>" +
        "<rect width='240' height='150' rx='8' fill='#eeeeee'/><circle cx='168' cy='48' r='14' fill='#bdbdbd'/>" +
        "<path d='M40 120 L95 62 L135 104 L160 80 L200 120 Z' fill='#bdbdbd'/></svg>");

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
        new() { Key = "Panel", Label = "パネル", Category = "レイアウト", Icon = "web_asset", Type = typeof(RadzenPanel), IsContainer = true,
                MainProps = ["Text", "Icon", "AllowCollapse", "Collapsed"], Initial = new() { ["Text"] = "パネル" } },
        new() { Key = "Fieldset", Label = "グループ枠", Category = "レイアウト", Icon = "select_all", Type = typeof(RadzenFieldset), IsContainer = true,
                MainProps = ["Text", "Icon", "AllowCollapse"], Initial = new() { ["Text"] = "グループ" } },
        new() { Key = "FormField", Label = "入力欄の枠", Category = "レイアウト", Icon = "input", Type = typeof(RadzenFormField), IsContainer = true,
                MainProps = ["Text", "Variant", "AllowFloatingLabel"], Initial = new() { ["Text"] = "項目名" } },
        new() { Key = "Tabs", Label = "タブ切り替え", Category = "レイアウト", Icon = "tab", Type = typeof(RadzenTabs), IsContainer = true,
                ChildSlot = "Tabs", ChildKinds = ["TabsItem"], SelectedIndexParam = "SelectedIndex",
                MainProps = ["TabPosition", "SelectedIndex"], InitialChildren = [Item("TabsItem", "タブ1"), Item("TabsItem", "タブ2")] },
        new() { Key = "TabsItem", Label = "タブ", Category = "レイアウト", Icon = "tab", Type = typeof(RadzenTabsItem), IsContainer = true,
                ParentKinds = ["Tabs"], WrapChildren = true, MainProps = ["Text", "Icon", "Disabled"], Initial = new() { ["Text"] = "タブ" } },
        new() { Key = "Accordion", Label = "アコーディオン", Category = "レイアウト", Icon = "unfold_more", Type = typeof(RadzenAccordion), IsContainer = true,
                ChildSlot = "Items", ChildKinds = ["AccordionItem"],
                MainProps = ["Multiple"], PropLabels = new() { ["Multiple"] = "複数を同時に開く" },
                InitialChildren = [("AccordionItem", new() { ["Text"] = "項目1", ["Selected"] = "true" }), Item("AccordionItem", "項目2")] },
        // 開くかどうかは項目の Selected で決まる（Accordion の SelectedIndex を変えても開かない）
        new() { Key = "AccordionItem", Label = "アコーディオンの項目", Category = "レイアウト", Icon = "unfold_more", Type = typeof(RadzenAccordionItem), IsContainer = true,
                ParentKinds = ["Accordion"], WrapChildren = true, ExpandParam = "Selected",
                MainProps = ["Text", "Icon", "Selected", "Disabled"], PropLabels = new() { ["Selected"] = "開いておく" }, Initial = new() { ["Text"] = "項目" } },
        new() { Key = "Splitter", Label = "分割", Category = "レイアウト", Icon = "vertical_split", Type = typeof(RadzenSplitter), IsContainer = true,
                ChildKinds = ["SplitterPane"], MainProps = ["Orientation"],
                // 高さを指定しないと、区画の高さが中身の分しかなくなる
                Initial = new() { ["Style"] = "height: 240px" },
                InitialChildren = [("SplitterPane", new() { ["Size"] = "50%" }), ("SplitterPane", new() { ["Size"] = "50%" })] },
        new() { Key = "SplitterPane", Label = "区画", Category = "レイアウト", Icon = "vertical_split", Type = typeof(RadzenSplitterPane), IsContainer = true,
                ParentKinds = ["Splitter"], WrapChildren = true, MainProps = ["Size", "Min", "Max", "Collapsible", "Resizable"],
                PropLabels = new() { ["Size"] = "大きさ（例: 30%）", ["Min"] = "最小の大きさ", ["Max"] = "最大の大きさ" } },

        // ---- 表示
        new() { Key = "Text", Label = "テキスト", Category = "表示", Icon = "title", Type = typeof(RadzenText),
                MainProps = ["Text", "TextStyle", "TagName", "TextAlign"], Initial = new() { ["Text"] = "テキスト" } },
        new() { Key = "Label", Label = "ラベル", Category = "表示", Icon = "label", Type = typeof(RadzenLabel),
                MainProps = ["Text", "Component"], Initial = new() { ["Text"] = "ラベル" } },
        new() { Key = "Icon", Label = "アイコン", Category = "表示", Icon = "star", Type = typeof(RadzenIcon),
                MainProps = ["Icon", "IconColor", "IconStyle"], Initial = new() { ["Icon"] = "home" } },
        new() { Key = "Image", Label = "画像", Category = "表示", Icon = "image", Type = typeof(RadzenImage),
                MainProps = ["Path", "AlternateText"], PropLabels = new() { ["Path"] = "画像の場所（URL）" },
                CanvasFallback = new() { ["Path"] = PlaceholderImage }, Initial = new() { ["AlternateText"] = "画像" } },
        new() { Key = "Badge", Label = "バッジ", Category = "表示", Icon = "new_releases", Type = typeof(RadzenBadge),
                MainProps = ["Text", "BadgeStyle", "Variant", "Shade", "IsPill"], Initial = new() { ["Text"] = "バッジ" } },
        new() { Key = "Chip", Label = "チップ", Category = "表示", Icon = "sell", Type = typeof(RadzenChip),
                MainProps = ["Text", "Icon", "ChipStyle", "Variant", "Shade", "Size"], Initial = new() { ["Text"] = "チップ" } },
        new() { Key = "Alert", Label = "お知らせ", Category = "表示", Icon = "info", Type = typeof(RadzenAlert),
                MainProps = ["Title", "Text", "AlertStyle", "Variant", "Shade", "Size", "ShowIcon", "AllowClose"],
                Initial = new() { ["Title"] = "お知らせ", ["Text"] = "メッセージ", ["AllowClose"] = "false" } },
        new() { Key = "Link", Label = "リンク", Category = "表示", Icon = "link", Type = typeof(RadzenLink),
                MainProps = ["Text", "Path", "Icon", "Target"], Initial = new() { ["Text"] = "リンク", ["Path"] = "/" } },
        new() { Key = "ProgressBar", Label = "進み具合", Category = "表示", Icon = "linear_scale", Type = typeof(RadzenProgressBar),
                MainProps = ["Value", "ProgressBarStyle", "Mode", "ShowValue", "Max"], Initial = new() { ["Value"] = "40" } },
        new() { Key = "ProgressBarCircular", Label = "進み具合（円）", Category = "表示", Icon = "donut_large", Type = typeof(RadzenProgressBarCircular),
                MainProps = ["Value", "ProgressBarStyle", "Mode", "Size", "ShowValue", "Max"], Initial = new() { ["Value"] = "40" } },
        new() { Key = "Skeleton", Label = "読み込み中の仮の枠", Category = "表示", Icon = "view_day", Type = typeof(RadzenSkeleton),
                MainProps = ["Variant", "Animation"], Initial = new() { ["Animation"] = "Wave" } },
        new() { Key = "QRCode", Label = "QR コード", Category = "表示", Icon = "qr_code_2", Type = typeof(RadzenQRCode),
                MainProps = ["Value", "Size", "Foreground", "Background"], Initial = new() { ["Value"] = "https://example.com", ["Size"] = "160px" } },
        new() { Key = "Barcode", Label = "バーコード", Category = "表示", Icon = "barcode", Type = typeof(RadzenBarcode),
                MainProps = ["Value", "Type", "ShowValue", "Width", "Height"], Initial = new() { ["Value"] = "123456789012", ["Width"] = "240px" } },
        new() { Key = "Markdown", Label = "Markdown の文章", Category = "表示", Icon = "markdown", Type = typeof(RadzenMarkdown),
                MainProps = ["Text"], MultilineProps = ["Text"], PropLabels = new() { ["Text"] = "本文（Markdown）" },
                Initial = new() { ["Text"] = "## 見出し\n\n本文の **強調** と [リンク](https://example.com)。" } },

        // ---- 入力
        new() { Key = "Button", Label = "ボタン", Category = "入力", Icon = "smart_button", Type = typeof(RadzenButton), HasClick = true,
                MainProps = ["Text", "Icon", "ButtonStyle", "Variant", "Shade", "Size", "Disabled"], Initial = new() { ["Text"] = "ボタン" } },
        new() { Key = "ToggleButton", Label = "オンとオフのボタン", Category = "入力", Icon = "touch_app", Type = typeof(RadzenToggleButton), FieldType = "bool", HasClick = true,
                MainProps = ["Text", "Icon", "ButtonStyle", "ToggleButtonStyle", "Variant", "Size", "Disabled"], Initial = new() { ["Text"] = "トグル" } },
        new() { Key = "SplitButton", Label = "メニュー付きのボタン", Category = "入力", Icon = "call_split", Type = typeof(RadzenSplitButton), HasClick = true,
                ItemsMode = ItemsMode.ChildItems, ItemType = typeof(RadzenSplitButtonItem), ItemsLabel = "メニューの項目",
                MainProps = ["Text", "Icon", "ButtonStyle", "Variant", "Size", "Disabled"],
                Initial = new() { ["Text"] = "保存", [DesignNode.ItemsKey] = "下書きとして保存\n名前を付けて保存" } },
        new() { Key = "TextBox", Label = "テキスト入力", Category = "入力", Icon = "text_fields", Type = typeof(RadzenTextBox), FieldType = "string?",
                MainProps = ["Placeholder", "Disabled", "ReadOnly"] },
        new() { Key = "TextArea", Label = "複数行の入力", Category = "入力", Icon = "notes", Type = typeof(RadzenTextArea), FieldType = "string?",
                MainProps = ["Placeholder", "Rows", "Disabled", "ReadOnly"] },
        new() { Key = "Password", Label = "パスワード", Category = "入力", Icon = "password", Type = typeof(RadzenPassword), FieldType = "string?",
                MainProps = ["Placeholder", "Disabled", "ReadOnly"] },
        new() { Key = "Mask", Label = "書式付きの入力", Category = "入力", Icon = "dialpad", Type = typeof(RadzenMask), FieldType = "string?",
                MainProps = ["Mask", "CharacterPattern", "Pattern", "Placeholder", "Disabled"],
                Initial = new() { ["Mask"] = "***-****", ["CharacterPattern"] = "[0-9]", ["Pattern"] = "[^0-9]", ["Placeholder"] = "000-0000" } },
        new() { Key = "Numeric", Label = "数値入力", Category = "入力", Icon = "pin", Type = typeof(RadzenNumeric<int>), TValue = "int", FieldType = "int",
                MainProps = ["Placeholder", "Min", "Max", "Step", "ShowUpDown", "Disabled"] },
        new() { Key = "Slider", Label = "スライダー", Category = "入力", Icon = "tune", Type = typeof(RadzenSlider<int>), TValue = "int", FieldType = "int",
                MainProps = ["Min", "Max", "Step", "Orientation", "Disabled"] },
        new() { Key = "Rating", Label = "星の評価", Category = "入力", Icon = "star_half", Type = typeof(RadzenRating), FieldType = "int",
                MainProps = ["Stars", "ReadOnly", "Disabled"] },
        new() { Key = "CheckBox", Label = "チェックボックス", Category = "入力", Icon = "check_box", Type = typeof(RadzenCheckBox<bool>), TValue = "bool", FieldType = "bool",
                MainProps = ["Name", "Disabled"] },
        new() { Key = "Switch", Label = "スイッチ", Category = "入力", Icon = "toggle_on", Type = typeof(RadzenSwitch), FieldType = "bool",
                MainProps = ["Name", "Disabled"] },
        new() { Key = "DatePicker", Label = "日付", Category = "入力", Icon = "event", Type = typeof(RadzenDatePicker<DateTime?>), TValue = "DateTime?", FieldType = "DateTime?",
                MainProps = ["DateFormat", "ShowTime", "AllowClear", "Disabled"] },
        new() { Key = "TimeSpanPicker", Label = "時間の長さ", Category = "入力", Icon = "timer", Type = typeof(RadzenTimeSpanPicker<TimeSpan?>), TValue = "TimeSpan?", FieldType = "TimeSpan?",
                MainProps = ["Placeholder", "TimeSpanFormat", "FieldPrecision", "AllowClear", "Disabled"] },
        new() { Key = "ColorPicker", Label = "色", Category = "入力", Icon = "palette", Type = typeof(RadzenColorPicker), FieldType = "string?",
                MainProps = ["ShowButton", "ShowColors", "ShowHSV", "ShowRGBA", "Disabled"] },
        new() { Key = "SecurityCode", Label = "確認コード", Category = "入力", Icon = "lock", Type = typeof(RadzenSecurityCode), FieldType = "string?",
                MainProps = ["Count", "Type", "Gap", "Disabled"] },

        // ---- 選択
        new() { Key = "DropDown", Label = "ドロップダウン", Category = "選択", Icon = "arrow_drop_down_circle", Type = typeof(RadzenDropDown<string>), TValue = "string", FieldType = "string?", ItemsMode = ItemsMode.Data,
                MainProps = ["Placeholder", "AllowClear", "AllowFiltering", "Disabled"],
                Initial = new() { [DesignNode.ItemsKey] = "選択肢1\n選択肢2\n選択肢3", ["Placeholder"] = "選択してください" } },
        new() { Key = "ListBox", Label = "リスト", Category = "選択", Icon = "list", Type = typeof(RadzenListBox<string>), TValue = "string", FieldType = "string?", ItemsMode = ItemsMode.Data,
                MainProps = ["AllowFiltering", "AllowClear", "Disabled", "ReadOnly"],
                Initial = new() { [DesignNode.ItemsKey] = "選択肢1\n選択肢2\n選択肢3" } },
        new() { Key = "AutoComplete", Label = "入力候補", Category = "選択", Icon = "manage_search", Type = typeof(RadzenAutoComplete), FieldType = "string?", ItemsMode = ItemsMode.Data,
                ItemsLabel = "候補", MainProps = ["Placeholder", "MinLength", "FilterOperator", "OpenOnFocus", "Disabled"],
                Initial = new() { [DesignNode.ItemsKey] = "東京都\n大阪府\n愛知県\n福岡県", ["Placeholder"] = "入力すると候補が出ます" } },
        new() { Key = "RadioButtonList", Label = "ラジオボタン", Category = "選択", Icon = "radio_button_checked", Type = typeof(RadzenRadioButtonList<string>), TValue = "string", FieldType = "string?",
                ItemsMode = ItemsMode.Items, ItemType = typeof(RadzenRadioButtonListItem<string>),
                MainProps = ["Orientation", "Gap", "Disabled"], Initial = new() { [DesignNode.ItemsKey] = "A\nB\nC" } },
        new() { Key = "CheckBoxList", Label = "チェックボックスの一覧", Category = "選択", Icon = "checklist", Type = typeof(RadzenCheckBoxList<string>), TValue = "string", FieldType = "IEnumerable<string>?",
                ItemsMode = ItemsMode.Items, ItemType = typeof(RadzenCheckBoxListItem<string>),
                MainProps = ["Orientation", "Gap", "AllowSelectAll", "Disabled"], Initial = new() { [DesignNode.ItemsKey] = "A\nB\nC" } },
        new() { Key = "SelectBar", Label = "切り替えボタン", Category = "選択", Icon = "view_week", Type = typeof(RadzenSelectBar<string>), TValue = "string", FieldType = "string?",
                ItemsMode = ItemsMode.Items, ItemType = typeof(RadzenSelectBarItem),
                MainProps = ["Orientation", "Size", "Disabled"], Initial = new() { [DesignNode.ItemsKey] = "日\n週\n月" } },

        // ---- ナビゲーション
        new() { Key = "Steps", Label = "手順（ステップ）", Category = "ナビゲーション", Icon = "format_list_numbered", Type = typeof(RadzenSteps), IsContainer = true,
                ChildSlot = "Steps", ChildKinds = ["StepsItem"], SelectedIndexParam = "SelectedIndex",
                MainProps = ["ShowStepsButtons", "AllowStepSelect", "NextText", "PreviousText", "SelectedIndex"],
                InitialChildren = [Item("StepsItem", "入力"), Item("StepsItem", "確認"), Item("StepsItem", "完了")] },
        new() { Key = "StepsItem", Label = "ステップ", Category = "ナビゲーション", Icon = "format_list_numbered", Type = typeof(RadzenStepsItem), IsContainer = true,
                ParentKinds = ["Steps"], WrapChildren = true, MainProps = ["Text", "Disabled", "NextText", "PreviousText"], Initial = new() { ["Text"] = "ステップ" } },
        new() { Key = "Menu", Label = "メニュー", Category = "ナビゲーション", Icon = "menu", Type = typeof(RadzenMenu), IsContainer = true, HasClick = true,
                ChildKinds = ["MenuItem"], MainProps = ["Responsive", "ClickToOpen"],
                InitialChildren = [Item("MenuItem", "ホーム"), Item("MenuItem", "製品"), Item("MenuItem", "お問い合わせ")] },
        new() { Key = "MenuItem", Label = "メニューの項目", Category = "ナビゲーション", Icon = "menu", Type = typeof(RadzenMenuItem), IsContainer = true,
                ParentKinds = ["Menu", "MenuItem"], ChildKinds = ["MenuItem"], MainProps = ["Text", "Icon", "Path", "Disabled"], Initial = new() { ["Text"] = "項目" } },
        new() { Key = "PanelMenu", Label = "パネルメニュー", Category = "ナビゲーション", Icon = "menu_open", Type = typeof(RadzenPanelMenu), IsContainer = true, HasClick = true,
                ChildKinds = ["PanelMenuItem"], MainProps = ["DisplayStyle", "Multiple", "ShowArrow"], PropLabels = new() { ["Multiple"] = "複数を同時に開く" },
                InitialChildren = [Item("PanelMenuItem", "ホーム"), Item("PanelMenuItem", "注文"), Item("PanelMenuItem", "設定")] },
        new() { Key = "PanelMenuItem", Label = "パネルメニューの項目", Category = "ナビゲーション", Icon = "menu_open", Type = typeof(RadzenPanelMenuItem), IsContainer = true,
                ParentKinds = ["PanelMenu", "PanelMenuItem"], ChildKinds = ["PanelMenuItem"], ExpandParam = "Expanded",
                MainProps = ["Text", "Icon", "Path", "Expanded", "Disabled"], Initial = new() { ["Text"] = "項目" } },
        new() { Key = "BreadCrumb", Label = "パンくずリスト", Category = "ナビゲーション", Icon = "chevron_right", Type = typeof(RadzenBreadCrumb), IsContainer = true,
                ChildKinds = ["BreadCrumbItem"], InitialChildren = [Item("BreadCrumbItem", "ホーム"), Item("BreadCrumbItem", "製品"), Item("BreadCrumbItem", "詳細")] },
        new() { Key = "BreadCrumbItem", Label = "パンくずの項目", Category = "ナビゲーション", Icon = "chevron_right", Type = typeof(RadzenBreadCrumbItem),
                ParentKinds = ["BreadCrumb"], MainProps = ["Text", "Path", "Icon"], Initial = new() { ["Text"] = "項目" } },

        // ---- ファイル・エディター
        new() { Key = "FileInput", Label = "ファイルの読み込み", Category = "ファイル・エディター", Icon = "attach_file", Type = typeof(RadzenFileInput<string>), TValue = "string", FieldType = "string?",
                MainProps = ["Accept", "ChooseText", "MaxFileSize", "Disabled"] },
        new() { Key = "Upload", Label = "アップロード", Category = "ファイル・エディター", Icon = "upload_file", Type = typeof(RadzenUpload),
                MainProps = ["Url", "Accept", "Multiple", "Auto", "ChooseText", "Disabled"] },
        new() { Key = "HtmlEditor", Label = "HTML エディター", Category = "ファイル・エディター", Icon = "wysiwyg", Type = typeof(RadzenHtmlEditor), FieldType = "string?",
                MainProps = ["Placeholder", "ShowToolbar", "Mode", "Disabled"] },
        new() { Key = "MarkdownEditor", Label = "Markdown エディター", Category = "ファイル・エディター", Icon = "edit_document", Type = typeof(RadzenMarkdownEditor), FieldType = "string?",
                MainProps = ["Placeholder", "Rows", "ShowToolbar", "Mode", "Disabled"] },
        new() { Key = "SignaturePad", Label = "署名", Category = "ファイル・エディター", Icon = "draw", Type = typeof(RadzenSignaturePad), FieldType = "string?",
                // 大きさを指定しないと、書く場所がほぼ 0 の大きさになる
                MainProps = ["StrokeColor", "StrokeWidth", "AllowClear", "ReadOnly", "Disabled"], Initial = new() { ["Style"] = "width: 320px; height: 160px" } },
    ];

    static readonly Dictionary<string, ComponentDef> ByKey = All.ToDictionary(d => d.Key);

    public static bool TryGet(string kind, [MaybeNullWhen(false)] out ComponentDef def) => ByKey.TryGetValue(kind, out def);

    /// <summary>
    /// parent の中に kind の部品を入れられるか。タブの項目（TabsItem）は Tabs の中にだけ入り、
    /// Tabs の中にはタブの項目しか入らない。
    /// </summary>
    public static bool CanContain(DesignNode parent, string kind)
    {
        if (!TryGet(kind, out var child)) return false;
        if (parent.Kind == RootKind) return !child.IsItem;
        if (!TryGet(parent.Kind, out var def) || !def.IsContainer) return false;
        if (child.ParentKinds is { } parents && !parents.Contains(parent.Kind)) return false;
        return def.ChildKinds is { } kinds ? kinds.Contains(kind) : !child.IsItem;
    }

    /// <summary>InitialChildren の 1 つ（見出しの文字だけを指定する）。</summary>
    static (string, Dictionary<string, string>) Item(string kind, string? text = null) =>
        (kind, text is null ? new() : new() { ["Text"] = text });
}
