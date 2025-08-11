using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

public class Dropdown : VisualElement, ITextProvider
{
    public List<string> Items { get; } = new();
    public int SelectedIndex { get; set; } = -1;
    public string? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
    public string Placeholder { get; set; } = "Select an option...";
    
    public event Action<int, string>? OnSelectionChanged;
    
    private bool _isOpen = false;
    private const float ItemHeight = 30f;
    private const float MaxDropdownHeight = 150f;
    private LayerManager? _layerManager;
    private string? _dropdownLayerName;
    
    public Dropdown() : base("Dropdown")
    {
        AddClass("dropdown");
        
        var clickable = new ClickableManipulator();
        clickable.OnClicked += ToggleDropdown;
        AddManipulator(clickable);
        AddManipulator(new HoverEffectManipulator());
    }
    
    public string? GetDisplayText() => SelectedItem ?? Placeholder;
    public IEnumerable<string>? GetTextOptions() => Items;
    public string? GetPlaceholderText() => Placeholder;
    
    protected override bool HandleSelfInputEvent(InputEvent inputEvent)
    {
        if (inputEvent is { Type: InputEventType.KeyDown, Key: KeyboardKey.Escape }
            && _isOpen)
        {
            CloseDropdown();
            return true;
        }
        
        return base.HandleSelfInputEvent(inputEvent);
    }
    
    public void SetLayerManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void AddItem(string item)
    {
        Items.Add(item);
    }
    
    public void RemoveItem(string item)
    {
        var index = Items.IndexOf(item);
        if (index >= 0)
        {
            Items.RemoveAt(index);
            if (SelectedIndex == index)
            {
                SelectedIndex = -1;
            }
            else if (SelectedIndex > index)
            {
                SelectedIndex--;
            }
        }
    }
    
    public void SelectItem(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            var oldIndex = SelectedIndex;
            SelectedIndex = index;
            if (oldIndex != index)
            {
                OnSelectionChanged?.Invoke(index, Items[index]);
            }
        }
    }
    
    private void ToggleDropdown()
    {
        if (!Enabled) return;
        
        if (_isOpen)
        {
            CloseDropdown();
        }
        else
        {
            OpenDropdown();
        }
    }
    
    private void OpenDropdown()
    {
        _isOpen = true;
        
        if (_layerManager != null)
        {
            _dropdownLayerName = $"dropdown_{Name}_{DateTime.Now.Ticks}";
            var layer = _layerManager.CreateLayer(_dropdownLayerName, 500);
            
            var dropdownList = new DropdownList(this);
            layer.AddElement(dropdownList);
            layer.BlocksInput = false;
        }
    }
    
    public void CloseDropdown()
    {
        _isOpen = false;
        
        if (_layerManager != null && _dropdownLayerName != null)
        {
            _layerManager.RemoveLayer(_dropdownLayerName);
            _dropdownLayerName = null;
        }
    }
    
    protected override void RenderSelf()
    {
        var bgColor = ResolvedStyle.GetBackgroundColorOrDefault();
        if (!Enabled)
            bgColor = new Color(240, 240, 240, 255);
        else if (IsHovered || _isOpen)
            bgColor = new Color(245, 245, 245, 255);
            
        Raylib.DrawRectangleRounded(GetBounds(), 0.2f, 8, bgColor);
        
        var borderColor = _isOpen ? new Color(33, 150, 243, 255) : new Color(200, 200, 200, 255);
        if (!Enabled)
            borderColor = new Color(220, 220, 220, 255);
            
        Raylib.DrawRectangleLinesEx(GetBounds(), 1f, borderColor);
        
        var displayText = SelectedItem ?? Placeholder;
        var textColor = SelectedItem != null ? ResolvedStyle.GetTextColorOrDefault() : Color.Gray;
        if (!Enabled)
            textColor = new Color(150, 150, 150, 255);
            
        var textPos = new Vector2(
            Position.X + ResolvedStyle.Padding.Left,
            Position.Y + (Size.Y - ResolvedStyle.GetFontSizeOrDefault()) / 2
        );
        
        var availableWidth = Size.X - ResolvedStyle.Padding.Left - ResolvedStyle.Padding.Right - 20;
        var clippedText = ClipText(displayText, availableWidth);
        
        Raylib.DrawText(clippedText, (int)textPos.X, (int)textPos.Y, ResolvedStyle.GetFontSizeOrDefault(), textColor);
        
        var arrowColor = Enabled ? new Color(100, 100, 100, 255) : new Color(180, 180, 180, 255);
        var arrowX = Position.X + Size.X - 15;
        var arrowY = Position.Y + Size.Y / 2;
        
        if (_isOpen)
        {
            Raylib.DrawTriangle(
                new Vector2(arrowX - 4, arrowY + 2),
                new Vector2(arrowX + 4, arrowY + 2),
                new Vector2(arrowX, arrowY - 2),
                arrowColor
            );
        }
        else
        {
            Raylib.DrawTriangle(
                new Vector2(arrowX - 4, arrowY - 2),
                new Vector2(arrowX + 4, arrowY - 2),
                new Vector2(arrowX, arrowY + 2),
                arrowColor
            );
        }
    }
    
    private string ClipText(string text, float maxWidth)
    {
        if (Raylib.MeasureText(text, ResolvedStyle.GetFontSizeOrDefault()) <= maxWidth)
            return text;
            
        for (int i = text.Length - 1; i >= 0; i--)
        {
            var substring = text.Substring(0, i) + "...";
            if (Raylib.MeasureText(substring, ResolvedStyle.GetFontSizeOrDefault()) <= maxWidth)
                return substring;
        }
        
        return "...";
    }
}

