# Backlog

Рабочий список конкретных задач, багов и технических долгов, которые не должны раздувать milestone roadmap. Для крупных изменений заводить ExecPlan в `plans/`.

## P0 - Runtime Correctness

### Освобождать физические тела при удалении ECS entity

**Проблема:** при удалении сущности ECS связанное тело может остаться в `IPhysicsWorld2D`. На disconnect это оставляет в физическом мире stale body, которое продолжает участвовать в simulation/collision queries без живой gameplay entity.

**Затронутые места:**

- `Modules/Shared/Physics/Physics2D.Core/ECS/Physics2DBodyDestroyer.cs`
- `Modules/Shared/Physics/Physics2D.Core/ECS/Components.cs`
- `MyGame/Server/MyGame.Server.Main/Systems/NetworkSystem.cs`

**Acceptance criteria:**

- [ ] Удаление entity с `PhysicsBodyRef` гарантированно вызывает `IPhysicsWorld2D.DestroyBody`.
- [ ] Disconnect cleanup игрока не оставляет тела в физическом мире.
- [ ] Cleanup не аллоцирует в ECS/system hot path после warm-up.
- [ ] Добавлен минимальный тест или smoke-сценарий на удаление entity с physics body.

