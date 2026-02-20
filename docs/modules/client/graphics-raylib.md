# Graphics.Raylib

> Графический модуль на базе Raylib

## 📋 Обзор

- **Слой**: Client
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`, `IModuleHotReload`

## 🎯 Назначение

Предоставляет реализацию графических абстракций через Raylib + ImGui.

## 📦 Сервисы

| Интерфейс | Реализация | Описание |
|-----------|------------|----------|
| `IWindow` | `RaylibWindow` | Окно и контекст |
| `IRenderer` | `RaylibRenderer` | Рендеринг |
| `ICamera` | `RaylibCamera` | Камера 2D |

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `ContextSystem` | Init | Инициализация контекста Raylib |

## 📁 Структура

```
Graphics.Raylib/
├── GraphicsRaylibInstaller.cs   # Инсталлер модуля
├── RaylibWindow.cs              # Реализация IWindow
├── RaylibRenderer.cs            # Реализация IRenderer
├── RaylibCamera.cs              # Реализация ICamera
├── RaylibFont.cs                # Шрифты
├── RaylibTexture2D.cs           # Текстуры
├── RaylibRenderTexture2D.cs     # Render targets
├── Systems/
│   └── ContextSystem.cs         # Инициализация
└── AssetManagement/
    ├── Assets/
    │   └── RaylibTexture2DAsset.cs
    └── Loaders/
        └── RaylibTexture2DLoader.cs
```

## 🔗 Зависимости

- `Graphics.Core` — абстракции графики
- `Raylib_cs` — нативные биндинги Raylib
- `ImGuiNET` — ImGui для .NET
- `rlImGui_cs` — интеграция ImGui с Raylib

## 🔄 Hot Reload

Поддерживает Hot Reload через `IModuleHotReload`:
- Сохраняет состояние ImGui
- Очищает нативные указатели перед перезагрузкой

## ⚠️ Особенности

- ImGui интегрируется через rlImGui
- Поддержка 2D рендеринга
- Автоматическое управление текстурами через AssetManagement
