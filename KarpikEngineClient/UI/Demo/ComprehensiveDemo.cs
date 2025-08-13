using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Raylib_cs;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Комплексное демо всех возможностей UI системы KarpikEngine
/// </summary>
public static class ComprehensiveDemo
{
    private static int _demoMode = 0;
    private static VisualElement? _currentDemo;
    
    /// <summary>
    /// Создает главное демо с переключением между различными режимами
    /// </summary>
    public static VisualElement CreateMainDemo()
    {
        var root = new VisualElement("MainDemoRoot");
        root.Size = new Vector2(1400, 900);
        root.Style.BackgroundColor = new Color(25, 25, 35, 255);
        
        // Применяем глобальные стили
        DemoStyles.ApplyDemoStyles(root);
        
        var mainContainer = new VBox();
        root.AddChild(mainContainer);
        
        // Создаем навигационную панель
        var navbar = CreateNavigationBar();
        mainContainer.AddChild(navbar);
        
        // Контейнер для текущего демо
        var demoContainer = new VisualElement("DemoContainer");
        //demoContainer.Size = new Vector2(1400, 820);
        demoContainer.Style.Padding = new Padding(10);
        mainContainer.AddChild(demoContainer);
        
        // Загружаем первое демо
        LoadDemo(demoContainer, 0);
        
        return root;
    }
    
    private static VisualElement CreateNavigationBar()
    {
        var navbar = new HBox { Gap = 10f };
        navbar.Size = new Vector2(1400, 80);
        navbar.Style.BackgroundColor = new Color(40, 40, 50, 255);
        navbar.Style.Padding = new Padding(15);
        navbar.Style.BorderWidth = 0f;
        navbar.Style.BorderColor = new Color(80, 80, 100, 255);
        
        // Заголовок
        var title = new Label("KarpikEngine UI System Demo");
        title.Style.FontSize = 24;
        // title.Style.FontWeight = FontWeight.Bold;
        title.Style.TextColor = Color.White;
        title.Style.Padding = new Padding(0, 0, 20, 0);
        navbar.AddChild(title);
        
        // Кнопки переключения демо
        var demoButtons = new[]
        {
            ("Basic Elements", 0),
            ("Layout System", 1),
            ("Containers", 2),
            ("Interactive", 3),
            ("Animations", 4),
            ("Styling", 5),
            ("Performance", 6)
        };
        
        foreach (var (name, index) in demoButtons)
        {
            var btn = new Button(name);
            btn.Style.BackgroundColor = _demoMode == index ? 
                new Color(70, 130, 180, 255) : 
                new Color(60, 60, 70, 255);
            btn.Style.TextColor = Color.White;
            btn.Style.BorderRadius = 6f;
            btn.Style.Padding = new Padding(12, 8);
            btn.Style.Margin = new Margin(2);
            
            var currentIndex = index; // Захватываем значение для замыкания
            btn.OnClick += () => SwitchDemo(currentIndex);
            
            navbar.AddChild(btn);
        }
        
        // Информационная панель
        var infoPanel = new HBox { Gap = 10f };
        infoPanel.Style.Margin = new Margin(20, 0, 0, 0);
        
        var fpsLabel = new Label("FPS: 60");
        fpsLabel.Style.TextColor = new Color(150, 255, 150, 255);
        fpsLabel.Style.FontSize = 12;
        infoPanel.AddChild(fpsLabel);
        
        var elementsLabel = new Label("Elements: 0");
        elementsLabel.Style.TextColor = new Color(150, 200, 255, 255);
        elementsLabel.Style.FontSize = 12;
        infoPanel.AddChild(elementsLabel);
        
        navbar.AddChild(infoPanel);
        
        return navbar;
    }
    
    private static void SwitchDemo(int demoIndex)
    {
        _demoMode = demoIndex;
        
        // Найдем контейнер демо и обновим его
        // Это упрощенная версия - в реальной реализации нужно найти контейнер
        Console.WriteLine($"Switching to demo mode: {demoIndex}");
    }
    
    private static void LoadDemo(VisualElement container, int demoIndex)
    {
        // Очищаем предыдущее демо
        container.Children.Clear();
        
        VisualElement demo = demoIndex switch
        {
            0 => CreateBasicElementsDemo(),
            1 => CreateLayoutSystemDemo(),
            2 => CreateContainersDemo(),
            3 => CreateInteractiveDemo(),
            4 => InteractiveDemo.CreateAnimationDemo(),
            5 => CreateStylingDemo(),
            6 => CreatePerformanceDemo(),
            _ => CreateBasicElementsDemo()
        };
        
        container.AddChild(demo);
        _currentDemo = demo;
    }
    
