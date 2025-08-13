using Karpik.Engine.Client.UIToolkit;
using KarpikEngineClient.UI.Demo;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Запускает различные демо UI системы
/// </summary>
public static class DemoLauncher
{
    public enum DemoType
    {
        Comprehensive,  // Полное демо со всеми возможностями
        BasicElements,  // Только базовые элементы
        Layouts,        // Только layout системы
        Containers,     // Только контейнеры
        Interactive,    // Интерактивные элементы
        Animation,      // Анимации и эффекты
        Performance     // Тест производительности
    }
    
    /// <summary>
    /// Создает и возвращает демо указанного типа
    /// </summary>
    public static VisualElement CreateDemo(DemoType demoType = DemoType.Comprehensive)
    {
        return demoType switch
        {
            DemoType.Comprehensive => ComprehensiveDemo.CreateMainDemo(),
            DemoType.BasicElements => UIDemo.CreateFullDemo(),
            DemoType.Layouts => CreateLayoutOnlyDemo(),
            DemoType.Containers => CreateContainersOnlyDemo(),
            DemoType.Interactive => InteractiveDemo.CreateAnimationDemo(),
            DemoType.Animation => InteractiveDemo.CreateAnimationDemo(),
            DemoType.Performance => CreatePerformanceDemo(),
            _ => ComprehensiveDemo.CreateMainDemo()
        };
    }
    
    /// <summary>
    /// Быстрый запуск демо для интеграции в Client.cs
    /// </summary>
    public static VisualElement QuickDemo()
    {
        // Создаем простое демо для быстрой интеграции
        var demo = UIDemo.CreateFullDemo();
        
        // Применяем стили
        DemoStyles.ApplyDemoStyles(demo);
        
        return demo;
    }
    
    /// <summary>
    /// Создает демо только с layout системами
    /// </summary>
    private static VisualElement CreateLayoutOnlyDemo()
    {
        // Реализация демо только layout'ов
        return UIDemo.CreateFullDemo(); // Временная заглушка
    }
    
    /// <summary>
    /// Создает демо только с контейнерами
    /// </summary>
    private static VisualElement CreateContainersOnlyDemo()
    {
        // Реализация демо только контейнеров
        return UIDemo.CreateFullDemo(); // Временная заглушка
    }
    
    /// <summary>
    /// Создает демо для тестирования производительности
    /// </summary>
    private static VisualElement CreatePerformanceDemo()
    {
        // Реализация демо производительности
        return UIDemo.CreateFullDemo(); // Временная заглушка
    }
}