# Graphics.Raylib - Code Review

> 📅 Дата: 2026-02-19

## 🔴 Критичные проблемы

### CR-001: Небезопасные касты типов
**Файл**: [`RaylibRenderer.cs`](Modules/Client/Graphics/Graphics.Raylib/RaylibRenderer.cs)

```csharp
// Строки 35, 80, 86, 91, 96, 102, 114, 119, 162, 167, 198
return Raylib.MeasureTextEx(((RaylibFont)font).Font, text, fontSize, spacing);
```

**Проблема**: Прямой каст `(RaylibFont)font` без проверки типа вызовет `InvalidCastException` если передана другая реализация `IFont`.

**Решение**:
```csharp
// Вариант 1: Проверка типа
if (font is not RaylibFont raylibFont)
    throw new ArgumentException($"Expected RaylibFont, got {font.GetType().Name}");
return Raylib.MeasureTextEx(raylibFont.Font, text, fontSize, spacing);

// Вариант 2: Pattern matching (C# 9+)
if (font is RaylibFont { Font: var f })
    return Raylib.MeasureTextEx(f, text, fontSize, spacing);
throw new ArgumentException("Invalid font type");
```

---

### CR-002: Null-forgiving operator без проверки
**Файл**: [`ContextSystem.cs`](Modules/Client/Graphics/Graphics.Raylib/Systems/ContextSystem.cs:19)

```csharp
[DI] private ICamera _mainCamera = null!;
```

**Проблема**: Использование `null!` подавляет предупреждение, но если DI не сможет разрешить зависимость, будет `NullReferenceException` в рантайме.

**Решение**:
```csharp
[DI] private ICamera _mainCamera = null!;

public void Run()
{
    ArgumentNullException.ThrowIfNull(_mainCamera, nameof(_mainCamera));
    // ... остальной код
}
```

---

## 🟡 Высокий приоритет

### HI-001: Захардкоженный цвет фона
**Файл**: [`ContextSystem.cs`](Modules/Client/Graphics/Graphics.Raylib/Systems/ContextSystem.cs:24)

```csharp
Raylib.ClearBackground(Color.DarkGreen);
```

**Проблема**: Цвет фона не конфигурируется. Должен быть настраиваемым через конфигурацию или сервис.

**Решение**:
```csharp
// Вариант 1: Через конфигурацию
public class GraphicsConfig
{
    public Color ClearColor { get; set; } = Color.DarkGreen;
}

// Вариант 2: Через сервис
public interface IGraphicsSettings
{
    Color ClearColor { get; }
}
```

---

### HI-002: Отсутствие обработки ошибок при инициализации
**Файл**: [`ContextSystem.cs`](Modules/Client/Graphics/Graphics.Raylib/Systems/ContextSystem.cs:13)

```csharp
internal class InitSystem : IEcsInit
{
    public void Init()
    {
        rlImGui.Setup();
    }
}
```

**Проблема**: Нет обработки ошибок при инициализации ImGui. Если инициализация не удалась, движок продолжит работу с неработающим UI.

**Решение**:
```csharp
internal class InitSystem : IEcsInit
{
    public void Init()
    {
        try
        {
            rlImGui.Setup();
        }
        catch (Exception ex)
        {
            // Логирование ошибки
            Console.Error.WriteLine($"Failed to initialize ImGui: {ex.Message}");
            throw;
        }
    }
}
```

---

### HI-003: Отсутствие проверки на двойную инициализацию
**Файл**: [`RaylibWindow.cs`](Modules/Client/Graphics/Graphics.Raylib/RaylibWindow.cs:101)

```csharp
public void Init(int width, int height, string title)
{
    R.InitWindow(width, height, title);
    R.SetExitKey(KeyboardKey.Null);
}
```

**Проблема**: Нет проверки, было ли окно уже инициализировано. Повторный вызов `InitWindow` может привести к утечкам ресурсов.

**Решение**:
```csharp
private bool _isInitialized;

public void Init(int width, int height, string title)
{
    if (_isInitialized)
    {
        Console.Warning("Window already initialized, skipping...");
        return;
    }
    
    R.InitWindow(width, height, title);
    R.SetExitKey(KeyboardKey.Null);
    _isInitialized = true;
}
```

---

## 🟢 Средний приоритет

### MI-001: Отсутствие IDisposable
**Файл**: [`RaylibRenderer.cs`](Modules/Client/Graphics/Graphics.Raylib/RaylibRenderer.cs)

**Проблема**: Класс создаёт ресурсы (камеру), но не реализует `IDisposable`.

**Решение**:
```csharp
public class RaylibRenderer : IRenderer, IDisposable
{
    private bool _disposed;
    
    public void Dispose()
    {
        if (_disposed) return;
        // Освобождение ресурсов
        _disposed = true;
    }
}
```

---

### MI-002: Магические числа
**Файл**: [`RaylibRenderer.cs`](Modules/Client/Graphics/Graphics.Raylib/RaylibRenderer.cs:75)

```csharp
Raylib.DrawTextEx(Raylib.GetFontDefault(), text, position, fontSize, 0, color.Raylib);
```

**Проблема**: `0` как spacing — магическое число.

**Решение**:
```csharp
private const float DEFAULT_TEXT_SPACING = 0f;

Raylib.DrawTextEx(Raylib.GetFontDefault(), text, position, fontSize, DEFAULT_TEXT_SPACING, color.Raylib);
```

---

## 📊 Метрики

| Метрика | Значение |
|---------|----------|
| Файлов | 10 |
| Классов | 8 |
| Интерфейсов | 0 |
| Методов | ~40 |
| Критичных проблем | 2 |
| Высокий приоритет | 3 |
| Средний приоритет | 2 |

---

## 💡 Предложения по развитию

### 1. Абстракция для рендер-пайплайна
Вынести последовательность `BeginDrawing -> BeginMode3D -> EndMode3D -> EndDrawing` в отдельный интерфейс для поддержки разных пайплайнов (2D/3D/VR).

### 2. Система слоёв рендеринга
Добавить систему слоёв для управления порядком отрисовки (background, game, UI, debug).

### 3. Batch rendering
Реализовать батчинг для 2D-спрайтов для уменьшения draw calls.

### 4. Object pooling для текстур
Добавить пул текстур для часто используемых ресурсов.

---

## ✅ Чек-лист исправлений

- [ ] CR-001: Добавить проверки типов для кастов
- [ ] CR-002: Добавить null-проверки для DI-зависимостей
- [ ] HI-001: Вынести цвет фона в конфигурацию
- [ ] HI-002: Добавить обработку ошибок инициализации
- [ ] HI-003: Добавить проверку на двойную инициализацию
- [ ] MI-001: Реализовать IDisposable
- [ ] MI-002: Убрать магические числа
