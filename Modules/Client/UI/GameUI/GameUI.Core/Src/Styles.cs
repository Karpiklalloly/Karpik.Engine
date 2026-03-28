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
    
    // Track which properties were explicitly set (for CSS specificity)
    public bool _explicitBackground;
    public bool _explicitTextColor;
    public bool _explicitBorderColor;
    public bool _explicitPadding;
    public bool _explicitMargin;
    public bool _explicitCornerRadius;
    public bool _explicitFontSize;
    public bool _explicitBorderWidth;
    public bool _explicitTextAlignment;

    private static readonly Pool<UIStyle> _pool = new(() => new UIStyle());

    public static UIStyle Rent() => _pool.Rent();

    public static void Return(UIStyle style)
    {
        style.Reset();
        _pool.Return(style);
    }

    public static void ClearPool()
    {
        _pool.Clear();
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
        
        _explicitBackground = false;
        _explicitTextColor = false;
        _explicitBorderColor = false;
        _explicitPadding = false;
        _explicitMargin = false;
        _explicitCornerRadius = false;
        _explicitFontSize = false;
        _explicitBorderWidth = false;
        _explicitTextAlignment = false;
        
        _textColorResourceKey = null;
        _backgroundResourceKey = null;
        _borderColorResourceKey = null;
    }

    public UIStyle BackgroundColor(Color color)
    {
        Background = color;
        _explicitBackground = true;
        return this;
    }

    public UIStyle BackgroundColor(string resourceKey)
    {
        if (resourceKey.StartsWith("$"))
        {
            _backgroundResourceKey = resourceKey.Substring(1);
        }
        else
        {
            _backgroundResourceKey = resourceKey;
        }
        _explicitBackground = true;
        return this;
    }

    public UIStyle Text(Color color)
    {
        TextColor = color;
        _explicitTextColor = true;
        return this;
    }

    public UIStyle Text(string resourceKey)
    {
        if (resourceKey.StartsWith("$"))
        {
            _textColorResourceKey = resourceKey.Substring(1);
        }
        else
        {
            _textColorResourceKey = resourceKey;
        }
        _explicitTextColor = true;
        return this;
    }

    public UIStyle Border(Color color, float width = 1)
    {
        BorderColor = color;
        BorderWidth = width;
        _explicitBorderColor = true;
        _explicitBorderWidth = true;
        return this;
    }

    public UIStyle Border(string resourceKey, float width = 1)
    {
        if (resourceKey.StartsWith("$"))
        {
            _borderColorResourceKey = resourceKey.Substring(1);
        }
        else
        {
            _borderColorResourceKey = resourceKey;
        }
        BorderWidth = width;
        _explicitBorderColor = true;
        _explicitBorderWidth = true;
        return this;
    }

    public UIStyle PaddingAll(float value)
    {
        Padding = new Padding(value);
        _explicitPadding = true;
        return this;
    }

    public UIStyle MarginAll(float value)
    {
        Margin = new Margin(value);
        _explicitMargin = true;
        return this;
    }

    public UIStyle CornerRadiusValue(float radius)
    {
        CornerRadius = radius;
        _explicitCornerRadius = true;
        return this;
    }

    public UIStyle FontSizeValue(float size)
    {
        FontSize = size;
        _explicitFontSize = true;
        return this;
    }

    public UIStyle Align(TextAlignment alignment)
    {
        TextAlignment = alignment;
        _explicitTextAlignment = true;
        return this;
    }

    // Resource reference support - stores the key to be resolved later
    private string? _textColorResourceKey;
    private string? _backgroundResourceKey;
    private string? _borderColorResourceKey;
    
    internal void SetTextColorResourceKey(string? key) => _textColorResourceKey = key;
    internal void SetBackgroundResourceKey(string? key) => _backgroundResourceKey = key;
    internal void SetBorderColorResourceKey(string? key) => _borderColorResourceKey = key;

    public UIStyle TextColorResource(string resourceKey)
    {
        _textColorResourceKey = resourceKey;
        _explicitTextColor = true;
        return this;
    }

    public UIStyle BackgroundColorResource(string resourceKey)
    {
        _backgroundResourceKey = resourceKey;
        _explicitBackground = true;
        return this;
    }

    public UIStyle BorderColorResource(string resourceKey)
    {
        _borderColorResourceKey = resourceKey;
        _explicitBorderColor = true;
        return this;
    }

    public bool HasTextColorResource => _textColorResourceKey != null;
    public bool HasBackgroundResource => _backgroundResourceKey != null;
    public bool HasBorderColorResource => _borderColorResourceKey != null;

    public string? TextColorResourceKey => _textColorResourceKey;
    public string? BackgroundResourceKey => _backgroundResourceKey;
    public string? BorderColorResourceKey => _borderColorResourceKey;

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
        
        target._explicitBackground = _explicitBackground;
        target._explicitTextColor = _explicitTextColor;
        target._explicitBorderColor = _explicitBorderColor;
        target._explicitPadding = _explicitPadding;
        target._explicitMargin = _explicitMargin;
        target._explicitCornerRadius = _explicitCornerRadius;
        target._explicitFontSize = _explicitFontSize;
        target._explicitBorderWidth = _explicitBorderWidth;
        target._explicitTextAlignment = _explicitTextAlignment;
        
        target._textColorResourceKey = _textColorResourceKey;
        target._backgroundResourceKey = _backgroundResourceKey;
        target._borderColorResourceKey = _borderColorResourceKey;
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

    public void Clear()
    {
        _stack.Clear();
    }
}