    private static VisualElement CreateBasicElementsDemo()
    {
        var demo = new ScrollView();
        demo.Size = new Vector2(1380, 800);
        demo.EnableVerticalScroll = true;
        
        var content = UIDemo.CreateFullDemo();
        demo.AddChild(content);
        
        return demo;
    }
    
    private static VisualElement CreateLayoutSystemDemo()
    {
        var demo = new VBox { Gap = 20f };
        demo.Style.Padding = new Padding(20);
        
        // Заголовок секции
        var title = new Label("Layout System Showcase");
        title.Style.FontSize = 28;
        // title.Style.FontWeight = FontWeight.Bold;
        title.Style.TextColor = Color.White;
        title.Style.TextAlign = AlignText.Center;
        title.Style.Padding = new Padding(20);
        demo.AddChild(title);
        
        // Демо различных layout'ов
        var layoutGrid = new Grid(2, 2) { ColumnGap = 20f, RowGap = 20f };
        
        // HBox демо
        var hboxDemo = CreateLayoutDemo("HBox Layout", () =>
        {
            var hbox = new HBox { Gap = 10f };
            hbox.Style.BackgroundColor = new Color(60, 60, 80, 200);
            hbox.Style.Padding = new Padding(15);
            hbox.Style.BorderRadius = 8f;
            
            for (int i = 1; i <= 5; i++)
            {
                var item = new Button($"H{i}");
                item.Style.BackgroundColor = new Color(100 + i * 20, 150, 200, 255);
                hbox.AddChild(item);
            }
            return hbox;
        });
        layoutGrid.AddChildAuto(hboxDemo);
        
        // VBox демо
        var vboxDemo = CreateLayoutDemo("VBox Layout", () =>
        {
            var vbox = new VBox { Gap = 8f };
            vbox.Style.BackgroundColor = new Color(80, 60, 60, 200);
            vbox.Style.Padding = new Padding(15);
            vbox.Style.BorderRadius = 8f;
            
            for (int i = 1; i <= 4; i++)
            {
                var item = new Label($"Vertical Item {i}");
                item.Style.BackgroundColor = new Color(200, 100 + i * 25, 150, 255);
                item.Style.TextColor = Color.White;
                item.Style.Padding = new Padding(8);
                item.Style.BorderRadius = 4f;
                vbox.AddChild(item);
            }
            return vbox;
        });
        layoutGrid.AddChildAuto(vboxDemo);
        
        // Grid демо
        var gridDemo = CreateLayoutDemo("Grid Layout", () =>
        {
            var grid = new Grid(3, 3) { ColumnGap = 5f, RowGap = 5f };
            grid.Style.BackgroundColor = new Color(60, 80, 60, 200);
            grid.Style.Padding = new Padding(15);
            grid.Style.BorderRadius = 8f;
            grid.Size = new Vector2(300, 200);
            
            for (int i = 0; i < 9; i++)
            {
                var cell = new Label($"{i + 1}");
                cell.Style.BackgroundColor = new Color(150 + i * 10, 200, 100 + i * 15, 255);
                cell.Style.TextColor = Color.White;
                cell.Style.TextAlign = AlignText.Center;
                cell.Style.Padding = new Padding(5);
                cell.Style.BorderRadius = 3f;
                grid.AddChildAuto(cell);
            }
            return grid;
        });
        layoutGrid.AddChildAuto(gridDemo);
        
        // Nested Layout демо
        var nestedDemo = CreateLayoutDemo("Nested Layouts", () =>
        {
            var container = new VBox { Gap = 10f };
            container.Style.BackgroundColor = new Color(80, 60, 80, 200);
            container.Style.Padding = new Padding(15);
            container.Style.BorderRadius = 8f;
            
            var header = new HBox { Gap = 5f };
            header.AddChild(new Button("Header 1"));
            header.AddChild(new Button("Header 2"));
            container.AddChild(header);
            
            var body = new HBox { Gap = 10f };
            var sidebar = new VBox { Gap = 5f };
            sidebar.AddChild(new Label("Sidebar"));
            sidebar.AddChild(new Button("Menu 1"));
            sidebar.AddChild(new Button("Menu 2"));
            
            var content = new Label("Main Content Area");
            content.Style.BackgroundColor = new Color(200, 200, 220, 255);
            content.Style.Padding = new Padding(20);
            content.Style.BorderRadius = 5f;
            
            body.AddChild(sidebar);
            body.AddChild(content);
            container.AddChild(body);
            
            return container;
        });
        layoutGrid.AddChildAuto(nestedDemo);
        
        demo.AddChild(layoutGrid);
        
        return demo;
    }
    
