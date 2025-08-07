using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Style
{
    // Размеры
    public StyleValue<float> Width { get; set; }
    public StyleValue<float> Height { get; set; }
    public StyleValue<float> MinWidth { get; set; }
    public StyleValue<float> MaxWidth { get; set; }
    public StyleValue<float> MinHeight { get; set; }
    public StyleValue<float> MaxHeight { get; set; }
    
    // Flexbox
    public StyleValue<float> FlexGrow { get; set; } = new StyleValue<float>(0);
    public StyleValue<float> FlexShrink { get; set; } = new StyleValue<float>(1);
    public StyleValue<float> FlexBasis { get; set; } = new StyleValue<float>(0);
    
    // Layout
    public StyleValue<FlexDirection> FlexDirection { get; set; } = new StyleValue<FlexDirection>(UIToolkit.FlexDirection.Column);
    public StyleValue<Justify> JustifyContent { get; set; } = new StyleValue<Justify>(Justify.FlexStart);
    public StyleValue<Align> AlignItems { get; set; } = new StyleValue<Align>(Align.Stretch);
    public StyleValue<Align> AlignSelf { get; set; } = new StyleValue<Align>(Align.Auto);
    
    // Внешний вид
    public StyleValue<Color> BackgroundColor { get; set; }
    public StyleValue<Color> BorderColor { get; set; }
    public StyleValue<float> BorderWidth { get; set; }
    public StyleValue<float> BorderRadius { get; set; }
    public StyleValue<float> PaddingLeft { get; set; }
    public StyleValue<float> PaddingRight { get; set; }
    public StyleValue<float> PaddingTop { get; set; }
    public StyleValue<float> PaddingBottom { get; set; }
    public StyleValue<float> MarginLeft { get; set; }
    public StyleValue<float> MarginRight { get; set; }
    public StyleValue<float> MarginTop { get; set; }
    public StyleValue<float> MarginBottom { get; set; }
    
    // Текст
    public StyleValue<Color> Color { get; set; } = new StyleValue<Color>(Raylib_cs.Color.Black);
    public StyleValue<int> FontSize { get; set; } = new StyleValue<int>(16);
    public StyleValue<TextAlign> TextAlign { get; set; } = new StyleValue<TextAlign>(UIToolkit.TextAlign.Left);
}

// Вспомогательные типы
public struct StyleValue<T> where T : struct
{
    public T Value { get; set; }
    public bool IsSet { get; set; }
    
    public StyleValue() { }
    
    public StyleValue(T value)
    {
        Value = value;
        IsSet = true;
    }
    
    public static implicit operator StyleValue<T>(T value)
    {
        return new StyleValue<T>(value);
    }
    
    public static implicit operator T(StyleValue<T> styleValue)
    {
        return styleValue.Value;
    }
}

public enum FlexDirection
{
    Row,
    Column,
    RowReverse,
    ColumnReverse
}

public enum Justify
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum Align
{
    Auto,
    FlexStart,
    FlexEnd,
    Center,
    Stretch,
    Baseline
}

public enum TextAlign
{
    Left,
    Center,
    Right
}