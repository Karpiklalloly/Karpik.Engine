using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Предопределенные стили для демо
/// </summary>
public static class DemoStyles
{
    public static StyleSheet CreateDemoStyleSheet()
    {
        var styleSheet = new StyleSheet();
        
        // Стили для кнопок
        styleSheet.AddClass("button", new Style
        {
            BackgroundColor = new Color(70, 130, 180, 255),
            TextColor = Color.White,
            Padding = new Padding(10, 15),
            BorderRadius = 5f,
            BorderWidth = 0f
        });
        
        styleSheet.AddHover("button", new Style
        {
            BackgroundColor = new Color(90, 150, 200, 255)
        });
        
        styleSheet.AddActive("button", new Style
        {
            BackgroundColor = new Color(50, 110, 160, 255)
        });
        
        // Стили для карточек
        styleSheet.AddClass("card", new Style
        {
            BackgroundColor = Color.White,
            BorderRadius = 8f,
            Padding = new Padding(16),
            Margin = new Margin(8)
        });
        
        styleSheet.AddClass("card-header", new Style
        {
            FontSize = 18,
            TextColor = new Color(50, 50, 50, 255),
            Padding = new Padding(0, 0, 8, 0)
        });
        
        // Стили для панелей
        styleSheet.AddClass("panel", new Style
        {
            BorderWidth = 1f,
            BorderColor = new Color(200, 200, 200, 255),
            BackgroundColor = new Color(250, 250, 250, 255),
            BorderRadius = 4f
        });
        
        styleSheet.AddClass("panel-header", new Style
        {
            BackgroundColor = new Color(240, 240, 240, 255),
            Padding = new Padding(8, 12),
            BorderRadius = 4f
        });
        
        // Стили для foldout
        styleSheet.AddClass("foldout", new Style
        {
            BorderWidth = 1f,
            BorderColor = new Color(220, 220, 220, 255),
            BackgroundColor = Color.White,
            BorderRadius = 6f
        });
        
        styleSheet.AddClass("foldout-header", new Style
        {
            BackgroundColor = new Color(248, 248, 248, 255),
            Padding = new Padding(8),
            BorderRadius = 6f
        });
        
        styleSheet.AddClass("foldout-toggle", new Style
        {
            BackgroundColor = new Color(200, 200, 200, 255),
            TextColor = new Color(80, 80, 80, 255),
            BorderRadius = 3f,
            Padding = new Padding(2)
        });
        
        // Стили для layout контейнеров
        styleSheet.AddClass("hbox", new Style
        {
            // HBox специфичные стили можно добавить здесь
        });
        
        styleSheet.AddClass("vbox", new Style
        {
            // VBox специфичные стили можно добавить здесь
        });
        
        styleSheet.AddClass("grid", new Style
        {
            // Grid специфичные стили можно добавить здесь
        });
        
        // Стили для scroll view
        styleSheet.AddClass("scroll-view", new Style
        {
            BackgroundColor = Color.White,
            BorderWidth = 1f,
            BorderColor = new Color(200, 200, 200, 255)
        });
        
        // Стили для labels
        styleSheet.AddClass("label", new Style
        {
            TextColor = new Color(50, 50, 50, 255),
            FontSize = 14
        });
        
        // Специальные стили для демо
        styleSheet.AddClass("demo-section", new Style
        {
            Margin = new Margin(10),
            Padding = new Padding(15),
            BackgroundColor = new Color(248, 249, 250, 255),
            BorderRadius = 8f,
            BorderWidth = 1f,
            BorderColor = new Color(230, 230, 230, 255)
        });
        
        styleSheet.AddClass("demo-title", new Style
        {
            FontSize = 20,
            TextColor = new Color(40, 40, 40, 255),
            Padding = new Padding(0, 0, 10, 0)
        });
        
        styleSheet.AddClass("demo-subtitle", new Style
        {
            FontSize = 16,
            TextColor = new Color(70, 70, 70, 255),
            Padding = new Padding(0, 0, 5, 0)
        });
        
        // Цветовые схемы
        styleSheet.AddClass("primary", new Style
        {
            BackgroundColor = new Color(70, 130, 180, 255),
            TextColor = Color.White
        });
        
        styleSheet.AddClass("success", new Style
        {
            BackgroundColor = new Color(40, 167, 69, 255),
            TextColor = Color.White
        });
        
        styleSheet.AddClass("warning", new Style
        {
            BackgroundColor = new Color(255, 193, 7, 255),
            TextColor = new Color(50, 50, 50, 255)
        });
        
        styleSheet.AddClass("danger", new Style
        {
            BackgroundColor = new Color(220, 53, 69, 255),
            TextColor = Color.White
        });
        
        styleSheet.AddClass("info", new Style
        {
            BackgroundColor = new Color(23, 162, 184, 255),
            TextColor = Color.White
        });
        
        // Hover эффекты для демо
        styleSheet.AddHover("hover-demo", new Style
        {
            BackgroundColor = new Color(120, 170, 220, 255),
            BorderWidth = 3f,
            BorderColor = new Color(70, 130, 180, 255)
        });
        
        return styleSheet;
    }
    
    /// <summary>
    /// Применяет демо стили к элементу
    /// </summary>
    public static void ApplyDemoStyles(VisualElement element)
    {
        element.StyleSheet = CreateDemoStyleSheet();
    }
}