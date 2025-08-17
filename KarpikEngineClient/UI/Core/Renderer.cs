using Raylib_cs;
using Color = Raylib_cs.Color;

namespace Karpik.Engine.Client.UIToolkit;

public class StackingContext
{
    public UIElement RootElement { get; }
    public int ZIndex { get; }

    // Элементы этого контекста, которые находятся "позади" родителя (z-index < 0)
    public List<StackingContext> ChildrenWithNegativeZIndex { get; } = new();
    // Элементы этого контекста, которые находятся в обычном потоке
    public List<UIElement> ChildrenInFlow { get; } = new();
    // Элементы этого контекста, которые находятся "впереди" родителя (z-index > 0)
    public List<StackingContext> ChildrenWithPositiveZIndex { get; } = new();

    public StackingContext(UIElement rootElement, int zIndex = 0)
    {
        RootElement = rootElement;
        ZIndex = zIndex;
    }
}

public class Renderer
{
    public void Render(UIElement root)
    {
        // --- ШАГ 1: Построить дерево контекстов наложения ---
        var rootContext = BuildStackingContextTree(root);
        
        // --- ШАГ 2: Рекурсивно отрисовать это дерево ---
        RenderContext(rootContext);
    }
    
    /// <summary>
    /// Рекурсивно строит дерево контекстов наложения, начиная с корневого элемента.
    /// </summary>
    private StackingContext BuildStackingContextTree(UIElement element)
    {
        var zIndex = IsStackingContext(element) 
            ? ParseInt(element.ComputedStyle.GetValueOrDefault("z-index", "0")) 
            : 0;

        var context = new StackingContext(element, zIndex);

        // Распределяем всех детей по группам внутри этого контекста
        foreach (var child in element.Children)
        {
            if (IsStackingContext(child))
            {
                // Если ребенок сам создает новый контекст, мы рекурсивно строим его
                var childContext = BuildStackingContextTree(child);
                if (childContext.ZIndex < 0)
                {
                    context.ChildrenWithNegativeZIndex.Add(childContext);
                }
                else
                {
                    // z-index 0 и auto попадают в ту же группу, что и элементы в потоке,
                    // но для простоты мы их тоже считаем позитивными.
                    context.ChildrenWithPositiveZIndex.Add(childContext);
                }
            }
            else
            {
                // Если ребенок не создает контекст, он просто попадает в поток
                context.ChildrenInFlow.Add(child);
            }
        }

        // Сортируем группы по z-index
        context.ChildrenWithNegativeZIndex.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));
        context.ChildrenWithPositiveZIndex.Sort((a, b) => a.ZIndex.CompareTo(b.ZIndex));

        return context;
    }

    /// <summary>
    /// Рекурсивно отрисовывает один контекст наложения, соблюдая правильный порядок.
    /// </summary>
    private void RenderContext(StackingContext context)
    {
        var element = context.RootElement;
        if (element.ComputedStyle.GetValueOrDefault("display") == "none") return;

        // --- Порядок отрисовки по спецификации CSS ---
        // https://www.w3.org/TR/CSS22/visuren.html#z-index

        // 1. Фон и границы самого элемента
        RenderElementVisuals(element);
        
        // 2. Контексты детей с отрицательным z-index
        foreach (var childContext in context.ChildrenWithNegativeZIndex)
        {
            RenderContext(childContext);
        }

        // 3. Дети в обычном потоке
        foreach (var childElement in context.ChildrenInFlow)
        {
            // Для детей в потоке мы снова строим и рендерим их под-дерево
            RenderContext(BuildStackingContextTree(childElement));
        }

        // 4. Контексты детей с положительным z-index
        foreach (var childContext in context.ChildrenWithPositiveZIndex)
        {
            RenderContext(childContext);
        }
    }

    /// <summary>
    /// Отрисовывает только ВИЗУАЛЬНУЮ ЧАСТЬ (фон, границы, текст) одного элемента.
    /// Не вызывает рекурсию для детей.
    /// </summary>
    private void RenderElementVisuals(UIElement element)
    {
        var style = element.ComputedStyle;
        var box = element.LayoutBox;
        
        bool hasOverflowHidden = style.GetValueOrDefault("overflow") == "hidden";
        if (hasOverflowHidden)
        {
            Raylib.BeginScissorMode((int)box.PaddingRect.X, (int)box.PaddingRect.Y, (int)box.PaddingRect.Width, (int)box.PaddingRect.Height);
        }

        var bgColor = ParseColor(style.GetValueOrDefault("background-color", "transparent"));
        if (bgColor.A > 0) Raylib.DrawRectangleRec(box.PaddingRect, bgColor);

        var borderColor = ParseColor(style.GetValueOrDefault("border-color", "black"));
        if (borderColor.A > 0)
        {
            var borderWidth = ParseFloat(style.GetValueOrDefault("border-width", "0"));
            if (borderWidth > 0) Raylib.DrawRectangleLinesEx(box.BorderRect, borderWidth, borderColor);
        }

        if (!string.IsNullOrEmpty(element.Text))
        {
            var textColor = ParseColor(style.GetValueOrDefault("color", "black"));
            var fontSize = (int)ParseFloat(style.GetValueOrDefault("font-size", "16"));
            Raylib.DrawText(element.Text, (int)box.ContentRect.X, (int)box.ContentRect.Y, fontSize, textColor);
        }
        
        if (hasOverflowHidden)
        {
            Raylib.EndScissorMode();
        }
    }
    
    /// <summary>
    /// Проверяет, создает ли элемент новый контекст наложения.
    /// </summary>
    private bool IsStackingContext(UIElement element)
    {
        var style = element.ComputedStyle;
        var position = style.GetValueOrDefault("position", "static");
        var zIndex = style.GetValueOrDefault("z-index", "auto");

        // Упрощенное правило: любой позиционированный элемент с z-index не auto
        return (position == "absolute" || position == "relative" || position == "fixed") && zIndex != "auto";
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