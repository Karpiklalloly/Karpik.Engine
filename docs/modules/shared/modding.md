# Modding

> Загрузка и выполнение Lua-модов

## Обзор

Modding разделён на два модуля:

| Модуль | Scope | Назначение |
|--------|-------|------------|
| `Modding.Core` | Engine | Контракты, метаданные и `ModMetaDataLoader` |
| `Modding.Lua` | Simulation | `ModManager`, Lua-контейнеры и ECS-системы |

`ModdingModuleInstaller` и `ModdingLuaModuleInstaller` не создают сервисы вручную. `ModManager` экспортируется атрибутами как `IModManager` и создаётся Autofac один раз на `Simulation`.

```csharp
[Export(typeof(IModManager))]
[ServiceRegistration(ModuleScope.Simulation, ServiceLifetime.Singleton)]
public sealed class ModManager(
    ILogger<ModManager> logger,
    IAssetsManager assets,
    Time time) : IModManager
{
    // ...
}
```

Фактический конструктор также получает отдельные типизированные логгеры для `ModContainer` и `GameAPI`.

## Жизненный Цикл

`ModdingLuaModule` регистрирует типы двух систем через `ISystemRegistry`:

- `InitSystem` определяет Client/Server сторону из `Application`, вызывает `IModManager.Init`, загружает моды из `IAssetsManager.ModsPath` и запускает их;
- `UpdateSystem` вызывает `UpdateMods()` в `LateUpdate`.

Моды загружаются во время `Init` новой Simulation, то есть после построения `Engine -> ModSet -> Simulation`. Они не добавляют регистрации в уже построенные Engine или ModSet containers.

`ModManager` реализует `IDisposable`. При уничтожении Simulation scope Autofac освобождает менеджер, а тот освобождает все `ModContainer` и очищает таблицу загруженных модов.

`ReloadAllMods` перезапускает Lua-контейнеры внутри текущей Simulation. Это не перезагрузка .NET-сборок и не изменение состава DI-container.

## Использование

Получайте менеджер через конструктор. Обычно загрузкой управляет встроенный `InitSystem`, поэтому gameplay-код не должен повторно загружать каталог:

```csharp
public sealed class ModDiagnostics(IModManager mods)
{
    public ModMetaData GetMetadata(string id) => mods.GetModMetadata(id);
}
```

## Ограничения

- Текущая реализация использует process-global регистрацию `MoonSharp.UserData` для `GameAPI`. Комментарий в коде фиксирует, что полноценная изоляция нескольких одновременных Simulation ещё требует отдельного решения.
- Lua-моды могут иметь Client/Server подкаталоги, выбранные через `ExecutionSide`.
- Метаданные загружаются через Engine-сервис `IAssetsManager` и экспортированный `ModMetaDataLoader`.
- `UpdateMods()` выполняется в simulation loop; моды не должны делать blocking I/O или непредсказуемо долгую работу в этом вызове.

## Связанные Документы

- [Dependency Injection и области жизни](../../01_Architecture/dependency-injection-and-scopes.md)
- [Hot Reload](../../01_Architecture/hot-reload.md)
