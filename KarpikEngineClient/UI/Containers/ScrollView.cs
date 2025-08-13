using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Прокручиваемый контейнер для больших объемов контента
/// </summary>
public class ScrollView : VisualElement
{
    public Vector2 ScrollOffset { get; set; } = Vector2.Zero;
    public bool EnableVerticalScroll { get; set; } = true;
    public bool EnableHorizontalScroll { get; set; } = false;
    public float ScrollSpeed { get; set; } = 20f;
    
    private Vector2 _contentSize;
    private bool _isDragging = false;
    private Vector2 _lastMousePos;
    
    public ScrollView() : base("ScrollView")
    {
        AddClass("scroll-view");
    }
    
    public override void Update(double deltaTime)
    {
        base.Update(deltaTime);
        UpdateContentSize();
        HandleScrollInput();
    }
    
    private void UpdateContentSize()
    {
        if (Children.Count == 0)
        {
            _contentSize = Vector2.Zero;
            return;
        }
        
        float maxX = 0, maxY = 0;
        foreach (var child in Children)
        {
            if (!child.Visible) continue;
            
            var childRight = child.Position.X + child.Size.X - Position.X;
            var childBottom = child.Position.Y + child.Size.Y - Position.Y;
            
            maxX = Math.Max(maxX, childRight);
            maxY = Math.Max(maxY, childBottom);
        }
        
        _contentSize = new Vector2(maxX, maxY);
    }
    
    private void HandleScrollInput()
    {
        var mousePos = Raylib.GetMousePosition();
        
        if (ContainsPoint(mousePos))
        {
            // Прокрутка колесом мыши
            var wheelMove = Raylib.GetMouseWheelMove();
            if (wheelMove != 0)
            {
                if (EnableVerticalScroll)
                {
                    ScrollOffset = new Vector2(
                        ScrollOffset.X,
                        Math.Max(0, Math.Min(_contentSize.Y - Size.Y, ScrollOffset.Y - wheelMove * ScrollSpeed))
                    );
                }
            }
            
            // Начало перетаскивания
            if (Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                _isDragging = true;
                _lastMousePos = mousePos;
            }
        }
        
        // Перетаскивание для прокрутки
        if (_isDragging)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var delta = mousePos - _lastMousePos;
                
                if (EnableHorizontalScroll)
                {
                    ScrollOffset = new Vector2(
                        Math.Max(0, Math.Min(_contentSize.X - Size.X, ScrollOffset.X - delta.X)),
                        ScrollOffset.Y
                    );
                }
                
                if (EnableVerticalScroll)
                {
                    ScrollOffset = new Vector2(
                        ScrollOffset.X,
                        Math.Max(0, Math.Min(_contentSize.Y - Size.Y, ScrollOffset.Y - delta.Y))
                    );
                }
                
                _lastMousePos = mousePos;
            }
            else
            {
                _isDragging = false;
            }
        }
    }
    
    public override void Render()
    {
        if (!Visible) return;
        
        // Рендерим фон контейнера
        RenderSelf();
        
        // Включаем scissor test для обрезки содержимого
        Raylib.BeginScissorMode(
            (int)Position.X, (int)Position.Y,
            (int)Size.X, (int)Size.Y
        );
        
        // Сдвигаем позиции детей на величину прокрутки
        foreach (var child in Children)
        {
            var originalPos = child.Position;
            child.Position = new Vector2(
                originalPos.X - ScrollOffset.X,
                originalPos.Y - ScrollOffset.Y
            );
            
            child.Render();
            
            // Восстанавливаем оригинальную позицию
            child.Position = originalPos;
        }
        
        Raylib.EndScissorMode();
        
        // Рендерим полосы прокрутки если нужно
        RenderScrollbars();
    }
    
    private void RenderScrollbars()
    {
        var scrollbarWidth = 12f;
        var scrollbarColor = new Color(128, 128, 128, 180);
        var thumbColor = new Color(160, 160, 160, 200);
        
        // Вертикальная полоса прокрутки
        if (EnableVerticalScroll && _contentSize.Y > Size.Y)
        {
            var scrollbarX = Position.X + Size.X - scrollbarWidth;
            var scrollbarHeight = Size.Y;
            
            // Фон полосы прокрутки
            Raylib.DrawRectangle(
                (int)scrollbarX, (int)Position.Y,
                (int)scrollbarWidth, (int)scrollbarHeight,
                scrollbarColor
            );
            
            // Ползунок
            var thumbHeight = Math.Max(20, (Size.Y / _contentSize.Y) * scrollbarHeight);
            var thumbY = Position.Y + (ScrollOffset.Y / (_contentSize.Y - Size.Y)) * (scrollbarHeight - thumbHeight);
            
            Raylib.DrawRectangle(
                (int)scrollbarX + 2, (int)thumbY,
                (int)scrollbarWidth - 4, (int)thumbHeight,
                thumbColor
            );
        }
        
        // Горизонтальная полоса прокрутки
        if (EnableHorizontalScroll && _contentSize.X > Size.X)
        {
            var scrollbarY = Position.Y + Size.Y - scrollbarWidth;
            var scrollbarWidthActual = Size.X;
            
            // Фон полосы прокрутки
            Raylib.DrawRectangle(
                (int)Position.X, (int)scrollbarY,
                (int)scrollbarWidthActual, (int)scrollbarWidth,
                scrollbarColor
            );
            
            // Ползунок
            var thumbWidth = Math.Max(20, (Size.X / _contentSize.X) * scrollbarWidthActual);
            var thumbX = Position.X + (ScrollOffset.X / (_contentSize.X - Size.X)) * (scrollbarWidthActual - thumbWidth);
            
            Raylib.DrawRectangle(
                (int)thumbX, (int)scrollbarY + 2,
                (int)thumbWidth, (int)scrollbarWidth - 4,
                thumbColor
            );
        }
    }
}