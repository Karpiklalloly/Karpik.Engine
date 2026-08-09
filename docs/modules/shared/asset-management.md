# Asset Management

> Загрузка, кэширование и сохранение ассетов для Client и Server

## Обзор

- **Модуль**: `AssetManagement.Core`
- **Инсталлер**: `AssetManagementModuleInstaller`
- **Область жизни**: `Engine`
- **Приоритет модуля**: `-10000`

`AssetsManager` является Engine-сервисом: один экземпляр используется всеми `ModSet` и `Simulation` текущего worker. Инсталлер только объявляет модуль и его порядок; обычные сервисы регистрируются атрибутами.

| Контракт | Реализация | Регистрация |
|----------|------------|-------------|
| `IAssetsManager` | `AssetsManager` | Engine singleton |
| `IFileSystem` | `PhysicalFileSystem` | Engine singleton |
| `IAssetLoader` | загрузчики разных модулей | Engine singleton, multiple export |
| `IAssetSaver` | сохранятели разных модулей | Engine singleton, multiple export |

## Регистрация Loader И Saver

Каждый loader/saver сам экспортирует общий контракт. Регистрировать его вручную в `AssetManagementModuleInstaller` не нужно:

```csharp
[Export(typeof(IAssetLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class TextureLoader : BaseAssetLoader<TextureAsset, Texture>
{
    // ...
}
```

Runner собирает все такие экспорты. Autofac передаёт `IAssetLoader[]` и `IAssetSaver[]` в конструктор `AssetsManager`, а менеджер строит таблицы поиска по расширению и типу ассета.

Поскольку `AssetsManager` принадлежит `Engine`, его loader/saver также должны быть доступны в `Engine`. Engine-сервис не может зависеть от `ModSet` или `Simulation`.

Повторная пара `(extension, asset type)` или повторный saver для одного типа сейчас перезаписывает предыдущее значение. Явной модели приоритетов ещё нет, поэтому совпадения должны быть намеренными и проверяться отдельно.

## Использование

Получайте `IAssetsManager` через конструктор:

```csharp
public sealed class LevelLoader(IAssetsManager assets)
{
    public async JobHandle<string> LoadDescription(string path)
    {
        using AssetHandle<TextAsset> handle = await assets.LoadAssetAsync<TextAsset>(path);
        return handle.Asset?.Text ?? string.Empty;
    }
}
```

`AssetHandle<T>` увеличивает счётчик ссылок. Его необходимо освобождать через `Dispose`/`using`; прямой вызов внутренних методов release не нужен.

## Жизненный Цикл

1. Runner строит Engine container и создаёт `AssetsManager` при разрешении сервиса.
2. Constructor injection передаёт файловую систему, логгер и все loader/saver.
3. Загруженные ассеты кэшируются по `(path hash, asset type)`.
4. При освобождении последнего handle ассет выгружается.
5. При уничтожении Engine scope Autofac вызывает `AssetsManager.Dispose()`, который освобождает оставшиеся ассеты.

Asset Manager не использует `OnAnotherModuleLoaded`, `OnConfigureComplete` или `IModuleDestroy`.

## Ограничения Производительности

- Загрузка, сохранение и файловый I/O не выполняются в frame/ECS hot path.
- Loader не должен блокировать основной поток при длительном I/O.
- Зависимости между ассетами учитываются через `TryAddDependency`; дочерний ассет удерживается до выгрузки родителя.
- `ConcurrentDictionary` защищает таблицы менеджера, но не превращает произвольный loader или сам объект ассета в thread-safe тип.

## Связанные Документы

- [Dependency Injection и области жизни](../../01_Architecture/dependency-injection-and-scopes.md)
