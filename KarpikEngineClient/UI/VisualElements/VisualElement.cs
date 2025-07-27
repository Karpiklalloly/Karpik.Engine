using System.Numerics;
using Raylib_cs;

namespace Karpik.Engine.Client.VisualElements;

public class VisualElement
{
    public VisualElement Parent { get; private set; }
    public List<VisualElement> Children { get; private set; } = [];
    internal IEnumerable<VisualElement> AllChildren => Children.Concat(Children.SelectMany(x => x.AllChildren));

    
    public Rectangle Bounds { get; protected set; }
    public Vector2 Size { get; set; }
    public Vector2 MinSize { get; set; } = Vector2.Zero;
    public Vector2 MaxSize { get; set; } = new(float.MaxValue, float.MaxValue);
    public Anchor Anchor { get; set; } = Anchor.TopLeft;
    public StretchMode Stretch { get; set; } = StretchMode.None;
    public Vector2 OffsetPosition { get; set; } = Vector2.Zero;
    public Vector2 Pivot { get; set; } = new Vector2(0.5f, 0.5f);

    public bool IsVisible
    {
        get => _isVisible && (Parent == null || Parent.IsVisible);
        set => _isVisible = value;
    }

    public bool IsEnabled
    {
        get => _isEnabled && (Parent == null || Parent.IsEnabled);
        set => _isEnabled = value;
    }
    public bool IsHovered { get; private set; }
    public int Order { get; set; } = 0;
    public string Name { get; set; } = string.Empty;
    
    private bool _isVisible = true;
    private bool _isEnabled = true;
    private HashSet<string> _tags = new();

    public VisualElement(Vector2 size)
    {
        Size = size;
    }
    
    public void Add(VisualElement child)
    {
        if (child != null && child != this && !Children.Contains(child))
        {
            child.Parent?.Remove(child);
            child.Parent = this;
            Children.Add(child);
            child.UpdateLayout();
        }
    }

    public void Remove(VisualElement child)
    {
        if (child != null && Children.Contains(child))
        {
            if (child.Parent == this)
            {
                child.Parent = null;
            }

            Children.Remove(child);
        }
    }

    public void AddTag(string tag)
    {
        _tags.Add(tag);
    }
    
    public void RemoveTag(string tag)
    {
        _tags.Remove(tag);
    }
    
    public bool HasTag(string tag)
    {
        return _tags.Contains(tag);
    }

    public virtual void UpdateLayout()
    {
        Vector2 finalSize;
        Vector2 finalPosition;

        if (Parent == null)
        {
            finalSize = Vector2.Clamp(Size, MinSize, MaxSize);
            finalPosition = OffsetPosition - finalSize * Pivot;
        }
        else
        {
            Rectangle parentBounds = Parent.Bounds;

            // 1. Определяем размер элемента
            Vector2 availableSpace = new Vector2(
                parentBounds.Width * (Anchor.Max.X - Anchor.Min.X),
                parentBounds.Height * (Anchor.Max.Y - Anchor.Min.Y)
            );

            // Горизонтальный размер
            if (Anchor.StretchesHorizontally && (Stretch & StretchMode.Horizontal) != 0)
            {
                // Растягиваем: доступное пространство минус горизонтальные смещения (теперь OffsetPosition - это смещение центра)
                // Это сложнее, так как OffsetPosition влияет на центр, а не края.
                // Упрощенный вариант: растягиваем до доступного пространства
                finalSize.X = availableSpace.X;
                // TODO: Более точный расчет с учетом OffsetPosition и Pivot для растягивания
            }
            else
            {
                finalSize.X = Size.X; // Используем заданный размер
            }

            // Вертикальный размер
            if (Anchor.StretchesVertically && (Stretch & StretchMode.Vertical) != 0)
            {
                finalSize.Y = availableSpace.Y;
                // TODO: Более точный расчет растягивания по вертикали
            }
            else
            {
                finalSize.Y = Size.Y;
            }

            // Ограничиваем размер Min/Max значениями
            finalSize = Vector2.Clamp(finalSize, MinSize, MaxSize);

            // 2. Определяем позицию точки якоря на родителе (используем Min якорь как базу)
            Vector2 anchorPoint = new Vector2(
                parentBounds.X + parentBounds.Width * Anchor.Min.X,
                parentBounds.Y + parentBounds.Height * Anchor.Min.Y
            );

            // 3. Рассчитываем позицию центра элемента
            Vector2 elementCenter = anchorPoint + OffsetPosition;

            // 4. Рассчитываем позицию верхнего левого угла (для Bounds)
            finalPosition = elementCenter - finalSize * Pivot;
        }

        // Устанавливаем финальные Bounds
        Bounds = new Rectangle((int)finalPosition.X, (int)finalPosition.Y, (int)finalSize.X, (int)finalSize.Y);
    }

    internal void Update(double deltaTime)
    {
        if (!IsVisible) return;
        
        UpdateLayout();

        var mousePosition = Input.MousePosition;
        IsHovered = Bounds.Contains(mousePosition);

        for (int i = Children.Count - 1; i >= 0; i--)
        {
            Children[i].Update(deltaTime);
        }
        
        HandleInput();
        OnUpdate(deltaTime);
    }

    internal void Draw(double deltaTime)
    {
        if (!IsVisible) return;
        //TODO: Добавить редрав только если нужно
        //if (!_needRedraw) return;

        UpdateLayout();
        DrawSelf();
    }
    
    protected virtual void HandleInput() { }
    
    protected virtual void DrawSelf() { }
    
    protected virtual void OnUpdate(double deltaTime) { }
    
    protected void Invalidate()
    {
        
    }
}