    private static VisualElement CreateLayoutDemo(string title, Func<VisualElement> createLayout)
    {
        var container = new VBox { Gap = 10f };
        container.Style.MinWidth = 350f;
        container.Style.MinHeight = 250f;
        
        var titleLabel = new Label(title);
        titleLabel.Style.FontSize = 16;
        // titleLabel.Style.FontWeight = FontWeight.Bold;
        titleLabel.Style.TextColor = Color.White;
        titleLabel.Style.TextAlign = AlignText.Center;
        titleLabel.Style.BackgroundColor = new Color(50, 50, 70, 255);
        titleLabel.Style.Padding = new Padding(10);
        titleLabel.Style.BorderRadius = 5f;
        container.AddChild(titleLabel);
        
        var layout = createLayout();
        container.AddChild(layout);
        
        return container;
    }
    
    private static VisualElement CreateContainersDemo()
    {
        var demo = new HBox { Gap = 20f };
        demo.Style.Padding = new Padding(20);
        
        // Левая колонка - ScrollView и Panel
        var leftColumn = new VBox { Gap = 15f };
        
        // ScrollView демо
        var scrollDemo = new Card("ScrollView Demo");
        scrollDemo.Style.BackgroundColor = new Color(50, 50, 70, 255);
        scrollDemo.Style.MinWidth = 400f;
        
        var scrollView = new ScrollView();
        scrollView.Size = new Vector2(350, 200);
        scrollView.EnableVerticalScroll = true;
        scrollView.Style.BackgroundColor = Color.White;
        
        var scrollContent = new VBox { Gap = 5f };
        for (int i = 1; i <= 30; i++)
        {
            var item = new Label($"Scrollable Item {i} - Long text to demonstrate scrolling behavior");
            item.Style.BackgroundColor = i % 2 == 0 ? new Color(240, 240, 240, 255) : Color.White;
            item.Style.Padding = new Padding(8);
            scrollContent.AddChild(item);
        }
        
        scrollView.AddChild(scrollContent);
        scrollDemo.AddContent(scrollView);
        leftColumn.AddChild(scrollDemo);
        
        // Panel демо
        var panelDemo = new GroupBox("Collapsible Panel Demo");
        panelDemo.Collapsible = true;
        panelDemo.Style.BackgroundColor = new Color(70, 50, 50, 255);
        panelDemo.Style.MinWidth = 400f;
        
        var panelContent = new VBox { Gap = 10f };
        panelContent.AddChild(new Label("This panel can be collapsed") { Style = { TextColor = Color.White } });
        panelContent.AddChild(new Button("Panel Button"));
        panelContent.AddChild(new Label("More content...") { Style = { TextColor = Color.White } });
        
        panelDemo.AddContent(panelContent);
        leftColumn.AddChild(panelDemo);
        
        demo.AddChild(leftColumn);
        
        // Правая колонка - Foldout и Card
        var rightColumn = new VBox { Gap = 15f };
        
        // Foldout демо
        var foldoutDemo = new VBox { Gap = 10f };
        foldoutDemo.Style.MinWidth = 400f;
        
        var foldout1 = new Foldout("Expandable Section 1", false);
        foldout1.Style.BackgroundColor = new Color(50, 70, 50, 255);
        
        var foldout1Content = new VBox { Gap = 5f };
        foldout1Content.AddChild(new Label("Content of section 1") { Style = { TextColor = Color.White } });
        foldout1Content.AddChild(new Button("Action 1"));
        foldout1.AddContent(foldout1Content);
        
        var foldout2 = new Foldout("Expandable Section 2", true);
        foldout2.Style.BackgroundColor = new Color(70, 50, 70, 255);
        
        var foldout2Content = new VBox { Gap = 5f };
        foldout2Content.AddChild(new Label("Content of section 2") { Style = { TextColor = Color.White } });
        foldout2Content.AddChild(new Button("Action 2"));
        
        // Nested foldout
        var nestedFoldout = new Foldout("Nested Foldout", false);
        nestedFoldout.AddContent(new Label("Deeply nested content") { Style = { TextColor = Color.White } });
        foldout2Content.AddChild(nestedFoldout);
        
        foldout2.AddContent(foldout2Content);
        
        foldoutDemo.AddChild(foldout1);
        foldoutDemo.AddChild(foldout2);
        rightColumn.AddChild(foldoutDemo);
        
        // Card демо
        var cardDemo = new Card("Card with Shadow");
        cardDemo.ShowShadow = true;
        cardDemo.ShadowOffset = 6f;
        cardDemo.Style.BackgroundColor = new Color(60, 60, 80, 255);
        cardDemo.Style.MinWidth = 400f;
        
        var cardContent = new VBox { Gap = 10f };
        cardContent.AddChild(new Label("This is a card with shadow effect") { Style = { TextColor = Color.White } });
        cardContent.AddChild(new Button("Card Action"));
        
        var cardGrid = new Grid(2, 2) { ColumnGap = 5f, RowGap = 5f };
        cardGrid.Size = new Vector2(200, 100);
        
        for (int i = 0; i < 4; i++)
        {
            var gridItem = new Label($"Item {i + 1}");
            gridItem.Style.BackgroundColor = new Color(100 + i * 30, 150, 200, 255);
            gridItem.Style.TextColor = Color.White;
            gridItem.Style.TextAlign = AlignText.Center;
            gridItem.Style.Padding = new Padding(5);
            gridItem.Style.BorderRadius = 3f;
            cardGrid.AddChildAuto(gridItem);
        }
        
        cardContent.AddChild(cardGrid);
        cardDemo.AddContent(cardContent);
        rightColumn.AddChild(cardDemo);
        
        demo.AddChild(rightColumn);
        
        return demo;
    }
    
