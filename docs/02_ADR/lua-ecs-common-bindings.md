---
title: "Общий ECS-доступ для Lua и экспериментальный allocation budget"
date: "2026-10-25"
status: "accepted"
tags:
  - adr
  - architecture
  - ecs
---

# Общий ECS-доступ и первый f32-срез Lua

## Context

Lua-системы имели metadata reads/writes, но не доступ к данным ECS. Native
Dragon queries возвращают borrowed buffers; они не обеспечивают стабильный набор
ID при структурных изменениях. Динамические схемы не имеют отдельных CLR Types,
а старые scheduling descriptors принимали только Type. MoonSharp выделяет память
на числа и интерпретацию даже без новых component objects.

## Decision

Общий механизм находится в `Modules/Shared/Modding/Modding.Core/Bindings`, без MoonSharp.
По поручению разработчика от 2026-10-25 он перенесён из ECS.Core: общий API
теперь принадлежит сборке Modding.Core и namespace `Karpik.Engine.Shared.Modding.Bindings`.
Engine владеет CLR bindings, ModSet — каноническими schema descriptions,
Simulation — данными и prepared queries. Первая подготовка собирает descriptions
из реально загруженных VM, атомарно фиксирует весь набор; последующие Simulation
проверяют совпадение. Дополнительная VM ради схем не создаётся.

На динамическую схему — плотный SoA-пул с sparse mapping и dense entity IDs.
Dense capacity растёт независимо от world.Capacity. Служебный unmanaged
DynamicComponentAnchor присутствует только пока есть хотя бы один dynamic
component любого мода. Native release-listener очищает внешние данные после
native pools, но до повторной выдачи ID; повторный native Del из него запрещён.

CLR Register<T> порождает typed ref field access при сборке, без reflection и
boxing значений в callback. Provider уникален для имени сборки. Static принимает
generated factories, Dynamic — обычные startup exports. Первый срез поддерживает
публичные плоские f32-поля; остальные согласованные scalar/nested виды остаются
следующими этапами и диагностируются как ещё не реализованные.

Queries inc/exc/opt имеют независимый readonly/mutable mode. Lua обходит обычные
int-ID, получает данные через prepared pools и переиспользуемые views. Snapshot
ID принадлежит named query; поля остаются live. Разные query names независимы,
повторный entities() того же имени перезапускает один курсор. Проверки владельца
и активного callback сохраняются и в Release. Generation protection int-ID нет.

Scheduling keys различают CLR Type и полный dynamic schema ID; граф один раз
превращает их в numeric resource indices. Accesses выводятся из всех queries,
включая presence и anchor writes. Legacy CLR metadata и default-null sentinel
сохраняются. LuaUpdateSystem пока остаётся глобально sequential.

Writes прямые, rollback нет. StopSimulation передаёт исключение host; обязательный
LogAndContinue прерывает callback, логирует контекст, продолжает следующие системы
и повторяет ошибочную систему в следующем кадре. Auto-disable не вводится.

## Experimental allocation exception

Разработчик явно разрешил только экспериментальному MoonSharp-срезу steady-state
потолок `4096 + 1600*N` bytes/callback после reserve. Общий C# слой должен давать
0 managed bytes. Рост измеряется отдельно. Это не production real-time endorsement
и не отмена общих no-allocation правил для остального движка.

Свежий actual-slice probe на .NET 10.0.13 / MoonSharp 2.0.0.0 показал
`1984 + 1464*N` bytes/callback; N=4096 — 5,998,528 bytes и p99 8.1063 ms
в одном локальном прогоне. Время не является гарантией. Reusable host view
не предотвращает копирования DynValue и VM allocations. Точный budget проверяется
исполняемыми тестами, не предполагается по устройству common storage.

## Consequences

### Уточнение 2026-10-29: typed scalar kinds

Согласованный [typed-контракт](../../plans/lua-ecs-type-catalog-spec.md) реализует bool/i32/u32/f64 вместе с F32=0. Тот же sparse/dense membership использует private Column<T> с bool[]/int[]/uint[]/float[]/double[]. Generated CLR access остаётся прямым и типизированным; Lua bool не использует truthiness, целые проверяют finite/integral/range до checked cast, f64 не проходит через float. Callback gate, права, disposal и ModSet schema equality сохраняются. Legacy F32 constructor/getter/Deconstruct/init/with закреплены tests.

Разработчик принял отдельные экспериментальные probes: для одного native get/read→dynamic add/write старый потолок 4096+1600*N сохраняется; для пяти полей mixed-five не превышает matched five-f32 при одинаковых get/add/read/write. Измерено: copy single-field 1984+1368*N, mixed-five и baseline одинаковые 1984+2136*N. Это разные workloads, не изменение арифметического f32 benchmark выше. C# reserved access — 0 B. Значения времени диагностические; [полная матрица](../../plans/lua-ecs-type-catalog-evidence.md) содержит p50/p95/p99/Gen0 и отдельный cold/growth probe. No-GC Lua и production real-time пригодность не объявляются.

