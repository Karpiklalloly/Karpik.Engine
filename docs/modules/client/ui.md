# GameUI — Документация модуля

> Дата создания: 2026-03-25

## Введение

GameUI — модуль для создания пользовательского интерфейса в играх. Отвечает за отображение меню, HUD, диалогов и другого игрового UI.

**Отдельный от DebugUI** — DebugUI используется для debug tools (imGui-style), GameUI — для игрового интерфейса.

---

## Архитектура

### Структура модуля

```
Modules/Client/UI/GameUI/
├── GameUI.Core/               # Базовые типы и абстракции
│   ├── Types.cs               # Rectangle, Color, Vector2, Size
│   ├── IRenderer.cs           # Интерфейс рендерера
│   └── Enums.cs               # UiTypeId, LayoutDirection, etc.
└── GameUI.EngineNative/      # Retained-mode реализация
    ├── Widgets/               # Виджеты и данные
    ├── Layout/                # Flexbox-подобная система
    ├── Events/                # Обработка событий
    ├── Input/                 # Hit testing
    ├── Styles/                # CSS-like стили
    └── Rendering/             # Рендеринг виджетов
```

### Слои системы

```
┌─────────────────────────────────────────┐
│           GameUI (Retained)              │
├─────────────────────────────────────────┤
│  Widgets → Layout → Events → Styles      │
├─────────────────────────────────────────┤
│           IRenderer (Graphics)           │
└─────────────────────────────────────────┘
```

---

## Core

### Базовые типы

```csharp
// Rectangle — позиция и размер
public struct Rectangle
{
    public float X, Y, Width, Height;
}

// Color — цвет в ARGB
public struct Color
{
    public byte A, R, G, B;
    
    public static Color Gray { get; }
    public static Color White { get; }
    public static Color Black { get; }
}
```

### UiTypeId — типы элементов

```csharp
public enum UiTypeId
{
    None = 0,
    Window = 1,
    Button = 2,
    Label = 3,
    InputField = 4,
    Slider = 5,
    ProgressBar = 6,
    ComboBox = 7,
    Horizontal = 8,
    Vertical = 9,
    Image = 10,
    Grid = 11,
}
```

### IRenderer — интерфейс рендерера

```csharp
public interface IRenderer
{
    void DrawRect(Rectangle rect, Color background);
    void DrawText(Rectangle rect, string text, Color color, float fontSize);
    void DrawTexture(Rectangle rect, TextureId texture);
    // ...
}
```

---

## Widgets (Виджеты)

### Принцип: Data-Driven, без наследования

Виджеты — это **данные**, не классы. Нет иерархии `Button : Element`.

```csharp
// Widget — базовая структура
public struct UIWidget
{
    public UiTypeId Type;
    public string Id;
    public Rectangle Bounds;
    public int ZIndex;
    
    // Иерархия — через индексы (data-driven)
    public int ParentIndex;        // -1 = нет parent
    public int FirstChildIndex;    // -1 = нет детей
    public int NextSiblingIndex;   // -1 = последний
    public int PrevSiblingIndex;   // -1 = первый
}

// Данные для конкретного типа — отдельные структуры
public struct ButtonData
{
    public string Text;
    public Color Background;
    public Action OnClick;
}

public struct LabelData
{
    public string Text;
    public Color TextColor;
    public float FontSize;
}

public struct ImageData
{
    public TextureId Texture;
    public Color Tint;
}
```

### Как это работает

```csharp
// Система рендера — смотрит на Type и выбирает нужный Data
public class RenderSystem
{
    public void Render(UIWidget widget, ButtonData data)
    {
        // Рендер кнопки
    }
    
    public void Render(UIWidget widget, LabelData data)
    {
        // Рендер текста
    }
}
```

### Почему без наследования

- **ECS-idiomatic** — данные, не объекты
- **Гибкость** — легко добавить новый тип
- **Производительность** — blittable структуры
- **Композиция** — можно добавить любые данные к любому виджету

