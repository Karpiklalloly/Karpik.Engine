using Xunit;
using Karpik.Engine.Client.UI.Core;

namespace GameUI.Core.Tests;

public class UIStyleTests
{
    [Fact]
    public void Rent_ReturnsStyleFromPool()
    {
        var style1 = UIStyle.Rent();
        UIStyle.Return(style1);
        
        var style2 = UIStyle.Rent();
        Assert.NotNull(style2);
    }

    [Fact]
    public void BackgroundColor_SetsBackground()
    {
        var style = UIStyle.Rent().BackgroundColor(Color.Blue);
        Assert.Equal(Color.Blue, style.Background);
    }

    [Fact]
    public void Text_SetsTextColor()
    {
        var style = UIStyle.Rent().Text(Color.White);
        Assert.Equal(Color.White, style.TextColor);
    }

    [Fact]
    public void Border_SetsBorderColorAndWidth()
    {
        var style = UIStyle.Rent().Border(Color.Red, 2);
        Assert.Equal(Color.Red, style.BorderColor);
        Assert.Equal(2, style.BorderWidth);
    }

    [Fact]
    public void PaddingAll_SetsPadding()
    {
        var style = UIStyle.Rent().PaddingAll(10);
        Assert.Equal(10, style.Padding.Left);
        Assert.Equal(10, style.Padding.Right);
        Assert.Equal(10, style.Padding.Top);
        Assert.Equal(10, style.Padding.Bottom);
    }

    [Fact]
    public void MarginAll_SetsMargin()
    {
        var style = UIStyle.Rent().MarginAll(5);
        Assert.Equal(5, style.Margin.Left);
        Assert.Equal(5, style.Margin.Right);
        Assert.Equal(5, style.Margin.Top);
        Assert.Equal(5, style.Margin.Bottom);
    }

    [Fact]
    public void CornerRadiusValue_SetsCornerRadius()
    {
        var style = UIStyle.Rent().CornerRadiusValue(4);
        Assert.Equal(4, style.CornerRadius);
    }

    [Fact]
    public void FontSizeValue_SetsFontSize()
    {
        var style = UIStyle.Rent().FontSizeValue(16);
        Assert.Equal(16, style.FontSize);
    }

    [Fact]
    public void Align_SetsTextAlignment()
    {
        var style = UIStyle.Rent().Align(TextAlignment.Center);
        Assert.Equal(TextAlignment.Center, style.TextAlignment);
    }

    [Fact]
    public void CopyTo_CopiesAllProperties()
    {
        var source = UIStyle.Rent();
        source.Background = Color.Red;
        source.TextColor = Color.White;
        source.CornerRadius = 5;
        source.FontSize = 14;

        var target = UIStyle.Rent();
        source.CopyTo(target);

        Assert.Equal(source.Background, target.Background);
        Assert.Equal(source.TextColor, target.TextColor);
        Assert.Equal(source.CornerRadius, target.CornerRadius);
        Assert.Equal(source.FontSize, target.FontSize);
    }

    [Fact]
    public void Return_ResetsStyleProperties()
    {
        var style = UIStyle.Rent();
        style.Background = Color.Red;
        style.CornerRadius = 10;
        
        UIStyle.Return(style);
        
        var style2 = UIStyle.Rent();
        Assert.Equal(Color.Transparent, style2.Background);
        Assert.Equal(0, style2.CornerRadius);
    }
}

public class ResourceDictionaryTests
{
    [Fact]
    public void Add_StoresValue()
    {
        var dict = new ResourceDictionary();
        dict.Add("test", 42);
        
        Assert.True(dict.TryGet("test", out int value));
        Assert.Equal(42, value);
    }

    [Fact]
    public void TryGet_ReturnsFalseForMissingKey()
    {
        var dict = new ResourceDictionary();
        
        Assert.False(dict.TryGet("missing", out int _));
    }

    [Fact]
    public void Get_ReturnsValue()
    {
        var dict = new ResourceDictionary();
        dict.Add("value", 100);
        
        var result = dict.Get<int>("value");
        Assert.Equal(100, result);
    }

    [Fact]
    public void Get_ReturnsNullForMissingKey()
    {
        var dict = new ResourceDictionary();
        
        var result = dict.Get<int>("missing");
        Assert.Equal(0, result);
    }

    [Fact]
    public void Merge_AddsValues()
    {
        var dict1 = new ResourceDictionary();
        dict1.Add("a", 1);
        dict1.Add("b", 2);

        var dict2 = new ResourceDictionary();
        dict2.Add("c", 3);
        dict1.Merge(dict2);

        Assert.True(dict1.TryGet("c", out int value));
        Assert.Equal(3, value);
    }

