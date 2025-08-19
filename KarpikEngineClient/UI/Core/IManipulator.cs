namespace Karpik.Engine.Client.UIToolkit;

public interface IManipulator
{
    public UIElement Target { get; set; }
    public void OnMouseEnter();
    public void OnMouseLeave();
    public void OnMouseDown();
    public void OnMouseUp();
    public void OnClick();
}