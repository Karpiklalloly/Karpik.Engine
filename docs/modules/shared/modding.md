# Modding

> Система модификаций на базе Lua

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleConfiguratable`, `IModuleDestroy`

## 🎯 Назначение

Предоставляет систему модов с поддержкой Lua-скриптов.

## 📦 Сервисы

| Интерфейс | Реализация | Описание |
|-----------|------------|----------|
| `IModManager` | `ModManager` | Менеджер модов |

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `UpdateSystem` | Update | Обновление Lua-скриптов |

## 📁 Структура

```
Modding.Core/
├── ModdingInstaller.cs       # Инсталлер ядра
├── IModManager.cs            # Интерфейс менеджера
├── IModContainer.cs          # Контейнер мода
├── ModMetaData.cs            # Метаданные мода
├── ExecutionSide.cs          # Client/Server
├── EventModMethods.cs        # События мода
└── AssetManagement/
    ├── Assets/
    │   └── ModMetaDataAsset.cs
    └── Loaders/
        └── ModMetaDataLoader.cs

Modding.Lua/
├── ModdingLuaInstaller.cs    # Инсталлер Lua
├── ModdingLuaModule.cs       # ECS-модуль
├── ModManager.cs             # Реализация менеджера
├── ModContainer.cs           # Контейнер Lua-мода
├── ModScriptLoader.cs        # Загрузчик скриптов
├── GameAPI.cs                # API для модов
└── Systems/
    └── UpdateSystem.cs       # Обновление модов
```

## 🔗 Зависимости

- `AssetManagement` — загрузка метаданных модов
- `MoonSharp` или аналогичный Lua-интерпретатор

## 💡 Использование

```csharp
// Загрузка модов
var modManager = services.Get<IModManager>();
modManager.LoadMods(pathToMods);

// API для модов (в Lua)
GameAPI.Log("Hello from mod!")
```

## 🔄 Жизненный цикл

1. **OnRegisterServices** — создание `ModManager`, определение стороны (Client/Server)
2. **OnConfigureComplete** — загрузка модов из директории
3. **Destroy** — очистка Lua-контекстов

## ⚠️ Особенности

- Разделение на Client/Server моды через `ExecutionSide`
- Изолированные Lua-контексты для каждого мода
- API для взаимодействия с движком
- Горячая перезагрузка скриптов
