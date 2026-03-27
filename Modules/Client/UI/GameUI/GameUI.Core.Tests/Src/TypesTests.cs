using Xunit;
using Karpik.Engine.Client.UI.Core;
using Vector2 = System.Numerics.Vector2;

namespace GameUI.Core.Tests;

public class RectangleTests
{
    [Fact]
    public void Create_WithParameters_SetsValues()
    {
        var rect = new Rectangle(10, 20, 100, 50);

        Assert.Equal(10, rect.X);
        Assert.Equal(20, rect.Y);
        Assert.Equal(100, rect.Width);
        Assert.Equal(50, rect.Height);
    }

    [Fact]
    public void LeftRightTopBottom_ReturnsCorrectEdges()
    {
        var rect = new Rectangle(10, 20, 100, 50);

        Assert.Equal(10, rect.Left);
        Assert.Equal(110, rect.Right);
        Assert.Equal(20, rect.Top);
        Assert.Equal(70, rect.Bottom);
    }

    [Fact]
    public void Center_ReturnsCorrectCenter()
    {
        var rect = new Rectangle(10, 20, 100, 50);

        Assert.Equal(60, rect.Center.X);
        Assert.Equal(45, rect.Center.Y);
    }

    [Fact]
    public void Contains_PointInside_ReturnsTrue()
    {
        var rect = new Rectangle(0, 0, 100, 100);

        Assert.True(rect.Contains(new Vector2(50, 50)));
        Assert.True(rect.Contains(new Vector2(0, 0)));
        Assert.True(rect.Contains(new Vector2(100, 100)));
    }

    [Fact]
    public void Contains_PointOutside_ReturnsFalse()
    {
        var rect = new Rectangle(0, 0, 100, 100);

        Assert.False(rect.Contains(new Vector2(-1, 50)));
        Assert.False(rect.Contains(new Vector2(50, 101)));
    }

    [Fact]
    public void Intersects_OverlappingRect_ReturnsTrue()
    {
        var rect1 = new Rectangle(0, 0, 100, 100);
        var rect2 = new Rectangle(50, 50, 100, 100);

        Assert.True(rect1.Intersects(rect2));
    }

    [Fact]
    public void Intersects_NonOverlappingRect_ReturnsFalse()
    {
        var rect1 = new Rectangle(0, 0, 50, 50);
        var rect2 = new Rectangle(100, 100, 50, 50);

        Assert.False(rect1.Intersects(rect2));
    }

    [Fact]
    public void Inflate_ExpandsRectangle()
    {
        var rect = new Rectangle(10, 10, 80, 80);
        var inflated = rect.Inflate(5);

        Assert.Equal(5, inflated.X);
        Assert.Equal(5, inflated.Y);
        Assert.Equal(90, inflated.Width);
        Assert.Equal(90, inflated.Height);
    }

    [Fact]
    public void Zero_ReturnsZeroRectangle()
    {
        var rect = Rectangle.Zero;

        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }
}

public class Vector2Tests
{
    [Fact]
    public void Create_SetsValues()
    {
        var v = new Vector2(3, 4);

        Assert.Equal(3, v.X);
        Assert.Equal(4, v.Y);
    }

    [Fact]
    public void Zero_ReturnsZeroVector()
    {
        var v = Vector2.Zero;

        Assert.Equal(0, v.X);
        Assert.Equal(0, v.Y);
    }

    [Fact]
    public void Operator_Add_AddsVectors()
    {
        var v1 = new Vector2(1, 2);
        var v2 = new Vector2(3, 4);

        var result = v1 + v2;

        Assert.Equal(4, result.X);
        Assert.Equal(6, result.Y);
    }

    [Fact]
    public void Operator_Subtract_SubtractsVectors()
    {
        var v1 = new Vector2(5, 7);
        var v2 = new Vector2(2, 3);

        var result = v1 - v2;

        Assert.Equal(3, result.X);
        Assert.Equal(4, result.Y);
    }

