using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class StyleRule
{
    public string Selector { get; set; }
    public Dictionary<string, string> Properties { get; } = new();
    public Dictionary<string, StyleRule> PseudoClasses { get; } = new();

    public StyleRule(string selector)
    {
        Selector = selector;
    }

    public bool Matches(VisualElement element)
    {
        // Простая реализация для типов, классов и ID
        if (Selector.StartsWith("#"))
        {
            // ID selector
            return element.Name == Selector.Substring(1);
        }
        else if (Selector.StartsWith("."))
        {
            // Class selector (пока упрощенно)
            return element.ClassList?.Contains(Selector.Substring(1)) ?? false;
        }
        else
        {
            // Type selector
            return element.GetType().Name.ToLower() == Selector.ToLower();
        }
    }

    public void ApplyTo(Style target)
    {
        foreach (var prop in Properties)
        {
            ApplyProperty(target, prop.Key, prop.Value);
        }
    }

    private void ApplyProperty(Style target, string name, string value)
    {
        switch (name.ToLower())
        {
            // Размеры
            case "width":
                target.Width = new StyleValue<float>(ParseFloat(value));
                break;
            case "height":
                target.Height = new StyleValue<float>(ParseFloat(value));
                break;
            case "min-width":
                target.MinWidth = new StyleValue<float>(ParseFloat(value));
                break;
            case "max-width":
                target.MaxWidth = new StyleValue<float>(ParseFloat(value));
                break;
            case "min-height":
                target.MinHeight = new StyleValue<float>(ParseFloat(value));
                break;
            case "max-height":
                target.MaxHeight = new StyleValue<float>(ParseFloat(value));
                break;

            // Flexbox свойства
            case "flex-grow":
                target.FlexGrow = new StyleValue<float>(ParseFloat(value));
                break;
            case "flex-shrink":
                target.FlexShrink = new StyleValue<float>(ParseFloat(value));
                break;
            case "flex-basis":
                target.FlexBasis = new StyleValue<float>(ParseFloat(value));
                break;

            // Layout свойства
            case "flex-direction":
                target.FlexDirection = new StyleValue<FlexDirection>(ParseFlexDirection(value));
                break;
            case "justify-content":
                target.JustifyContent = new StyleValue<Justify>(ParseJustify(value));
                break;
            case "align-items":
                target.AlignItems = new StyleValue<Align>(ParseAlign(value));
                break;
            case "align-self":
                target.AlignSelf = new StyleValue<Align>(ParseAlign(value));
                break;

            // Цвета и оформление
            case "background-color":
                target.BackgroundColor = new StyleValue<Color>(ParseColor(value));
                break;
            case "border-color":
                target.BorderColor = new StyleValue<Color>(ParseColor(value));
                break;
            case "color":
                target.Color = new StyleValue<Color>(ParseColor(value));
                break;

            // Размеры и отступы
            case "border-width":
                target.BorderWidth = new StyleValue<float>(ParseFloat(value));
                break;
            case "border-radius":
                target.BorderRadius = new StyleValue<float>(ParseFloat(value));
                break;

            // Padding
            case "padding":
            {
                var paddingValue = ParseFloat(value);
                target.PaddingTop = new StyleValue<float>(paddingValue);
                target.PaddingRight = new StyleValue<float>(paddingValue);
                target.PaddingBottom = new StyleValue<float>(paddingValue);
                target.PaddingLeft = new StyleValue<float>(paddingValue);
            }
                break;
            case "padding-top":
                target.PaddingTop = new StyleValue<float>(ParseFloat(value));
                break;
            case "padding-right":
                target.PaddingRight = new StyleValue<float>(ParseFloat(value));
                break;
            case "padding-bottom":
                target.PaddingBottom = new StyleValue<float>(ParseFloat(value));
                break;
            case "padding-left":
                target.PaddingLeft = new StyleValue<float>(ParseFloat(value));
                break;

            // Margin
            case "margin":
            {
                var marginValue = ParseFloat(value);
                target.MarginTop = new StyleValue<float>(marginValue);
                target.MarginRight = new StyleValue<float>(marginValue);
                target.MarginBottom = new StyleValue<float>(marginValue);
                target.MarginLeft = new StyleValue<float>(marginValue);
            }
                break;
            case "margin-top":
                target.MarginTop = new StyleValue<float>(ParseFloat(value));
                break;
            case "margin-right":
                target.MarginRight = new StyleValue<float>(ParseFloat(value));
                break;
            case "margin-bottom":
                target.MarginBottom = new StyleValue<float>(ParseFloat(value));
                break;
            case "margin-left":
                target.MarginLeft = new StyleValue<float>(ParseFloat(value));
                break;

            // Текст
            case "font-size":
                target.FontSize = new StyleValue<int>((int)ParseFloat(value));
                break;
            case "text-align":
                target.TextAlign = new StyleValue<TextAlign>(ParseTextAlign(value));
                break;
        }
    }

    private float ParseFloat(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;

        // Убираем единицы измерения (пока поддерживаем только px)
        value = value.Trim().Replace("px", "").Replace("pt", "");

        if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float result))
            return result;

        return 0;
    }

    private Color ParseColor(string value)
    {
        if (string.IsNullOrEmpty(value)) return Color.White;

        value = value.Trim().ToLower();

        // Именованные цвета
        switch (value)
        {
            case "transparent": return new Color(0, 0, 0, 0);
            case "black": return Color.Black;
            case "white": return Color.White;
            case "red": return Color.Red;
            case "green": return Color.Green;
            case "blue": return Color.Blue;
            case "yellow": return Color.Yellow;
            case "orange": return Color.Orange;
            case "purple": return Color.Purple;
            case "gray":
            case "grey": return Color.Gray;
            case "lightgray":
            case "lightgrey": return Color.LightGray;
            case "darkgray":
            case "darkgrey": return Color.DarkGray;
        }

        // HEX цвета (#RRGGBB или #RGB)
        if (value.StartsWith("#"))
        {
            var hex = value.Substring(1);
            try
            {
                if (hex.Length == 3)
                {
                    // #RGB формат
                    var r = int.Parse(hex[0].ToString() + hex[0].ToString(),
                        System.Globalization.NumberStyles.HexNumber);
                    var g = int.Parse(hex[1].ToString() + hex[1].ToString(),
                        System.Globalization.NumberStyles.HexNumber);
                    var b = int.Parse(hex[2].ToString() + hex[2].ToString(),
                        System.Globalization.NumberStyles.HexNumber);
                    return new Color((byte)r, (byte)g, (byte)b, (byte)255);
                }
                else if (hex.Length == 6)
                {
                    // #RRGGBB формат
                    var r = int.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    var g = int.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    var b = int.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new Color((byte)r, (byte)g, (byte)b, (byte)255);
                }
            }
            catch
            {
                // Игнорируем ошибки парсинга
            }
        }

        // RGBA цвета (rgba(255, 128, 64, 0.5))
        if (value.StartsWith("rgba(") && value.EndsWith(")"))
        {
            try
            {
                var parts = value.Substring(5, value.Length - 6).Split(',');
                if (parts.Length == 4)
                {
                    var r = int.Parse(parts[0].Trim());
                    var g = int.Parse(parts[1].Trim());
                    var b = int.Parse(parts[2].Trim());
                    var a = (int)(float.Parse(parts[3].Trim(),
                        System.Globalization.CultureInfo.InvariantCulture) * 255);
                    return new Color((byte)r, (byte)g, (byte)b, (byte)a);
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        // RGB цвета (rgb(255, 128, 64))
        if (value.StartsWith("rgb(") && value.EndsWith(")"))
        {
            try
            {
                var parts = value.Substring(4, value.Length - 5).Split(',');
                if (parts.Length == 3)
                {
                    var r = int.Parse(parts[0].Trim());
                    var g = int.Parse(parts[1].Trim());
                    var b = int.Parse(parts[2].Trim());
                    return new Color((byte)r, (byte)g, (byte)b, (byte)255);
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        return Color.White;
    }

    private FlexDirection ParseFlexDirection(string value)
    {
        switch (value.ToLower())
        {
            case "row": return FlexDirection.Row;
            case "column": return FlexDirection.Column;
            case "row-reverse": return FlexDirection.RowReverse;
            case "column-reverse": return FlexDirection.ColumnReverse;
            default: return FlexDirection.Column;
        }
    }

    private Justify ParseJustify(string value)
    {
        switch (value.ToLower().Replace(" ", ""))
        {
            case "flex-start":
            case "start": return Justify.FlexStart;
            case "flex-end":
            case "end": return Justify.FlexEnd;
            case "center": return Justify.Center;
            case "space-between": return Justify.SpaceBetween;
            case "space-around": return Justify.SpaceAround;
            case "space-evenly": return Justify.SpaceEvenly;
            default: return Justify.FlexStart;
        }
    }

    private Align ParseAlign(string value)
    {
        switch (value.ToLower().Replace(" ", ""))
        {
            case "auto": return Align.Auto;
            case "flex-start":
            case "start": return Align.FlexStart;
            case "flex-end":
            case "end": return Align.FlexEnd;
            case "center": return Align.Center;
            case "stretch": return Align.Stretch;
            case "baseline": return Align.Baseline;
            default: return Align.Stretch;
        }
    }

    private TextAlign ParseTextAlign(string value)
    {
        switch (value.ToLower())
        {
            case "left": return TextAlign.Left;
            case "center": return TextAlign.Center;
            case "right": return TextAlign.Right;
            default: return TextAlign.Left;
        }
    }
}