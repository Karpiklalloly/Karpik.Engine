using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Демонстрация всех возможностей UI системы KarpikEngine
/// </summary>
public static class UIDemo
{
    /// <summary>
    /// Создает полное демо UI с демонстрацией всех контейнеров и элементов
    /// </summary>
    public static VisualElement CreateFullDemo()
    {
        var root = new VisualElement("DemoRoot");
        root.Size = new Vector2(1200, 800);
        root.Style.BackgroundColor = new Color(240, 240, 240, 255);
        root.Style.Padding = new Padding(20);
        
        // Главный скролл для всего демо
        var mainScroll = new ScrollView();
        mainScroll.Size = new Vector2(1160, 760);
        mainScroll.EnableVerticalScroll = true;
        mainScroll.Style.BackgroundColor = Color.White;
        mainScroll.Style.BorderWidth = 2f;
        mainScroll.Style.BorderColor = new Color(200, 200, 200, 255);
        root.AddChild(mainScroll);
        
        var content = new VBox { Gap = 20f };
        content.Style.Padding = new Padding(20);
        mainScroll.AddChild(content);
        
        // Заголовок демо
        var title = new Label("KarpikEngine UI System Demo");
        title.Style.FontSize = 24;
        // title.Style.FontWeight = FontWeight.Bold;
        title.Style.TextAlign = AlignText.Center;
        title.Style.BackgroundColor = new Color(70, 130, 180, 255);
        title.Style.TextColor = Color.White;
        title.Style.Padding = new Padding(15);
        title.Style.BorderRadius = 8f;
        content.AddChild(title);
        
        // Секция базовых элементов
        content.AddChild(CreateBasicElementsSection());
        
        // Секция layout контейнеров
        content.AddChild(CreateLayoutSection());
        
        // Секция специализированных контейнеров
        content.AddChild(CreateSpecializedContainersSection());
        
        // Секция интерактивных элементов
        content.AddChild(CreateInteractiveSection());
        
        // Секция стилизации
        content.AddChild(CreateStylingSection());
        
        return root;
    }
    
    private static VisualElement CreateBasicElementsSection()
    {
        var section = new Card("Базовые элементы");
        section.Style.MinWidth = 1100f;
        
        var grid = new Grid(3, 2) { ColumnGap = 15f, RowGap = 15f };
        
        // Label демо
        var labelDemo = new VBox { Gap = 5f };
        labelDemo.AddChild(new Label("Label Examples:"));
        labelDemo.AddChild(new Label("Обычный текст"));
        labelDemo.AddChild(new Label("Центрированный") { Style = { TextAlign = AlignText.Center, BackgroundColor = new Color(230, 230, 230, 255) } });
        labelDemo.AddChild(new Label("Правый") { Style = { TextAlign = AlignText.Right, BackgroundColor = new Color(230, 230, 230, 255) } });
        grid.AddChildAuto(labelDemo);
        
        // Button демо
        var buttonDemo = new VBox { Gap = 5f };
        buttonDemo.AddChild(new Label("Button Examples:"));
        
        var normalBtn = new Button("Normal Button");
        normalBtn.OnClick += () => ShowToast("Normal button clicked!");
        buttonDemo.AddChild(normalBtn);
        
        var styledBtn = new Button("Styled Button");
        styledBtn.Style.BackgroundColor = new Color(50, 150, 50, 255);
        styledBtn.Style.TextColor = Color.White;
        styledBtn.Style.BorderRadius = 15f;
        styledBtn.OnClick += () => ShowToast("Styled button clicked!");
        buttonDemo.AddChild(styledBtn);
        
        var disabledBtn = new Button("Disabled Button");
        disabledBtn.Enabled = false;
        disabledBtn.Style.BackgroundColor = new Color(150, 150, 150, 255);
        buttonDemo.AddChild(disabledBtn);
        
        grid.AddChildAuto(buttonDemo);
        
        // Toast демо
        var toastDemo = new VBox { Gap = 5f };
        toastDemo.AddChild(new Label("Toast Examples:"));
        
        var infoToastBtn = new Button("Info Toast");
        infoToastBtn.OnClick += () => ShowToast("This is an info message", ToastType.Info);
        toastDemo.AddChild(infoToastBtn);
        
        var warningToastBtn = new Button("Warning Toast");
        warningToastBtn.OnClick += () => ShowToast("This is a warning!", ToastType.Warning);
        toastDemo.AddChild(warningToastBtn);
        
        var errorToastBtn = new Button("Error Toast");
        errorToastBtn.OnClick += () => ShowToast("This is an error!", ToastType.Error);
        toastDemo.AddChild(errorToastBtn);
        
        grid.AddChildAuto(toastDemo);
        
        section.AddContent(grid);
        return section;
    }
    
