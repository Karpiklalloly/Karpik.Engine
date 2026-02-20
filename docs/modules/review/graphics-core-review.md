# Code Review: Graphics.Core

**Module:** Graphics.Core  
**Type:** Abstraction Layer (Interfaces)  
**Status:** ✅ Good - Minimal issues

---

## Overview

Graphics.Core — модуль абстракции графики. Содержит интерфейсы для рендерера, окна, камеры, текстур и шрифтов. Позволяет переключать графические бэкенды (Raylib, потенциально другие).

---

## Statistics

| Metric | Value |
|--------|-------|
| Files | 9 |
| Interfaces | 6 |
| Enums | 1 |
| Lines of Code | ~200 |

---

## Issues Found

### MI-1: Пустой инсталлер [Medium]

**File:** [`GraphicsCoreInstaller.cs`](Modules/Client/Graphics/Graphics.Core/GraphicsCoreInstaller.cs:7)

```csharp
[Module]
public class GraphicsCoreInstaller : IModule, IModuleConfiguratable
{
    public void OnRegisterServices(IServiceRegister services)
    {
        // Пусто - интерфейсы не регистрируются
    }
}
```

**Problem:** Модуль не регистрирует никаких сервисов. Интерфейсы IRenderer, IWindow должны регистрироваться в конкретных реализациях (Graphics.Raylib).

**Solution:** Это корректное поведение для абстрактного модуля. Документировать намеренность.

---

### MI-2: IWindow смешивает ввод и окно [Medium]

**File:** [`IWindow.cs`](Modules/Client/Graphics/Graphics.Core/IWindow.cs:15)

```csharp
public interface IWindow
{
    // Window management
    void Init(int width, int height, string title);
    void SetWindowState(WindowFlags flags);
    
    // Input handling - смешивание ответственностей
    public int GetKeyPressed();
    public char GetCharPressed();
    public bool IsMouseButtonPressed(int button);
    public Vector2 GetMousePosition();
}
```

**Problem:** Интерфейс смешивает управление окном и обработку ввода. Нарушает SRP (Single Responsibility Principle).

**Solution:** Разделить на два интерфейса:
```csharp
public interface IWindow { /* window management */ }
public interface IWindowInput { /* input handling */ }
```

---

### MI-3: Magic numbers в WindowFlags [Low]

**File:** [`WindowFlags.cs`](Modules/Client/Graphics/Graphics.Core/WindowFlags.cs:8)

```csharp
public enum WindowFlags
{
    VSyncHint = 0x00000040,
    FullscreenMode = 0x00000002,
    // ...
}
```

**Problem:** Значения соответствуют Raylib-константам, но не документировано.

**Solution:** Добавить XML-doc с указанием источника констант:
```csharp
/// <summary>
/// Raylib ConfigFlags values - https://github.com/raysan5/raylib
/// </summary>
```

---

### MI-4: IRenderer перегружен методами [Low]

**File:** [`IRenderer.cs`](Modules/Client/Graphics/Graphics.Core/IRenderer.cs:6)

```csharp
public interface IRenderer
{
    // 4 overloads for DrawTexture
    public void DrawTexture(ITexture2D texture, Vector2 position, Color color);
    public void DrawTexture(ITexture2D texture, Vector2 position, float rotation, float scale, Color color);
    public void DrawTexture(ITexture2D texture, RectangleF source, Vector2 position, Color color);
    public void DrawTexture(ITexture2D texture, RectangleF source, RectangleF destination, Vector2 origin, float rotation, Color color);
    
    // 4 overloads for DrawText
    public void DrawText(string text, Vector2 position, float fontSize, Color color);
    // ...
}
```

**Problem:** Много перегрузок делает интерфейс громоздким. 

**Solution:** Рассмотреть паттерн Builder или optional параметры:
```csharp
public void DrawTexture(ITexture2D texture, in DrawTextureOptions options);
```

---

## Positive Aspects ✅

1. **Чистая абстракция** — интерфейсы не зависят от конкретного бэкенда
2. **readonly struct для WindowFlags** — корректное использование enum
3. **ICamera с Vector3** — готовность к 3D
4. **IFont как интерфейс** — возможность кастомных шрифтов

---

## Recommendations

| Priority | Action |
|----------|--------|
| Low | Разделить IWindow на IWindow + IWindowInput |
| Low | Документировать источник констант WindowFlags |
| Optional | Рассмотреть Builder для сложных draw-операций |

---

## Verdict

**Хороший модуль.** Минимальные проблемы, чистая архитектура. Интерфейсы хорошо спроектированы для абстракции графического бэкенда.
