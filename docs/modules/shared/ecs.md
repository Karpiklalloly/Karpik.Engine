# ECS

> Интеграция DragonECS с движком

## Обзор

- **Слой**: Shared (Client + Server)
- **Приоритет**: 0 (стандартный)
- **Инсталлер**: `EcsModuleInstaller`
- **Область жизни**: `Simulation`

## Назначение

`ECS.Core` использует DragonECS как внутренний backend и предоставляет Karpik API для игровой логики.

Пользовательский код должен:

- реализовывать Karpik lifecycle-интерфейсы `ISystem*`;
- получать facade worlds через конструктор;
- использовать `struct`-компоненты с `IEcsComponent`;
- использовать Dragon `EcsAspect`, `EcsPool<T>` и `EcsReadonlyPool<T>` внутри аспектов.

Прямые Dragon lifecycle-интерфейсы остаются compatibility/backend API. Новые пользовательские системы не должны реализовывать `IEcsRun`, `IEcsInit`, `IEcsDestroy`, `IEcsRunLate` или `IEcsRunParallel`.

## Сервисы

| Рекомендуемый facade | Dragon backend | Назначение |
|----------------------|----------------|------------|
| `DefaultWorld` | `EcsDefaultWorld` | Основной gameplay-мир |
| `EventWorld` | `EcsEventWorld` | Мир событий |
| `MetaWorld` | `EcsMetaWorld` | Метаданные |

Facade world предоставляет создание и удаление сущностей, data-first операции с компонентами, запросы через аспекты и component lifecycle.

Свойство `World.Base` открывает исходный Dragon world. Это compatibility/backend escape hatch. Использовать его в gameplay-коде стоит только когда facade API пока недостаточно.

## Lifecycle Систем

Порядок основных фаз:

```text
Init
MainThreadBegin -> MainThreadFrameBegin -> Begin -> FixedUpdate -> Update
-> LateUpdate -> RenderPrepare -> Render
Destroy
```

| Интерфейс | Назначение |
|-----------|------------|
| `ISystemInit` | Однократная инициализация |
| `ISystemMainThreadBegin` | Main-thread input/window работа перед simulation frame |
| `ISystemMainThreadFrameBegin` | Main-thread подготовка render frame |
| `ISystemBegin` | Начало gameplay frame |
| `ISystemFixedUpdate` | Фиксированный simulation tick |
| `ISystemUpdate` | Gameplay update |
| `ISystemLateUpdate` | Последовательная post-simulation работа |
| `ISystemRenderPrepare` | Подготовка render-данных после simulation |
| `ISystemRender` | Main-thread render submit, ImGui и present |
| `ISystemDestroy` | Однократное освобождение ресурсов |

В `0.4` фазы выполняются последовательно. Изолированный прототип `IEcsRunParallel` не является пользовательским API. Его интеграция в `ISystemUpdate`, thread affinity и полноценная многопоточность относятся к `0.5`.

## Компоненты И Аспекты

```csharp
using DCFApixels.DragonECS;
using DragonExtensions;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS;

public struct Position : IEcsComponent
{
    public float X;
    public float Y;
}

public sealed class MovementSystem : ISystemUpdate
{
    private sealed class Aspect : EcsAspect
    {
        public EcsPool<Position> Position = Inc;
    }

    private readonly DefaultWorld _world;

    public MovementSystem(DefaultWorld world)
    {
        _world = world;
    }

    public void Update()
    {
        foreach (var entity in _world.Where(out Aspect aspect))
        {
            ref var position = ref aspect.Position.Get(entity);
            position.X += 1f;
        }
    }
}
```

Для чтения без изменения компонента используйте `EcsReadonlyPool<T>`:

```csharp
private sealed class Aspect : EcsAspect
{
    public EcsReadonlyPool<Position> Position = Inc;
}
```

## Data-First Операции

```csharp
public sealed class SpawnSystem(DefaultWorld world) : ISystemInit
{
    public void Init()
    {
        var entity = world.New();
        world.Add(entity.ID, new Position { X = 10f, Y = 20f });

        ref var position = ref world.Get<Position>(entity.ID);
        position.X += 1f;
    }
}
```

