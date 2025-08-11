namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Интерфейс для элементов, которые предоставляют текстовое содержимое для расчета размеров в LayoutEngine
/// </summary>
public interface ITextProvider
{
    /// <summary>
    /// Основной отображаемый текст элемента
    /// </summary>
    string? GetDisplayText();
    
    /// <summary>
    /// Все возможные текстовые варианты (например, для dropdown - все элементы списка)
    /// </summary>
    IEnumerable<string>? GetTextOptions();
    
    /// <summary>
    /// Текст-заглушка (placeholder)
    /// </summary>
    string? GetPlaceholderText();
}