    [Fact]
    public void Operator_Multiply_ScalesVector()
    {
        var v = new Vector2(2, 3);

        var result = v * 2;

        Assert.Equal(4, result.X);
        Assert.Equal(6, result.Y);
    }

    [Fact]
    public void Operator_Equality_SameValues_ReturnsTrue()
    {
        var v1 = new Vector2(1, 2);
        var v2 = new Vector2(1, 2);

        Assert.True(v1 == v2);
    }
}

public class ColorTests
{
    [Fact]
    public void Create_SetsARGB()
    {
        var color = new Color(255, 128, 64, 32);

        Assert.Equal(255, color.A);
        Assert.Equal(128, color.R);
        Assert.Equal(64, color.G);
        Assert.Equal(32, color.B);
    }

    [Fact]
    public void FromHex_6Digits_CreatesCorrectColor()
    {
        var color = Color.FromHex("#FF8040");

        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(64, color.B);
    }

    [Fact]
    public void FromHex_8Digits_CreatesCorrectColor()
    {
        var color = Color.FromHex("#80FF8040");

        Assert.Equal(128, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(128, color.G);
        Assert.Equal(64, color.B);
    }

    [Fact]
    public void ToArgb_ReturnsPackedValue()
    {
        var color = new Color(255, 128, 64, 32);

        var result = color.ToArgb();

        Assert.Equal(unchecked((int)0xFF804020), result);
    }

    [Fact]
    public void White_ReturnsWhite()
    {
        var color = Color.White;

        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
    }

    [Fact]
    public void Black_ReturnsBlack()
    {
        var color = Color.Black;

        Assert.Equal(255, color.A);
        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }
}

public class PaddingMarginTests
{
    [Fact]
    public void Padding_AllConstructor_SetsAllSides()
    {
        var padding = new Padding(10);

        Assert.Equal(10, padding.Left);
        Assert.Equal(10, padding.Top);
        Assert.Equal(10, padding.Right);
        Assert.Equal(10, padding.Bottom);
    }

    [Fact]
    public void Padding_HorizontalVertical_SetsCorrectly()
    {
        var padding = new Padding(5, 10);

        Assert.Equal(5, padding.Left);
        Assert.Equal(5, padding.Right);
        Assert.Equal(10, padding.Top);
        Assert.Equal(10, padding.Bottom);
    }

    [Fact]
    public void Padding_Horizontal_ReturnsSum()
    {
        var padding = new Padding(5, 10, 15, 20);

        Assert.Equal(20, padding.Horizontal);
    }

    [Fact]
    public void Padding_Vertical_ReturnsSum()
    {
        var padding = new Padding(5, 10, 15, 20);

        Assert.Equal(30, padding.Vertical);
    }

    [Fact]
    public void Padding_Zero_ReturnsZeroPadding()
    {
        var padding = Padding.Zero;

        Assert.Equal(0, padding.Left);
        Assert.Equal(0, padding.Right);
        Assert.Equal(0, padding.Top);
        Assert.Equal(0, padding.Bottom);
    }

    [Fact]
    public void Margin_AllConstructor_SetsAllSides()
    {
        var margin = new Margin(10);

        Assert.Equal(10, margin.Left);
        Assert.Equal(10, margin.Top);
        Assert.Equal(10, margin.Right);
        Assert.Equal(10, margin.Bottom);
    }

    [Fact]
    public void Margin_HorizontalVertical_SetsCorrectly()
    {
        var margin = new Margin(5, 10);

        Assert.Equal(5, margin.Left);
        Assert.Equal(5, margin.Right);
        Assert.Equal(10, margin.Top);
        Assert.Equal(10, margin.Bottom);
    }

    [Fact]
    public void Margin_Zero_ReturnsZeroMargin()
    {
        var margin = Margin.Zero;

        Assert.Equal(0, margin.Left);
        Assert.Equal(0, margin.Right);
        Assert.Equal(0, margin.Top);
        Assert.Equal(0, margin.Bottom);
    }
    
