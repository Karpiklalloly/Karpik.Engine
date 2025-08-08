using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class Dropdown : VisualElement
{
    public List<string> Items { get; } = new();
    public int SelectedIndex { get; set; } = -1;
    public string? SelectedItem => SelectedIndex >= 0 && SelectedIndex < Items.Count ? Items[SelectedIndex] : null;
    public string Placeholder { get; set; } = "Select an option...";
    
    public event Action<int, string>? OnSelectionChanged;
    
    private bool _isOpen = false;
    private const float ItemHeight = 30f;
    private const float MaxDropdownHeight = 150f;
    
    public Dropdown() : base("Dropdown")
    {
        AddClass("dropdown");
        
        var clickable = new ClickableManipulator();
        clickable.OnClicked += ToggleDropdown;
        AddManipulator(clickable);
        AddManipulator(new HoverEffectManipulator());
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
        _isOpen = !_isOpen;
    }
    
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        
        // Закрываем dropdown при клике вне его
        if (_isOpen && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = Raylib.GetMousePosition();
            var dropdownRect = GetDropdownRect();
            
            if (!ContainsPoint(mousePos) && !Raylib.CheckCollisionPointRec(mousePos, dropdownRect))
            {
                _isOpen = false;
            }
            else if (Raylib.CheckCollisionPointRec(mousePos, dropdownRect))
            {
                // Обработка клика по элементу списка
                var relativeY = mousePos.Y - dropdownRect.Y;
                var itemIndex = (int)(relativeY / ItemHeight);
                
                if (itemIndex >= 0 && itemIndex < Items.Count)
                {
                    SelectItem(itemIndex);
                    _isOpen = false;
                }
            }
        }
        
        // Закрываем dropdown при нажатии Escape
        if (_isOpen && Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _isOpen = false;
        }
    }
    
    private Rectangle GetDropdownRect()
    {
        var itemCount = Math.Min(Items.Count, (int)(MaxDropdownHeight / ItemHeight));
        var dropdownHeight = itemCount * ItemHeight;
        
        return new Rectangle(Position.X, Position.Y + Size.Y, Size.X, dropdownHeight);
    }
    
    protected override void RenderSelf()
    {
        // Рендерим основную кнопку
        var bgColor = ResolvedStyle.BackgroundColor;
        if (!Enabled)
            bgColor = new Color(240, 240, 240, 255);
        else if (IsHovered || _isOpen)
            bgColor = new Color(245, 245, 245, 255);
            
        Raylib.DrawRectangleRounded(GetBounds(), 0.2f, 8, bgColor);
        
        // Рамка
        var borderColor = _isOpen ? new Color(33, 150, 243, 255) : new Color(200, 200, 200, 255);
        if (!Enabled)
            borderColor = new Color(220, 220, 220, 255);
            
        Raylib.DrawRectangleLinesEx(GetBounds(), 1f, borderColor);
        
        // Текст
        var displayText = SelectedItem ?? Placeholder;
        var textColor = SelectedItem != null ? ResolvedStyle.TextColor : Color.Gray;
        if (!Enabled)
            textColor = new Color(150, 150, 150, 255);
            
        var textPos = new Vector2(
            Position.X + ResolvedStyle.Padding.Left,
            Position.Y + (Size.Y - ResolvedStyle.FontSize) / 2
        );
        
        // Обрезаем текст если не помещается
        var availableWidth = Size.X - ResolvedStyle.Padding.Left - ResolvedStyle.Padding.Right - 20; // -20 для стрелки
        var clippedText = ClipText(displayText, availableWidth);
        
        Raylib.DrawText(clippedText, (int)textPos.X, (int)textPos.Y, ResolvedStyle.FontSize, textColor);
        
        // Стрелка
        var arrowColor = Enabled ? new Color(100, 100, 100, 255) : new Color(180, 180, 180, 255);
        var arrowX = Position.X + Size.X - 15;
        var arrowY = Position.Y + Size.Y / 2;
        
        if (_isOpen)
        {
            // Стрелка вверх
            Raylib.DrawTriangle(
                new Vector2(arrowX - 4, arrowY + 2),
                new Vector2(arrowX + 4, arrowY + 2),
                new Vector2(arrowX, arrowY - 2),
                arrowColor
            );
        }
        else
        {
            // Стрелка вниз
            Raylib.DrawTriangle(
                new Vector2(arrowX - 4, arrowY - 2),
                new Vector2(arrowX + 4, arrowY - 2),
                new Vector2(arrowX, arrowY + 2),
                arrowColor
            );
        }
        
        // Рендерим выпадающий список
        if (_isOpen && Items.Count > 0)
        {
            RenderDropdownList();
        }
    }
    
    private void RenderDropdownList()
    {
        var dropdownRect = GetDropdownRect();
        
        // Фон списка
        Raylib.DrawRectangleRounded(dropdownRect, 0.2f, 8, Color.White);
        
        // Рамка списка
        Raylib.DrawRectangleLinesEx(dropdownRect, 1f, new Color(200, 200, 200, 255));
        
        // Элементы списка
        var mousePos = Raylib.GetMousePosition();
        
        for (int i = 0; i < Items.Count && i < (int)(MaxDropdownHeight / ItemHeight); i++)
        {
            var itemRect = new Rectangle(
                dropdownRect.X,
                dropdownRect.Y + i * ItemHeight,
                dropdownRect.Width,
                ItemHeight
            );
            
            // Подсветка при наведении
            if (Raylib.CheckCollisionPointRec(mousePos, itemRect))
            {
                Raylib.DrawRectangle((int)itemRect.X, (int)itemRect.Y, (int)itemRect.Width, (int)itemRect.Height, 
                    new Color(240, 240, 240, 255));
            }
            
            // Подсветка выбранного элемента
            if (i == SelectedIndex)
            {
                Raylib.DrawRectangle((int)itemRect.X, (int)itemRect.Y, (int)itemRect.Width, (int)itemRect.Height, 
                    new Color(33, 150, 243, 50));
            }
            
            // Текст элемента
            var itemTextPos = new Vector2(
                itemRect.X + 10,
                itemRect.Y + (ItemHeight - ResolvedStyle.FontSize) / 2
            );
            
            Raylib.DrawText(Items[i], (int)itemTextPos.X, (int)itemTextPos.Y, ResolvedStyle.FontSize, ResolvedStyle.TextColor);
        }
    }
    
    private string ClipText(string text, float maxWidth)
    {
        if (Raylib.MeasureText(text, ResolvedStyle.FontSize) <= maxWidth)
            return text;
            
        for (int i = text.Length - 1; i >= 0; i--)
        {
            var substring = text.Substring(0, i) + "...";
            if (Raylib.MeasureText(substring, ResolvedStyle.FontSize) <= maxWidth)
                return substring;
        }
        
        return "...";
    }
}