---

## Иерархия элементов

### Принцип: Linked List в данных

Дерево UI хранится как **linked list** через индексы — data-driven подход.

```csharp
struct UIWidget
{
    // Основные данные
    public UiTypeId Type;
    public string Id;
    public Rectangle Bounds;
    public int ZIndex;
    
    // Иерархия — через индексы
    public int ParentIndex;        // -1 = нет parent
    public int FirstChildIndex;   // -1 = нет детей
    public int NextSiblingIndex;  // -1 = последний
    public int PrevSiblingIndex;  // -1 = первый
}
```

### Почему через индексы

- **Cache-friendly** — данные в массиве
- **Без классов** — соответствует data-driven подходу
- **Быстро** — O(1) добавление/удаление
- **Просто** — linked list логика

### Пример обхода

```csharp
// Обход всех детей
public IEnumerable<int> GetChildren(int parentIndex)
{
    var childIndex = _widgets[parentIndex].FirstChildIndex;
    while (childIndex != -1)
    {
        yield return childIndex;
        childIndex = _widgets[childIndex].NextSiblingIndex;
    }
}

// Обход всего дерева (DFS)
public void Traverse(int rootIndex, Action<int> visit)
{
    visit(rootIndex);
    foreach (var child in GetChildren(rootIndex))
    {
        Traverse(child, visit);
    }
}
```

---

## Глобальные ресурсы (ResourceDictionary)

### Назначение

Централизованное хранение:
- Цветов с именами
- Размеров (margins, paddings)
- Шрифтов
- Стилей по умолчанию

Аналог: `ResourceDictionary` в WPF, `ControlTheme` в Avalonia.

### Пример структуры (XAML/HTML-like)

```xml
<!-- themes/default.uitheme -->
<Resources>
    <Colors>
        <Color name="primary" value="#007bff"/>
        <Color name="secondary" value="#6c757d"/>
        <Color name="success" value="#28a745"/>
        <Color name="danger" value="#dc3545"/>
        <Color name="background" value="#ffffff"/>
        <Color name="surface" value="#f8f9fa"/>
        <Color name="text-primary" value="#212529"/>
        <Color name="text-secondary" value="#6c757d"/>
    </Colors>
    
    <Sizes>
        <Size name="padding-small" value="4"/>
        <Size name="padding-medium" value="8"/>
        <Size name="padding-large" value="16"/>
        <Size name="margin-small" value="4"/>
        <Size name="margin-medium" value="8"/>
        <Size name="margin-large" value="16"/>
        <Size name="border-radius" value="4"/>
        <Size name="font-size-small" value="12"/>
        <Size name="font-size-normal" value="14"/>
        <Size name="font-size-large" value="16"/>
    </Sizes>
    
    <Fonts>
        <Font name="default" family="Inter"/>
        <Font name="heading" family="Inter Bold"/>
    </Fonts>
</Resources>
```

### Programmatic определение

```csharp
// Определение ресурсов в коде
public static class GameResources
{
    public static readonly ResourceDictionary Default = new()
    {
        // Цвета
        { "primary", Color.FromHex("#007bff") },
        { "secondary", Color.FromHex("#6c757d") },
        { "background", Color.FromHex("#ffffff") },
        
        // Размеры
        { "padding-medium", 8f },
        { "border-radius", 4f },
        
        // Шрифты
        { "font-default", "Inter" },
    };
}
```

### Использование в стилях

```css
/* Ссылка на ресурс */
Button {
    background: $primary;
    padding: $padding-medium;
    border-radius: $border-radius;
}

Label {
    color: $text-primary;
    font-size: $font-size-normal;
}
```

### Динамическое переключение тем

```csharp
// Загрузка темы
public void LoadTheme(string themeName)
{
    var theme = LoadThemeFile($"themes/{themeName}.uitheme");
    Resources.Merge(theme);
}

// Переключение в рантайме
void OnThemeChanged(string newTheme)
{
    LoadTheme(newTheme);
    // Перерисовать весь UI
}
```