    [Fact]
    public void Margin_Equality_WorksCorrectly()
    {
        var m1 = new Margin(1, 2, 3, 4);
        var m2 = new Margin(1, 2, 3, 4);
        var m3 = new Margin(5, 6, 7, 8);

        Assert.True(m1 == m2);
        Assert.False(m1 == m3);
    }
    
    [Fact]
    public void Margin_Inequality_WorksCorrectly()
    {
        var m1 = new Margin(1, 2, 3, 4);
        var m2 = new Margin(5, 6, 7, 8);

        Assert.True(m1 != m2);
    }
}

public class ColorEdgeCaseTests
{
    [Fact]
    public void FromHex_InvalidLength_Throws()
    {
        Assert.Throws<ArgumentException>(() => Color.FromHex("#12"));
    }
    
    [Fact]
    public void FromHex_InvalidChars_Throws()
    {
        Assert.Throws<ArgumentException>(() => Color.FromHex("#GGGGGG"));
    }
    
    [Fact]
    public void FromArgb_RoundTrip_ReturnsSameValue()
    {
        var original = new Color(128, 200, 50, 75);
        
        var packed = original.ToArgb();
        var restored = Color.FromArgb(packed);
        
        Assert.Equal(original.A, restored.A);
        Assert.Equal(original.R, restored.R);
        Assert.Equal(original.G, restored.G);
        Assert.Equal(original.B, restored.B);
    }
    
    [Fact]
    public void FromArgb_Zero_ReturnsTransparent()
    {
        var color = Color.FromArgb(0);
        
        Assert.Equal(0, color.A);
        Assert.Equal(0, color.R);
        Assert.Equal(0, color.G);
        Assert.Equal(0, color.B);
    }
    
    [Fact]
    public void FromArgb_MaxValue_ReturnsMaxColor()
    {
        var color = Color.FromArgb(-1);
        
        Assert.Equal(255, color.A);
        Assert.Equal(255, color.R);
        Assert.Equal(255, color.G);
        Assert.Equal(255, color.B);
    }
    
    [Fact]
    public void Color_Equality_WorksCorrectly()
    {
        var c1 = new Color(255, 128, 64, 32);
        var c2 = new Color(255, 128, 64, 32);
        var c3 = new Color(0, 0, 0, 0);
        
        Assert.True(c1 == c2);
        Assert.True(c1 != c3);
    }
}

public class RectangleEdgeCaseTests
{
    [Fact]
    public void Contains_PointOnEdge_ReturnsTrue()
    {
        var rect = new Rectangle(10, 10, 100, 50);
        
        Assert.True(rect.Contains(new Vector2(10, 10)));
        Assert.True(rect.Contains(new Vector2(110, 60)));
    }
    
    [Fact]
    public void Contains_PointOutside_ReturnsFalse()
    {
        var rect = new Rectangle(10, 10, 100, 50);
        
        Assert.False(rect.Contains(new Vector2(9, 10)));
        Assert.False(rect.Contains(new Vector2(111, 60)));
    }
    
    [Fact]
    public void Contains_NegativeCoordinates_Works()
    {
        var rect = new Rectangle(-100, -50, 100, 50);
        
        Assert.True(rect.Contains(new Vector2(-50, -25)));
        Assert.False(rect.Contains(new Vector2(-101, -25)));
    }
    
    [Fact]
    public void Intersects_Overlapping_ReturnsTrue()
    {
        var rect1 = new Rectangle(0, 0, 100, 100);
        var rect2 = new Rectangle(50, 50, 100, 100);
        
        Assert.True(rect1.Intersects(rect2));
    }
    
    [Fact]
    public void Intersects_NoOverlap_ReturnsFalse()
    {
        var rect1 = new Rectangle(0, 0, 100, 100);
        var rect2 = new Rectangle(200, 200, 50, 50);
        
        Assert.False(rect1.Intersects(rect2));
    }
    
    [Fact]
    public void Intersects_Adjacent_ReturnsFalse()
    {
        var rect1 = new Rectangle(0, 0, 100, 100);
        var rect2 = new Rectangle(100, 0, 50, 50);
        
        Assert.False(rect1.Intersects(rect2));
    }
    