public class ResourceDictionary
{
    private readonly Dictionary<string, object> _resources = new();
    private readonly Dictionary<string, ResourceDictionary> _mergedDictionaries = new();

    internal Dictionary<string, object> Resources => _resources;

    public void Add(string key, object value)
    {
        _resources[key] = value;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_resources.TryGetValue(key, out var obj))
        {
            if (obj == null)
            {
                if (typeof(T).IsClass)
                {
                    value = default;
                    return true;
                }
            }
            else if (obj is T t)
            {
                value = t;
                return true;
            }
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

    public void Merge(ResourceDictionary? dictionary, bool overrideExisting = false)
    {
        if (dictionary == null)
            return;
            
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
    private ResourceDictionary _resources;
    private readonly Dictionary<int, WidgetStyleData> _styleData = new();

    public StyleEngine(ResourceDictionary? resources = null)
    {
        _resources = resources ?? ResourceDictionary.Default;
    }

    public void SetResources(ResourceDictionary resources)
    {
        _resources = resources ?? ResourceDictionary.Default;
    }

    public ResourceDictionary GetResources() => _resources;

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
            // Handle combined selectors like ".button:Hover" or "Button:hover"
            var combinedParts = part.Split(':');
            
            for (int i = 0; i < combinedParts.Length; i++)
            {
                var p = combinedParts[i];
                
                // Check for pseudo-state (either after : or as first element like ":Hover")
                bool isPseudoOnly = (i == 0 && combinedParts.Length > 1) || 
                                   (i > 0 && !p.StartsWith('.') && !p.StartsWith('#') && 
                                    Enum.TryParse<PseudoState>(p, out _));
                
                if (string.IsNullOrEmpty(p) && i < combinedParts.Length - 1)
                {
                    // Handle case like ".button:Hover" where p is empty at i=1
                    var pseudoPart = combinedParts[i + 1];
                    if (Enum.TryParse<PseudoState>(pseudoPart, out var pseudoState))
                    {
                        rule.Selectors.Add(StyleSelector.Pseudo(pseudoState));
                    }
                    continue;
                }
                
                if (p.StartsWith('.'))
                    rule.Selectors.Add(StyleSelector.Class(p.Substring(1)));
                else if (p.StartsWith('#'))
                    rule.Selectors.Add(StyleSelector.Id(p.Substring(1)));
                else if (!string.IsNullOrEmpty(p) && !isPseudoOnly)
                    rule.Selectors.Add(StyleSelector.ForType(p));
                
                // Handle pseudo-state if present in next part
                if (i < combinedParts.Length - 1)
                {
                    var pseudoPart = combinedParts[i + 1];
                    if (Enum.TryParse<PseudoState>(pseudoPart, out var pseudoState))
                    {
                        rule.Selectors.Add(StyleSelector.Pseudo(pseudoState));
                    }
                }
            }
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
        
        // Track which specificity level set each property
        var propertySpecificity = new Dictionary<string, int>();

        var matchingRules = _rules
            .Where(r => r.Matches(widget, styleData))
            .OrderByDescending(r => r.Specificity)
            .ToList();

        foreach (var rule in matchingRules)
        {
            MergeStyleWithSpecificity(computed, rule.Style, rule.Specificity, propertySpecificity);
        }

        if (styleData.InlineStyle != null)
        {
            // Inline style has infinite specificity
            MergeStyleWithSpecificity(computed, styleData.InlineStyle, int.MaxValue, propertySpecificity);
        }

        ResolveResourceReferences(computed, _resources);

        return computed;
    }

    private void MergeStyleWithSpecificity(UIStyle target, UIStyle source, int specificity, Dictionary<string, int> propertySpecificity)
    {
        // Only set properties that were explicitly set in the source style
        if (source._explicitBackground)
        {
            // Check if this is a resource reference
            if (source.HasBackgroundResource)
                TrySetResourceProperty(target, "Background", source.BackgroundResourceKey, specificity, propertySpecificity);
            else
                TrySetProperty(target, "Background", source.Background, specificity, propertySpecificity);
        }
        if (source._explicitTextColor)
        {
            if (source.HasTextColorResource)
                TrySetResourceProperty(target, "TextColor", source.TextColorResourceKey, specificity, propertySpecificity);
            else
                TrySetProperty(target, "TextColor", source.TextColor, specificity, propertySpecificity);
        }
        if (source._explicitBorderColor)
        {
            if (source.HasBorderColorResource)
                TrySetResourceProperty(target, "BorderColor", source.BorderColorResourceKey, specificity, propertySpecificity);
            else
                TrySetProperty(target, "BorderColor", source.BorderColor, specificity, propertySpecificity);
        }
        if (source._explicitBorderWidth)
            TrySetProperty(target, "BorderWidth", source.BorderWidth, specificity, propertySpecificity);
        if (source._explicitPadding)
            TrySetProperty(target, "Padding", source.Padding, specificity, propertySpecificity);
        if (source._explicitMargin)
            TrySetProperty(target, "Margin", source.Margin, specificity, propertySpecificity);
        if (source._explicitCornerRadius)
            TrySetProperty(target, "CornerRadius", source.CornerRadius, specificity, propertySpecificity);
        if (source._explicitFontSize)
            TrySetProperty(target, "FontSize", source.FontSize, specificity, propertySpecificity);
        if (source._explicitTextAlignment)
            TrySetProperty(target, "TextAlignment", source.TextAlignment, specificity, propertySpecificity);
    }

    private void TrySetResourceProperty(UIStyle target, string propertyName, string? resourceKey, int specificity, Dictionary<string, int> propertySpecificity)
    {
        if (propertySpecificity.TryGetValue(propertyName, out var existingSpecificity))
        {
            if (existingSpecificity >= specificity)
                return;
        }
        
        switch (propertyName)
        {
            case "Background":
                target.SetBackgroundResourceKey(resourceKey);
                target._explicitBackground = true;
                break;
            case "TextColor":
                target.SetTextColorResourceKey(resourceKey);
                target._explicitTextColor = true;
                break;
            case "BorderColor":
                target.SetBorderColorResourceKey(resourceKey);
                target._explicitBorderColor = true;
                break;
        }
        
        propertySpecificity[propertyName] = specificity;
    }

    private void TrySetProperty<T>(UIStyle target, string propertyName, T value, int specificity, Dictionary<string, int> propertySpecificity)
    {
        // Check if this property was already set by a higher specificity rule
        if (propertySpecificity.TryGetValue(propertyName, out var existingSpecificity))
        {
            if (existingSpecificity >= specificity)
                return; // Skip - already set by higher specificity
        }
        
        // Set the property and track its specificity
        switch (propertyName)
        {
            case "Background": target.Background = (Color)(object)value; break;
            case "TextColor": target.TextColor = (Color)(object)value; break;
            case "BorderColor": target.BorderColor = (Color)(object)value; break;
            case "BorderWidth": target.BorderWidth = (float)(object)value; break;
            case "Padding": target.Padding = (Padding)(object)value; break;
            case "Margin": target.Margin = (Margin)(object)value; break;
            case "CornerRadius": target.CornerRadius = (float)(object)value; break;
            case "FontSize": target.FontSize = (float)(object)value; break;
            case "TextAlignment": target.TextAlignment = (TextAlignment)(object)value; break;
        }
        
        propertySpecificity[propertyName] = specificity;
    }

    private void TrySetProperty<T>(UIStyle target, UIStyle source, string propertyName, T value, int specificity, Dictionary<string, int> propertySpecificity)
    {
        // Check if this property was already set by a higher specificity rule
        if (propertySpecificity.TryGetValue(propertyName, out var existingSpecificity))
        {
            if (existingSpecificity >= specificity)
                return; // Skip - already set by higher specificity
        }
        
        // Set the property and track its specificity
        switch (propertyName)
        {
            case "Background": target.Background = (Color)(object)value; break;
            case "TextColor": target.TextColor = (Color)(object)value; break;
            case "BorderColor": target.BorderColor = (Color)(object)value; break;
            case "BorderWidth": target.BorderWidth = (float)(object)value; break;
            case "Padding": target.Padding = (Padding)(object)value; break;
            case "Margin": target.Margin = (Margin)(object)value; break;
            case "CornerRadius": target.CornerRadius = (float)(object)value; break;
            case "FontSize": target.FontSize = (float)(object)value; break;
            case "TextAlignment": target.TextAlignment = (TextAlignment)(object)value; break;
        }
        
        propertySpecificity[propertyName] = specificity;
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
        if (style.HasTextColorResource)
        {
            var key = style.TextColorResourceKey;
            style.TextColor = ResolveColor(key, resources, Color.White);
        }
        
        if (style.HasBackgroundResource)
        {
            var key = style.BackgroundResourceKey;
            style.Background = ResolveColor(key, resources, Color.Transparent);
        }
        
        if (style.HasBorderColorResource)
        {
            var key = style.BorderColorResourceKey;
            style.BorderColor = ResolveColor(key, resources, Color.Transparent);
        }
    }

    private Color ResolveColor(string? key, ResourceDictionary resources, Color fallback)
    {
        if (key == null) return fallback;
        
        if (resources.TryGet(key, out Color color))
        {
            return color;
        }
        
        if (resources.TryGet<string>(key, out var aliasKey) && aliasKey != null && aliasKey.StartsWith("$"))
        {
            var resolvedKey = aliasKey.Substring(1);
            if (resources.TryGet(resolvedKey, out Color resolved))
            {
                return resolved;
            }
        }
        
        return fallback;
    }

    public void ClearCache()
    {
        _styleData.Clear();
    }
}

public class LightTheme
{
    private readonly ResourceDictionary _resources = new();