### Каскад с ресурсами

```
Стиль элемента
    ↓ (fallback)
Класс стиля
    ↓ (fallback)
Ресурс (ResourceDictionary)
    ↓ (fallback)
Значение по умолчанию
```

```css
/* Стиль ссылается на ресурс */
Button {
    background: $primary;  // Если нет — используется значение по умолчанию
}
```

---

## Layout (Компоновка)

### Упрощённый Flexbox

Система компоновки — упрощённый flexbox, как в CSS.

### Свойства контейнера

```csharp
public struct FlexContainerStyle
{
    public FlexDirection Direction;      // Row | Column
    public JustifyContent Justify;       // Start | Center | End | SpaceBetween | SpaceAround
    public AlignItems Align;             // Start | Center | End | Stretch
    public float Gap;                    // Отступ между элементами
    public bool Wrap;                    // Перенос на новую строку
}

public enum FlexDirection { Row, Column }
public enum JustifyContent { Start, Center, End, SpaceBetween, SpaceAround }
public enum AlignItems { Start, Center, End, Stretch }
```

### Пример

```csharp
// Контейнер с flexbox
var panel = new UIWidget
{
    Type = UiTypeId.Vertical,
    Style = new FlexContainerStyle
    {
        Direction = FlexDirection.Column,
        Justify = JustifyContent.Center,
        Gap = 8
    }
};
```

### Как работает

1. **Pass 1** — вычисление preferred size каждого элемента
2. **Pass 2** — распределение по контейнеру согласно flexbox правилам
3. **Pass 3** — запись финальных Bounds в каждый виджет

### Производительность

- **O(n)** — линейно от количества элементов в контейнере
- Для >1000 элементов — использовать виртуализацию
- Dirty flags — пересчитывать только при изменении

---

## Events (События)

### Обработка событий

#### Callbacks (для простого UI)

```csharp
var button = new UIWidget { Type = UiTypeId.Button };
button.OnClick = () => Debug.Log("Clicked!");
```

#### Events (для сложного UI с несколькими подписчиками)

```csharp
public class ButtonWidget
{
    public event Action OnClick;
    public event Action OnHover;
    public event Action OnPress;
}
```

#### ECS Events — НЕ используются для UI

События в ECS создают сущности — это overhead для UI. Вместо этого используются lightweight callbacks.

### Bubbling (всплытие)

Для MVVM поддерживается bubbling — событие поднимается к родителю.

```
Button (click)
    ↓ bubble
Panel (click)  
    ↓ bubble
Window (click)
```

```csharp
// Включение bubbling
button.BubbleEvents = true;

panel.OnClick = (e) => 
{
    Console.WriteLine("Клик на элемент внутри!");
    e.Bubble(); // продолжить наверх
};
```

---

## Input (Ввод)

### Hit Testing

Определение элемента под курсором.

#### Простой подход (Z-index + linear search)

```csharp
// O(n) — проверяем все элементы в обратном Z-порядке
public UIWidget HitTest(Vector2 position)
{
    for (int i = _widgets.Count - 1; i >= 0; i--)
    {
        if (_widgets[i].Bounds.Contains(position))
            return _widgets[i];
    }
    return null;
}
```

**Когда использовать:**
- <100 элементов
- Простые UI (меню, диалоги)

#### Z-index

```csharp
// Элементы сортированы по Z — проверяем сверху вниз
// Как только нашли — stop
```

#### Quadtree (потом, если понадобится)

Для 10,000+ элементов — quadtree для O(log n) поиска.

### Состояния элементов

```csharp
public enum InteractionState
{
    Normal,
    Hovered,
    Pressed,
    Focused,
    Disabled
}
```

---

## Styles (Стили)

### Принцип: CSS-like

Стили работают как в CSS — каскад, специфичность, наследование.

### Определение стилей