    [Fact]
    public void Inflate_BothDirections_ExpandsEqually()
    {
        var rect = new Rectangle(100, 100, 200, 100);
        var inflated = rect.Inflate(10);
        
        Assert.Equal(90, inflated.X);
        Assert.Equal(90, inflated.Y);
        Assert.Equal(220, inflated.Width);
        Assert.Equal(120, inflated.Height);
    }
    
    [Fact]
    public void Inflate_HorizontalVertical_DifferentValues()
    {
        var rect = new Rectangle(100, 100, 200, 100);
        var inflated = rect.Inflate(20, 10);
        
        Assert.Equal(80, inflated.X);
        Assert.Equal(90, inflated.Y);
        Assert.Equal(240, inflated.Width);
        Assert.Equal(120, inflated.Height);
    }
    
    [Fact]
    public void Zero_ReturnsZeroRectangle()
    {
        var rect = Rectangle.Zero;
        
        Assert.Equal(0, rect.X);
        Assert.Equal(0, rect.Y);
        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }
}

public class SizeTests
{
    [Fact]
    public void Create_SetsWidthAndHeight()
    {
        var size = new Size(100, 50);

        Assert.Equal(100, size.Width);
        Assert.Equal(50, size.Height);
    }

    [Fact]
    public void Create_Both_SetsBoth()
    {
        var size = new Size(10);

        Assert.Equal(10, size.Width);
        Assert.Equal(10, size.Height);
    }

    [Fact]
    public void Zero_ReturnsZeroSize()
    {
        var size = Size.Zero;

        Assert.Equal(0, size.Width);
        Assert.Equal(0, size.Height);
    }

    [Fact]
    public void Auto_ReturnsNegativeSize()
    {
        var size = Size.Auto;

        Assert.True(size.IsAuto);
    }

    [Fact]
    public void IsAuto_ForPositiveSize_ReturnsFalse()
    {
        var size = new Size(100, 50);

        Assert.False(size.IsAuto);
    }
}

public class UIWidgetTests
{
    [Fact]
    public void Create_SetsDefaultValues()
    {
        var widget = new UIWidget(UiTypeId.Button);

        Assert.Equal(UiTypeId.Button, widget.Type);
        Assert.Equal(string.Empty, widget.Id);
        Assert.Equal(Rectangle.Zero, widget.Bounds);
        Assert.Equal(0, widget.ZIndex);
        Assert.Equal(UIWidget.NoParent, widget.ParentIndex);
        Assert.Equal(UIWidget.NoChild, widget.FirstChildIndex);
        Assert.Equal(UIWidget.NoSibling, widget.NextSiblingIndex);
        Assert.Equal(UIWidget.NoSibling, widget.PrevSiblingIndex);
        Assert.Equal(InteractionState.Normal, widget.State);
        Assert.True(widget.IsVisible);
        Assert.True(widget.IsEnabled);
        Assert.False(widget.BubbleEvents);
        Assert.True(widget.IsDirty);
    }

    [Fact]
    public void HasParent_WhenNoParent_ReturnsFalse()
    {
        var widget = new UIWidget(UiTypeId.Button);

        Assert.False(widget.HasParent);
    }

    [Fact]
    public void HasParent_WhenHasParent_ReturnsTrue()
    {
        var widget = new UIWidget(UiTypeId.Button) { ParentIndex = 0 };

        Assert.True(widget.HasParent);
    }

    [Fact]
    public void HasChildren_WhenNoChildren_ReturnsFalse()
    {
        var widget = new UIWidget(UiTypeId.Button);

        Assert.False(widget.HasChildren);
    }

    [Fact]
    public void HasChildren_WhenHasChildren_ReturnsTrue()
    {
        var widget = new UIWidget(UiTypeId.Button) { FirstChildIndex = 1 };

        Assert.True(widget.HasChildren);
    }

