using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class Button : VisualElement
{
    public string Text
    {
        get => _label.Text;
        set => _label.Text = value;
    }

    public Font Font
    {
        get => _label.Font;
        set => _label.Font = value;
    }

    public float FontSize
    {
        get => _label.FontSize;
        set => _label.FontSize = value;
    }

    public Color TextColor
    {
        get => _label.Color;
        set => _label.Color = value;
    }
    public Color BackgroundColor { get; set; } = Color.DarkGray;
    public Color HoverBackgroundColor { get; set; } = Color.Gray;
    public Color PressedBackgroundColor { get; set; } = Color.LightGray;
    public Color DisabledBackgroundColor { get; set; } = Color.DarkGray;
    public Texture2D BackgroundTexture { get; set; }
    
    public event Action Clicked;

    private Label _label;
    private Panel _panel;
    private bool _isPressed;
    private Color _currentColor;
    
    public Button(Vector2 size, string text) : base(size)
    {
        _label = new Label(text);
        _panel = new Panel(size);
        Text = text;
        Add(_panel);
        Add(_label);
    }

    protected override void HandleInput()
    {
        if (!IsEnabled)
        {
            _isPressed = false;
            Invalidate();
        }

        if (!IsHovered)
        {
            _isPressed = false;
            Invalidate();
            return;
        }

        if (Input.IsMouseLeftButtonDown)
        {
            Clicked?.Invoke();
            _isPressed = true;
            Invalidate();
        }
        else if (Input.IsMouseLeftButtonHold)
        {
            _isPressed = true;
            Invalidate();
        }
        else
        {
            _isPressed = false;
            Invalidate();
        }
    }

    protected override void DrawSelf()
    {
        Color currentBgColor = BackgroundColor;
        if (!IsEnabled)
        {
            currentBgColor = DisabledBackgroundColor;
        }
        else if (_isPressed)
        {
            currentBgColor = PressedBackgroundColor;
        }
        else if (IsHovered)
        {
            currentBgColor = HoverBackgroundColor;
        }
        
        _panel.Color = currentBgColor;
        _panel.Size = Bounds.Size;
    }
    
    
}