    private static VisualElement CreateInteractiveDemo()
    {
        return InteractiveDemo.CreateAnimationDemo();
    }
    
    private static VisualElement CreateStylingDemo()
    {
        var demo = new VBox { Gap = 20f };
        demo.Style.Padding = new Padding(20);
        
        var title = new Label("Styling & Theming Demo");
        title.Style.FontSize = 24;
        //title.Style.FontWeight = FontWeight.Bold;
        title.Style.TextColor = Color.White;
        title.Style.TextAlign = AlignText.Center;
        demo.AddChild(title);
        
        // Цветовые схемы
        var colorSection = new Card("Color Schemes");
        colorSection.Style.BackgroundColor = new Color(50, 50, 70, 255);
        
        var colorGrid = new Grid(4, 2) { ColumnGap = 10f, RowGap = 10f };
        
        var colorSchemes = new[]
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
        
        foreach (var (name, color) in colorSchemes)
        {
            var colorBtn = new Button(name);
            colorBtn.Style.BackgroundColor = color;
            colorBtn.Style.TextColor = IsLightColor(color) ? Color.Black : Color.White;
            colorBtn.Style.BorderRadius = 8f;
            colorBtn.OnClick += () => Console.WriteLine($"{name} theme selected");
            colorGrid.AddChildAuto(colorBtn);
        }
        
        colorSection.AddContent(colorGrid);
        demo.AddChild(colorSection);
        
        return demo;
    }
    
    private static VisualElement CreatePerformanceDemo()
    {
        var demo = new VBox { Gap = 20f };
        demo.Style.Padding = new Padding(20);
        
        var title = new Label("Performance Test Demo");
        title.Style.FontSize = 24;
        // title.Style.FontWeight = FontWeight.Bold;
        title.Style.TextColor = Color.White;
        title.Style.TextAlign = AlignText.Center;
        demo.AddChild(title);
        
        // Контролы для тестирования производительности
        var controlPanel = new HBox { Gap = 15f };
        
        var addElementsBtn = new Button("Add 100 Elements");
        addElementsBtn.AddClass("success");
        addElementsBtn.OnClick += () => AddPerformanceElements(demo, 100);
        controlPanel.AddChild(addElementsBtn);
        
        var add1000Btn = new Button("Add 1000 Elements");
        add1000Btn.AddClass("warning");
        add1000Btn.OnClick += () => AddPerformanceElements(demo, 1000);
        controlPanel.AddChild(add1000Btn);
        
        var clearBtn = new Button("Clear All");
        clearBtn.AddClass("danger");
        clearBtn.OnClick += () => ClearPerformanceElements(demo);
        controlPanel.AddChild(clearBtn);
        
        demo.AddChild(controlPanel);
        
        // Контейнер для элементов производительности
        var perfContainer = new ScrollView();
        perfContainer.Size = new Vector2(1300, 600);
        perfContainer.EnableVerticalScroll = true;
        perfContainer.Style.BackgroundColor = new Color(40, 40, 50, 255);
        
        var perfGrid = new Grid(10, 10) { ColumnGap = 2f, RowGap = 2f };
        perfGrid.AutoRows = true;
        perfGrid.Style.Padding = new Padding(10);
        
        perfContainer.AddChild(perfGrid);
        demo.AddChild(perfContainer);
        
        return demo;
    }
    
    private static void AddPerformanceElements(VisualElement demo, int count)
    {
        // Найти grid контейнер и добавить элементы
        Console.WriteLine($"Adding {count} performance test elements");
        
        // В реальной реализации здесь будет поиск grid контейнера
        // и добавление элементов для тестирования производительности
    }
    
    private static void ClearPerformanceElements(VisualElement demo)
    {
        Console.WriteLine("Clearing all performance test elements");
        
        // В реальной реализации здесь будет очистка элементов
    }
    
    private static bool IsLightColor(Color color)
    {
        var brightness = (color.R * 0.299 + color.G * 0.587 + color.B * 0.114) / 255.0;
        return brightness > 0.5;
    }
}