namespace Karpik.Engine.Client.UIToolkit;

public interface IManipulator
{
    void Attach(VisualElement element);
    void Detach(VisualElement element);
}