Lua `reads/writes` заменяются queries, callback получает `(dt, ctx)`.
LuaComponentRegistry теперь требует общий ClrComponentBindings через constructor;
LuaSimulationPreparation получает common services и policy через DI. Existing
CLR attributes и обычный world.Where(out Aspect) сохраняются.

`game:Position` + `weather:Temperature(f32)` — законченный экспериментальный срез.
Точные числа, nested fields, dynamic markers, ctx:create, schema hot reload,
save/network integration и public ergonomic CLR query API сюда не входят.
Ручная игра и NativeAOT publish не подтверждены автоматическими composition tests.

## Verification

Тесты `ClrComponentBindingGeneratorTests`, `DynamicComponentStorageTests`,
`MixedComponentQueryTests`, `MixedAccessGraphTests`, `LuaEcsBindingsTests` и
`LuaEcsAllocationTests` проверяют codegen/discovery/DI, lifecycle и reuse,
ownership, snapshots/growth, обе policies, numeric boundaries и allocation budget.
Полный набор команд и актуальные результаты — в
[ExecPlan](../../plans/lua-ecs-bindings-execplan.md).

## Расширение 2026-11-11: полный каталог и detached snapshots

Принятые новые контракты и реальные проверки — [ExecPlan](../../plans/modding-types-execplan.md) и [API справочник](../modules/modding/typed-bindings.md). Исторические запреты nested/exact/pointer выше относятся к первому f32-срезу.

Typed columns расширяются до всех выбранных scalar kinds; recursive schema tree канонизируется/freeze в ModSet, fixed scalar leaves используют общий dense массив со stride. Native nested/fixed leaf access генерируется напрямую. Metadata field descriptor хранит одну ссылку на accessor; scalar значения не boxятся.

Live nested views переиспользуют root и следуют последнему get/add. copy/new_snapshot создают отделённое typed хранилище без source pool/world references; copy_to повторно заполняет buffer без common allocations. Snapshot lifetime ограничен Simulation, не source entity; pointer snapshot сохраняет только opaque token. Engine/ModSet/Simulation ownership и callback permissions сохраняются.

Exact Lua values неизменяемы, поддерживают checked CLR arithmetic и explicit conversion; цена immutable results включается в отдельный experimental workload. Согласованный ceiling новых probes 4096+1600*N*L+544*N*O B/callback; L — copied scalar leaves/entity, O — exact operations/entity. Старые gates не повышаются. Common C# остаётся 0 B после подготовки; Lua не объявляется production no-GC.

Pointers доступны только через host-registered type/domain, с unmanaged metadata в columns, nil/null и revocation. Host обязан управлять pointee lifetime, raw addresses/dereference не передаются Lua. Snapshot не продлевает жизнь native памяти.

## Расширение 2026-11-13: отложенные структурные команды

Сохраняются немедленные pool APIs; отдельный `ctx.commands` принимает create/add/del. Simulation владеет общим ограниченным буфером команд, payload и проверки, переиспользуемым последовательными callback. Mutable query handles сохраняют permissions и scheduler accesses, включая dynamic anchor. Данные snapshot копируются при enqueue, не удерживаются как ссылка на изменяемый source.

После callback вся очередь проверяется по состоянию мира с симуляцией FIFO membership и peak capacity; только затем применяется до следующей системы. Pending token ограничен одним owner/invocation и не превращается в persistent entity handle. Физическое создание при первом add избегает накопления пустых новых сущностей; пустое создание и отложенное удаление последнего логического компонента отклоняются. Альтернативы — единая миграция старых immediate APIs, phase-end flush и lifetime guards — отклонены в пользу совместимости и меньшего объёма backend изменений.

Любая command API ошибка, включая перехваченную pcall, инвалидирует очередь; immediate writes сохраняются. Предсказуемые ошибки следуют существующей ErrorPolicy; неожиданный playback failure останавливает Simulation без обещания rollback. Native host listeners не включены в transactional observer/no-GC гарантию. Array growth в новом successful common path запрещён; world/pool capacity exhaustion отклоняется заранее, отдельно от лимитов очереди. Lua сохраняет experimental статус и отдельный measurable canonical budget, прежние gates не повышаются.

API/ограничения: [структурные команды](../modules/modding/structural-commands.md). Проверки и фактический статус реализации: [ExecPlan](../../plans/lua-structural-commands-execplan.md). Этот ADR не является доказательством выполненных тестов.
