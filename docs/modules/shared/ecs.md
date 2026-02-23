# ECS

> Интеграция DragonECS с движком

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Интерфейсы**: `IModule`, `IModuleHotReload`, `IModuleConfiguratable`

## 🎯 Назначение

Интегрирует ECS-фреймворк DragonECS, предоставляет World и системы для игровой логики.

## 📦 Сервисы

| Тип | Описание |
|-----|----------|
| `EcsDefaultWorld` | Основной мир ECS |
| `EcsEventWorld` | Мир для событий |
| `EcsMetaWorld` | Мир для метаданных |

## 🔧 ECS-системы

| Система | Слой | Описание |
|---------|------|----------|
| `RunnerSystem` | Update | Запуск пайплайна |
| `DestroySystem` | Cleanup | Удаление сущностей |

## 📁 Структура

```
ECS.Core/
├── ECSInstaller.cs            # Инсталлер модуля
├── ECSModule.cs               # ECS-модуль
├── EcsMetaWorld.cs            # Метамир
├── EcsWorldExtensions.cs      # Расширения для World
├── EcsCommandBuffer.cs        # Буфер команд
├── ComponentsTemplate.cs      # Шаблон компонентов
├── IComponentTemplate.cs      # Интерфейс шаблона
├── EntitySnapshot.cs          # Снапшот сущности
├── HotReloadInfo.cs           # Данные для Hot Reload
├── PausableRunner.cs          #Runner с паузой
├── SystemExecutionNode.cs     # Узел выполнения
├── CoreComponentExtensions.cs # Расширения компонентов
├── ToTemplateExtensions.cs    # Конвертация в шаблоны
├── WorldEventListener.cs      # Слушатель событий мира
└── AssetManagement/
    ├── Assets/
    │   └── ComponentsTemplateAsset.cs
    ├── Loaders/
    │   └── ComponentsTemplateLoader.cs
    └── Savers/
        └── ComponentsTemplateSaver.cs
```

## 🔗 Зависимости

- `DragonECS` — сторонний ECS-фреймворк
- `AssetManagement` — для загрузки шаблонов

## 💡 Использование

```csharp
// Получение мира
var world = services.Get<EcsDefaultWorld>();

// Создание сущности
var entity = world.NewEntity();

// Добавление компонента
world.AddComponent(entity, new Position { X = 0, Y = 0 });
```

## 🔄 Hot Reload

Полная поддержка Hot Reload:
1. **OnPrepareHotReload** — сериализация миров в JSON
2. **OnHotReload** — восстановление миров из JSON

```csharp
// Сериализация через Snapshot
string snapshot = world.Snapshot;

// Восстановление
EcsWorld.FromSnapshot(world, snapshot, assetManager);
```

## ⚠️ Особенности

- Три мира: Default (игра), Event (события), Meta (метаданные)
- `EcsCommandBuffer` для отложенных операций
- Шаблоны компонентов через AssetManagement
- `IEcsRunParallel` для параллельных систем
- `IEcsRunLate` для late update
