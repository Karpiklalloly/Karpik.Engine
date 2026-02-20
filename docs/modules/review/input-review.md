# Input - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Опечатка в имени метода
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:107)

```csharp
internal void Destory()
```

**Проблема**: Метод называется `Destory()` вместо `Destroy()`. Это нарушает соглашения об именовании и может вызвать путаницу.

**Решение**:
```csharp
internal void Destroy()
```

---

### CR-002: Присвоение null! событиям
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:109-114)

```csharp
internal void Destory()
{
    KeyPressed = null!;
    KeyUnPressed = null!;
    KeyPressing = null!;
    // ...
}
```

**Проблема**: Присвоение `null!` событиям — плохая практика. Если на события были подписчики, они не будут отписаны. Это может привести к утечкам памяти.

**Решение**:
```csharp
internal void Destroy()
{
    // Отписываем всех подписчиков
    KeyPressed = null;
    KeyUnPressed = null;
    KeyPressing = null;
    CharPressed = null;
    CharUnPressed = null;
    CharPressing = null;
    
    _keys.Clear();
    _chars.Clear();
    _keyStates.Clear();
    _charStates.Clear();
    _window = null!;
}
```

---

## 🟡 Высокий приоритет

### HI-001: Избыточное использование ConcurrentDictionary
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:25-26)

```csharp
private ConcurrentDictionary<KeyboardKeys, State> _keyStates = new();
private ConcurrentDictionary<char, State> _charStates = new();
```

**Проблема**: `ConcurrentDictionary` используется, но `Update()` вызывается из одного потока (главный поток рендеринга). Это создаёт ненужные накладные расходы на синхронизацию.

**Решение**:
```csharp
// Использовать обычный Dictionary
private Dictionary<KeyboardKeys, State> _keyStates = new();
private Dictionary<char, State> _charStates = new();

// Или если нужна потокобезопасность — использовать lock
private readonly object _stateLock = new();
```

---

### HI-002: Аллокации каждый кадр
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:34)

```csharp
public IEnumerable<KeyboardKeys> PressedKeys => _keyStates.Keys.Where(IsPressed);
```

**Проблема**: LINQ `Where()` создаёт новый `IEnumerable` при каждом обращении. В hot path это создаёт GC pressure.

**Решение**:
```csharp
// Вариант 1: Кэшировать результат
private List<KeyboardKeys> _pressedKeysCache = new();

public IReadOnlyList<KeyboardKeys> PressedKeys
{
    get
    {
        _pressedKeysCache.Clear();
        foreach (var key in _keyStates.Keys)
        {
            if (IsPressed(key))
                _pressedKeysCache.Add(key);
        }
        return _pressedKeysCache;
    }
}

// Вариант 2: Использовать struct enumerator (Zero-allocation)
```

---

### HI-003: Аллокации при обновлении
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:98-99)

```csharp
private List<KeyboardKeys> _keys = new();
private List<char> _chars = new();
```

**Проблема**: Списки очищаются каждый кадр, но если количество нажатых клавиш превысит Capacity, будет аллокация.

**Решение**:
```csharp
// Установить разумный начальный размер
private List<KeyboardKeys> _keys = new(16); // Обычно не более 16 клавиш одновременно
private List<char> _chars = new(8);
```

---

### HI-004: Отсутствие null-проверки для _window
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:56-84)

```csharp
public bool IsPressed(KeyboardKeys key)
{
    return _window.IsKeyPressed((int)key);
}
```

**Проблема**: Если `_window` не инициализирован или уничтожен, будет `NullReferenceException`.

**Решение**:
```csharp
public bool IsPressed(KeyboardKeys key)
{
    return _window?.IsKeyPressed((int)key) ?? false;
}
```

---

## 🟢 Средний приоритет

### MI-001: Дублирование кода для кнопок мыши
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:36-52)

```csharp
public bool IsMouseLeftButtonDown => _window.IsMouseButtonPressed((int)MouseButtons.Left);
public bool IsMouseLeftButtonUp => _window.IsMouseButtonReleased((int)MouseButtons.Left);
public bool IsMouseLeftButtonHold => _window.IsMouseButtonDown((int)MouseButtons.Left);
// ... повторяется для Right и Middle
```

**Проблема**: Дублирование кода. При добавлении новых кнопок нужно копировать и менять.

**Решение**:
```csharp
public bool IsMouseButtonDown(MouseButtons button) => _window.IsMouseButtonPressed((int)button);
public bool IsMouseButtonUp(MouseButtons button) => _window.IsMouseButtonReleased((int)button);
public bool IsMouseButtonHold(MouseButtons button) => _window.IsMouseButtonDown((int)button);

// Convenience properties
public bool IsMouseLeftDown => IsMouseButtonDown(MouseButtons.Left);
public bool IsMouseRightDown => IsMouseButtonDown(MouseButtons.Right);
```

---

### MI-002: Сложная логика состояний
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:134-171)

**Проблема**: Логика переключения состояний сложная и трудна для понимания. Много вложенных условий.

**Решение**: Рефакторинг через State Machine:
```csharp
private State TransitionState(State current, bool isPressed)
{
    return (current, isPressed) switch
    {
        (State.DownEvent, true) => State.DownHold,
        (State.DownHold, true) => State.DownHold,
        (State.UpEvent, true) => State.DownEvent,
        (State.UpHold, true) => State.DownEvent,
        (State.DownEvent, false) => State.UpEvent,
        (State.DownHold, false) => State.UpEvent,
        (State.UpEvent, false) => State.UpHold,
        (State.UpHold, false) => State.UpHold,
        _ => current
    };
}
```

---

### MI-003: Захардкоженные имена методов
**Файл**: [`Input.cs`](Modules/Client/Input/Input.cs:86-96)

```csharp
public void LockCursor()
{
    _isMouseLocked = true;
    _window.DisableCursor();
}
```

**Проблема**: Состояние `_isMouseLocked` дублирует реальное состояние курсора в window.

**Решение**:
```csharp
public bool IsMouseLocked
{
    get => _isMouseLocked;
    set
    {
        if (value == _isMouseLocked) return;
        _isMouseLocked = value;
        if (value)
            _window.DisableCursor();
        else
            _window.EnableCursor();
    }
}
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 5 |
| Классов | 3 |
| Строк кода | ~184 |
| Критичных проблем | 2 |
| Высокий приоритет | 4 |
| Средний приоритет | 3 |

---

## 💡 Предложения по развитию

### 1. Input Action System
Добавить систему действий (Input Actions) для переопределения клавиш:
```csharp
public class InputAction
{
    public string Name { get; }
    public KeyboardKeys DefaultKey { get; }
    public KeyboardKeys CurrentKey { get; set; }
    public bool IsPressed => Input.IsPressed(CurrentKey);
}
```

### 2. Input Recording/Playback
Добавить возможность записи и воспроизведения ввода для тестов.

### 3. Gamepad Support
Расширить для поддержки геймпадов и джойстиков.

### 4. Input Contexts
Добавить контексты ввода (Menu, Game, Dialog) с разными маппингами.

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Исправить опечатку `Destory` → `Destroy`
- [ ] CR-002: Правильно отписывать события при уничтожении
- [ ] HI-001: Заменить ConcurrentDictionary на Dictionary
- [ ] HI-002: Убрать LINQ из hot path
- [ ] HI-003: Установить Capacity для списков
- [ ] HI-004: Добавить null-проверки для _window
- [ ] MI-001: Рефакторинг дублирования кнопок мыши
- [ ] MI-002: Упростить логику состояний
- [ ] MI-003: Улучшить управление курсором
