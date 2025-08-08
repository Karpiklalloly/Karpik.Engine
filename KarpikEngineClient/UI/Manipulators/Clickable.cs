namespace Karpik.Engine.Client.UIToolkit;

public class Clickable : IManipulator
{
    public event Action OnClicked;
    public event Action OnPressed;
    public event Action OnReleased;
    
    private VisualElement _attachedElement;
    private bool _isPressed = false;
    
    public void Attach(VisualElement element)
    {
        if (_attachedElement != null)
        {
            Detach(_attachedElement);
        }
        
        _attachedElement = element;
        
        // Подписываемся на события элемента
        _attachedElement.OnMouseDown += HandleMouseDown;
        _attachedElement.OnMouseUp += HandleMouseUp;
        _attachedElement.OnClick += HandleClick;
    }
    
    public void Detach(VisualElement element)
    {
        if (_attachedElement == element)
        {
            // Отписываемся от событий
            _attachedElement.OnMouseDown -= HandleMouseDown;
            _attachedElement.OnMouseUp -= HandleMouseUp;
            _attachedElement.OnClick -= HandleClick;
            _attachedElement = null;
        }
    }
    
    private void HandleMouseDown(MouseEvent mouseEvent)
    {
        if (_attachedElement != null && _attachedElement.Enabled && !_isPressed)
        {
            _isPressed = true;
            OnPressed?.Invoke();
        }
    }
    
    private void HandleMouseUp(MouseEvent mouseEvent)
    {
        if (_attachedElement != null && _attachedElement.Enabled && _isPressed)
        {
            _isPressed = false;
            OnReleased?.Invoke();
        }
    }
    
    private void HandleClick(MouseEvent mouseEvent)
    {
        if (_attachedElement != null && _attachedElement.Enabled)
        {
            OnClicked?.Invoke();
        }
    }
    
    // Свойства для отслеживания состояния
    public bool IsPressed => _isPressed;
    public VisualElement AttachedElement => _attachedElement;
}