    [Fact]
    public void SetPosition_SetsXAndY()
    {
        var widget = new UIWidget(UiTypeId.Button);
        widget.SetPosition(10, 20);

        Assert.Equal(10, widget.Bounds.X);
        Assert.Equal(20, widget.Bounds.Y);
    }

    [Fact]
    public void SetSize_SetsWidthAndHeight()
    {
        var widget = new UIWidget(UiTypeId.Button);
        widget.SetSize(100, 50);

        Assert.Equal(100, widget.Bounds.Width);
        Assert.Equal(50, widget.Bounds.Height);
    }
}

public class EnumsTests
{
    [Fact]
    public void UiTypeId_HasAllExpectedValues()
    {
        Assert.Equal(UiTypeId.None, UiTypeId.None);
        Assert.Equal(UiTypeId.Window, UiTypeId.Window);
        Assert.Equal(UiTypeId.Button, UiTypeId.Button);
        Assert.Equal(UiTypeId.Label, UiTypeId.Label);
        Assert.Equal(UiTypeId.Image, UiTypeId.Image);
    }

    [Fact]
    public void FlexDirection_HasRowAndColumn()
    {
        Assert.Equal(FlexDirection.Row, FlexDirection.Row);
        Assert.Equal(FlexDirection.Column, FlexDirection.Column);
    }

    [Fact]
    public void JustifyContent_HasAllValues()
    {
        Assert.Equal(JustifyContent.Start, JustifyContent.Start);
        Assert.Equal(JustifyContent.Center, JustifyContent.Center);
        Assert.Equal(JustifyContent.End, JustifyContent.End);
        Assert.Equal(JustifyContent.SpaceBetween, JustifyContent.SpaceBetween);
        Assert.Equal(JustifyContent.SpaceAround, JustifyContent.SpaceAround);
        Assert.Equal(JustifyContent.SpaceEvenly, JustifyContent.SpaceEvenly);
    }

    [Fact]
    public void AlignItems_HasAllValues()
    {
        Assert.Equal(AlignItems.Start, AlignItems.Start);
        Assert.Equal(AlignItems.Center, AlignItems.Center);
        Assert.Equal(AlignItems.End, AlignItems.End);
        Assert.Equal(AlignItems.Stretch, AlignItems.Stretch);
    }

    [Fact]
    public void InteractionState_HasAllValues()
    {
        Assert.Equal(InteractionState.Normal, InteractionState.Normal);
        Assert.Equal(InteractionState.Hovered, InteractionState.Hovered);
        Assert.Equal(InteractionState.Pressed, InteractionState.Pressed);
        Assert.Equal(InteractionState.Focused, InteractionState.Focused);
        Assert.Equal(InteractionState.Disabled, InteractionState.Disabled);
    }

    [Fact]
    public void PseudoState_HasAllValues()
    {
        Assert.Equal(PseudoState.None, PseudoState.None);
        Assert.Equal(PseudoState.Hover, PseudoState.Hover);
        Assert.Equal(PseudoState.Active, PseudoState.Active);
        Assert.Equal(PseudoState.Focus, PseudoState.Focus);
        Assert.Equal(PseudoState.Disabled, PseudoState.Disabled);
        Assert.Equal(PseudoState.Checked, PseudoState.Checked);
    }
}

public class WidgetDataTests
{
    [Fact]
    public void ButtonData_DefaultValues_AreCorrect()
    {
        var data = default(ButtonData);

        Assert.Null(data.Text);
        Assert.Equal(Color.Transparent, data.Background);
    }

    [Fact]
    public void LabelData_DefaultValues_AreCorrect()
    {
        var data = default(LabelData);

        Assert.Null(data.Text);
        Assert.Equal(TextAlignment.Left, data.Alignment);
    }

    [Fact]
    public void ImageData_DefaultValues_AreCorrect()
    {
        var data = default(ImageData);

        Assert.Equal(TextureId.Empty, data.Texture);
        Assert.Equal(ImageStretch.None, data.Stretch);
    }

