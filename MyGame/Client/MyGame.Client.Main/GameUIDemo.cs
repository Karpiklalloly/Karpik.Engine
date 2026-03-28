using System.Numerics;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.UI.Core;
using GraphicsRenderer = Karpik.Engine.Client.Graphics.Core.IRenderer;
using GameUIInputSystem = Karpik.Engine.Client.UI.Core.InputSystem;
using ImGuiNET;

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
        var darkTheme = new DarkTheme()
            .Primary(new Color(255, 0, 153, 255))
            .Success(new Color(255, 72, 199, 103))
            .Danger(new Color(255, 255, 92, 107))
            .Warning(new Color(255, 255, 193, 7));
        
        var resources = darkTheme.Build();
        
        resources.Add("dark", new Color(255, 30, 30, 30));
        resources.Add("light", new Color(255, 233, 236, 239));
        resources.Add("text-dark", new Color(255, 30, 30, 30));
        resources.Add("text-light", new Color(255, 233, 236, 239));
        resources.Add("border-color", new Color(255, 90, 90, 90));
        
        resources.Add("panel-bg", new Color(255, 45, 45, 45));
        resources.Add("panel-bg-alt", new Color(255, 61, 86, 110));
        
        resources.Add("padding-small", 8f);
        resources.Add("padding-medium", 16f);
        resources.Add("padding-large", 24f);
        
        resources.Add("font-small", 12f);
        resources.Add("font-normal", 14f);
        resources.Add("font-large", 18f);
        resources.Add("font-title", 28f);
        
        resources.Add("border-radius", 4f);
        resources.Add("border-radius-large", 8f);
        
        return resources;
    }

    private void SetupStyles()
    {
        var windowStyle = UIStyle.Rent()
            .BackgroundColor("$Background")
            .Border("$BorderColor")
            .PaddingAll(20);
        _styleEngine.AddRule("Window", windowStyle);
        
        var panelStyle = UIStyle.Rent()
            .BackgroundColor("panel-bg")
            .PaddingAll(16)
            .CornerRadiusValue(8);
        _styleEngine.AddRule("Panel", panelStyle);
        
        var titleStyle = UIStyle.Rent()
            .Text("$TextColor")
            .FontSizeValue(28)
            .Align(TextAlignment.Center);
        _styleEngine.AddRule("Label.title", titleStyle);
        
        var headingStyle = UIStyle.Rent()
            .Text("$TextColor")
            .FontSizeValue(18);
        _styleEngine.AddRule("Label.heading", headingStyle);
        
        var bodyStyle = UIStyle.Rent()
            .Text("$TextColor")
            .FontSizeValue(14);
        _styleEngine.AddRule("Label.body", bodyStyle);
        
        var buttonBase = UIStyle.Rent()
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button", buttonBase);
        
        var primaryBtn = UIStyle.Rent()
            .BackgroundColor("$Primary")
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.primary", primaryBtn);
        
        var primaryHover = UIStyle.Rent()
            .BackgroundColor("$SurfaceHover")
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.primary:hover", primaryHover);
        
        var successBtn = UIStyle.Rent()
            .BackgroundColor("$Success")
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.success", successBtn);
        
        var dangerBtn = UIStyle.Rent()
            .BackgroundColor("$Danger")
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.danger", dangerBtn);
        
        var outlineBtn = UIStyle.Rent()
            .BackgroundColor(Color.Transparent)
            .Border("$BorderColor")
            .Text("$TextColor")
            .PaddingAll(12)
            .CornerRadiusValue(4);
        _styleEngine.AddRule("Button.outline", outlineBtn);
        
        var horizontalContainer = UIStyle.Rent()
            .BackgroundColorResource("panel-bg")
            .PaddingAll(16);
        _styleEngine.AddRule("Horizontal", horizontalContainer);
        
        var verticalContainer = UIStyle.Rent()
            .BackgroundColorResource("panel-bg-alt")
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
        _panelData[_windowIndex] = new PanelData { Background = new Color(255, 30, 30, 30), ClipChildren = false };
        
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
        _panelData[panelIndex] = new PanelData { Background = new Color(255, 45, 45, 45), ClipChildren = false };
        
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
        
        var style = _styleEngine.ComputeStyle(button, styleData);
        
        _buttonData[index] = new ButtonData
        {
            Text = text,
            Background = style.Background,
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

    public void RenderImGuiDebug()
    {
        if (!ImGui.Begin("UI Widget Tree"))
        {
            ImGui.End();
            return;
        }

        if (_windowIndex < 0)
        {
            ImGui.Text("No UI loaded");
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("DebugTabs"))
        {
            if (ImGui.BeginTabItem("Widget Tree"))
            {
                RenderWidgetTreeNode(_windowIndex, 0);
                ImGui.EndTabItem();
            }
            
            if (ImGui.BeginTabItem("Styles"))
            {
                RenderStylesDebug();
                ImGui.EndTabItem();
            }
            
            ImGui.EndTabBar();
        }
        
        ImGui.Separator();
        
        if (ImGui.Button("Clear Hover State"))
        {
            _hoveredButtons.Clear();
            _lastHoveredIndex = -1;
        }
        
        ImGui.Text($"Hovered buttons: {_hoveredButtons.Count}");
        
        ImGui.End();
    }

    private void RenderStylesDebug()
    {
        if (ImGui.Button("Refresh Styles"))
        {
        }
        
        ImGui.Separator();
        
        RenderStyleForWidget(_windowIndex, 0);
    }

    private void RenderStyleForWidget(int index, int depth)
    {
        var widget = _storage.Get(index);
        
        var headerLabel = $"{widget.Type}";
        if (!string.IsNullOrEmpty(widget.Id))
        {
            headerLabel += $"##{index}";
        }
        
        if (ImGui.CollapsingHeader(headerLabel))
        {
            ImGui.Indent();
            
            var styleData = _styleEngine.GetOrCreateStyleData(index);
            var computedStyle = _styleEngine.ComputeStyle(widget, styleData);
            
            ImGui.Text($"Index: {index}");
            ImGui.Text($"Type: {widget.Type}");
            ImGui.Text($"Id: {widget.Id ?? "(none)"}");
            ImGui.Text($"State: {widget.State}");
            ImGui.Text($"Bounds: ({widget.Bounds.X:F0}, {widget.Bounds.Y:F0}, {widget.Bounds.Width:F0}x{widget.Bounds.Height:F0})");
            
            ImGui.Separator();
            ImGui.Text("Computed Style:");
            
            ImGui.TextColored(new Vector4(0, 1, 0, 1), $"Background: ({computedStyle.Background.R}, {computedStyle.Background.G}, {computedStyle.Background.B}, {computedStyle.Background.A})");
            ImGui.TextColored(new Vector4(0, 1, 1, 1), $"TextColor: ({computedStyle.TextColor.R}, {computedStyle.TextColor.G}, {computedStyle.TextColor.B}, {computedStyle.TextColor.A})");
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"BorderColor: ({computedStyle.BorderColor.R}, {computedStyle.BorderColor.G}, {computedStyle.BorderColor.B}, {computedStyle.BorderColor.A})");
            ImGui.Text($"BorderWidth: {computedStyle.BorderWidth}");
            ImGui.Text($"CornerRadius: {computedStyle.CornerRadius}");
            ImGui.Text($"FontSize: {computedStyle.FontSize}");
            ImGui.Text($"Padding: {computedStyle.Padding}");
            ImGui.Text($"Margin: {computedStyle.Margin}");
            ImGui.Text($"TextAlignment: {computedStyle.TextAlignment}");
            
            ImGui.Separator();
            ImGui.Text("Style Data:");
            ImGui.Text($"Classes: {string.Join(", ", styleData.Classes)}");
            ImGui.Text($"PseudoStates: {string.Join(", ", styleData.PseudoStates)}");
            ImGui.Text($"InlineStyle: {(styleData.InlineStyle != null ? "yes" : "no")}");
            
            UIStyle.Return(computedStyle);
            
            if (widget.HasChildren)
            {
                ImGui.Separator();
                var childIndex = widget.FirstChildIndex;
                while (childIndex != UIWidget.NoChild)
                {
                    RenderStyleForWidget(childIndex, depth + 1);
                    var child = _storage.Get(childIndex);
                    childIndex = child.NextSiblingIndex;
                }
            }
            
            ImGui.Unindent();
        }
    }

    private void RenderWidgetTreeNode(int index, int depth)
    {
        var widget = _storage.Get(index);
        
        var label = $"{widget.Type}";
        if (!string.IsNullOrEmpty(widget.Id))
        {
            label += $"##{widget.Id}";
        }
        
        var isExpanded = ImGui.TreeNode(label);
        
        ImGui.SameLine();
        ImGui.Text($"[{index}]");
        
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), 
            $"({widget.Bounds.X:F0}, {widget.Bounds.Y:F0}, {widget.Bounds.Width:F0}x {widget.Bounds.Height:F0})");
        
        if (widget.State != InteractionState.Normal)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 1, 0, 1), $"[{widget.State}]");
        }
        
        if (_buttonData.TryGetValue(index, out var btnData))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 0, 1), "Btn");
        }
        
        if (_labelData.TryGetValue(index, out var lblData))
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0, 1, 1, 1), $"\"{lblData.Text}\"");
        }
        
        if (isExpanded)
        {
            var childIndex = widget.FirstChildIndex;
            while (childIndex != UIWidget.NoChild)
            {
                RenderWidgetTreeNode(childIndex, depth + 1);
                var child = _storage.Get(childIndex);
                childIndex = child.NextSiblingIndex;
            }
            ImGui.TreePop();
        }
    }
}