internal class DropdownList : VisualElement
{
    private readonly Dropdown _parentDropdown;
    private const float ItemHeight = 30f;
    private const float MaxDropdownHeight = 150f;
    
    public DropdownList(Dropdown parentDropdown) : base("DropdownList")
    {
        _parentDropdown = parentDropdown;
        
        var dropdownRect = GetDropdownRect();
        Position = new Vector2(dropdownRect.X, dropdownRect.Y);
        Size = new Vector2(dropdownRect.Width, dropdownRect.Height);
        
        Style.Position = Karpik.Engine.Client.UIToolkit.Position.Fixed;
        Style.Left = Position.X;
        Style.Top = Position.Y;
        Style.Width = Size.X;
        Style.Height = Size.Y;
        }
    
    private Rectangle GetDropdownRect()
    {
        var itemCount = Math.Min(_parentDropdown.Items.Count, (int)(MaxDropdownHeight / ItemHeight));
        var dropdownHeight = Math.Max(itemCount * ItemHeight, ItemHeight); // Минимум одна строка
        
        var rect = new Rectangle(
            _parentDropdown.Position.X, 
            _parentDropdown.Position.Y + _parentDropdown.Size.Y, 
            _parentDropdown.Size.X, 
            dropdownHeight
        );
        
        return rect;
    }
    
    protected override bool HandleSelfInputEvent(InputEvent inputEvent)
    {
        if (inputEvent.Type == InputEventType.MouseClick && 
            inputEvent.MouseButton == MouseButton.Left)
        {
            var mousePos = inputEvent.MousePosition;
            var bounds = GetBounds();
            
            if (Raylib.CheckCollisionPointRec(mousePos, bounds))
            {
                var relativeY = mousePos.Y - bounds.Y;
                var itemIndex = (int)(relativeY / ItemHeight);
                
                if (itemIndex >= 0 && itemIndex < _parentDropdown.Items.Count)
                {
                    _parentDropdown.SelectItem(itemIndex);
                    _parentDropdown.CloseDropdown();
                    return true;
                }
            }
            else if (!_parentDropdown.ContainsPoint(mousePos))
            {
                _parentDropdown.CloseDropdown();
                return true;
            }
        }
        
        return base.HandleSelfInputEvent(inputEvent);
    }
    
    protected override void RenderSelf()
    {
        var bounds = GetBounds();
        
        Raylib.DrawRectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height, Color.White);
        
        Raylib.DrawRectangleLinesEx(bounds, 2f, Color.Red);
        
        var mousePos = Raylib.GetMousePosition();
        var maxItems = Math.Min(_parentDropdown.Items.Count, (int)(MaxDropdownHeight / ItemHeight));
        
        for (int i = 0; i < maxItems; i++)
        {
            var itemRect = new Rectangle(
                bounds.X,
                bounds.Y + i * ItemHeight,
                bounds.Width,
                ItemHeight
            );
            
            if (Raylib.CheckCollisionPointRec(mousePos, itemRect))
            {
                Raylib.DrawRectangle((int)itemRect.X, (int)itemRect.Y, (int)itemRect.Width, (int)itemRect.Height, 
                    new Color(240, 240, 240, 255));
            }
            
            if (i == _parentDropdown.SelectedIndex)
            {
                Raylib.DrawRectangle((int)itemRect.X, (int)itemRect.Y, (int)itemRect.Width, (int)itemRect.Height, 
                    new Color(33, 150, 243, 50));
            }
            
            var fontSize = _parentDropdown.ResolvedStyle.GetFontSizeOrDefault();
            var textColor = _parentDropdown.ResolvedStyle.GetTextColorOrDefault();
            
            var itemTextPos = new Vector2(
                itemRect.X + 10,
                itemRect.Y + (ItemHeight - fontSize) / 2
            );
            
            Raylib.DrawText(_parentDropdown.Items[i], (int)itemTextPos.X, (int)itemTextPos.Y, fontSize, textColor);
        }
    }
}