    [Fact]
    public void PanelData_DefaultValues_AreCorrect()
    {
        var data = default(PanelData);

        Assert.Equal(Color.Transparent, data.Background);
        Assert.False(data.ClipChildren);
    }

    [Fact]
    public void SliderData_DefaultValues_AreCorrect()
    {
        var data = default(SliderData);

        Assert.Equal(0, data.Value);
        Assert.Equal(0, data.MinValue);
        Assert.Equal(0, data.MaxValue);
    }

    [Fact]
    public void ProgressBarData_DefaultValues_AreCorrect()
    {
        var data = default(ProgressBarData);

        Assert.Equal(0, data.Value);
        Assert.Equal(0, data.MinValue);
        Assert.Equal(0, data.MaxValue);
    }

    [Fact]
    public void CheckBoxData_DefaultValues_AreCorrect()
    {
        var data = default(CheckBoxData);

        Assert.False(data.IsChecked);
        Assert.Null(data.Text);
    }

    [Fact]
    public void ToggleData_DefaultValues_AreCorrect()
    {
        var data = default(ToggleData);

        Assert.False(data.IsOn);
    }

    [Fact]
    public void ComboBoxData_DefaultValues_AreCorrect()
    {
        var data = default(ComboBoxData);

        Assert.Equal(0, data.SelectedIndex);
        Assert.False(data.IsOpen);
    }

    [Fact]
    public void InputFieldData_DefaultValues_AreCorrect()
    {
        var data = default(InputFieldData);

        Assert.Null(data.Text);
        Assert.Equal(0, data.MaxLength);
        Assert.False(data.IsPassword);
        Assert.False(data.IsReadOnly);
    }
}

public class UIWidgetEdgeCaseTests
{
    [Fact]
    public void SetPosition_UpdatesXAndY()
    {
        var widget = new UIWidget(UiTypeId.Button);
        
        widget.SetPosition(100, 200);
        
        Assert.Equal(100, widget.Bounds.X);
        Assert.Equal(200, widget.Bounds.Y);
    }
    
    [Fact]
    public void SetSize_UpdatesWidthAndHeight()
    {
        var widget = new UIWidget(UiTypeId.Button);
        
        widget.SetSize(300, 150);
        
        Assert.Equal(300, widget.Bounds.Width);
        Assert.Equal(150, widget.Bounds.Height);
    }
    
    [Fact]
    public void HasChildren_TrueWhenHasFirstChild()
    {
        var widget = new UIWidget(UiTypeId.Window);
        widget.FirstChildIndex = 0;
        
        Assert.True(widget.HasChildren);
    }
    
    [Fact]
    public void HasChildren_FalseWhenNoChildren()
    {
        var widget = new UIWidget(UiTypeId.Window);
        
        Assert.False(widget.HasChildren);
    }
    
    [Fact]
    public void HasParent_TrueWhenParentIndexNotNegative()
    {
        var widget = new UIWidget(UiTypeId.Button);
        widget.ParentIndex = 5;
        
        Assert.True(widget.HasParent);
    }
    
    [Fact]
    public void HasParent_FalseWhenNoParent()
    {
        var widget = new UIWidget(UiTypeId.Button);
        widget.ParentIndex = UIWidget.NoParent;
        
        Assert.False(widget.HasParent);
    }
}

public class RectanglePropertyBasedTests
{
    private readonly Random _random = new(12345);

    [Fact]
    public void Contains_RandomPoints_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = _random.Next(-1000, 1000);
            float y = _random.Next(-1000, 1000);
            float w = _random.Next(1, 1000);
            float h = _random.Next(1, 1000);
            
            var rect = new Rectangle(x, y, w, h);
            
            float px = x + _random.Next(0, (int)w);
            float py = y + _random.Next(0, (int)h);
            var point = new Vector2(px, py);
            
