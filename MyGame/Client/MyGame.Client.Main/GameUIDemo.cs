using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.UI.Core;
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

    private const float WindowWidth = 600;
    private const float WindowHeight = 500;
    
    private ResourceDictionary _resources = null!;

    public void Initialize(GraphicsRenderer renderer)
    {
        _renderer = renderer;
        _font = renderer.GetFontDefault();
        _tree = new WidgetTree(_storage);
        _events = new WidgetEvents(_storage);
        _dispatcher = new EventDispatcher(_storage, _events);
        _inputSystem = new GameUIInputSystem(_storage, _tree, _dispatcher);
        
        _resources = CreateCustomResources();
        _styleEngine = new StyleEngine(_resources);
        
        SetupStyles();
        CreateMainMenu();
    }

    private ResourceDictionary CreateCustomResources()
    {
        var resources = new ResourceDictionary();
        
        resources.Add("primary", Color.FromHex("#3498db"));
        resources.Add("primary-dark", Color.FromHex("#2980b9"));
        resources.Add("success", Color.FromHex("#27ae60"));
        resources.Add("danger", Color.FromHex("#e74c3c"));
        resources.Add("warning", Color.FromHex("#f39c12"));
        resources.Add("dark", Color.FromHex("#2c3e50"));
        resources.Add("light", Color.FromHex("#ecf0f1"));
        resources.Add("text-dark", Color.FromHex("#2c3e50"));
        resources.Add("text-light", Color.FromHex("#ffffff"));
        resources.Add("border-color", Color.FromHex("#bdc3c7"));
        
        resources.Add("padding-small", 8f);
        resources.Add("padding-medium", 16f);
        resources.Add("padding-large", 24f);
        
        resources.Add("font-small", 12f);
        resources.Add("font-normal", 14f);
        resources.Add("font-large", 18f);
        resources.Add("font-title", 24f);
        
        resources.Add("border-radius", 4f);
        resources.Add("border-radius-large", 8f);
        
        return resources;
    }

    private void SetupStyles()
    {
        var windowStyle = UIStyle.Rent()
            .BackgroundColorResource("dark")
            .PaddingAll(20);
        _styleEngine.AddRule("Window", windowStyle);
        
        var panelStyle = UIStyle.Rent()
            .BackgroundColor(Color.FromHex("#34495e"))
            .PaddingAll(16)
            .CornerRadiusValue(8);
        _styleEngine.AddRule("Panel", panelStyle);
        
        var titleStyle = UIStyle.Rent()
            .TextColorResource("text-light")
            .FontSizeValue(28)
            .Align(TextAlignment.Center);
        _styleEngine.AddRule("Label.title", titleStyle);
        
        var headingStyle = UIStyle.Rent()
            .TextColorResource("text-light")
            .FontSizeValue(18);
        _styleEngine.AddRule("Label.heading", headingStyle);
        
        var bodyStyle = UIStyle.Rent()
            .TextColorResource("text-light")
            .FontSizeValue(14);
        _styleEngine.AddRule("Label.body", bodyStyle);
        
        var buttonBase = UIStyle.Rent()
            .TextColorResource("text-light")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button", buttonBase);
        
        var primaryBtn = UIStyle.Rent()
            .BackgroundColorResource("primary")
            .TextColorResource("text-light")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.primary", primaryBtn);
        
        var primaryHover = UIStyle.Rent()
            .BackgroundColorResource("primary-dark")
            .TextColorResource("text-light")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.primary:hover", primaryHover);
        
        var successBtn = UIStyle.Rent()
            .BackgroundColorResource("success")
            .TextColorResource("text-light")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.success", successBtn);
        
        var dangerBtn = UIStyle.Rent()
            .BackgroundColorResource("danger")
            .TextColorResource("text-light")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.danger", dangerBtn);
        
        var outlineBtn = UIStyle.Rent()
            .BackgroundColor(Color.Transparent)
            .Border(Color.FromHex("#bdc3c7"), 1)
            .TextColorResource("text-dark")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.outline", outlineBtn);
        
        var horizontalContainer = UIStyle.Rent()
            .BackgroundColor(Color.FromHex("#2c3e50"))
            .PaddingAll(16);
        _styleEngine.AddRule("Horizontal", horizontalContainer);
        
        var verticalContainer = UIStyle.Rent()
            .BackgroundColor(Color.FromHex("#3d566e"))
            .PaddingAll(16);
        _styleEngine.AddRule("Vertical", verticalContainer);
    }

    private void CreateMainMenu()
    {
        float screenWidth = 1024;
        float screenHeight = 768;
        
        _windowX = (screenWidth - WindowWidth) / 2;
        _windowY = (screenHeight - WindowHeight) / 2;
        
        var window = new UIWidget(UiTypeId.Window)
        {
            Id = "settings-window",
            Bounds = new Rectangle(_windowX, _windowY, WindowWidth, WindowHeight),
            ZIndex = 100,
            IsVisible = true,
            IsEnabled = true
        };
        _windowIndex = _storage.Add(window);
        _panelData[_windowIndex] = new PanelData { Background = new Color(44, 62, 80), ClipChildren = false };
        
        AddLabel(_windowIndex, "settings-title", 20, 20, WindowWidth - 40, 40, "Settings", 28, "title");
        
        float contentStartY = 80;
        
        int graphicsPanel = AddPanel(_windowIndex, "graphics-panel", 20, contentStartY, WindowWidth - 40, 120, "Graphics");
        AddLabel(graphicsPanel, "graphics-text", 10, 30, WindowWidth - 80, 30, "Resolution: 1920x1080", 14, "body");
        AddButton(graphicsPanel, "res-720p", 10, 70, 100, 30, "720p", () => Console.WriteLine("720p"));
        AddButton(graphicsPanel, "res-1080p", 120, 70, 100, 30, "1080p", () => Console.WriteLine("1080p"));
        AddButton(graphicsPanel, "res-4k", 230, 70, 100, 30, "4K", () => Console.WriteLine("4K"));
        
        int audioPanel = AddPanel(_windowIndex, "audio-panel", 20, contentStartY + 140, WindowWidth - 40, 100, "Audio");
        AddLabel(audioPanel, "volume-label", 10, 30, WindowWidth - 80, 30, "Master Volume: 75%", 14, "body");
        AddButton(audioPanel, "mute-btn", 10, 70, 120, 30, "Mute", () => Console.WriteLine("Mute"));
        
        int controlsPanel = AddPanel(_windowIndex, "controls-panel", 20, contentStartY + 260, WindowWidth - 40, 80, "Controls");
        AddLabel(controlsPanel, "controls-text", 10, 30, WindowWidth - 80, 30, "Keybindings configured", 14, "body");
        
        float buttonY = WindowHeight - 80;
        float buttonX = 20;
        
        AddButton(_windowIndex, "save-btn", buttonX, buttonY, 150, 40, "Save", () => Console.WriteLine("Save clicked!"), "success");
        
        buttonX += 170;
        AddButton(_windowIndex, "cancel-btn", buttonX, buttonY, 150, 40, "Cancel", () => Console.WriteLine("Cancel clicked!"), "outline");
        
        buttonX += 170;
        AddButton(_windowIndex, "reset-btn", buttonX, buttonY, 150, 40, "Reset", () => Console.WriteLine("Reset clicked!"), "danger");
    }

    private int AddPanel(int parentIndex, string id, float x, float y, float width, float height, string title)
    {
        var panel = new UIWidget(UiTypeId.Panel)
        {
            Id = id,
            Bounds = new Rectangle(x, y, width, height),
            ZIndex = 101,
            IsVisible = true,
            IsEnabled = true
        };
        int panelIndex = _storage.AddChild(parentIndex, panel);
        _panelData[panelIndex] = new PanelData { Background = new Color(52, 73, 94), ClipChildren = false };
        
        AddLabel(panelIndex, id + "-title", 10, 10, width - 40, 25, title, 16, "heading");
        
        return panelIndex;
    }

    private void AddLabel(int parentIndex, string id, float localX, float localY, float width, float height, string text, float fontSize, string? styleClass = null)
    {
        var label = new UIWidget(UiTypeId.Label)
        {
            Id = id,
            Bounds = new Rectangle(localX, localY, width, height),
            ZIndex = 102,
            IsVisible = true,
            IsEnabled = true
        };
        var index = _storage.AddChild(parentIndex, label);
        
        var styleData = _styleEngine.GetOrCreateStyleData(index);
        if (styleClass != null)
            styleData.AddClass(styleClass);
        
        var style = _styleEngine.ComputeStyle(label, styleData);
        
        _labelData[index] = new LabelData
        {
            Text = text,
            TextColor = style.TextColor,
            FontSize = style.FontSize > 0 ? style.FontSize : fontSize,
            Alignment = style.TextAlignment
        };
        
        UIStyle.Return(style);
    }

    private void AddButton(int parentIndex, string id, float localX, float localY, float width, float height, string text, Action onClick, string? styleClass = null)
    {
        var button = new UIWidget(UiTypeId.Button)
        {
            Id = id,
            Bounds = new Rectangle(localX, localY, width, height),
            ZIndex = 102,
            IsVisible = true,
            IsEnabled = true,
            BubbleEvents = true
        };
        var index = _storage.AddChild(parentIndex, button);
        
        var styleData = _styleEngine.GetOrCreateStyleData(index);
        if (styleClass != null)
            styleData.AddClass(styleClass);
        
        styleData.SetPseudoState(PseudoState.Hover);
        
        var style = _styleEngine.ComputeStyle(button, styleData);
        
        _buttonData[index] = new ButtonData
        {
            Text = text,
            Background = style.Background.A > 0 ? style.Background : new Color(52, 152, 219),
            TextColor = style.TextColor,
            FontSize = style.FontSize > 0 ? style.FontSize : 14
        };
        
        UIStyle.Return(style);
        
        var handlers = _events.GetOrCreate(index);
        handlers.OnClick += idx => 
        {
            if (_buttonData.TryGetValue(idx, out var data))
            {
                onClick();
            }
        };
    }

    private int _lastHoveredIndex = -1;
    private HashSet<int> _hoveredButtons = new();

    public void Update(Vector2 mousePosition, bool mouseDown)
    {
        if (_windowIndex < 0) return;
        
        int hitIndex = HitTest.FindWidgetAt(_storage, _windowIndex, mousePosition);
        
        if (hitIndex >= 0)
        {
            _inputSystem.Update(mousePosition, mouseDown, hitIndex);
            
            var widget = _storage.Get(hitIndex);
            
            if (widget.Type == UiTypeId.Button)
            {
                var styleData = _styleEngine.GetOrCreateStyleData(hitIndex);
                
                if (!_hoveredButtons.Contains(hitIndex))
                {
                    _hoveredButtons.Add(hitIndex);
                    UpdateButtonStyle(hitIndex, true);
                }
                
                if (mouseDown && widget.State == InteractionState.Normal)
                {
                    _dispatcher.DispatchPress(hitIndex);
                }
                else if (!mouseDown && widget.State == InteractionState.Pressed)
                {
                    _dispatcher.DispatchRelease(hitIndex);
                    _dispatcher.DispatchClick(hitIndex);
                }
            }
            
            if (_lastHoveredIndex >= 0 && _lastHoveredIndex != hitIndex)
            {
                var prevWidget = _storage.Get(_lastHoveredIndex);
                if (prevWidget.Type == UiTypeId.Button && !_hoveredButtons.Contains(hitIndex))
                {
                    _hoveredButtons.Remove(_lastHoveredIndex);
                    UpdateButtonStyle(_lastHoveredIndex, false);
                }
            }
            
            _lastHoveredIndex = hitIndex;
        }
        else
        {
            if (_lastHoveredIndex >= 0)
            {
                var prevWidget = _storage.Get(_lastHoveredIndex);
                if (prevWidget.Type == UiTypeId.Button)
                {
                    _hoveredButtons.Remove(_lastHoveredIndex);
                    UpdateButtonStyle(_lastHoveredIndex, false);
                }
                _lastHoveredIndex = -1;
            }
        }
    }

    private void UpdateButtonStyle(int index, bool isHovered)
    {
        if (!_buttonData.TryGetValue(index, out var data)) return;
        
        var widget = _storage.Get(index);
        var styleData = _styleEngine.GetOrCreateStyleData(index);
        
        if (isHovered)
            styleData.SetPseudoState(PseudoState.Hover);
        else
            styleData.RemovePseudoState(PseudoState.Hover);
        
        var style = _styleEngine.ComputeStyle(widget, styleData);
        
        _buttonData[index] = data with 
        { 
            Background = style.Background.A > 0 ? style.Background : data.Background,
            TextColor = style.TextColor
        };
        
        UIStyle.Return(style);
    }

    private Rectangle GetAbsoluteBounds(int index, Rectangle parentBounds)
    {
        var widget = _storage.Get(index);
        var localBounds = widget.Bounds;
        
        var absoluteBounds = new Rectangle(
            parentBounds.X + localBounds.X,
            parentBounds.Y + localBounds.Y,
            localBounds.Width,
            localBounds.Height
        );
        
        return absoluteBounds;
    }

    public void Render()
    {
        if (_windowIndex < 0) return;
        
        var window = _storage.Get(_windowIndex);
        var windowBounds = window.Bounds;
        
        RenderWidget(_windowIndex, window, windowBounds);
    }

    private void RenderWidget(int index, UIWidget widget, Rectangle absoluteBounds)
    {
        switch (widget.Type)
        {
            case UiTypeId.Window:
                if (_panelData.TryGetValue(index, out var panelData))
                {
                    var c = System.Drawing.Color.FromArgb(panelData.Background.ToArgb());
                    _renderer.DrawRectangle(
                        new System.Drawing.RectangleF(absoluteBounds.X, absoluteBounds.Y, absoluteBounds.Width, absoluteBounds.Height), 
                        c);
                }
                break;
                
            case UiTypeId.Panel:
                if (_panelData.TryGetValue(index, out var pData))
                {
                    var c = System.Drawing.Color.FromArgb(pData.Background.ToArgb());
                    _renderer.DrawRectangleRounded(
                        new System.Drawing.RectangleF(absoluteBounds.X, absoluteBounds.Y, absoluteBounds.Width, absoluteBounds.Height),
                        8, 0, c);
                }
                break;
                
            case UiTypeId.Button:
                if (_buttonData.TryGetValue(index, out var buttonData))
                {
                    var c = System.Drawing.Color.FromArgb(buttonData.Background.ToArgb());
                    _renderer.DrawRectangleRounded(
                        new System.Drawing.RectangleF(absoluteBounds.X, absoluteBounds.Y, absoluteBounds.Width, absoluteBounds.Height),
                        4, 0, c);
                    
                    var textC = System.Drawing.Color.FromArgb(buttonData.TextColor.ToArgb());
                    var centerX = absoluteBounds.X + absoluteBounds.Width / 2;
                    var centerY = absoluteBounds.Y + absoluteBounds.Height / 2;
                    var textSize = _renderer.MeasureText(_font, buttonData.Text, buttonData.FontSize, 1f);
                    var textPos = new Vector2(centerX - textSize.X / 2, centerY - textSize.Y / 2);
                    _renderer.DrawText(buttonData.Text, textPos, buttonData.FontSize, textC);
                }
                break;
                
            case UiTypeId.Label:
                if (_labelData.TryGetValue(index, out var labelData))
                {
                    var textC = System.Drawing.Color.FromArgb(labelData.TextColor.ToArgb());
                    var centerX = absoluteBounds.X + absoluteBounds.Width / 2;
                    var centerY = absoluteBounds.Y + absoluteBounds.Height / 2;
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
                var childAbsoluteBounds = new Rectangle(
                    absoluteBounds.X + child.Bounds.X,
                    absoluteBounds.Y + child.Bounds.Y,
                    child.Bounds.Width,
                    child.Bounds.Height
                );
                RenderWidget(childIndex, child, childAbsoluteBounds);
                childIndex = child.NextSiblingIndex;
            }
        }
    }
}