    private static VisualElement CreateLayoutSection()
    {
        var section = new Card("Layout Контейнеры");
        section.Style.MinWidth = 1100f;
        
        var mainContainer = new VBox { Gap = 15f };
        
        // HBox демо
        var hboxDemo = new VBox { Gap = 5f };
        hboxDemo.AddChild(new Label("HBox (Horizontal Layout):"));
        
        var hbox = new HBox { Gap = 10f };
        hbox.Style.BackgroundColor = new Color(240, 248, 255, 255);
        hbox.Style.Padding = new Padding(10);
        hbox.Style.BorderRadius = 5f;
        
        for (int i = 1; i <= 4; i++)
        {
            var item = new Button($"Item {i}");
            item.Style.BackgroundColor = new Color(100 + i * 30, 150, 200, 255);
            item.Style.TextColor = Color.White;
            hbox.AddChild(item);
        }
        
        hboxDemo.AddChild(hbox);
        mainContainer.AddChild(hboxDemo);
        
        // VBox демо
        var vboxDemo = new VBox { Gap = 5f };
        vboxDemo.AddChild(new Label("VBox (Vertical Layout):"));
        
        var vbox = new VBox { Gap = 5f };
        vbox.Style.BackgroundColor = new Color(255, 248, 240, 255);
        vbox.Style.Padding = new Padding(10);
        vbox.Style.BorderRadius = 5f;
        
        for (int i = 1; i <= 3; i++)
        {
            var item = new Label($"Vertical Item {i}");
            item.Style.BackgroundColor = new Color(200, 100 + i * 30, 150, 255);
            item.Style.TextColor = Color.White;
            item.Style.Padding = new Padding(8);
            item.Style.BorderRadius = 3f;
            vbox.AddChild(item);
        }
        
        vboxDemo.AddChild(vbox);
        mainContainer.AddChild(vboxDemo);
        
        // Grid демо
        var gridDemo = new VBox { Gap = 5f };
        gridDemo.AddChild(new Label("Grid Layout (3x2):"));
        
        var demoGrid = new Grid(3, 2) { ColumnGap = 8f, RowGap = 8f };
        demoGrid.Style.BackgroundColor = new Color(248, 248, 255, 255);
        demoGrid.Style.Padding = new Padding(10);
        demoGrid.Style.BorderRadius = 5f;
        demoGrid.Size = new Vector2(300, 120);
        
        for (int i = 0; i < 6; i++)
        {
            var gridItem = new Label($"Cell {i + 1}");
            gridItem.Style.BackgroundColor = new Color(150 + i * 15, 100 + i * 20, 200, 255);
            gridItem.Style.TextColor = Color.White;
            gridItem.Style.TextAlign = AlignText.Center;
            gridItem.Style.Padding = new Padding(5);
            gridItem.Style.BorderRadius = 3f;
            demoGrid.AddChildAuto(gridItem);
        }
        
        gridDemo.AddChild(demoGrid);
        mainContainer.AddChild(gridDemo);
        
        section.AddContent(mainContainer);
        return section;
    }
    
