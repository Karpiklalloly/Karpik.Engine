namespace Karpik.Engine.Client.UI.Core;

public class UIStyle
{
    public Color Background;
    public Color TextColor;
    public Color BorderColor;
    public Padding Padding;
    public Margin Margin;
    public float CornerRadius;
    public float FontSize;
    public float BorderWidth;
    public TextAlignment TextAlignment;
    public FlexContainerStyle FlexStyle;

    private static readonly Pool<UIStyle> _pool = new(() => new UIStyle());

    public static UIStyle Rent() => _pool.Rent();

    public static void Return(UIStyle style)
    {
        style.Reset();
        _pool.Return(style);
    }

    private void Reset()
    {
        Background = Color.Transparent;
        TextColor = Color.White;
        BorderColor = Color.Transparent;
        Padding = Padding.Zero;
        Margin = Margin.Zero;
        CornerRadius = 0;
        FontSize = 14;
        BorderWidth = 0;
        TextAlignment = TextAlignment.Left;
        FlexStyle = FlexContainerStyle.Default;
    }

    public UIStyle BackgroundColor(Color color)
    {
        Background = color;
        return this;
    }

    public UIStyle Text(Color color)
    {
        TextColor = color;
        return this;
    }

    public UIStyle Border(Color color, float width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        return this;
    }

    public UIStyle PaddingAll(float value)
    {
        Padding = new Padding(value);
        return this;
    }

    public UIStyle MarginAll(float value)
    {
        Margin = new Margin(value);
        return this;
    }

    public UIStyle CornerRadiusValue(float radius)
    {
        CornerRadius = radius;
        return this;
    }

    public UIStyle FontSizeValue(float size)
    {
        FontSize = size;
        return this;
    }

    public UIStyle Align(TextAlignment alignment)
    {
        TextAlignment = alignment;
        return this;
    }

    public void CopyTo(UIStyle target)
    {
        target.Background = Background;
        target.TextColor = TextColor;
        target.BorderColor = BorderColor;
        target.Padding = Padding;
        target.Margin = Margin;
        target.CornerRadius = CornerRadius;
        target.FontSize = FontSize;
        target.BorderWidth = BorderWidth;
        target.TextAlignment = TextAlignment;
        target.FlexStyle = FlexStyle;
    }
}

internal class Pool<T> where T : class, new()
{
    private readonly Stack<T> _stack = new();
    private readonly Func<T> _factory;

    public Pool(Func<T> factory)
    {
        _factory = factory;
    }

    public T Rent()
    {
        return _stack.Count > 0 ? _stack.Pop() : _factory();
    }

    public void Return(T item)
    {
        _stack.Push(item);
    }
}

public class ResourceDictionary
{
    private readonly Dictionary<string, object> _resources = new();
    private readonly Dictionary<string, ResourceDictionary> _mergedDictionaries = new();

    public void Add(string key, object value)
    {
        _resources[key] = value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_resources.TryGetValue(key, out var obj) && obj is T t)
        {
            value = t;
            return true;
        }

        foreach (var dict in _mergedDictionaries.Values)
        {
            if (dict.TryGet(key, out value))
                return true;
        }

        value = default;
        return false;
    }

    public T? Get<T>(string key)
    {
        TryGet(key, out T? value);
        return value;
    }

    public void Merge(ResourceDictionary dictionary, bool overrideExisting = false)
    {
        foreach (var kvp in dictionary._resources)
        {
            if (overrideExisting || !_resources.ContainsKey(kvp.Key))
            {
                _resources[kvp.Key] = kvp.Value;
            }
        }
    }

    public static ResourceDictionary Default { get; } = CreateDefault();

    private static ResourceDictionary CreateDefault()
    {
        var dict = new ResourceDictionary();

        dict.Add("primary", Color.FromHex("#007bff"));
        dict.Add("secondary", Color.FromHex("#6c757d"));
        dict.Add("success", Color.FromHex("#28a745"));
        dict.Add("danger", Color.FromHex("#dc3545"));
        dict.Add("warning", Color.FromHex("#ffc107"));
        dict.Add("info", Color.FromHex("#17a2b8"));
        dict.Add("light", Color.FromHex("#f8f9fa"));
        dict.Add("dark", Color.FromHex("#343a40"));
        dict.Add("background", Color.FromHex("#ffffff"));
        dict.Add("surface", Color.FromHex("#f8f9fa"));
        dict.Add("text-primary", Color.FromHex("#212529"));
        dict.Add("text-secondary", Color.FromHex("#6c757d"));

        dict.Add("padding-small", 4f);
        dict.Add("padding-medium", 8f);
        dict.Add("padding-large", 16f);
        dict.Add("margin-small", 4f);
        dict.Add("margin-medium", 8f);
        dict.Add("margin-large", 16f);
        dict.Add("border-radius", 4f);
        dict.Add("font-size-small", 12f);
        dict.Add("font-size-normal", 14f);
        dict.Add("font-size-large", 16f);
        dict.Add("font-size-heading", 24f);

        return dict;
    }
}

public class StyleSelector
{
    public SelectorType Type;
    public string Value;
    public int Specificity;

    public enum SelectorType
    {
        Type,
        Class,
        Id,
        PseudoState
    }

