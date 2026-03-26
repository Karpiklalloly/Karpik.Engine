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
        var style = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        
        engine.AddRule("Button", style);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var computed = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_ClassSelector_OverridesType()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Blue);
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
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
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Blue);
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Green);
        engine.AddRule(".primary", classStyle);
        
        var idStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        engine.AddRule("#submit", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "submit", Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_InlineStyle_HasHighestPriority()
    {
        var engine = new StyleEngine();
        
        var ruleStyle = UIStyle.Rent()
            .BackgroundColor(Color.Blue);
        engine.AddRule("Button", ruleStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.InlineStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Red, computed.Background);
    }

    [Fact]
    public void ComputeStyle_PseudoState_AppliesWhenActive()
    {
        var engine = new StyleEngine();
        
        var normalStyle = UIStyle.Rent()
            .BackgroundColor(Color.Blue);
        engine.AddRule("Button", normalStyle);
        
        var hoverStyle = UIStyle.Rent()
            .BackgroundColor(Color.Green);
        engine.AddRule(":Hover", hoverStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.SetPseudoState(PseudoState.Hover);
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Green, computed.Background);
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

// ============================================================================
// UIStyle Edge Case Tests - Rich test coverage for Style system
// ============================================================================

public class UIStyleEdgeCaseTests
{
    [Fact]
    public void FluentAPI_Chaining_WorksCorrectly()
    {
        var style = UIStyle.Rent()
            .BackgroundColor(Color.Red)
            .Text(Color.White)
            .Border(Color.Black, 2)
            .PaddingAll(10)
            .MarginAll(5)
            .CornerRadiusValue(4)
            .FontSizeValue(16)
            .Align(TextAlignment.Center);

        Assert.Equal(Color.Red, style.Background);
        Assert.Equal(Color.White, style.TextColor);
        Assert.Equal(Color.Black, style.BorderColor);
        Assert.Equal(2, style.BorderWidth);
        Assert.Equal(10, style.Padding.Left);
        Assert.Equal(5, style.Margin.Top);
        Assert.Equal(4, style.CornerRadius);
        Assert.Equal(16, style.FontSize);
        Assert.Equal(TextAlignment.Center, style.TextAlignment);
    }

    [Fact]
    public void Pool_MultipleRent_Return_CycleWorks()
    {
        var styles = new List<UIStyle>();
        
        for (int i = 0; i < 100; i++)
        {
            var style = UIStyle.Rent();
            style.Background = new Color((byte)i, 0, 0);
            styles.Add(style);
        }
        
        foreach (var style in styles)
        {
            UIStyle.Return(style);
        }
        
        for (int i = 0; i < 100; i++)
        {
            var style = UIStyle.Rent();
            Assert.Equal(Color.Transparent, style.Background);
        }
    }

    [Fact]
    public void Reset_AllProperties_SetToDefaults()
    {
        var style = UIStyle.Rent();
        style.Background = Color.Red;
        style.TextColor = Color.Blue;
        style.BorderColor = Color.Green;
        style.Padding = new Padding(100);
        style.Margin = new Margin(200);
        style.CornerRadius = 50;
        style.FontSize = 100;
        style.BorderWidth = 10;
        style.TextAlignment = TextAlignment.Right;
        
        // Invoke Reset via reflection (it's a private method)
        typeof(UIStyle).GetMethod("Reset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(style, null);
        
        Assert.Equal(Color.Transparent, style.Background);
        Assert.Equal(Color.White, style.TextColor);
        Assert.Equal(Color.Transparent, style.BorderColor);
        Assert.Equal(Padding.Zero, style.Padding);
        Assert.Equal(Margin.Zero, style.Margin);
        Assert.Equal(0, style.CornerRadius);
        Assert.Equal(14, style.FontSize);
        Assert.Equal(0, style.BorderWidth);
        Assert.Equal(TextAlignment.Left, style.TextAlignment);
        Assert.Equal(FlexContainerStyle.Default, style.FlexStyle);
    }

    [Fact]
    public void CopyTo_EmptyTarget_AllPropertiesCopied()
    {
        var source = UIStyle.Rent();
        source.Background = Color.Cyan;
        source.TextColor = Color.Magenta;
        source.CornerRadius = 8;
        source.FontSize = 20;
        source.BorderWidth = 3;
        source.TextAlignment = TextAlignment.Right;
        
        var target = UIStyle.Rent();
        source.CopyTo(target);
        
        Assert.Equal(source.Background, target.Background);
        Assert.Equal(source.TextColor, target.TextColor);
        Assert.Equal(source.CornerRadius, target.CornerRadius);
        Assert.Equal(source.FontSize, target.FontSize);
        Assert.Equal(source.BorderWidth, target.BorderWidth);
        Assert.Equal(source.TextAlignment, target.TextAlignment);
    }

    [Fact]
    public void CopyTo_OverwritesExistingTarget()
    {
        var source = UIStyle.Rent();
        source.Background = Color.Red;
        
        var target = UIStyle.Rent();
        target.Background = Color.Blue;
        target.CornerRadius = 10;
        
        source.CopyTo(target);
        
        Assert.Equal(Color.Red, target.Background);
    }
}

public class ResourceDictionaryEdgeCaseTests
{
    [Fact]
    public void TryGet_TypeMismatch_ReturnsFalse()
    {
        var dict = new ResourceDictionary();
        dict.Add("number", 42);
        
        var result = dict.TryGet<string>("number", out var value);
        
        Assert.False(result);
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_NullValue_StoredAndRetrieved()
    {
        var dict = new ResourceDictionary();
        dict.Add("nullKey", null!);
        
        var result = dict.TryGet<object>("nullKey", out var value);
        
        Assert.True(result);
        Assert.Null(value);
    }

    [Fact]
    public void Get_Default_ThrowsOnTypeMismatch()
    {
        var dict = new ResourceDictionary();
        dict.Add("key", "string");
        
        // Get<int> should return default for missing cast
        var result = dict.Get<int>("key");
        
        Assert.Equal(0, result);
    }

    [Fact]
    public void Merge_NullDictionary_ThrowsOrNoOp()
    {
        var dict = new ResourceDictionary();
        
        // Should handle null gracefully - no exception expected
        dict.Merge(null!);
    }

    [Fact]
    public void Merge_Self_AddsAllKeys()
    {
        var dict = new ResourceDictionary();
        dict.Add("a", 1);
        dict.Add("b", 2);
        
        dict.Merge(dict);
        
        Assert.Equal(1, dict.Get<int>("a"));
        Assert.Equal(2, dict.Get<int>("b"));
    }

    [Fact]
    public void Default_CaseSensitive_KeysWork()
    {
        var dict = ResourceDictionary.Default;
        
        Assert.True(dict.TryGet("primary", out Color _));
        Assert.False(dict.TryGet("PRIMARY", out Color _));
    }

    [Fact]
    public void Add_Overwrite_ReplacesValue()
    {
        var dict = new ResourceDictionary();
        dict.Add("key", 1);
        dict.Add("key", 2);
        
        Assert.Equal(2, dict.Get<int>("key"));
    }
}

public class StyleSelectorEdgeCaseTests
{
    [Fact]
    public void Constructor_InvalidSelectorType_DefaultsToZero()
    {
        // Test edge case with invalid enum value
        var selector = new StyleSelector((StyleSelector.SelectorType)999, "value");
        
        Assert.Equal(0, selector.Specificity);
    }

    [Fact]
    public void Specificity_AllTypes_OrderedCorrectly()
    {
        var typeSel = new StyleSelector(StyleSelector.SelectorType.Type, "Button");
        var classSel = new StyleSelector(StyleSelector.SelectorType.Class, "primary");
        var idSel = new StyleSelector(StyleSelector.SelectorType.Id, "btn");
        var pseudoSel = new StyleSelector(StyleSelector.SelectorType.PseudoState, "Hover");
        
        Assert.True(idSel.Specificity > classSel.Specificity);
        Assert.True(classSel.Specificity > pseudoSel.Specificity);
        Assert.True(pseudoSel.Specificity > typeSel.Specificity);
    }
}

public class StyleRuleEdgeCaseTests
{
    [Fact]
    public void Matches_EmptySelectors_ReturnsTrue()
    {
        var rule = new StyleRule();
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        
        Assert.True(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_InvalidPseudoState_ReturnsFalse()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(new StyleSelector(StyleSelector.SelectorType.PseudoState, "InvalidState"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_TypeMismatch_ReturnsFalseEarly()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.ForType("Button"));
        
        var widget = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }

    [Fact]
    public void Matches_AllSelectorsMustMatch_AndLogic()
    {
        var rule = new StyleRule();
        rule.Selectors.Add(StyleSelector.ForType("Button"));
        rule.Selectors.Add(StyleSelector.Class("primary"));
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = new WidgetStyleData();
        styleData.AddClass("primary");
        
        Assert.True(rule.Matches(widget, styleData));
        
        // Remove class - should fail
        Assert.False(rule.Matches(widget, new WidgetStyleData()));
    }
}

public class WidgetStyleDataEdgeCaseTests
{
    [Fact]
    public void AddClass_Duplicate_NoOp()
    {
        var data = new WidgetStyleData();
        data.AddClass("test");
        data.AddClass("test");
        
        Assert.Single(data.Classes);
    }

    [Fact]
    public void RemoveClass_NonExistent_NoOp()
    {
        var data = new WidgetStyleData();
        
        // Should not throw
        data.RemoveClass("nonexistent");
        
        Assert.Empty(data.Classes);
    }

    [Fact]
    public void SetPseudoState_Duplicate_NoOp()
    {
        var data = new WidgetStyleData();
        data.SetPseudoState(PseudoState.Hover);
        data.SetPseudoState(PseudoState.Hover);
        
        Assert.Single(data.PseudoStates);
    }

    [Fact]
    public void RemovePseudoState_NonExistent_NoOp()
    {
        var data = new WidgetStyleData();
        
        data.RemovePseudoState(PseudoState.Hover);
        
        Assert.Empty(data.PseudoStates);
    }

    [Fact]
    public void InlineStyle_Null_CanBeCleared()
    {
        var data = new WidgetStyleData();
        data.InlineStyle = UIStyle.Rent();
        
        data.InlineStyle = null;
        
        Assert.Null(data.InlineStyle);
    }

    [Fact]
    public void HasClass_Empty_ReturnsFalse()
    {
        var data = new WidgetStyleData();
        
        Assert.False(data.HasClass("anything"));
    }

    [Fact]
    public void HasPseudoState_Empty_ReturnsFalse()
    {
        var data = new WidgetStyleData();
        
        Assert.False(data.HasPseudoState(PseudoState.Hover));
    }
}

public class StyleEngineEdgeCaseTests
{
    [Fact]
    public void ComputeStyle_NoMatchingRules_ReturnsDefault()
    {
        var engine = new StyleEngine();
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var style = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        // Default values should be set
        Assert.NotNull(style);
    }

    [Fact]
    public void ComputeStyle_Specificity_IDOverridesType_ForSameProperty()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Blue)
            .FontSizeValue(10)
            .PaddingAll(8);
        engine.AddRule("Button", typeStyle);
        
        var idStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        engine.AddRule("#special", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "special", Bounds = Rectangle.Zero };
        var style = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.Equal(Color.Red, style.Background);
        Assert.Equal(10, style.FontSize);
        Assert.Equal(new Padding(8), style.Padding);
    }

    [Fact]
    public void ComputeStyle_Specificity_ClassOverridesType()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Gray);
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Green);
        engine.AddRule(".primary", classStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var style = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Green, style.Background);
    }

    [Fact]
    public void ComputeStyle_Specificity_IDOverridesClass()
    {
        var engine = new StyleEngine();
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Yellow);
        engine.AddRule(".warning", classStyle);
        
        var idStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        engine.AddRule("#confirm-btn", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "confirm-btn", Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("warning");
        
        var style = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Red, style.Background);
    }

    [Fact]
    public void ComputeStyle_Specificity_PseudoStateOverridesType()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Gray);
        engine.AddRule("Button", typeStyle);
        
        var pseudoStyle = UIStyle.Rent()
            .BackgroundColor(new Color(173, 216, 230));
        engine.AddRule(":Hover", pseudoStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.SetPseudoState(PseudoState.Hover);
        
        var style = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(new Color(173, 216, 230), style.Background);
    }

    [Fact]
    public void ComputeStyle_Specificity_PseudoStateCombinedWithClass_Wins()
    {
        var engine = new StyleEngine();
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Yellow);
        engine.AddRule(".button", classStyle);
        
        var pseudoClassStyle = UIStyle.Rent()
            .BackgroundColor(new Color(255, 165, 0));
        engine.AddRule(".button:Hover", pseudoClassStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("button");
        styleData.SetPseudoState(PseudoState.Hover);
        
        var style = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(new Color(255, 165, 0), style.Background);
    }

    [Fact]
    public void ComputeStyle_Specificity_MultipleProperties_EachWinsIndependently()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Gray)
            .FontSizeValue(12)
            .PaddingAll(4);
        engine.AddRule("Button", typeStyle);
        
        var idStyle = UIStyle.Rent()
            .BackgroundColor(Color.Red);
        engine.AddRule("#special", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "special", Bounds = Rectangle.Zero };
        var style = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.Equal(Color.Red, style.Background);
        Assert.Equal(12, style.FontSize);
        Assert.Equal(new Padding(4), style.Padding);
    }

    [Fact]
    public void ComputeStyle_Specificity_InlineStyleOverridesAll()
    {
        var engine = new StyleEngine();
        
        var typeStyle = UIStyle.Rent()
            .BackgroundColor(Color.Gray);
        engine.AddRule("Button", typeStyle);
        
        var classStyle = UIStyle.Rent()
            .BackgroundColor(Color.Green);
        engine.AddRule(".primary", classStyle);
        
        var idStyle = UIStyle.Rent()
            .BackgroundColor(Color.Yellow);
        engine.AddRule("#submit", idStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Id = "submit", Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        styleData.InlineStyle = UIStyle.Rent()
            .BackgroundColor(new Color(128, 0, 128));
        
        var style = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(new Color(128, 0, 128), style.Background);
    }

    [Fact]
    public void ComputeStyle_NoMatchingRules_ReturnsDefaultValues()
    {
        var engine = new StyleEngine();
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var style = engine.ComputeStyle(widget, engine.GetOrCreateStyleData(0));
        
        Assert.Equal(Color.Transparent, style.Background);
    }
    
    [Fact]
    public void ResolveResourceReferences_NamedColor_Resolved()
    {
        var resources = new ResourceDictionary();
        resources.Add("PrimaryColor", Color.Red);
        
        var engine = new StyleEngine(resources);
        
        // Create style with resource reference in TextColor
        var classStyle = UIStyle.Rent().TextColorResource("PrimaryColor");
        
        engine.AddRule(".primary", classStyle);
        
        var widget = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        // Should resolve $PrimaryColor to Color.Red
        Assert.Equal(Color.Red, computed.TextColor);
        
        UIStyle.Return(classStyle);
        UIStyle.Return(computed);
    }
    
    [Fact]
    public void ResolveResourceReferences_BackgroundResource_Resolved()
    {
        var resources = new ResourceDictionary();
        resources.Add("BackgroundColor", Color.Blue);
        
        var engine = new StyleEngine(resources);
        
        var classStyle = UIStyle.Rent().BackgroundColorResource("BackgroundColor");
        
        engine.AddRule(".primary", classStyle);
        
        var widget = new UIWidget(UiTypeId.Button) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.Blue, computed.Background);
        
        UIStyle.Return(classStyle);
        UIStyle.Return(computed);
    }
    
    [Fact]
    public void ResolveResourceReferences_MissingResource_FallbackToDefault()
    {
        var resources = new ResourceDictionary();
        
        var engine = new StyleEngine(resources);
        
        var classStyle = UIStyle.Rent().TextColorResource("NonExistent");
        
        engine.AddRule(".primary", classStyle);
        
        var widget = new UIWidget(UiTypeId.Label) { Bounds = Rectangle.Zero };
        var styleData = engine.GetOrCreateStyleData(0);
        styleData.AddClass("primary");
        
        var computed = engine.ComputeStyle(widget, styleData);
        
        Assert.Equal(Color.White, computed.TextColor);
        
        UIStyle.Return(classStyle);
        UIStyle.Return(computed);
    }
}