    private static VisualElement CreateSpecializedContainersSection()
    {
        var section = new Card("Специализированные контейнеры");
        section.Style.MinWidth = 1100f;
        
        var mainContainer = new VBox { Gap = 15f };
        
        // ScrollView демо
        var scrollDemo = new VBox { Gap = 5f };
        scrollDemo.AddChild(new Label("ScrollView:"));
        
        var scrollView = new ScrollView();
        scrollView.Size = new Vector2(400, 150);
        scrollView.Style.BackgroundColor = Color.White;
        scrollView.Style.BorderWidth = 1f;
        scrollView.Style.BorderColor = new Color(200, 200, 200, 255);
        
        var scrollContent = new VBox { Gap = 5f };
        scrollContent.Style.Padding = new Padding(10);
        
        for (int i = 1; i <= 20; i++)
        {
            var scrollItem = new Label($"Scrollable Item {i} - This is a long text to demonstrate scrolling");
            scrollItem.Style.BackgroundColor = i % 2 == 0 ? new Color(245, 245, 245, 255) : Color.White;
            scrollItem.Style.Padding = new Padding(5);
            scrollContent.AddChild(scrollItem);
        }
        
        scrollView.AddChild(scrollContent);
        scrollDemo.AddChild(scrollView);
        mainContainer.AddChild(scrollDemo);
        
        // Panel демо
        var panelDemo = new VBox { Gap = 5f };
        panelDemo.AddChild(new Label("Panel with Collapsible:"));
        
        var panel = new GroupBox("Collapsible Panel");
        panel.Collapsible = true;
        panel.Style.MinWidth = 300f;
        
        var panelContent = new VBox { Gap = 8f };
        panelContent.AddChild(new Label("This content can be collapsed"));
        panelContent.AddChild(new Button("Panel Button"));
        panelContent.AddChild(new Label("More panel content here..."));
        
        panel.AddContent(panelContent);
        panelDemo.AddChild(panel);
        mainContainer.AddChild(panelDemo);
        
        // Foldout демо
        var foldoutDemo = new VBox { Gap = 5f };
        foldoutDemo.AddChild(new Label("Foldout:"));
        
        var foldout = new Foldout("Expandable Section", false);
        foldout.Style.MinWidth = 300f;
        
        var foldoutContent = new VBox { Gap = 5f };
        foldoutContent.AddChild(new Label("Hidden content"));
        foldoutContent.AddChild(new Button("Hidden Button"));
        
        var nestedFoldout = new Foldout("Nested Foldout", false);
        nestedFoldout.AddContent(new Label("Deeply nested content"));
        foldoutContent.AddChild(nestedFoldout);
        
        foldout.AddContent(foldoutContent);
        foldoutDemo.AddChild(foldout);
        mainContainer.AddChild(foldoutDemo);
        
        section.AddContent(mainContainer);
        return section;
    }
    
    private static VisualElement CreateInteractiveSection()
    {
        var section = new Card("Интерактивные элементы");
        section.Style.MinWidth = 1100f;
        
        var mainContainer = new HBox { Gap = 20f };
        
        // Hover эффекты
        var hoverDemo = new VBox { Gap = 10f };
        hoverDemo.AddChild(new Label("Hover Effects:"));
        
        var hoverBox = new VisualElement("HoverBox");
        hoverBox.Size = new Vector2(100, 100);
        hoverBox.Style.BackgroundColor = new Color(100, 150, 200, 255);
        hoverBox.Style.BorderRadius = 10f;
        hoverBox.AddClass("hover-demo");
        
        // Добавляем hover эффект через манипулятор
        var hoverEffect = hoverBox.GetManipulator<HoverEffectManipulator>();
        if (hoverEffect != null)
        {
            // Настраиваем hover эффект
        }
        
        hoverDemo.AddChild(new Label("Hover over the box:"));
        hoverDemo.AddChild(hoverBox);
        mainContainer.AddChild(hoverDemo);
        
        // Click эффекты
        var clickDemo = new VBox { Gap = 10f };
        clickDemo.AddChild(new Label("Click Effects:"));
        
        var clickCounter = 0;
        var clickLabel = new Label($"Clicks: {clickCounter}");
        
        var clickBtn = new Button("Click Me!");
        clickBtn.OnClick += () =>
        {
            clickCounter++;
            clickLabel.Text = $"Clicks: {clickCounter}";
            ShowToast($"Button clicked {clickCounter} times!");
        };
        
        clickDemo.AddChild(clickLabel);
        clickDemo.AddChild(clickBtn);
        mainContainer.AddChild(clickDemo);
        
        // Focus демо
        var focusDemo = new VBox { Gap = 10f };
        focusDemo.AddChild(new Label("Focus Demo:"));
        
        var focusBtn1 = new Button("Focus 1");
        var focusBtn2 = new Button("Focus 2");
        var focusBtn3 = new Button("Focus 3");
        
        focusBtn1.OnClick += () => ShowToast("Focus 1 clicked");
        focusBtn2.OnClick += () => ShowToast("Focus 2 clicked");
        focusBtn3.OnClick += () => ShowToast("Focus 3 clicked");
        
        focusDemo.AddChild(focusBtn1);
        focusDemo.AddChild(focusBtn2);
        focusDemo.AddChild(focusBtn3);
        mainContainer.AddChild(focusDemo);
        
        section.AddContent(mainContainer);
        return section;
    }
    