    public StyleSelector(SelectorType type, string value)
    {
        Type = type;
        Value = value;
        Specificity = type switch
        {
            SelectorType.Type => 1,
            SelectorType.Class => 10,
            SelectorType.Id => 100,
            SelectorType.PseudoState => 5,
            _ => 0
        };
    }

    public static StyleSelector ForType(string typeName) => new(SelectorType.Type, typeName);
    public static StyleSelector Class(string className) => new(SelectorType.Class, className);
    public static StyleSelector Id(string id) => new(SelectorType.Id, id);
    public static StyleSelector Pseudo(PseudoState state) => new(SelectorType.PseudoState, state.ToString());
}

public class StyleRule
{
    public List<StyleSelector> Selectors = new();
    public UIStyle Style = null!;
    public int Specificity;

    public bool Matches(UIWidget widget, WidgetStyleData styleData)
    {
        foreach (var selector in Selectors)
        {
            switch (selector.Type)
            {
                case StyleSelector.SelectorType.Type:
                    if (widget.Type.ToString() != selector.Value)
                        return false;
                    break;

                case StyleSelector.SelectorType.Class:
                    if (!styleData.Classes.Contains(selector.Value))
                        return false;
                    break;

                case StyleSelector.SelectorType.Id:
                    if (widget.Id != selector.Value)
                        return false;
                    break;

                case StyleSelector.SelectorType.PseudoState:
                    if (!Enum.TryParse<PseudoState>(selector.Value, out var state))
                        return false;
                    if (!styleData.PseudoStates.Contains(state))
                        return false;
                    break;
            }
        }
        return true;
    }
}

public class WidgetStyleData
{
    public HashSet<string> Classes = new();
    public HashSet<PseudoState> PseudoStates = new();
    public UIStyle? InlineStyle;
    public string? StyleClass;

    public void AddClass(string className)
    {
        Classes.Add(className);
    }

    public void RemoveClass(string className)
    {
        Classes.Remove(className);
    }

    public bool HasClass(string className) => Classes.Contains(className);

    public void SetPseudoState(PseudoState state)
    {
        PseudoStates.Add(state);
    }

    public void RemovePseudoState(PseudoState state)
    {
        PseudoStates.Remove(state);
    }

    public bool HasPseudoState(PseudoState state) => PseudoStates.Contains(state);
}

public class StyleEngine
{
    private readonly List<StyleRule> _rules = new();
    private readonly ResourceDictionary _resources;
    private readonly Dictionary<int, WidgetStyleData> _styleData = new();

    public StyleEngine(ResourceDictionary? resources = null)
    {
        _resources = resources ?? ResourceDictionary.Default;
    }

    public void AddRule(StyleRule rule)
    {
        _rules.Add(rule);
        rule.Specificity = rule.Selectors.Sum(s => s.Specificity);
    }

    public void AddRule(string selector, UIStyle style)
    {
        var rule = new StyleRule { Style = style };

        var parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part.StartsWith('.'))
                rule.Selectors.Add(StyleSelector.Class(part.Substring(1)));
            else if (part.StartsWith('#'))
                rule.Selectors.Add(StyleSelector.Id(part.Substring(1)));
            else if (part.StartsWith(':'))
                rule.Selectors.Add(StyleSelector.Pseudo(Enum.Parse<PseudoState>(part.Substring(1))));
            else
                rule.Selectors.Add(StyleSelector.ForType(part));
        }

        AddRule(rule);
    }

    public WidgetStyleData GetOrCreateStyleData(int widgetIndex)
    {
        if (!_styleData.TryGetValue(widgetIndex, out var data))
        {
            data = new WidgetStyleData();
            _styleData[widgetIndex] = data;
        }
        return data;
    }

    public UIStyle ComputeStyle(UIWidget widget)
    {
        return ComputeStyle(widget, GetOrCreateStyleData(widget.GetHashCode()));
    }

    public UIStyle ComputeStyle(UIWidget widget, WidgetStyleData styleData)
    {
        var computed = UIStyle.Rent();

        var matchingRules = _rules
            .Where(r => r.Matches(widget, styleData))
            .OrderByDescending(r => r.Specificity)
            .ToList();

        foreach (var rule in matchingRules)
        {
            MergeStyle(computed, rule.Style, _resources);
        }

        if (styleData.InlineStyle != null)
        {
            MergeStyle(computed, styleData.InlineStyle, _resources);
        }

        ResolveResourceReferences(computed, _resources);

        return computed;
    }

    private void MergeStyle(UIStyle target, UIStyle source, ResourceDictionary resources)
    {
        if (source.Background.A > 0 || source.Background != Color.Transparent)
            target.Background = source.Background;

        if (source.TextColor != Color.Transparent)
            target.TextColor = source.TextColor;

        if (source.BorderColor != Color.Transparent || source.BorderWidth > 0)
        {
            target.BorderColor = source.BorderColor;
            target.BorderWidth = source.BorderWidth;
        }

        if (source.Padding != Padding.Zero)
            target.Padding = source.Padding;

        if (source.Margin != Margin.Zero)
            target.Margin = source.Margin;

        if (source.CornerRadius > 0)
            target.CornerRadius = source.CornerRadius;

        if (source.FontSize > 0)
            target.FontSize = source.FontSize;
    }

    private void ResolveResourceReferences(UIStyle style, ResourceDictionary resources)
    {
    }

    public void ClearCache()
    {
        _styleData.Clear();
    }
}
