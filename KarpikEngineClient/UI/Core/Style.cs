using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Style
{
    // Размеры
    public float? Width { get; set; }
    public float? Height { get; set; }
    public float? MinWidth { get; set; }
    public float? MaxWidth { get; set; }
    public float? MinHeight { get; set; }
    public float? MaxHeight { get; set; }

    // Позиционирование
    public Position Position { get; set; } = Position.Relative;
    public float? Left { get; set; }
    public float? Top { get; set; }
    public float? Right { get; set; }
    public float? Bottom { get; set; }

    // Отступы
    public Padding Padding { get; set; } = new();
    public Margin Margin { get; set; } = new();

    // Внешний вид
    public Color BackgroundColor { get; set; } = Color.Blank;
    public Color BorderColor { get; set; } = Color.Blank;
    public float BorderWidth { get; set; } = 0;
    public float BorderRadius { get; set; } = 0;

    // Flexbox
    public FlexDirection FlexDirection { get; set; } = FlexDirection.Column;
    public JustifyContent JustifyContent { get; set; } = JustifyContent.FlexStart;
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;
    public float FlexGrow { get; set; } = 0;
    public float FlexShrink { get; set; } = 1;

    // Текст
    public Color TextColor { get; set; } = Color.Black;
    public int FontSize { get; set; } = 16;
    public AlignText? TextAlign { get; set; } = null;

    // Копирование стилей
    public void CopyFrom(Style other)
    {
        Width = other.Width ?? Width;
        Height = other.Height ?? Height;
        MinWidth = other.MinWidth ?? MinWidth;
        MaxWidth = other.MaxWidth ?? MaxWidth;
        MinHeight = other.MinHeight ?? MinHeight;
        MaxHeight = other.MaxHeight ?? MaxHeight;

        Position = other.Position;
        Left = other.Left ?? Left;
        Top = other.Top ?? Top;
        Right = other.Right ?? Right;
        Bottom = other.Bottom ?? Bottom;

        Padding.CopyFrom(other.Padding);
        Margin.CopyFrom(other.Margin);

        if (other.BackgroundColor.A > 0) BackgroundColor = other.BackgroundColor;
        if (other.BorderColor.A > 0) BorderColor = other.BorderColor;
        if (other.BorderWidth > 0) BorderWidth = other.BorderWidth;
        if (other.BorderRadius > 0) BorderRadius = other.BorderRadius;

        FlexDirection = other.FlexDirection;
        JustifyContent = other.JustifyContent;
        AlignItems = other.AlignItems;
        if (other.FlexGrow > 0) FlexGrow = other.FlexGrow;
        if (other.FlexShrink != 1) FlexShrink = other.FlexShrink;

        if (other.TextColor.A > 0) TextColor = other.TextColor;
        if (other.FontSize != 16) FontSize = other.FontSize;
        if (other.TextAlign.HasValue) TextAlign = other.TextAlign;
    }
}

public class Padding
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }

    public Padding() { }

    public Padding(float all)
    {
        Left = Top = Right = Bottom = all;
    }

    public Padding(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    public Padding(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public void CopyFrom(Padding other)
    {
        if (other.Left > 0) Left = other.Left;
        if (other.Top > 0) Top = other.Top;
        if (other.Right > 0) Right = other.Right;
        if (other.Bottom > 0) Bottom = other.Bottom;
    }
}

public class Margin
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }

    public Margin() { }

    public Margin(float all)
    {
        Left = Top = Right = Bottom = all;
    }

    public Margin(float horizontal, float vertical)
    {
        Left = Right = horizontal;
        Top = Bottom = vertical;
    }

    public Margin(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public void CopyFrom(Margin other)
    {
        if (other.Left > 0) Left = other.Left;
        if (other.Top > 0) Top = other.Top;
        if (other.Right > 0) Right = other.Right;
        if (other.Bottom > 0) Bottom = other.Bottom;
    }
}

public enum Position
{
    Relative,
    Absolute,
    Fixed
}

public enum FlexDirection
{
    Row,
    Column,
    RowReverse,
    ColumnReverse
}

public enum JustifyContent
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum AlignItems
{
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    Baseline
}

public enum AlignText
{
    Left,
    Center,
    Right
}

public enum PseudoClass
{
    None = 0,
    Hover = 1,
    Active = 2,
    Focus = 4,
    Disabled = 8,
    Checked = 16,
    FirstChild = 32,
    LastChild = 64
}