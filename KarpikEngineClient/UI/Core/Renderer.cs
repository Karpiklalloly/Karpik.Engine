using System.Drawing;
using Raylib_cs;
using Color = Raylib_cs.Color;

namespace Karpik.Engine.Client.UIToolkit;

public class Renderer
{
        private List<UIElement> _renderList;

    public void Render(UIElement root)
    {
        _renderList = new List<UIElement>();
        BuildRenderList(root);

        _renderList = _renderList.OrderBy(el => 
            ParseInt(el.ComputedStyle.GetValueOrDefault("z-index", "0"))
        ).ToList();

        foreach (var element in _renderList)
        {
            RenderElement(element);
        }
    }
    
    private void BuildRenderList(UIElement element)
    {
        if (element.ComputedStyle.GetValueOrDefault("display") == "none")
        {
            return;
        }

        _renderList.Add(element);

        foreach (var child in element.Children)
        {
            BuildRenderList(child);
        }
    }

    private void RenderElement(UIElement element)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;
        
        bool hasOverflowHidden = style.GetValueOrDefault("overflow") == "hidden";
        if (hasOverflowHidden)
        {
            Raylib.BeginScissorMode((int)box.PaddingRect.X, (int)box.PaddingRect.Y, (int)box.PaddingRect.Width, (int)box.PaddingRect.Height);
        }

        var bgColor = ParseColor(style.GetValueOrDefault("background-color", "transparent"));
        if (bgColor.A > 0)
        {
            Raylib.DrawRectangleRec(box.PaddingRect, bgColor);
        }

        var borderColor = ParseColor(style.GetValueOrDefault("border-color", "black"));
        if (borderColor.A > 0)
        {
            var borderWidth = ParseFloat(style.GetValueOrDefault("border-width", "0"));
            if (borderWidth > 0)
            {
                Raylib.DrawRectangleLinesEx(box.BorderRect, borderWidth, borderColor);
            }
        }

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
        
        if (hasOverflowHidden)
        {
            Raylib.EndScissorMode();
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
    
    private int ParseInt(string value, int defaultValue = 0)
    {
        return int.TryParse(value, out int result) ? result : defaultValue;
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