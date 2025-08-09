using Karpik.Engine.Client.UIToolkit.Elements;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Manipulators;

public class TooltipManipulator : IManipulator
{
    private VisualElement? _element;
    private Tooltip? _tooltip;
    private float _hoverTimer = 0f;
    private bool _isHovering = false;
    private readonly string _tooltipText;
    private readonly float _showDelay;
    private TooltipManager? _tooltipManager;
    
    public TooltipManipulator(string tooltipText, float showDelay = 0.5f)
    {
        _tooltipText = tooltipText;
        _showDelay = showDelay;
    }
    
    public void Attach(VisualElement element)
    {
        _element = element;
        _tooltip = new Tooltip(_tooltipText) { ShowDelay = _showDelay };
        _tooltip.CalculateSize();
        
        // Находим TooltipManager через UIManager
        _tooltipManager = FindTooltipManager();
    }
    
    public void Detach(VisualElement element)
    {
        if (_tooltip != null)
        {
            if (_tooltipManager != null)
            {
                _tooltipManager.HideTooltip(_tooltip);
            }
            else
            {
                _tooltip.Hide();
                // Удаляем tooltip из родительского элемента
                _tooltip.Parent?.RemoveChild(_tooltip);
            }
        }
        _element = null;
        _tooltip = null;
        _tooltipManager = null;
        _isHovering = false;
        _hoverTimer = 0f;
    }
    
    public void Update(float deltaTime)
    {
        var mousePos = Raylib.GetMousePosition();
        
        if (_isHovering)
        {
            _hoverTimer += deltaTime;
            
            if (_hoverTimer >= _showDelay)
            {
                // Пытаемся найти TooltipManager еще раз, если не нашли при Attach
                if (_tooltipManager == null)
                {
                    _tooltipManager = FindTooltipManager();
                }
                
                // Используем TooltipManager если доступен, иначе простое решение
                if (_tooltipManager != null)
                {
                    _tooltipManager.ShowTooltip(_tooltip, mousePos);
                }
                else
                {
                    // Позиционируем tooltip относительно мыши
                    if (!_tooltip.Visible)
                    {
                        var root = FindRootElement(_element);
                        if (root != null)
                        {
                            root.AddChild(_tooltip);
                            _tooltip.Show(mousePos);
                        }
                    }
                    else
                    {
                        _tooltip.UpdatePosition(mousePos);
                    }
                }
            }
        }
    }

    public bool Handle(InputEvent inputEvent)
    {
        if (_element == null || _tooltip == null) return false;

        if (inputEvent.Type != InputEventType.MouseMove) return false;

        bool handled = false;
        
        var mousePos = inputEvent.MousePosition;
        var isCurrentlyHovering = _element.ContainsPoint(mousePos);
        
        if (isCurrentlyHovering && !_isHovering)
        {
            // Начинаем наведение
            _isHovering = true;
            _hoverTimer = 0f;
            handled = true;
        }
        else if (!isCurrentlyHovering && _isHovering)
        {
            // Заканчиваем наведение
            _isHovering = false;
            _hoverTimer = 0f;
            
            // Пытаемся найти TooltipManager еще раз, если не нашли при Attach
            if (_tooltipManager == null)
            {
                _tooltipManager = FindTooltipManager();
                handled = true;
            }
            
            if (_tooltipManager != null)
            {
                _tooltipManager.HideTooltip(_tooltip);
                handled = true;
            }
            else
            {
                _tooltip.Hide();
                _tooltip.Parent?.RemoveChild(_tooltip);
                handled = true;
            }
        }

        return handled;
    }

    private VisualElement? FindRootElement(VisualElement element)
    {
        var current = element;
        while (current.Parent != null)
        {
            current = current.Parent;
        }
        return current;
    }
    
    private static TooltipManager? FindTooltipManager()
    {
        // Ищем UIManager через статический доступ
        // В реальном приложении это может быть сервис-локатор или DI контейнер
        return UIManagerInstance.Current?.TooltipManager;
    }
}