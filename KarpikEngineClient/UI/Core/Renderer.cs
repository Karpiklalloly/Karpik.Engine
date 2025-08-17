using System.Drawing;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace Karpik.Engine.Client.UIToolkit;

public class Renderer
{
    public void Render(UIElement root)
    {
        RenderNode(root);
    }

    private void RenderNode(UIElement element)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;

        // --- 1. Отрисовка фона ---
        var bgColor = ParseColor(style.GetValueOrDefault("background-color", "transparent"));
        if (bgColor.A > 0)
        {
            // Метод ToRaylibRect больше не нужен!
            Raylib.DrawRectangleRec(box.PaddingRect, bgColor);
        }

        // --- 2. Отрисовка границ ---
        var borderColor = ParseColor(style.GetValueOrDefault("border-color", "black"));
        if (borderColor.A > 0)
        {
            var borderWidth = ParseFloat(style.GetValueOrDefault("border-width", "0"));
            if (borderWidth > 0)
            {
                Raylib.DrawRectangleLinesEx(box.BorderRect, borderWidth, borderColor);
            }
        }

        // --- 3. Отрисовка текста ---
        if (!string.IsNullOrEmpty(element.Text))
        {
            var textColor = ParseColor(style.GetValueOrDefault("color", "black"));
            var fontSize = (int)ParseFloat(style.GetValueOrDefault("font-size", "16"));
            
            Raylib.DrawText(
                element.Text,
                (int)box.ContentRect.X,
                (int)box.ContentRect.Y,
                fontSize,
                textColor
            );
        }

        // --- 4. Рекурсивная отрисовка дочерних элементов ---
        foreach (var child in element.Children)
        {
            RenderNode(child);
        }
    }

    #region Вспомогательные методы

    // Безопасно парсит строку в число
    private float ParseFloat(string value, float defaultValue = 0f)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        value = value.Replace("px", "").Trim();
        if (float.TryParse(value, out float result))
        {
            return result;
        }
        return defaultValue;
    }

    // Парсит строку с названием цвета или hex-кодом в цвет Raylib
    private Color ParseColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Color.Blank;
        
        switch (value.ToLower().Trim())
        {
            case "transparent": return Color.Blank;
            case "white": return Color.White;
            case "black": return Color.Black;
            case "red": return Color.Red;
            case "blue": return Color.Blue;
            case "green": return Color.Green;
            case "lightblue": return Color.SkyBlue;
            case "lightgray": return Color.LightGray;
            case "lightyellow": return Color.RayWhite; // Raylib's yellow is dark
            case "darkblue": return Color.DarkBlue;
            default: return Color.Blank; // Возвращаем прозрачный, если цвет не распознан
        }
    }
    
    #endregion
}