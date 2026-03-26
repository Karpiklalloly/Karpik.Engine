using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.UI.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using GraphicsRenderer = Karpik.Engine.Client.Graphics.Core.IRenderer;
using GameUIInputSystem = Karpik.Engine.Client.UI.Core.InputSystem;

namespace Karpik.Engine.MyGame.Client.Main;

public class GameUIDemo
{
    private WidgetStorage _storage = new();
    private WidgetTree _tree = null!;
    private WidgetEvents _events = null!;
    private EventDispatcher _dispatcher = null!;
    private GameUIInputSystem _inputSystem = null!;
    private StyleEngine _styleEngine = null!;
    
    private GraphicsRenderer _renderer = null!;
    private IFont _font = null!;
    private int _windowIndex = -1;
    private float _windowX, _windowY;
    
    private Dictionary<int, ButtonData> _buttonData = new();
    private Dictionary<int, LabelData> _labelData = new();
    private Dictionary<int, PanelData> _panelData = new();
    private Dictionary<int, Vector2> _widgetPositions = new();

    private const float WindowWidth = 400;
    private const float WindowHeight = 400;

    public void Initialize(GraphicsRenderer renderer)
    {
        _renderer = renderer;
        _font = renderer.GetFontDefault();
        _tree = new WidgetTree(_storage);
        _events = new WidgetEvents(_storage);
        _dispatcher = new EventDispatcher(_storage, _events);
        _inputSystem = new GameUIInputSystem(_storage, _tree, _dispatcher);
        _styleEngine = new StyleEngine();
        
        SetupStyles();
        CreateMainMenu();
    }

    private void SetupStyles()
    {
        var windowStyle = UIStyle.Rent()
            .BackgroundColor(new Color(30, 30, 30))
            .PaddingAll(20);
        _styleEngine.AddRule("Window", windowStyle);
        
        var buttonStyle = UIStyle.Rent()
            .BackgroundColor(new Color(70, 130, 180))
            .Text(Color.White)
            .PaddingAll(15)
            .CornerRadiusValue(5);
        _styleEngine.AddRule("Button", buttonStyle);
        
        var buttonHoverStyle = UIStyle.Rent()
            .BackgroundColor(new Color(100, 149, 237))
            .Text(Color.White)
            .PaddingAll(15)
            .CornerRadiusValue(5);
        _styleEngine.AddRule("Button:hover", buttonHoverStyle);
        
        var labelStyle = UIStyle.Rent()
            .Text(Color.White)
            .FontSizeValue(24);
        _styleEngine.AddRule("Label", labelStyle);
        
        var panelStyle = UIStyle.Rent()
            .BackgroundColor(new Color(45, 45, 45))
            .PaddingAll(10);
        _styleEngine.AddRule("Panel", panelStyle);
    }

    private void CreateMainMenu()
    {
        float screenWidth = 1024;
        float screenHeight = 768;
        
        _windowX = (screenWidth - WindowWidth) / 2;
        _windowY = (screenHeight - WindowHeight) / 2;
        
        var window = new UIWidget(UiTypeId.Window)
        {
            Id = "main-menu",
            Bounds = new Rectangle(_windowX, _windowY, WindowWidth, WindowHeight),
            ZIndex = 100,
            IsVisible = true,
            IsEnabled = true
        };
        _windowIndex = _storage.Add(window);
        _panelData[_windowIndex] = new PanelData { Background = new Color(30, 30, 30), ClipChildren = false };
        
        float padding = 20;
        
        AddLabel(_windowIndex, "title", _windowX + padding, _windowY + padding, WindowWidth - padding * 2, 50, "My Game", 32);
        
        float buttonY = _windowY + 100;
        float buttonWidth = 300;
        float buttonHeight = 50;
        float buttonX = _windowX + padding + (WindowWidth - padding * 2 - buttonWidth) / 2;
        
        AddButton(_windowIndex, "play-btn", buttonX, buttonY, buttonWidth, buttonHeight, "Play", 
            () => Console.WriteLine("Play clicked!"));
        
        buttonY += 70;
        
        AddButton(_windowIndex, "settings-btn", buttonX, buttonY, buttonWidth, buttonHeight, "Settings",
            () => Console.WriteLine("Settings clicked!"));
        
        buttonY += 70;
        
        AddButton(_windowIndex, "quit-btn", buttonX, buttonY, buttonWidth, buttonHeight, "Quit",
            () => Console.WriteLine("Quit clicked!"));
        
        AddLabel(_windowIndex, "version", _windowX + padding, _windowY + WindowHeight - padding - 30, WindowWidth - padding * 2, 30, "v1.0.0", 14);
    }

