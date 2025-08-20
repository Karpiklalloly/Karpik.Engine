namespace Karpik.Engine.Client.UIToolkit;

public class DropdownManipulator : IManipulator
{
    private readonly string _panelId;
    public DropdownManipulator(string panelId)
    {
        _panelId = panelId;
    }

    public UIElement Target { get; set; }

    public void OnMouseEnter()
    {
        var panel = Target.Children.FirstOrDefault(c => c.Id == _panelId);
        if (panel != null)
        {
            Console.WriteLine("Enter");
            // Используем InlineStyles, так как это динамическое изменение
            panel.InlineStyles["display"] = "flex";
        }
    }

    public void OnMouseLeave()
    {
        var panel = Target.Children.FirstOrDefault(c => c.Id == _panelId);
        if (panel != null)
        {
            Console.WriteLine("leave");
            panel.InlineStyles["display"] = "none";
        }
    }

    public void OnMouseDown()
    {
        
    }

    public void OnMouseUp()
    {
        
    }

    public void OnClick()
    {
        
    }
}