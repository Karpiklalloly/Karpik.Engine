using Karpik.Engine.Client.UIToolkit;

namespace KarpikEngineClient.UI.Demo;

/// <summary>
/// Пример интеграции UI демо в основной клиент
/// </summary>
public static class ClientIntegrationExample
{
    /// <summary>
    /// Замените метод CreateNewUI() в Client.cs на этот код для демонстрации UI
    /// </summary>
    public static VisualElement CreateDemoUI()
    {
        // Вариант 1: Быстрое демо (рекомендуется для начала)
        return DemoLauncher.QuickDemo();
        
        // Вариант 2: Полное комплексное демо
        // return DemoLauncher.CreateDemo(DemoLauncher.DemoType.Comprehensive);
        
        // Вариант 3: Специфичное демо
        // return DemoLauncher.CreateDemo(DemoLauncher.DemoType.Interactive);
    }
    
    /// <summary>
    /// Пример полной замены метода CreateNewUI() в Client.cs
    /// </summary>
    public static void ReplaceCreateNewUIMethod()
    {
        /*
        Замените весь метод CreateNewUI() в Client.cs на следующий код:
        
        private void CreateNewUI()
        {
            // Создаем демо UI вместо существующего кода
            var demoRoot = ClientIntegrationExample.CreateDemoUI();
            _uiManager.SetRoot(demoRoot);
        }
        
        Или для более продвинутого демо:
        
        private void CreateNewUI()
        {
            // Создаем комплексное демо со всеми возможностями
            var comprehensiveDemo = DemoLauncher.CreateDemo(DemoLauncher.DemoType.Comprehensive);
            _uiManager.SetRoot(comprehensiveDemo);
        }
        */
    }
    
    /// <summary>
    /// Пример создания кастомного демо с дополнительными элементами
    /// </summary>
    public static VisualElement CreateCustomDemo()
    {
        // Получаем базовое демо
        var baseDemo = DemoLauncher.QuickDemo();
        
        // Применяем кастомные стили
        DemoStyles.ApplyDemoStyles(baseDemo);
        
        // Можно добавить дополнительные элементы
        // baseDemo.AddChild(new CustomElement());
        
        return baseDemo;
    }
}