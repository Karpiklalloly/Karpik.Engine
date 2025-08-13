using System.Numerics;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Интерактивное демо с анимациями и эффектами
/// </summary>
public static class InteractiveDemo
{
    private static Random _random = new Random();
    
    /// <summary>
    /// Создает интерактивное демо с анимациями
    /// </summary>
    public static VisualElement CreateAnimationDemo()
    {
        var root = new VisualElement("AnimationDemoRoot");
        root.Size = new Vector2(800, 600);
        root.Style.BackgroundColor = new Color(30, 30, 40, 255);
        root.Style.Padding = new Padding(20);
        
        // Применяем стили
        DemoStyles.ApplyDemoStyles(root);
        
        var mainContainer = new VBox { Gap = 20f };
        root.AddChild(mainContainer);
        
        // Заголовок
        var title = new Label("Interactive Animation Demo");
        title.AddClass("demo-title");
        title.Style.TextColor = Color.White;
        title.Style.TextAlign = AlignText.Center;
        mainContainer.AddChild(title);
        
        // Панель управления анимациями
        var controlPanel = CreateAnimationControlPanel();
        mainContainer.AddChild(controlPanel);
        
        // Область для анимированных элементов
        var animationArea = CreateAnimationArea();
        mainContainer.AddChild(animationArea);
        
        // Демо различных эффектов
        var effectsDemo = CreateEffectsDemo();
        mainContainer.AddChild(effectsDemo);
        
        return root;
    }
    
    private static VisualElement CreateAnimationControlPanel()
    {
        var panel = new Card("Animation Controls");
        panel.Style.BackgroundColor = new Color(50, 50, 60, 255);
        panel.Style.BorderColor = new Color(100, 100, 120, 255);
        
        var controlGrid = new Grid(4, 2) { ColumnGap = 10f, RowGap = 10f };
        
        // Создаем анимированный элемент для демонстрации
        var animatedBox = new VisualElement("AnimatedBox");
        animatedBox.Size = new Vector2(60, 60);
        animatedBox.Position = new Vector2(400, 100);
        animatedBox.Style.BackgroundColor = new Color(255, 100, 100, 255);
        animatedBox.Style.BorderRadius = 10f;
        
        // Кнопки управления анимациями
        var fadeInBtn = new Button("Fade In");
        fadeInBtn.AddClass("primary");
        fadeInBtn.OnClick += () => AnimateFadeIn(animatedBox);
        controlGrid.AddChildAuto(fadeInBtn);
        
        var fadeOutBtn = new Button("Fade Out");
        fadeOutBtn.AddClass("primary");
        fadeOutBtn.OnClick += () => AnimateFadeOut(animatedBox);
        controlGrid.AddChildAuto(fadeOutBtn);
        
        var scaleBtn = new Button("Scale");
        scaleBtn.AddClass("success");
        scaleBtn.OnClick += () => AnimateScale(animatedBox);
        controlGrid.AddChildAuto(scaleBtn);
        
        var rotateBtn = new Button("Rotate");
        rotateBtn.AddClass("success");
        rotateBtn.OnClick += () => AnimateRotate(animatedBox);
        controlGrid.AddChildAuto(rotateBtn);
        
        var moveBtn = new Button("Move");
        moveBtn.AddClass("warning");
        moveBtn.OnClick += () => AnimateMove(animatedBox);
        controlGrid.AddChildAuto(moveBtn);
        
        var colorBtn = new Button("Color");
        colorBtn.AddClass("warning");
        colorBtn.OnClick += () => AnimateColor(animatedBox);
        controlGrid.AddChildAuto(colorBtn);
        
        var shakeBtn = new Button("Shake");
        shakeBtn.AddClass("danger");
        shakeBtn.OnClick += () => AnimateShake(animatedBox);
        controlGrid.AddChildAuto(shakeBtn);
        
        var pulseBtn = new Button("Pulse");
        pulseBtn.AddClass("info");
        pulseBtn.OnClick += () => AnimatePulse(animatedBox);
        controlGrid.AddChildAuto(pulseBtn);
        
        panel.AddContent(controlGrid);
        
        // Добавляем анимированный элемент в панель
        panel.AddContent(animatedBox);
        
        return panel;
    }
    
    private static VisualElement CreateAnimationArea()
    {
        var area = new VisualElement("AnimationArea");
        area.Size = new Vector2(760, 200);
        area.Style.BackgroundColor = new Color(40, 40, 50, 255);
        area.Style.BorderWidth = 2f;
        area.Style.BorderColor = new Color(80, 80, 100, 255);
        area.Style.BorderRadius = 8f;
        area.Style.Padding = new Padding(20);
        
        // Создаем несколько элементов для демонстрации
        for (int i = 0; i < 5; i++)
        {
            var element = new VisualElement($"Element{i}");
            element.Size = new Vector2(40, 40);
            element.Position = new Vector2(50 + i * 80, 80);
            element.Style.BackgroundColor = GetRandomColor();
            element.Style.BorderRadius = 20f;
            
            // Добавляем интерактивность
            var clickable = new ClickableManipulator();
            clickable.OnClicked += () => OnElementClicked(element);
            element.AddManipulator(clickable);
            
            area.AddChild(element);
        }
        
        return area;
    }
    