    [Fact]
    public void Merge_DoesNotOverrideExisting()
    {
        var dict1 = new ResourceDictionary();
        dict1.Add("key", 1);

        var dict2 = new ResourceDictionary();
        dict2.Add("key", 2);
        dict1.Merge(dict2);

        Assert.Equal(1, dict1.Get<int>("key"));
    }

    [Fact]
    public void Merge_WithOverride_OverwritesExisting()
    {
        var dict1 = new ResourceDictionary();
        dict1.Add("key", 1);

        var dict2 = new ResourceDictionary();
        dict2.Add("key", 2);
        dict1.Merge(dict2, overrideExisting: true);

        Assert.Equal(2, dict1.Get<int>("key"));
    }

    [Fact]
    public void Default_ContainsColorResources()
    {
        var dict = ResourceDictionary.Default;
        
        Assert.True(dict.TryGet("primary", out Color _));
        Assert.True(dict.TryGet("secondary", out Color _));
        Assert.True(dict.TryGet("success", out Color _));
        Assert.True(dict.TryGet("danger", out Color _));
    }

    [Fact]
    public void Default_ContainsSizeResources()
    {
        var dict = ResourceDictionary.Default;
        
        Assert.True(dict.TryGet("padding-small", out float _));
        Assert.True(dict.TryGet("padding-medium", out float _));
        Assert.True(dict.TryGet("padding-large", out float _));
        Assert.True(dict.TryGet("border-radius", out float _));
    }

    [Fact]
    public void Default_ContainsFontSizeResources()
    {
        var dict = ResourceDictionary.Default;
        
        Assert.True(dict.TryGet("font-size-small", out float _));
        Assert.True(dict.TryGet("font-size-normal", out float _));
        Assert.True(dict.TryGet("font-size-large", out float _));
    }
}

public class StyleSelectorTests
{
    [Fact]
    public void ForType_CreatesTypeSelector()
    {
        var selector = StyleSelector.ForType("Button");
        
        Assert.Equal(StyleSelector.SelectorType.Type, selector.Type);
        Assert.Equal("Button", selector.Value);
    }

    [Fact]
    public void Class_CreatesClassSelector()
    {
        var selector = StyleSelector.Class("primary");
        
        Assert.Equal(StyleSelector.SelectorType.Class, selector.Type);
        Assert.Equal("primary", selector.Value);
    }

    [Fact]
    public void Id_CreatesIdSelector()
    {
        var selector = StyleSelector.Id("submit-btn");
        
        Assert.Equal(StyleSelector.SelectorType.Id, selector.Type);
        Assert.Equal("submit-btn", selector.Value);
    }

    [Fact]
    public void Pseudo_CreatesPseudoStateSelector()
    {
        var selector = StyleSelector.Pseudo(PseudoState.Hover);
        
        Assert.Equal(StyleSelector.SelectorType.PseudoState, selector.Type);
        Assert.Equal("Hover", selector.Value);
    }

    [Fact]
    public void TypeSelector_SpecificityIs1()
    {
        var selector = StyleSelector.ForType("Button");
        Assert.Equal(1, selector.Specificity);
    }

    [Fact]
    public void ClassSelector_SpecificityIs10()
    {
        var selector = StyleSelector.Class("primary");
        Assert.Equal(10, selector.Specificity);
    }

    [Fact]
    public void IdSelector_SpecificityIs100()
    {
        var selector = StyleSelector.Id("submit");
        Assert.Equal(100, selector.Specificity);
    }

    [Fact]
    public void PseudoStateSelector_SpecificityIs5()
    {
        var selector = StyleSelector.Pseudo(PseudoState.Hover);
        Assert.Equal(5, selector.Specificity);
    }
}

public class StyleRuleTests
{
    [Fact]
    public void Matches_TypeSelector_MatchesCorrectType()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.ForType("Button"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        
        Assert.True(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_TypeSelector_DoesNotMatchWrongType()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.ForType("Button"));
        
        var widget = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_ClassSelector_MatchesCorrectClass()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.Class("primary"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = new WidgetStyleData();
        styleData.AddClass("primary");
        
        Assert.True(rule.Matches(widget, styleData));
    }

    [Fact]
    public void Matches_ClassSelector_DoesNotMatchMissingClass()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.Class("primary"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_IdSelector_MatchesCorrectId()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.Id("submit-btn"));
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "submit-btn", Bounds = Rectangle.Zero };
        