```csharp
// Стиль — класс с пулом
public class UIStyle
{
    public Color Background;
    public Color TextColor;
    public Padding Padding;
    public Margin Margin;
    public float CornerRadius;
    public float FontSize;
    public Border Border;
    
    // Пул для переиспользования
    private static readonly ObjectPool<UIStyle> Pool = new();
    
    public static UIStyle Rent() => Pool.Rent();
    public void Return() => Pool.Return(this);
}
```

### Селекторы

| Селектор | Пример | Приоритет |
|----------|--------|-----------|
| Тип | `Button` | 1 |
| Класс | `.primary` | 10 |
| ID | `#main-menu` | 100 |
| Псевдо-состояние | `:hover`, `:active` | — |

### Применение стилей

```csharp
// Добавить класс
button.AddClass("primary");
button.AddClass("rounded");

// Установить ID
button.Id = "submit-btn";

// Состояния
button.SetPseudoState(PseudoState.Hover);
```

### Каскад

Порядок применения — как в CSS:

```css
/* 1. Тип */
Button { background: gray; }

/* 2. Класс */
.primary { background: blue; }

/* 3. ID (высший приоритет) */
#submit-btn { background: green; }
```

**Специфичность:**
- ID > Class > Type

### Наследуемые свойства

```csharp
// Наследуется: Color, FontSize, FontFamily
// Не наследуется: Background, Padding, Margin, Border

public enum StyleProperty
{
    // Наследуемые
    Color,
    FontSize,
    FontFamily,
    
    // Не наследуемые
    Background,
    Padding,
    Margin,
    Border,
    CornerRadius,
}
```

---

## MVVM для сложных меню

### Когда использовать

- **Простой UI** (HP bar, ammo) → Direct update (ECS → UI)
- **Сложные меню** (настройки, инвентарь) → MVVM

### MVVM архитектура

```
View (UI Elements)
    ↓ DataBinding
ViewModel (данные + команды)
    ↓
Model (данные)
```

### Интерфейс IViewModel

Только интерфейс, без наследования — каждый ViewModel сам решает что реализовать.

```csharp
public interface IViewModel
{
    // Маркерный интерфейс — без обязательных членов
    // Каждый VM реализует что нужно
}
```

### Пример

```csharp
// ViewModel — реализует IViewModel
public class SettingsViewModel : IViewModel
{
    public Bindable<int> Volume { get; } = new(50);
    public Bindable<bool> VSync { get; } = new(true);
    
    public ICommand ApplyCommand { get; }
    public ICommand CancelCommand { get; }
}

// View — связывается с ViewModel
public class SettingsMenu
{
    public void Build(UIBuilder b, SettingsViewModel vm)
    {
        b.Label("Volume").BindText(vm.Volume);
        b.Slider().BindValue(vm.Volume);
        b.Checkbox("VSync").BindChecked(vm.VSync);
        b.Button("Apply").BindCommand(vm.ApplyCommand);
    }
}
```

### Почему не для игрового UI

- **Динамичность** — HP bar обновляется каждый кадр, binding overhead
- **Уже есть ECS** — данные уже в ECS, лишний слой
- **Performance** — для 10,000 элементов таблицы binding не нужен

### Производительность MVVM

Для меню (открывается редко, <100 элементов) — **не критично**.

| Фактор | Влияние |
|--------|---------|
| Количество элементов | 10-50 — нормально |
| Частота обновления | Меню меняется редко |
| Открытие меню | 100-500ms — приемлемо |

---

## Таблицы и Grid (Виртуализация)

### Проблема

Табличные игры (Excel-like) могут иметь 10,000+ ячеек.

### Решение: Виртуализация

Рендерим только видимые элементы.

