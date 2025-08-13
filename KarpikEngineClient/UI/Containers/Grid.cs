using System.Numerics;

namespace Karpik.Engine.Client.UIToolkit;

/// <summary>
/// Сетка для табличного размещения элементов
/// </summary>
public class Grid : VisualElement
{
    public int Columns { get; set; } = 1;
    public int Rows { get; set; } = 1;
    public float ColumnGap { get; set; } = 0f;
    public float RowGap { get; set; } = 0f;
    public bool AutoRows { get; set; } = true; // Автоматически добавлять строки при необходимости
    
    private readonly Dictionary<VisualElement, GridPosition> _childPositions = new();
    
    public Grid(int columns = 1, int rows = 1) : base("Grid")
    {
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        AddClass("grid");
    }
    
    /// <summary>
    /// Добавляет элемент в указанную ячейку сетки
    /// </summary>
    public void AddChild(VisualElement child, int column, int row, int columnSpan = 1, int rowSpan = 1)
    {
        if (column < 0 || row < 0 || columnSpan < 1 || rowSpan < 1)
            throw new ArgumentException("Invalid grid position or span values");
            
        // Автоматически расширяем сетку если нужно
        if (AutoRows && row >= Rows)
            Rows = row + 1;
            
        if (column >= Columns)
            Columns = column + 1;
        
        _childPositions[child] = new GridPosition(column, row, columnSpan, rowSpan);
        AddChild(child);
    }
    
    /// <summary>
    /// Добавляет элемент в следующую доступную ячейку
    /// </summary>
    public void AddChildAuto(VisualElement child, int columnSpan = 1, int rowSpan = 1)
    {
        var position = FindNextAvailablePosition(columnSpan, rowSpan);
        AddChild(child, position.Column, position.Row, columnSpan, rowSpan);
    }

    protected override void OnChildRemoved(VisualElement child)
    {
        _childPositions.Remove(child);
        base.OnChildRemoved(child);
    }
    
    public override void Update(double deltaTime)
    {
        base.Update(deltaTime);
        ArrangeChildren();
    }
    
    private void ArrangeChildren()
    {
        if (Children.Count == 0) return;
        
        var cellWidth = CalculateCellWidth();
        var cellHeight = CalculateCellHeight();
        
        var startX = Position.X + (ResolvedStyle.Padding?.Left ?? 0);
        var startY = Position.Y + (ResolvedStyle.Padding?.Top ?? 0);
        
        foreach (var child in Children)
        {
            if (!child.Visible || child.IgnoreLayout) continue;
            
            if (!_childPositions.TryGetValue(child, out var gridPos))
                continue;
                
            var x = startX + gridPos.Column * (cellWidth + ColumnGap);
            var y = startY + gridPos.Row * (cellHeight + RowGap);
            
            var width = cellWidth * gridPos.ColumnSpan + ColumnGap * (gridPos.ColumnSpan - 1);
            var height = cellHeight * gridPos.RowSpan + RowGap * (gridPos.RowSpan - 1);
            
            child.Position = new Vector2(x, y);
            child.Size = new Vector2(width, height);
        }
        
        // Обновляем размер сетки
        UpdateGridSize(cellWidth, cellHeight);
    }
    
    private float CalculateCellWidth()
    {
        var availableWidth = Size.X - (ResolvedStyle.Padding?.Left ?? 0) - (ResolvedStyle.Padding?.Right ?? 0);
        var totalGaps = ColumnGap * (Columns - 1);
        return Math.Max(0, (availableWidth - totalGaps) / Columns);
    }
    
    private float CalculateCellHeight()
    {
        var availableHeight = Size.Y - (ResolvedStyle.Padding?.Top ?? 0) - (ResolvedStyle.Padding?.Bottom ?? 0);
        var totalGaps = RowGap * (Rows - 1);
        return Math.Max(0, (availableHeight - totalGaps) / Rows);
    }
    
    private void UpdateGridSize(float cellWidth, float cellHeight)
    {
        var totalWidth = Columns * cellWidth + ColumnGap * (Columns - 1) + 
                        (ResolvedStyle.Padding?.Left ?? 0) + (ResolvedStyle.Padding?.Right ?? 0);
        var totalHeight = Rows * cellHeight + RowGap * (Rows - 1) + 
                         (ResolvedStyle.Padding?.Top ?? 0) + (ResolvedStyle.Padding?.Bottom ?? 0);
        
        // Обновляем размер только если он не задан явно
        if (!Style.Width.HasValue)
            Size = new Vector2(totalWidth, Size.Y);
            
        if (!Style.Height.HasValue)
            Size = new Vector2(Size.X, totalHeight);
    }
    
    private GridPosition FindNextAvailablePosition(int columnSpan, int rowSpan)
    {
        var occupiedCells = new HashSet<(int, int)>();
        
        // Отмечаем занятые ячейки
        foreach (var (child, pos) in _childPositions)
        {
            for (int r = pos.Row; r < pos.Row + pos.RowSpan; r++)
            {
                for (int c = pos.Column; c < pos.Column + pos.ColumnSpan; c++)
                {
                    occupiedCells.Add((c, r));
                }
            }
        }
        
        // Ищем свободное место
        for (int row = 0; row < Rows || AutoRows; row++)
        {
            for (int col = 0; col <= Columns - columnSpan; col++)
            {
                bool canPlace = true;
                
                // Проверяем, можем ли разместить элемент в этой позиции
                for (int r = row; r < row + rowSpan && canPlace; r++)
                {
                    for (int c = col; c < col + columnSpan && canPlace; c++)
                    {
                        if (occupiedCells.Contains((c, r)))
                            canPlace = false;
                    }
                }
                
                if (canPlace)
                    return new GridPosition(col, row, columnSpan, rowSpan);
            }
            
            if (!AutoRows && row >= Rows - 1)
                break;
        }
        
        // Если не нашли место, добавляем новую строку (если AutoRows включен)
        if (AutoRows)
        {
            Rows++;
            return new GridPosition(0, Rows - 1, columnSpan, rowSpan);
        }
        
        return new GridPosition(0, 0, columnSpan, rowSpan);
    }
}

/// <summary>
/// Позиция элемента в сетке
/// </summary>
public record GridPosition(int Column, int Row, int ColumnSpan = 1, int RowSpan = 1);