    public LightTheme()
    {
        Background(Color.White);
        TextColor(Color.Black);
        BorderColor(new Color(255, 200, 200, 200));
        Primary(new Color(255, 0, 122, 204));
        Secondary(new Color(255, 108, 117, 125));
        Success(new Color(255, 40, 167, 69));
        Danger(new Color(255, 220, 53, 69));
        Warning(new Color(255, 255, 193, 7));
        Info(new Color(255, 23, 162, 184));
        Surface(new Color(255, 248, 249, 250));
        SurfaceHover(new Color(255, 233, 236, 239));
        SurfaceActive(new Color(255, 222, 226, 230));
    }

    public LightTheme Background(Color color) { _resources.Add("Background", color); return this; }
    public LightTheme TextColor(Color color) { _resources.Add("TextColor", color); return this; }
    public LightTheme BorderColor(Color color) { _resources.Add("BorderColor", color); return this; }
    public LightTheme Primary(Color color) { _resources.Add("Primary", color); return this; }
    public LightTheme Secondary(Color color) { _resources.Add("Secondary", color); return this; }
    public LightTheme Success(Color color) { _resources.Add("Success", color); return this; }
    public LightTheme Danger(Color color) { _resources.Add("Danger", color); return this; }
    public LightTheme Warning(Color color) { _resources.Add("Warning", color); return this; }
    public LightTheme Info(Color color) { _resources.Add("Info", color); return this; }
    public LightTheme Surface(Color color) { _resources.Add("Surface", color); return this; }
    public LightTheme SurfaceHover(Color color) { _resources.Add("SurfaceHover", color); return this; }
    public LightTheme SurfaceActive(Color color) { _resources.Add("SurfaceActive", color); return this; }

