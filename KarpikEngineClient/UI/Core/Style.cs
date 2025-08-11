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
    public Position? Position { get; set; } = null;
    public float? Left { get; set; }
    public float? Top { get; set; }
    public float? Right { get; set; }
    public float? Bottom { get; set; }

    // Отступы
    public Padding Padding { get; set; } = new();
    public Margin Margin { get; set; } = new();

    // Внешний вид
    public Color? BackgroundColor { get; set; } = null;
    public Color? BorderColor { get; set; } = null;
    public float? BorderWidth { get; set; } = null;
    public float? BorderRadius { get; set; } = null;

    // Flexbox
    public FlexDirection? FlexDirection { get; set; } = null;
    public JustifyContent? JustifyContent { get; set; } = null;
    public AlignItems? AlignItems { get; set; } = null;
    public float? FlexGrow { get; set; } = null;
    public float? FlexShrink { get; set; } = null;

    // Текст
    public Color? TextColor { get; set; } = null;
    public int? FontSize { get; set; } = null;
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

        if (other.Position.HasValue) Position = other.Position;
        Left = other.Left ?? Left;
        Top = other.Top ?? Top;
        Right = other.Right ?? Right;
        Bottom = other.Bottom ?? Bottom;

        Padding.CopyFrom(other.Padding);
        Margin.CopyFrom(other.Margin);

        if (other.BackgroundColor.HasValue) BackgroundColor = other.BackgroundColor;
        if (other.BorderColor.HasValue) BorderColor = other.BorderColor;
        if (other.BorderWidth.HasValue) BorderWidth = other.BorderWidth;
        if (other.BorderRadius.HasValue) BorderRadius = other.BorderRadius;

        if (other.FlexDirection.HasValue) FlexDirection = other.FlexDirection;
        if (other.JustifyContent.HasValue) JustifyContent = other.JustifyContent;
        if (other.AlignItems.HasValue) AlignItems = other.AlignItems;
        if (other.FlexGrow.HasValue) FlexGrow = other.FlexGrow;
        if (other.FlexShrink.HasValue) FlexShrink = other.FlexShrink;

        if (other.TextColor.HasValue) TextColor = other.TextColor;
        if (other.FontSize.HasValue) FontSize = other.FontSize;
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