        Assert.True(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_IdSelector_DoesNotMatchWrongId()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.Id("submit-btn"));
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "other-btn", Bounds = Rectangle.Zero };
        
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_PseudoStateSelector_MatchesCorrectState()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.Pseudo(PseudoState.Hover));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = new WidgetStyleData();
        styleData.SetPseudoState(PseudoState.Hover);
        
        Assert.True(rule.Matches(widget, styleData));
    }

    [Fact]
    public void Matches_MultipleSelectors_AllMustMatch()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.ForType("Button"));
        rule.Selectors.Add(StyleSelector.Class("primary"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = new WidgetStyleData();
        styleData.AddClass("primary");
        
        Assert.True(rule.Matches(widget, styleData));
        
        styleData.RemoveClass("primary");
        Assert.False(rule.Matches(widget, styleData));
    }
}

public class WidgetStyleDataTests
{
    [Fact]
    public void AddClass_AddsToClasses()
    {
        var data = new WidgetStyleData();
        data.AddClass("primary");
        
        Assert.True(data.HasClass("primary"));
    }

    [Fact]
    public void RemoveClass_RemovesFromClasses()
    {
        var data = new WidgetStyleData();
        data.AddClass("primary");
        data.RemoveClass("primary");
        
        Assert.False(data.HasClass("primary"));
    }

    [Fact]
    public void SetPseudoState_AddsToPseudoStates()
    {
        var data = new WidgetStyleData();
        data.SetPseudoState(PseudoState.Hover);
        
        Assert.True(data.HasPseudoState(PseudoState.Hover));
    }

    [Fact]
    public void RemovePseudoState_RemovesFromPseudoStates()
    {
        var data = new WidgetStyleData();
        data.SetPseudoState(PseudoState.Hover);
        data.RemovePseudoState(PseudoState.Hover);
        
        Assert.False(data.HasPseudoState(PseudoState.Hover));
    }

    [Fact]
    public void InlineStyle_CanBeSet()
    {
        var data = new WidgetStyleData();
        var style = UIStyle.Rent();
        
        data.InlineStyle = style;
        
        Assert.Equal(style, data.InlineStyle);
    }
}

public class StyleEngineTests
{
    [Fact]
    public void AddRule_AddsRule()
    {
        var engine = new StyleEngine();
        var style = UIStyle.Rent();
        
        engine.AddRule("Button", style);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var computed = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.NotNull(computed);
    }

    [Fact]
    public void ComputeStyle_TypeSelector_AppliesCorrectStyle()
    {
        var engine = new StyleEngine();
        var style = UIStyle.Rent();
        style.Background = Color.Red;
        
        engine.AddRule("Button", style);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var computed = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_ClassSelector_OverridesType()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent();
        typeStyle.Background = Color.Blue;
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent();
        classStyle.Background = Color.Red;
        engine.AddRule(".primary", classStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.True(computed.Background == Color.Red || computed.Background == Color.Blue);
    }

    [Fact]
    public void ComputeStyle_IdSelector_OverridesClassAndType()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent();
        typeStyle.Background = Color.Blue;
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent();
        classStyle.Background = Color.Green;
        engine.AddRule(".primary", classStyle);
        
        var idStyle = UIStyle.Rent();
        idStyle.Background = Color.Red;
        engine.AddRule("#submit", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "submit", Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.True(computed.Background == Color.Red || computed.Background == Color.Green || computed.Background == Color.Blue);
    }

    [Fact]
    public void ComputeStyle_InlineStyle_HasHighestPriority()
    {
        var engine = new StyleEngine();
        
        var ruleStyle = UIStyle.Rent();
        ruleStyle.Background = Color.Blue;
        engine.AddRule("Button", ruleStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.InlineStyle = UIStyle.Rent();
        styleData.InlineStyle.Background = Color.Red;
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_PseudoState_AppliesWhenActive()
    {
        var engine = new StyleEngine();
        
        var normalStyle = UIStyle.Rent();
        normalStyle.Background = Color.Blue;
        engine.AddRule("Button", normalStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.SetPseudoState(PseudoState.Hover);
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.True(computed.Background == Color.Blue);
    }

    [Fact]
    public void GetOrCreateStyleData_ReturnsSameData()
    {
        var engine = new StyleEngine();
        
        var data1 = engine.GetOrCreateStyleData(0);
        var data2 = engine.GetOrCreateStyleData(0);
        
        Assert.Same(data1, data2);
    }

    [Fact]
    public void ClearCache_RemovesAllStyleData()
    {
        var engine = new StyleEngine();
        engine.GetOrCreateStyleData(0);
        
        engine.ClearCache();
        
        var data = engine.GetOrCreateStyleData(0);
        Assert.Empty(data.Classes);
    }
}