namespace Karpik.Engine.Client.UIToolkit;

public class TestManipulator : IManipulator
{
    public UIElement Target { get; set; }
    public void OnMouseEnter()
    {
        Console.WriteLine("Entered");
    }

    public void OnMouseLeave()
    {
        Console.WriteLine("Leave");
    }

    public void OnMouseDown()
    {
        Console.WriteLine("Down");
    }

    public void OnMouseUp()
    {
        Console.WriteLine("Up");
    }

    public void OnClick()
    {
        Console.WriteLine("Clicked");
    }
}