    private void AddLabel(int parentIndex, string id, float x, float y, float width, float height, string text, float fontSize)
    {
        var label = new UIWidget(UiTypeId.Label)
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            ZIndex = 101,
            IsVisible = true,
            IsEnabled = true
        };
        var index = _storage.AddChild(parentIndex, label);
        _widgetPositions[index] = new Vector2(x + width / 2, y + height / 2);
        _labelData[index] = new LabelData
        {
            Text = text,
            TextColor = Color.White,
            FontSize = fontSize,
            Alignment = TextAlignment.Center
        };
    }

    private void AddButton(int parentIndex, string id, float x, float y, float width, float height, string text, Action onClick)
    {
        var button = new UIWidget(UiTypeId.Button)
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            ZIndex = 101,
            IsVisible = true,
            IsEnabled = true,
            BubbleEvents = true
        };
        var index = _storage.AddChild(parentIndex, button);
        _widgetPositions[index] = new Vector2(x + width / 2, y + height / 2);
        _buttonData[index] = new ButtonData
        {
            Text = text,
            Background = new Color(70, 130, 180),
            TextColor = Color.White,
            FontSize = 18
        };
        
        var handlers = _events.GetOrCreate(index);
        handlers.OnClick += idx => 
        {
            if (_buttonData.TryGetValue(idx, out var data))
            {
                onClick();
            }
        };
        handlers.OnHover += idx =>
        {
            if (_buttonData.TryGetValue(idx, out var data))
            {
                data.Background = new Color(100, 149, 237);
            }
        };
        handlers.OnUnhover += idx =>
        {
            if (_buttonData.TryGetValue(idx, out var data))
            {
                data.Background = id == "quit-btn" ? new Color(180, 70, 70) : new Color(70, 130, 180);
            }
        };
    }

    private int _lastHoveredIndex = -1;

    public void Update(System.Numerics.Vector2 mousePosition, bool mouseDown)
    {
        if (_windowIndex < 0) return;
        
        int hitIndex = HitTest.FindWidgetAt(_storage, _windowIndex, mousePosition);
        
        if (hitIndex >= 0)
        {
            _inputSystem.Update(mousePosition, mouseDown, hitIndex);
            
            int currentHovered = hitIndex;
            
            if (currentHovered != _lastHoveredIndex)
            {
                if (_lastHoveredIndex >= 0 && _buttonData.ContainsKey(_lastHoveredIndex))
                {
                    UpdateButtonColors(_lastHoveredIndex, false);
                }
                
                if (currentHovered >= 0 && _buttonData.ContainsKey(currentHovered))
                {
                    UpdateButtonColors(currentHovered, true);
                }
                
                _lastHoveredIndex = currentHovered;
            }
            
            if (mouseDown)
            {
                var widget = _storage.Get(hitIndex);
                if (widget.Type == UiTypeId.Button)
                {
                    _dispatcher.DispatchPress(hitIndex);
                }
            }
            else
            {
                var widget = _storage.Get(hitIndex);
                if (widget.State == InteractionState.Pressed)
                {
                    _dispatcher.DispatchRelease(hitIndex);
                    _dispatcher.DispatchClick(hitIndex);
                }
            }
        }
        else if (_lastHoveredIndex >= 0)
        {
            if (_buttonData.ContainsKey(_lastHoveredIndex))
            {
                UpdateButtonColors(_lastHoveredIndex, false);
            }
            _lastHoveredIndex = -1;
        }
    }

    private void UpdateButtonColors(int index, bool isHovered)
    {
        if (!_buttonData.TryGetValue(index, out var data)) return;
        
        var widget = _storage.Get(index);
        
        if (widget.Id == "quit-btn")
        {
            _buttonData[index] = data with { Background = isHovered ? new Color(200, 90, 90) : new Color(180, 70, 70) };
        }
        else
        {
            _buttonData[index] = data with { Background = isHovered ? new Color(100, 149, 237) : new Color(70, 130, 180) };
        }
    }

    public void Render()
    {
        for (int i = 0; i < _storage.Count; i++)
        {
            var widget = _storage.Get(i);
            if (!widget.IsVisible) continue;
            
            RenderWidget(i, widget);
        }
    }

    private void RenderWidget(int index, UIWidget widget)
    {
        var bounds = widget.Bounds;
        
        switch (widget.Type)
        {
            case UiTypeId.Window:
                if (_panelData.TryGetValue(index, out var panelData))
                {
                    var c = System.Drawing.Color.FromArgb(panelData.Background.ToArgb());
                    _renderer.DrawRectangle(
                        new System.Drawing.RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height), 
                        c);
                }
                break;
                
            case UiTypeId.Button:
                if (_buttonData.TryGetValue(index, out var buttonData))
                {
                    var c = System.Drawing.Color.FromArgb(buttonData.Background.ToArgb());
                    _renderer.DrawRectangleRounded(
                        new System.Drawing.RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                        8, 0, c);
                    
                    var textC = System.Drawing.Color.FromArgb(buttonData.TextColor.ToArgb());
                    var centerX = bounds.X + bounds.Width / 2;
                    var centerY = bounds.Y + bounds.Height / 2;
                    var textSize = _renderer.MeasureText(_font, buttonData.Text, buttonData.FontSize, 1f);
                    var textPos = new Vector2(centerX - textSize.X / 2, centerY - textSize.Y / 2);
                    _renderer.DrawText(buttonData.Text, textPos, buttonData.FontSize, textC);
                }
                break;
                
            case UiTypeId.Label:
                if (_labelData.TryGetValue(index, out var labelData))
                {
                    var textC = System.Drawing.Color.FromArgb(labelData.TextColor.ToArgb());
                    var centerX = bounds.X + bounds.Width / 2;
                    var centerY = bounds.Y + bounds.Height / 2;
                    var textSize = _renderer.MeasureText(_font, labelData.Text, labelData.FontSize, 1f);
                    var textPos = new Vector2(centerX - textSize.X / 2, centerY - textSize.Y / 2);
                    _renderer.DrawText(labelData.Text, textPos, labelData.FontSize, textC);
                }
                break;
        }
        
        if (widget.HasChildren)
        {
            var childIndex = widget.FirstChildIndex;
            while (childIndex != UIWidget.NoChild)
            {
                var child = _storage.Get(childIndex);
                RenderWidget(childIndex, child);
                childIndex = child.NextSiblingIndex;
            }
        }
    }
}