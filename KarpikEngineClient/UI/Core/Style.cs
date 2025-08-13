using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Style
{
    // Размеры
    public float? Width;
    public float? Height;
    public float? MinWidth;
    public float? MaxWidth;
    public float? MinHeight;
    public float? MaxHeight;

    // Позиционирование
    public Position? Position;
    public float? Left;
    public float? Top;
    public float? Right;
    public float? Bottom;

    // Отступы
    public Padding Padding = new();
    public Margin Margin = new();

    // Внешний вид
    public Color? BackgroundColor;
    public Color? BorderColor;
    public float? BorderWidth;
    public float? BorderRadius;

    // Flexbox
    public FlexDirection? FlexDirection;
    public JustifyContent? JustifyContent;
    public AlignItems? AlignItems;
    public float? FlexGrow;
    public float? FlexShrink;

    // Текст
    public Color? TextColor;
    public int? FontSize;
    public AlignText? TextAlign;

    // Копирование стилей
    public void CopyFrom(Style other)
    {
        if (other.Width.HasValue) Width = other.Width;
        if (other.Height.HasValue) Height = other.Height;
        if (other.MinWidth.HasValue) MinWidth = other.MinWidth;
        if (other.MaxWidth.HasValue) MaxWidth = other.MaxWidth;
        if (other.MinHeight.HasValue) MinHeight = other.MinHeight;
        if (other.MaxHeight.HasValue) MaxHeight = other.MaxHeight;

        if (other.Position.HasValue) Position = other.Position;
        if (other.Left.HasValue) Left = other.Left;
        if (other.Top.HasValue) Top = other.Top;
        if (other.Right.HasValue) Right = other.Right;
        if (other.Bottom.HasValue) Bottom = other.Bottom;

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
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

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
    public float Left;
    public float Top;
    public float Right;
    public float Bottom;

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