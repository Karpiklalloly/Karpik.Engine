# Ёмкость мира и ещё не освобождённые ID

`world.Count` показывает живые сущности, но удалённый ID возвращается dispenser только при обработке deferred-delete buffer. До release такой ID продолжает занимать слот. Поэтому `Capacity - Count` завышает доступную ёмкость.

Для текущего DragonECS при обычном публичном lifecycle, одном владельце мира и отсутствии конкурентных изменений:

```text
free = world.Capacity - 1 - world.Count - world.DelEntBufferCount
```

Единица — служебный null-ID. Например, Capacity=8, Count=5 и DelEntBufferCount=2 означают ноль свободных слотов. Формула зависит от реализации dispenser; при появлении отдельного резервирования ID её нужно пересмотреть.

Проверять ёмкость до изменения мира. Не вызывать release ради успешного прохождения проверки: это уже изменяет ECS и может запускать listeners. `world.Entities` и `ToSpan()` тоже могут автоматически обработать deferred deletes, поэтому не подходят для чистой preflight-проверки. Недостаток подготовленной ёмкости должен отклонять очередь, а не незаметно увеличивать массивы в горячем пути.

[Preflight](../../../Modules/Shared/Modding/Modding.Core/Bindings/StructuralCommandBuffer.cs) · [Backend lifecycle](../../../third-parties/DragonECS/src/EcsWorld.cs) · [Dispenser](../../../third-parties/DragonECS/src/Internal/IdDispenser.cs) · [Тест unreleased ID](../../../tests/Karpik.Engine.Core.Runner.Tests/StructuralCommandBufferTests.cs)