            var result = rect.Contains(point);
            Assert.True(result, $"Point ({px},{py}) should be inside rect ({x},{y},{w},{h})");
        }
    }

    [Fact]
    public void Contains_RandomPointsOutside_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = _random.Next(-1000, 1000);
            float y = _random.Next(-1000, 1000);
            float w = _random.Next(1, 1000);
            float h = _random.Next(1, 1000);
            
            var rect = new Rectangle(x, y, w, h);
            
            float px = x + w + 1 + Math.Abs(_random.Next(1, 100));
            float py = y + h + 1 + Math.Abs(_random.Next(1, 100));
            var point = new Vector2(px, py);
            
            var result = rect.Contains(point);
            Assert.False(result, $"Point ({px},{py}) should be outside rect ({x},{y},{w},{h})");
        }
    }

    [Fact]
    public void Intersects_RandomRectangles_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x1 = _random.Next(-1000, 1000);
            float y1 = _random.Next(-1000, 1000);
            float w1 = _random.Next(1, 500);
            float h1 = _random.Next(1, 500);
            
            float x2 = x1 + _random.Next(-200, 200);
            float y2 = y1 + _random.Next(-200, 200);
            float w2 = _random.Next(1, 500);
            float h2 = _random.Next(1, 500);
            
            var rect1 = new Rectangle(x1, y1, w1, h1);
            var rect2 = new Rectangle(x2, y2, w2, h2);
            
            bool intersects = rect1.Intersects(rect2);
            
            bool overlap = !(x1 + w1 <= x2 || x2 + w2 <= x1 || y1 + h1 <= y2 || y2 + h2 <= y1);
            Assert.Equal(overlap, intersects);
        }
    }

    [Fact]
    public void Inflate_RandomValues_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            float x = _random.Next(-500, 500);
            float y = _random.Next(-500, 500);
            float w = _random.Next(1, 500);
            float h = _random.Next(1, 500);
            float dx = _random.Next(-200, 200);
            float dy = _random.Next(-200, 200);
            
            var rect = new Rectangle(x, y, w, h);
            var inflated = rect.Inflate(dx, dy);
            
            Assert.Equal(x - dx, inflated.X);
            Assert.Equal(y - dy, inflated.Y);
            Assert.Equal(w + 2 * dx, inflated.Width);
            Assert.Equal(h + 2 * dy, inflated.Height);
        }
    }
}

public class ColorPropertyBasedTests
{
    private readonly Random _random = new(54321);

    [Fact]
    public void FromHex_RandomColors_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            byte r = (byte)_random.Next(0, 256);
            byte g = (byte)_random.Next(0, 256);
            byte b = (byte)_random.Next(0, 256);
            byte a = (byte)_random.Next(0, 256);
            
            string hex = $"#{r:X2}{g:X2}{b:X2}";
            var color = Color.FromHex(hex);
            
            Assert.Equal(r, color.R);
            Assert.Equal(g, color.G);
            Assert.Equal(b, color.B);
            Assert.Equal(255, color.A);
        }
    }

    [Fact]
    public void FromHex_RandomColorsWithAlpha_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            byte r = (byte)_random.Next(0, 256);
            byte g = (byte)_random.Next(0, 256);
            byte b = (byte)_random.Next(0, 256);
            byte a = (byte)_random.Next(0, 256);
            
            string hex = $"#{a:X2}{r:X2}{g:X2}{b:X2}";
            var color = Color.FromHex(hex);
            
            Assert.Equal(r, color.R);
            Assert.Equal(g, color.G);
            Assert.Equal(b, color.B);
            Assert.Equal(a, color.A);
        }
    }

    [Fact]
    public void ToArgb_RandomColors_1000Cases()
    {
        for (int i = 0; i < 1000; i++)
        {
            byte a = (byte)_random.Next(0, 256);
            byte r = (byte)_random.Next(0, 256);
            byte g = (byte)_random.Next(0, 256);
            byte b = (byte)_random.Next(0, 256);
            
            var color = new Color(a, r, g, b);
            int expected = (a << 24) | (r << 16) | (g << 8) | b;
            
            Assert.Equal(expected, color.ToArgb());
        }
    }
}