    private static VisualElement CreateEffectsDemo()
    {
        var demo = new Card("Visual Effects Demo");
        demo.Style.BackgroundColor = new Color(50, 50, 60, 255);
        
        var effectsContainer = new HBox { Gap = 15f };
        
        // Glow эффект
        var glowBox = new VisualElement("GlowBox");
        glowBox.Size = new Vector2(80, 80);
        glowBox.Style.BackgroundColor = new Color(100, 200, 255, 255);
        glowBox.Style.BorderRadius = 40f;
        // Здесь можно добавить glow эффект через шейдеры
        
        var glowContainer = new VBox { Gap = 5f };
        glowContainer.AddChild(new Label("Glow Effect") { Style = { TextColor = Color.White, TextAlign = AlignText.Center } });
        glowContainer.AddChild(glowBox);
        effectsContainer.AddChild(glowContainer);
        
        // Gradient эффект
        var gradientBox = new VisualElement("GradientBox");
        gradientBox.Size = new Vector2(80, 80);
        gradientBox.Style.BackgroundColor = new Color(255, 100, 150, 255);
        gradientBox.Style.BorderRadius = 10f;
        
        var gradientContainer = new VBox { Gap = 5f };
        gradientContainer.AddChild(new Label("Gradient") { Style = { TextColor = Color.White, TextAlign = AlignText.Center } });
        gradientContainer.AddChild(gradientBox);
        effectsContainer.AddChild(gradientContainer);
        
        // Shadow эффект
        var shadowBox = new VisualElement("ShadowBox");
        shadowBox.Size = new Vector2(80, 80);
        shadowBox.Style.BackgroundColor = new Color(150, 255, 150, 255);
        shadowBox.Style.BorderRadius = 8f;
        
        var shadowContainer = new VBox { Gap = 5f };
        shadowContainer.AddChild(new Label("Shadow") { Style = { TextColor = Color.White, TextAlign = AlignText.Center } });
        shadowContainer.AddChild(shadowBox);
        effectsContainer.AddChild(shadowContainer);
        
        // Particle эффект
        var particleBox = new VisualElement("ParticleBox");
        particleBox.Size = new Vector2(80, 80);
        particleBox.Style.BackgroundColor = new Color(255, 200, 100, 255);
        particleBox.Style.BorderRadius = 15f;
        
        var particleContainer = new VBox { Gap = 5f };
        particleContainer.AddChild(new Label("Particles") { Style = { TextColor = Color.White, TextAlign = AlignText.Center } });
        particleContainer.AddChild(particleBox);
        effectsContainer.AddChild(particleContainer);
        
        demo.AddContent(effectsContainer);
        return demo;
    }
    
    // Методы анимации (заглушки для демонстрации)
    private static void AnimateFadeIn(VisualElement element)
    {
        // Здесь должна быть анимация fade in
        element.Style.BackgroundColor = new Color(255, 100, 100, 255);
        Console.WriteLine("Fade In animation triggered");
    }
    
    private static void AnimateFadeOut(VisualElement element)
    {
        // Здесь должна быть анимация fade out
        element.Style.BackgroundColor = new Color(255, 100, 100, 100);
        Console.WriteLine("Fade Out animation triggered");
    }
    
    private static void AnimateScale(VisualElement element)
    {
        // Анимация масштабирования
        var newSize = element.Size * 1.5f;
        element.Size = newSize;
        Console.WriteLine("Scale animation triggered");
    }
    
    private static void AnimateRotate(VisualElement element)
    {
        // Здесь должна быть анимация поворота
        Console.WriteLine("Rotate animation triggered");
    }
    
    private static void AnimateMove(VisualElement element)
    {
        // Анимация перемещения
        var newPos = new Vector2(
            _random.Next(50, 600),
            _random.Next(50, 150)
        );
        element.Position = newPos;
        Console.WriteLine($"Move animation to {newPos}");
    }
    
    private static void AnimateColor(VisualElement element)
    {
        // Анимация цвета
        element.Style.BackgroundColor = GetRandomColor();
        Console.WriteLine("Color animation triggered");
    }
    
    private static void AnimateShake(VisualElement element)
    {
        // Здесь должна быть анимация тряски
        Console.WriteLine("Shake animation triggered");
    }
    
    private static void AnimatePulse(VisualElement element)
    {
        // Анимация пульсации
        var originalSize = element.Size;
        element.Size = originalSize * 1.2f;
        // Через некоторое время вернуть к исходному размеру
        Console.WriteLine("Pulse animation triggered");
    }
    
    private static void OnElementClicked(VisualElement element)
    {
        // Обработка клика по элементу
        element.Style.BackgroundColor = GetRandomColor();
        AnimateScale(element);
        Console.WriteLine($"Element {element.Name} clicked!");
    }
    
    private static Color GetRandomColor()
    {
        return new Color(
            (byte)_random.Next(100, 255),
            (byte)_random.Next(100, 255),
            (byte)_random.Next(100, 255),
            (byte)255
        );
    }
}