    public LightTheme Add(string key, string value) { _resources.Add(key, value); return this; }

    public ResourceDictionary Build()
    {
        var result = new ResourceDictionary();
        foreach (var kvp in _resources.Resources)
        {
            result.Add(kvp.Key, kvp.Value);
        }
        return result;
    }
}

public class DarkTheme
{
    private readonly ResourceDictionary _resources = new();

    public DarkTheme()
    {
        Background(new Color(255, 30, 30, 30));
        TextColor(new Color(255, 233, 236, 239));
        BorderColor(new Color(255, 90, 90, 90));
        Primary(new Color(255, 0, 153, 255));
        Secondary(new Color(255, 173, 181, 189));
        Success(new Color(255, 72, 199, 103));
        Danger(new Color(255, 255, 92, 107));
        Warning(new Color(255, 255, 193, 7));
        Info(new Color(255, 57, 211, 238));
        Surface(new Color(255, 45, 45, 45));
        SurfaceHover(new Color(255, 60, 60, 60));
        SurfaceActive(new Color(255, 75, 75, 75));
    }

    public DarkTheme Background(Color color) { _resources.Add("Background", color); return this; }
    public DarkTheme TextColor(Color color) { _resources.Add("TextColor", color); return this; }
    public DarkTheme BorderColor(Color color) { _resources.Add("BorderColor", color); return this; }
    public DarkTheme Primary(Color color) { _resources.Add("Primary", color); return this; }
    public DarkTheme Secondary(Color color) { _resources.Add("Secondary", color); return this; }
    public DarkTheme Success(Color color) { _resources.Add("Success", color); return this; }
    public DarkTheme Danger(Color color) { _resources.Add("Danger", color); return this; }
    public DarkTheme Warning(Color color) { _resources.Add("Warning", color); return this; }
    public DarkTheme Info(Color color) { _resources.Add("Info", color); return this; }
    public DarkTheme Surface(Color color) { _resources.Add("Surface", color); return this; }
    public DarkTheme SurfaceHover(Color color) { _resources.Add("SurfaceHover", color); return this; }
    public DarkTheme SurfaceActive(Color color) { _resources.Add("SurfaceActive", color); return this; }

    public DarkTheme Add(string key, string value) { _resources.Add(key, value); return this; }

    public ResourceDictionary Build()
    {
        var result = new ResourceDictionary();
        foreach (var kvp in _resources.Resources)
        {
            result.Add(kvp.Key, kvp.Value);
        }
        return result;
    }
}
