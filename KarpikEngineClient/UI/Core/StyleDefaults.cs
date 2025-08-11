using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Централизованное хранилище стандартных значений для стилей UI элементов
/// </summary>
public static class StyleDefaults
{
    // Текст и шрифты
    public const int FontSize = 16;
    public const AlignText TextAlign = AlignText.Left;
    public static readonly Color TextColor = Color.Black;
    
    // Цвета
    public static readonly Color BackgroundColor = Color.White;
    public static readonly Color BorderColor = Color.Gray;
    
    // Flex Layout
    public const FlexDirection FlexDirection = UIToolkit.FlexDirection.Column;
    public const JustifyContent JustifyContent = UIToolkit.JustifyContent.FlexStart;
    public const AlignItems AlignItems = UIToolkit.AlignItems.Stretch;
    public const float FlexGrow = 0f;
    public const float FlexShrink = 1f;
    
    // Размеры и позиционирование
    public const Position Position = UIToolkit.Position.Relative;
    public const float BorderWidth = 1f;
    public const float BorderRadius = 0f;
    public const float MinWidth = 0f;
    public const float MinHeight = 0f;
    
    // Отступы
    public static readonly Padding Padding = new(0);
    public static readonly Margin Margin = new(0);
    
    // Размеры контейнеров (для LayoutEngine)
    public const float DefaultContainerWidth = 300f;
    public const float DefaultContainerHeight = 250f;
    public const float HorizontalContainerWidth = 300f;
    public const float VerticalContainerWidth = 250f;
    public const float VerticalContainerHeight = 200f;
    public const float HorizontalContainerHeight = 150f;
}

/// <summary>
/// Методы расширения для удобного получения значений по умолчанию
/// </summary>
public static class StyleExtensions
{
    public static int GetFontSizeOrDefault(this Style style) => style.FontSize ?? StyleDefaults.FontSize;
    public static AlignText GetTextAlignOrDefault(this Style style) => style.TextAlign ?? StyleDefaults.TextAlign;
    public static Color GetTextColorOrDefault(this Style style) => style.TextColor ?? StyleDefaults.TextColor;
    public static Color GetBackgroundColorOrDefault(this Style style) => style.BackgroundColor ?? StyleDefaults.BackgroundColor;
    public static Color GetBorderColorOrDefault(this Style style) => style.BorderColor ?? StyleDefaults.BorderColor;
    public static FlexDirection GetFlexDirectionOrDefault(this Style style) => style.FlexDirection ?? StyleDefaults.FlexDirection;
    public static JustifyContent GetJustifyContentOrDefault(this Style style) => style.JustifyContent ?? StyleDefaults.JustifyContent;
    public static AlignItems GetAlignItemsOrDefault(this Style style) => style.AlignItems ?? StyleDefaults.AlignItems;
    public static float GetFlexGrowOrDefault(this Style style) => style.FlexGrow ?? StyleDefaults.FlexGrow;
    public static Position GetPositionOrDefault(this Style style) => style.Position ?? StyleDefaults.Position;
    public static float GetMinWidthOrDefault(this Style style) => style.MinWidth ?? StyleDefaults.MinWidth;
    public static float GetMinHeightOrDefault(this Style style) => style.MinHeight ?? StyleDefaults.MinHeight;
}