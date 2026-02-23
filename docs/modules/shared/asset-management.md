# AssetManagement

> Система загрузки и управления ассетами

## 📋 Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: -100 (загружается первым)
- **Интерфейсы**: `IModule`, `IModuleListener`, `IModuleConfiguratable`, `IModuleDestroy`

## 🎯 Назначение

Централизованная система загрузки, кэширования и освобождения игровых ассетов.

## 📦 Сервисы

| Интерфейс | Реализация | Описание |
|-----------|------------|----------|
| `IAssetsManager` | `AssetsManager` | Менеджер ассетов |

## 🔧 ECS-системы

Нет ECS-систем. Модуль работает на уровне сервисов.

## 📁 Структура

```
AssetManagement.Core/
├── AssetManagementInstaller.cs  # Инсталлер модуля
├── AssetsManager.cs             # Менеджер ассетов
├── Asset.cs                     # Базовый класс ассета
├── AssetHandle.cs               # Хэндл для доступа к ассету
├── AssetPath.cs                 # Пути к ассетам
├── IAssetLoader.cs              # Интерфейс загрузчика
├── IAssetSaver.cs               # Интерфейс сохранения
├── IFileSystem.cs               # Абстракция файловой системы
├── PhysicalFileSystem.cs        # Физическая ФС
├── BaseAssetLoader.cs           # Базовый загрузчик
├── BaseAssetSaver.cs            # Базовый сохранятель
├── AssetLoaders/
│   ├── JsonLoader.cs            # Загрузка JSON
│   └── RawTextLoader.cs         # Загрузка текста
└── Assets/
    └── TextAsset.cs             # Текстовый ассет
```

## 🔗 Зависимости

Нет внешних зависимостей от других модулей.

## 💡 Использование

```csharp
// Получение менеджера
var assets = services.Get<IAssetsManager>();

// Загрузка ассета
var handle = assets.Load<TextAsset>("path/to/file.txt");
var text = handle.Asset.Text;

// Освобождение
handle.Release();
```

## 🔄 Жизненный цикл

1. **OnRegisterServices** — создание `AssetsManager`
2. **OnConfigureComplete** — регистрация встроенных загрузчиков
3. **OnAnotherModuleLoaded** — регистрация загрузчиков из других модулей
4. **Destroy** — освобождение всех ассетов

## ⚠️ Особенности

- Автоматическое обнаружение загрузчиков через рефлексию
- Поддержка зависимостей между ассетами (`IHasDependencies`)
- Абстракция файловой системы для тестирования
- Hot Reload поддержка через сохранение/загрузку JSON

## 🚨 Performance Notes

| Проблема | Решение |
|----------|---------|
| `AssetHandle` без `Span` API | P2-2: Добавить `ReadOnlySpan<byte> GetData()` |
| Аллокации при загрузке | Использовать `ArrayPool<byte>.Shared` |
