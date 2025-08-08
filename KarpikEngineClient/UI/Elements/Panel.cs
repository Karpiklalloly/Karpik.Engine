using Karpik.Engine.Client.UIToolkit;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Panel : VisualElement
{
    public Panel(string name = "Panel") : base(name)
    {
        // Добавляем класс по умолчанию
        AddClass("panel");
        
        // Устанавливаем flexbox по умолчанию
        Style.FlexDirection = UIToolkit.FlexDirection.Column;
    }
}