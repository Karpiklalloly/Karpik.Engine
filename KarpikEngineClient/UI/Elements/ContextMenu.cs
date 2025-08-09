using System.Numerics;
using Karpik.Engine.Client.UIToolkit.Manipulators;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit.Elements;

public class ContextMenuItem
{
    public string Text { get; set; }
    public string? Icon { get; set; }
    public Action? Action { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsSeparator { get; set; } = false;
    public List<ContextMenuItem>? SubItems { get; set; }
    
    public ContextMenuItem(string text, Action? action = null)
    {
        Text = text;
        Action = action;
    }
    
    public static ContextMenuItem Separator()
    {
        return new ContextMenuItem("") { IsSeparator = true };
    }
}

public class ContextMenu : VisualElement
{
    private readonly List<ContextMenuItem> _items = new();
    private const float ItemHeight = 30f;
    private const float SeparatorHeight = 8f;
    private const float MinWidth = 150f;
    
    public event Action? OnClose;
    
    public ContextMenu() : base("ContextMenu")
    {
        AddClass("context-menu");
        Visible = false;
    }
    
    public void AddItem(ContextMenuItem item)
    {
        _items.Add(item);
        RecalculateSize();
    }
    
    public void AddItem(string text, Action? action = null)
    {
        AddItem(new ContextMenuItem(text, action));
    }
    
    public void AddSeparator()
    {
        AddItem(ContextMenuItem.Separator());
    }
    
    public void Clear()
    {
        _items.Clear();
        RecalculateSize();
    }
    
    private void RecalculateSize()
    {
        if (_items.Count == 0)
        {
            Size = new Vector2(MinWidth, 0);
            return;
        }
        
        // Вычисляем ширину на основе самого длинного текста
        var maxWidth = MinWidth;
        foreach (var item in _items.Where(i => !i.IsSeparator))
        {
            var textWidth = Raylib.MeasureText(item.Text, Style.FontSize) + 20; // +20 для отступов
            maxWidth = Math.Max(maxWidth, textWidth);
        }
        
        // Вычисляем высоту
        var totalHeight = 0f;
        foreach (var item in _items)
        {
            totalHeight += item.IsSeparator ? SeparatorHeight : ItemHeight;
        }
        
        Size = new Vector2(maxWidth, totalHeight + 10); // +10 для отступов сверху и снизу
    }
    
    public void ShowAt(Vector2 position)
    {
        Position = position;
        
        // Проверяем, не выходит ли меню за границы экрана
        var screenWidth = Raylib.GetRenderWidth();
        var screenHeight = Raylib.GetRenderHeight();
        
        if (Position.X + Size.X > screenWidth)
        {
            Position = new Vector2(screenWidth - Size.X, Position.Y);
        }
        
        if (Position.Y + Size.Y > screenHeight)
        {
            Position = new Vector2(Position.X, screenHeight - Size.Y);
        }
        
        Visible = true;
        
        // Анимация появления
        SlideIn(new Vector2(0, -10), 0.15f);
    }
    
    public void Hide()
    {
        FadeOut(0.1f, () =>
        {
            Visible = false;
            OnClose?.Invoke();
        });
    }
    
    public override void Update(float deltaTime)
    {
        base.Update(deltaTime);
        
        if (!Visible) return;
        
        // Закрываем меню при клике вне его
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = Raylib.GetMousePosition();
            if (!ContainsPoint(mousePos))
            {
                Hide();
            }
        }
        
        // Обрабатываем клики по элементам
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = Raylib.GetMousePosition();
            if (ContainsPoint(mousePos))
            {
                var clickedItem = GetItemAtPosition(mousePos);
                if (clickedItem != null && !clickedItem.IsSeparator && clickedItem.Enabled)
                {
                    clickedItem.Action?.Invoke();
                    Hide();
                }
            }
        }
    }
    
    private ContextMenuItem? GetItemAtPosition(Vector2 position)
    {
        var relativeY = position.Y - Position.Y - 5; // -5 для верхнего отступа
        var currentY = 0f;
        
        foreach (var item in _items)
        {
            var itemHeight = item.IsSeparator ? SeparatorHeight : ItemHeight;
            
            if (relativeY >= currentY && relativeY < currentY + itemHeight)
            {
                return item;
            }
            
            currentY += itemHeight;
        }
        
        return null;
    }
    
    protected override void RenderSelf()
    {
        if (!Visible) return;
        
        // Рендерим тень
        var shadowOffset = new Vector2(2, 2);
        var shadowColor = new Color(0, 0, 0, 80);
        Raylib.DrawRectangle(
            (int)(Position.X + shadowOffset.X), 
            (int)(Position.Y + shadowOffset.Y),
            (int)Size.X, (int)Size.Y, 
            shadowColor
        );
        
        // Рендерим фон меню
        Raylib.DrawRectangle((int)Position.X, (int)Position.Y, (int)Size.X, (int)Size.Y, Color.White);
        Raylib.DrawRectangleLinesEx(GetBounds(), 1f, new Color(200, 200, 200, 255));
        
        // Рендерим элементы
        var currentY = Position.Y + 5;
        var mousePos = Raylib.GetMousePosition();
        
        foreach (var item in _items)
        {
            if (item.IsSeparator)
            {
                // Рендерим разделитель
                var separatorY = currentY + SeparatorHeight / 2;
                Raylib.DrawLine(
                    (int)(Position.X + 10), (int)separatorY,
                    (int)(Position.X + Size.X - 10), (int)separatorY,
                    new Color(220, 220, 220, 255)
                );
                currentY += SeparatorHeight;
            }
            else
            {
                // Рендерим элемент меню
                var itemRect = new Rectangle(Position.X, currentY, Size.X, ItemHeight);
                
                // Подсветка при наведении
                if (item.Enabled && Raylib.CheckCollisionPointRec(mousePos, itemRect))
                {
                    Raylib.DrawRectangle((int)itemRect.X, (int)itemRect.Y, (int)itemRect.Width, (int)itemRect.Height,
                        new Color(240, 240, 240, 255));
                }
                
                // Текст элемента
                var textColor = item.Enabled ? Color.Black : new Color(150, 150, 150, 255);
                var textY = currentY + (ItemHeight - Style.FontSize) / 2;
                
                Raylib.DrawText(item.Text, (int)(Position.X + 10), (int)textY, Style.FontSize, textColor);
                
                currentY += ItemHeight;
            }
        }
    }
}

public class ContextMenuManager
{
    private readonly LayerManager _layerManager;
    private ContextMenu? _activeMenu;
    private string? _activeLayerName;
    
    public ContextMenuManager(LayerManager layerManager)
    {
        _layerManager = layerManager;
    }
    
    public void ShowContextMenu(ContextMenu menu, Vector2 position)
    {
        // Закрываем предыдущее меню если есть
        HideContextMenu();
        
        var layerName = $"context_menu_{DateTime.Now.Ticks}";
        var layer = _layerManager.CreateLayer(layerName, 2000); // Высокий Z-индекс
        
        layer.Root = menu;
        layer.BlocksInput = false; // Не блокируем ввод полностью
        
        menu.ShowAt(position);
        menu.OnClose += HideContextMenu;
        
        _activeMenu = menu;
        _activeLayerName = layerName;
    }
    
    public void HideContextMenu()
    {
        if (_activeMenu != null && _activeLayerName != null)
        {
            _activeMenu.Hide();
            _layerManager.RemoveLayer(_activeLayerName);
            _activeMenu = null;
            _activeLayerName = null;
        }
    }
    
    public bool HasActiveMenu => _activeMenu != null;
}