Сначала формируются данные компонента, затем компонент добавляется в мир. Это особенно важно для компонентов с lifecycle.

## Component Lifecycle

Компонент с внешними ресурсами может реализовать `IComponentLifecycleAsync<T>`:

```csharp
public struct SpriteRenderer : IEcsComponent, IComponentLifecycleAsync<SpriteRenderer>
{
    public string TexturePath;
    private AssetHandle<TextureAsset> _handle;

    public async JobHandle<SpriteRenderer> EnableAsync(
        SpriteRenderer component,
        ComponentLifecycleContext context)
    {
        var assets = context.Services.Get<IAssetsManager>();
        component._handle = await assets.LoadAssetAsync<TextureAsset>(component.TexturePath);
        return component;
    }

    public JobHandle<SpriteRenderer> DisableAsync(
        SpriteRenderer component,
        ComponentLifecycleContext context)
    {
        component._handle.Dispose();
        return JobHandle<SpriteRenderer>.FromResult(component);
    }
}
```

Добавление выполняется data-first: перед `EnableAsync` facade записывает переданные данные в pool.

```csharp
var pool = _world.Base.GetPool<SpriteRenderer>();
await _world.AddEnabledAsync(entity.ID, renderer, pool);
await _world.DelEnabledAsync(entity.ID, pool);
```

`AddEnabled` и `DelEnabled` являются временными blocking compatibility-обёртками. Не вызывайте их из hot path, если lifecycle может ждать I/O или загрузку asset.

Ошибки lifecycle оборачиваются в `ComponentLifecycleException`. Диагностика содержит phase, world name, world id, entity id и component type.

## Hot Path Ограничения

- Не выделяйте managed memory в `Begin`, `FixedUpdate`, `Update`, `LateUpdate` и `Render`.
- Не вызывайте blocking lifecycle-обёртки из frame path, если операция может ждать I/O.
- Кэшируйте pools через aspect или во время инициализации системы.
- Для неизменяемых данных используйте `EcsReadonlyPool<T>`.
- Не храните gameplay object graph в ECS-компонентах. Компоненты должны оставаться компактными `struct`.

## Hot Reload

`EcsRestartWorkerStateProvider` зарегистрирован как `IRestartWorkerStateProvider` в `Simulation`. При restart-worker reload он сериализует и восстанавливает backend-миры `EcsDefaultWorld`, `EcsEventWorld` и `EcsMetaWorld`.

```text
Capture -> snapshot ECS worlds -> restart worker -> построение Simulation scope -> Restore
```

Snapshot снимается на основном потоке через `MainThreadScheduler`. После успешного `Capture` старый worker прекращает loop; pipeline и области Autofac освобождаются обычным shutdown-путём. Новый worker сначала строит `Engine`, `ModSet` и `Simulation`, затем вызывает `Restore` до разрешения систем, построения pipeline и `Init`.

Snapshot API работает на backend-уровне. Gameplay-системам не следует самостоятельно сериализовать world во время кадра. Полный порядок работы ядра и контракт runtime-only ресурсов описаны в [Hot Reload](../../01_Architecture/hot-reload.md).

## Compatibility Граница

Временно допустимы:

- Dragon-типы внутри аспектов;
- `World.Base`, когда facade ещё не покрывает нужную backend-операцию;
- `IEcsRunOnEvent<T>` до появления Karpik event-system API;
- существующие legacy Dragon-системы на время миграции.

Новые обычные lifecycle-системы должны использовать `ISystem*`.

## Структура

```text
ECS.Core/
├── EcsModuleInstaller.cs
├── EcsRestartWorkerStateProvider.cs
├── Worlds.cs
├── EcsMetaWorld.cs
├── EcsWorldExtensions.cs
├── EcsCommandBuffer.cs
├── ComponentsTemplate.cs
├── IComponentTemplate.cs
├── EntitySnapshot.cs
├── HotReloadInfo.cs
├── IEcsRunParallel.cs
├── SystemExecutionNode.cs
├── CoreComponentExtensions.cs
├── ToTemplateExtensions.cs
├── WorldEventListener.cs
└── AssetManagement/
```
