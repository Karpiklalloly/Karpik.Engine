using Xunit;
using Karpik.Engine.Client.UI.Core;

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
    public void Length_ReturnsMagnitude()
    {
        var v = new Vector2(3, 4);

        Assert.Equal(5, v.Length);
    }

    [Fact]
    public void LengthSquared_ReturnsSquaredMagnitude()
    {
        var v = new Vector2(3, 4);

        Assert.Equal(25, v.LengthSquared);
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
}
