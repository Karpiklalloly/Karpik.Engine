# UIToolkit - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Большой файл LayoutEngine.cs
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs)

**Проблема**: Файл содержит ~32,000 символов (около 800+ строк). Это нарушает принцип Single Responsibility и делает код трудным для понимания и поддержки.

**Решение**: Разбить на отдельные классы:
- `FlexLayoutCalculator` - расчёт flexbox
- `GridLayoutCalculator` - расчёт grid
- `BlockLayoutCalculator` - расчёт block элементов
- `PositionCalculator` - расчёт позиционирования
- `MeasureText` - измерение текста

---

### CR-002: Мутабельное поле _renderer
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:14)

```csharp
private IRenderer _renderer;

public void Init(IRenderer renderer)
{
    _renderer = renderer;
}
```

**Проблема**: `_renderer` не инициализирован в конструкторе и может быть `null` если `Init()` не вызван.

**Решение**:
```csharp
private readonly IRenderer _renderer;

public LayoutEngine(IRenderer renderer)
{
    _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
}
```

---

## 🟡 Высокий приоритет

### HI-001: Аллокации при каждом вызове Layout
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:11-12)

```csharp
private readonly List<UIElement> _absoluteElements = [];
private readonly List<UIElement> _fixedElements = [];
```

**Проблема**: Списки очищаются и заполняются при каждом вызове `Layout()`. Это создаёт GC pressure.

**Решение**: Использовать пул списков или кэшировать:
```csharp
private readonly List<UIElement> _absoluteElements = new(32);
private readonly List<UIElement> _fixedElements = new(16);

// В Layout():
_absoluteElements.Clear();
_fixedElements.Clear();
```

---

### HI-002: Внутренние классы создаются многократно
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:16-29)

```csharp
private class FlexItemData { ... }
private class FlexLine { ... }
```

**Проблема**: Эти классы создаются многократно при расчёте flexbox. Каждое создание — аллокация.

**Решение**: Использовать `struct` или пул объектов:
```csharp
private struct FlexItemData
{
    public UIElement Element;
    public float FlexBasis;
    public float FinalMainSize;
}

// Использовать Span<FlexItemData> или массив
private FlexItemData[] _flexItemBuffer = new FlexItemData[64];
```

---

### HI-003: Отсутствие null-проверок
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:36-56)

```csharp
public void Layout(UIElement root, RectangleF viewport, IFont defaultFont)
{
    _absoluteElements.Clear();
    _fixedElements.Clear();
    _viewport = viewport;

    FindFixedAndAbsolute(root);
    // ...
}
```

**Проблема**: Нет проверок на `null` для `root` и `defaultFont`.

**Решение**:
```csharp
public void Layout(UIElement root, RectangleF viewport, IFont defaultFont)
{
    ArgumentNullException.ThrowIfNull(root);
    ArgumentNullException.ThrowIfNull(defaultFont);
    
    // ...
}
```

---

### HI-004: Строковые ключи для стилей
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:82)

```csharp
if (parent.ComputedStyle.GetValueOrDefault("display") == "none")
```

**Проблема**: Использование строковых ключей создаёт аллокации при каждом обращении. Сравнение строк медленнее чем enum.

**Решение**: Использовать enum или int-ключи:
```csharp
public enum StyleProperty
{
    Display,
    Position,
    Width,
    Height,
    // ...
}

if (parent.ComputedStyle.Get(StyleProperty.Display) == Display.None)
```

---

## 🟢 Средний приоритет

### MI-001: Отсутствие XML документации
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs)

**Проблема**: Публичные методы не имеют XML документации.

**Решение**:
```csharp
/// <summary>
/// Выполняет компоновку UI элементов в заданном viewport.
/// </summary>
/// <param name="root">Корневой элемент UI дерева.</param>
/// <param name="viewport">Прямоугольник viewport для компоновки.</param>
/// <param name="defaultFont">Шрифт по умолчанию для измерения текста.</param>
public void Layout(UIElement root, RectangleF viewport, IFont defaultFont)
```