```csharp
public class VirtualGrid
{
    private int _visibleRows;
    private int _visibleCols;
    private int _scrollX, _scrollY;
    
    // Данные — отдельно от UI
    private CellData[,] _cells;
    
    public void Render(IRenderer renderer)
    {
        // Рендерим только видимые
        for (int row = _scrollY; row < _scrollY + _visibleRows; row++)
        {
            for (int col = _scrollX; col < _scrollX + _visibleCols; col++)
            {
                RenderCell(renderer, row, col);
            }
        }
    }
}
```

### Без ECS

Таблицы — это data, не game entities. Данные в обычных массивах, UI читает напрямую.

```csharp
// Данные — простой массив
CellData[,] Cells = new CellData[1000, 1000];

// Grid UI — читает из массива напрямую
// Никаких сущностей, никаких компонентов
```

### Интерактивность

- Клик на ячейку → редактирование
- Drag-drop → перемещение данных
- Всё обрабатывается через standard events

---

## Roadmap

### Phase 1: Core (MVP)

- [ ] Типы (Rectangle, Color, Vector2)
- [ ] IRenderer — интерфейс
- [ ] Базовые виджеты (Element, Button, Label, Image, Panel)
- [ ] Простой layout (без flexbox)
- [ ] Hit testing — простой

### Phase 2: Layout

- [ ] Flexbox контейнеры
- [ ] Стили — базовые
- [ ] Callbacks для событий

### Phase 3: Events

- [ ] Events (не только callbacks)
- [ ] Bubbling
- [ ] Keyboard/focus handling

### Phase 4: Advanced

- [ ] CSS-like селекторы
- [ ] Каскад стилей
- [ ] Псевдо-состояния (:hover, :active)

### Phase 5: MVVM

- [ ] ViewModel база
- [ ] Data binding
- [ ] Commands

### Phase 6: Tables

- [ ] VirtualGrid
- [ ] Inline editing
- [ ] 10,000+ элементов оптимизация

---

## Примеры использования

### Простое меню

```csharp
public class MainMenu
{
    public void Build(UIBuilder b)
    {
        b.Window("Main Menu", (window) =>
        {
            window.Style.Direction = FlexDirection.Column;
            window.Style.Justify = JustifyContent.Center;
            window.Style.Gap = 16;
            
            window.Child(b => b.Label("My Game").FontSize(32));
            window.Child(b => b.Button("Play").OnClick(OnPlay));
            window.Child(b => b.Button("Settings").OnClick(OnSettings));
            window.Child(b => b.Button("Quit").OnClick(OnQuit));
        });
    }
}
```

### Стили

```csharp
// Определение стилей
public static class UIStyles
{
    public static UIStyle Button => UIStyle.Rent()
        .Background(Color.DarkGray)
        .TextColor(Color.White)
        .Padding(16, 8)
        .CornerRadius(4);
        
    public static UIStyle ButtonPrimary => UIStyle.Rent()
        .Background(Color.Blue)
        .TextColor(Color.White)
        .Padding(16, 8)
        .CornerRadius(4);
}

// Использование
var button = b.Button("Play");
button.AddClass("button");
button.AddClass("primary");
```

### MVVM

```csharp
public class SettingsViewModel
{
    public Bindable<int> Volume { get; } = new(50);
    public Bindable<bool> VSync { get; } = new(true);
    
    public ICommand Save => new Command(OnSave);
    
    private void OnSave() { /* save settings */ }
}

public class SettingsMenu : IView<SettingsViewModel>
{
    public void Build(UIBuilder b, SettingsViewModel vm)
    {
        b.Column(gap: 8, children =>
        {
            b.Label("Settings");
            
            b.Row(children =>
            {
                b.Label("Volume:");
                b.Slider(0, 100).BindValue(vm.Volume);
                b.Label(vm.Volume.ToString());
            });
            
            b.Checkbox("VSync").BindChecked(vm.VSync);
            
            b.Button("Save").BindCommand(vm.Save);
        });
    }
}
```

---

## Ссылки

- [Dragon ECS](../shared/ecs.md) — ECS движок
- [Graphics.Core](../client/graphics-core.md) — Graphics модуль
