# Physics 2D

> Simulation-scoped 2D physics с ECS-интеграцией

## Обзор

Physics 2D разделён на API/системы и backend:

| Модуль | Scope | Назначение |
|--------|-------|------------|
| `Physics2D.Core` | Simulation | Компоненты, `IPhysicsWorld2D` и ECS-системы синхронизации |
| `Physics2D.Aether2D` | Simulation | Реализация `IPhysicsWorld2D` на Aether.Physics2D |

`AetherPhysicsWorld` экспортируется как Simulation singleton. `Physics2DAetherModuleInstaller` явно создаёт backend `World` с гравитацией; это сложная регистрация стороннего типа, поэтому она находится в installer.

Системы регистрируются типами через `Physics2DModule` и получают `IPhysicsWorld2D`, `DefaultWorld` и `Time` через конструкторы.

## ECS-Данные

| Компонент | Назначение |
|-----------|------------|
| `Transform2D` | Устойчивая позиция и rotation сущности |
| `Velocity2D` | Линейная и угловая скорость |
| `PhysicsBodyDefinition` | Устойчивая конфигурация тела и shape |
| `PhysicsBodyRef` | Process-local handle backend-тела |
| `CreateBodyRequest` | Запрос на создание тела |
| `DestroyBodyRequest` | Запрос на удаление тела |
| `TeleportRequest` | Запрос на изменение transform |
| `SetVelocityRequest` | Запрос на изменение скорости |

`BodyConfig` задаёт тип тела, массу, friction, restitution, sensor/gravity flags и collision masks. `ShapeConfig` создаётся через `ShapeConfig.Box(size)` или `ShapeConfig.Circle(radius)`.

## Создание Тела

Constructor injection используется и в пользовательских системах:

```csharp
public sealed class SpawnPhysicsBodySystem(DefaultWorld world) : ISystemInit
{
    public void Init()
    {
        var entity = world.New();
        world.Add(entity.ID, new Transform2D
        {
            Position = new Vector2(0f, 5f),
            Rotation = 0f
        });
        world.Add(entity.ID, new CreateBodyRequest
        {
            BodyConfig = new BodyConfig
            {
                Type = BodyType.Dynamic,
                Mass = 1f,
                Friction = 0.3f,
                CategoryBits = Physics2DLayers.Player,
                MaskBits = Physics2DLayers.All
            },
            ShapeConfig = ShapeConfig.Box(new Vector2(1f, 1f))
        });
    }
}
```

`Physics2DBodyCreator` обрабатывает запрос в `Begin`, создаёт backend-тело, записывает `PhysicsBodyDefinition` и добавляет `PhysicsBodyRef`. Для удаления добавьте `DestroyBodyRequest`.

## Порядок Синхронизации

```text
Init:        Physics2DBodyRestoreSystem подготавливает восстановленные тела
Begin:       create requests и push teleport/velocity в backend
FixedUpdate: PhysicsStepSystem выполняет Step(Time.FixedDeltaTime)
LateUpdate:  destroy requests и копирование transform/velocity обратно в ECS
```

Физика всегда использует fixed dt. Нельзя подменять его frame delta.

## Запросы К Backend

`IPhysicsWorld2D` предоставляет raycast, overlap, collision events, force/impulse и прямые операции с velocity. API принимает caller-provided spans:

```csharp
Span<RaycastHit2D> hits = stackalloc RaycastHit2D[16];
int count = physics.Raycast(
    start,
    end,
    Physics2DLayers.Platform | Physics2DLayers.Player,
    hits);

for (int i = 0; i < count; i++)
{
    int entityId = hits[i].Entity;
    // ...
}
```

Размер буфера ограничивает число возвращённых результатов. Не создавайте новый массив для каждого запроса в hot path.

## Restart-Worker Hot Reload

`PhysicsBodyRef` нельзя переносить между процессами: handle относится к конкретному экземпляру backend. Устойчивая `PhysicsBodyDefinition` сохраняется вместе с ECS-миром.

После `EcsRestartWorkerStateProvider.Restore()` система `Physics2DBodyRestoreSystem` удаляет восстановленные старые handles и создаёт `CreateBodyRequest`. Новые backend-тела создаются обычным simulation lifecycle.

## Ограничения Производительности

- Компоненты остаются unmanaged `struct`; не храните backend objects в ECS.
- Массовая синхронизация выполняется плотными буферами и spans.
- Создание/удаление тел — структурные операции, их не следует выполнять без необходимости каждый tick.
- Raycast/overlap получают буфер от вызывающего кода и не должны выделять память на каждый запрос.
- Не выполняйте blocking I/O, DI resolution или LINQ внутри physics-систем.

## Связанные Документы

- [ECS](ecs.md)
- [Hot Reload](../../01_Architecture/hot-reload.md)
- [Dependency Injection и области жизни](../../01_Architecture/dependency-injection-and-scopes.md)