---

### MI-002: Рекурсивный обход дерева
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:59-76)

```csharp
private void FindFixedAndAbsolute(UIElement parent)
{
    foreach (var child in parent.Children)
    {
        // ...
        FindFixedAndAbsolute(child);
    }
}
```

**Проблема**: Рекурсивный обход может вызвать `StackOverflowException` при глубокой вложенности.

**Решение**: Использовать итеративный подход со стеком:
```csharp
private void FindFixedAndAbsolute(UIElement root)
{
    var stack = new Stack<UIElement>();
    stack.Push(root);
    
    while (stack.Count > 0)
    {
        var current = stack.Pop();
        foreach (var child in current.Children)
        {
            switch (child.GetPosition())
            {
                case s.position_fixed:
                    _fixedElements.Add(child);
                    break;
                case s.position_absolute:
                    _absoluteElements.Add(child);
                    break;
                default:
                    stack.Push(child);
                    break;
            }
        }
    }
}
```

---

### MI-003: Использование System.Drawing
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs:1)

```csharp
using System.Drawing;
```

**Проблема**: `System.Drawing` зависит от GDI+ и может иметь проблемы с кроссплатформенностью.

**Решение**: Использовать собственные структуры:
```csharp
namespace Karpik.Engine.Client.Graphics.Core;

public readonly struct RectF
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}
```

---

### MI-004: Отсутствие оптимизации для неизменённых элементов
**Файл**: [`LayoutEngine.cs`](Modules/Client/UIToolkit/UIToolkit.Core/LayoutEngine.cs)

**Проблема**: Layout пересчитывается полностью даже если элементы не изменились.

**Решение**: Добавить dirty flags:
```csharp
public void Layout(UIElement root, RectangleF viewport, IFont defaultFont)
{
    if (!root.IsDirty && _lastViewport == viewport)
        return;
    
    // ... расчёт
    
    root.IsDirty = false;
    _lastViewport = viewport;
}
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 18 |
| Классов | ~12 |
| Строк кода | ~2000+ |
| Критичных проблем | 2 |
| Высокий приоритет | 4 |
| Средний приоритет | 4 |

---

## 💡 Предложения по развитию

### 1. Incremental Layout
Добавить инкрементальный расчёт только для изменённых элементов:
```csharp
public void MarkDirty(UIElement element)
{
    element.IsDirty = true;
    element.Parent?.MarkDirty();
}
```

### 2. Layout Cache
Кэшировать результаты расчёта для повторного использования:
```csharp
private readonly Dictionary<UIElement, LayoutResult> _layoutCache = new();
```

### 3. SIMD оптимизации
Использовать SIMD для массовых расчётов:
```csharp
using System.Numerics;
// Vector4 для RectangleF операций
```

### 4. Virtual Layout
Добавить виртуальную компоновку для больших списков:
```csharp
public class VirtualLayoutEngine
{
    public IEnumerable<UIElement> GetVisibleElements(RectangleF viewport);
}
```

### 5. Accessibility
Добавить поддержку доступности (accessibility):
- Screen reader support
- Keyboard navigation
- High contrast mode

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Разбить LayoutEngine.cs на отдельные классы
- [ ] CR-002: Инициализировать _renderer в конструкторе
- [ ] HI-001: Оптимизировать аллокации списков
- [ ] HI-002: Использовать struct для FlexItemData/FlexLine
- [ ] HI-003: Добавить null-проверки
- [ ] HI-004: Заменить строковые ключи на enum
- [ ] MI-001: Добавить XML документацию
- [ ] MI-002: Заменить рекурсию на итерацию
- [ ] MI-003: Заменить System.Drawing на собственные структуры
- [ ] MI-004: Добавить dirty flags для оптимизации
