using Karpik.Engine.Client.UIToolkit.Core;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Panel : VisualElement
{
    public Panel(string name = "Panel") : base(name)
    {
        // Добавляем класс по умолчанию
        AddClass("panel");
        
        // Устанавливаем flexbox по умолчанию
        Style.FlexDirection = Core.FlexDirection.Column;
    }
}