    private static VisualElement CreateStylingSection()
    {
        var section = new Card("Стилизация и темы");
        section.Style.MinWidth = 1100f;
        
        var mainContainer = new VBox { Gap = 15f };
        
        // Цветовые схемы
        var colorDemo = new VBox { Gap = 10f };
        colorDemo.AddChild(new Label("Color Schemes:"));
        
        var colorGrid = new Grid(4, 2) { ColumnGap = 10f, RowGap = 10f };
        
        var colors = new[]
        {
            ("Primary", new Color(70, 130, 180, 255)),
            ("Success", new Color(40, 167, 69, 255)),
            ("Warning", new Color(255, 193, 7, 255)),
            ("Danger", new Color(220, 53, 69, 255)),
            ("Info", new Color(23, 162, 184, 255)),
            ("Light", new Color(248, 249, 250, 255)),
            ("Dark", new Color(52, 58, 64, 255)),
            ("Custom", new Color(138, 43, 226, 255))
        };
        
        foreach (var (name, color) in colors)
        {
            var colorBox = new Button(name);
            colorBox.Style.BackgroundColor = color;
            colorBox.Style.TextColor = IsLightColor(color) ? Color.Black : Color.White;
            colorBox.Style.BorderRadius = 8f;
            colorBox.OnClick += () => ShowToast($"{name} color selected!");
            colorGrid.AddChildAuto(colorBox);
        }
        
        colorDemo.AddChild(colorGrid);
        mainContainer.AddChild(colorDemo);
        
        // Border radius демо
        var borderDemo = new VBox { Gap = 10f };
        borderDemo.AddChild(new Label("Border Radius Examples:"));
        
        var borderGrid = new HBox { Gap = 15f };
        
        var radiusValues = new[] { 0f, 5f, 10f, 20f };
        foreach (var radius in radiusValues)
        {
            var borderBox = new VisualElement($"Border{radius}");
            borderBox.Size = new Vector2(80, 60);
            borderBox.Style.BackgroundColor = new Color(100, 150, 200, 255);
            borderBox.Style.BorderRadius = radius;
            borderBox.Style.BorderWidth = 2f;
            borderBox.Style.BorderColor = new Color(50, 100, 150, 255);
            
            var borderContainer = new VBox { Gap = 5f };
            borderContainer.AddChild(new Label($"Radius: {radius}") { Style = { TextAlign = AlignText.Center } });
            borderContainer.AddChild(borderBox);
            
            borderGrid.AddChild(borderContainer);
        }
        
        borderDemo.AddChild(borderGrid);
        mainContainer.AddChild(borderDemo);
        
        // Padding и Margin демо
        var spacingDemo = new VBox { Gap = 10f };
        spacingDemo.AddChild(new Label("Padding & Margin:"));
        
        var spacingContainer = new HBox { Gap = 20f };
        
        // Padding пример
        var paddingExample = new VisualElement("PaddingExample");
        paddingExample.Style.BackgroundColor = new Color(255, 240, 240, 255);
        paddingExample.Style.BorderWidth = 2f;
        paddingExample.Style.BorderColor = Color.Red;
        paddingExample.Style.Padding = new Padding(20);
        
        var paddingContent = new Label("Padding: 20px");
        paddingContent.Style.BackgroundColor = new Color(240, 255, 240, 255);
        paddingExample.AddChild(paddingContent);
        
        var paddingContainer = new VBox { Gap = 5f };
        paddingContainer.AddChild(new Label("Padding Example:"));
        paddingContainer.AddChild(paddingExample);
        spacingContainer.AddChild(paddingContainer);
        
        // Margin пример
        var marginContainer = new VBox { Gap = 5f };
        marginContainer.AddChild(new Label("Margin Example:"));
        
        var marginExample = new Label("Margin: 15px");
        marginExample.Style.BackgroundColor = new Color(240, 240, 255, 255);
        marginExample.Style.Margin = new Margin(15);
        marginExample.Style.BorderWidth = 2f;
        marginExample.Style.BorderColor = Color.Blue;
        
        marginContainer.AddChild(marginExample);
        spacingContainer.AddChild(marginContainer);
        
        spacingDemo.AddChild(spacingContainer);
        mainContainer.AddChild(spacingDemo);
        
        section.AddContent(mainContainer);
        return section;
    }
    
    private static bool IsLightColor(Color color)
    {
        // Простая формула для определения светлого цвета
        var brightness = (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255.0;
        return brightness > 0.5;
    }
    
    private static void ShowToast(string message, ToastType type = ToastType.Info)
    {
        // Здесь должна быть логика показа toast уведомления
        // Пока что просто выводим в консоль
        Console.WriteLine($"[{type}] {message}");
    }
}

public enum ToastType
{
    Info,
    Success,
    Warning,
    Error
}