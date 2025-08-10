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
                
                // Используем TooltipManager если доступен
                if (_tooltipManager != null)
                {
                    _tooltipManager.ShowTooltip(_tooltip, mousePos);
                }
                else
                {
                    // Если TooltipManager недоступен, логируем предупреждение
                    Console.WriteLine("Warning: TooltipManager not available, tooltip will not be shown");
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
            }
            else
            {
                _tooltip.Hide();
            }
            handled = true;
        }

        return handled;
    }


    
    private static TooltipManager? FindTooltipManager()
    {
        // Ищем UIManager через статический доступ
        // В реальном приложении это может быть сервис-локатор или DI контейнер
        return UIManagerInstance.Current?.TooltipManager;
    }
}