namespace Karpik.Engine.Client.UIToolkit;

public interface IManipulator
{
    void Attach(VisualElement element);
    void Detach(VisualElement element);
    void Update(float deltaTime);
    bool Handle(InputEvent inputEvent);
}

public abstract class Manipulator : IManipulator
{
    protected VisualElement? Element { get; private set; }
    
    public virtual void Attach(VisualElement element)
    {
        Element = element;
        OnAttach();
    }
    
    public virtual void Detach(VisualElement element)
    {
        OnDetach();
        Element = null;
    }
    
    public abstract void Update(float deltaTime);
    public abstract bool Handle(InputEvent inputEvent);

    protected virtual void OnAttach() { }
    protected virtual void OnDetach() { }
}