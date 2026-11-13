# Отложенные структурные команды Lua

Контракт: [design](../../../plans/lua-structural-commands-design.md), [ExecPlan и текущие проверки](../../../plans/lua-structural-commands-execplan.md). Lua остаётся экспериментальным backend; command API не меняет немедленные `pool:add/del/get`.

## API и видимость

```lua
-- В активном callback системы; оба pool должны быть объявлены mutable в queries.
local position = ctx.queries.q.components["game:Position"]
local temperature = ctx.queries.q.components["weather:Temperature"]

-- Snapshot factory аллоцирует: создать один раз и переиспользовать в следующих callback.
initialPosition = initialPosition or position:new_snapshot()
initialPosition.X = 10

local entity = ctx.commands:create()
ctx.commands:add(position, entity, initialPosition)
ctx.commands:add(temperature, entity) -- значения по умолчанию
initialPosition.X = 99 -- уже поставленная команда по-прежнему содержит X=10
```

`ctx.commands:create()` возвращает непрозрачный pending token. `add(pool, target [, snapshot])` и `del(pool, target)` принимают такой token либо существующий целочисленный ID. Методы вызываются через `:` и не возвращают live view. Payload — root snapshot совместимого компонента той же Simulation; nested snapshot view и live ECS view не подходят. Копируются только writable leaves; readonly native fields у нового компонента остаются default. Существующий snapshot не получает автоматического writeback.

Во время callback команды не меняют ECS; queries не видят новые сущности или отложенные изменения. После успешного callback движок проверяет всю очередь по актуальному состоянию мира, затем применяет её до следующей системы. Порядок компонентных операций — FIFO. Физическое создание происходит при первом `add` соответствующего token; порядок numeric IDs и native NewEntity events не обязан совпадать с порядком объявления tokens. После применения сущности доступны следующим системам через обычные queries. Сохраняемая ссылка на результат создания не предоставляется.

Токен действует только в породившем его callback. Его нельзя передавать немедленным pool APIs или сохранять для следующего вызова. Сохранённые `ctx.commands`/pool handles можно использовать только в активном callback своей системы, а не другой системы той же VM. Readonly-handle не становится writable из-за наличия mutable-handle в другой query.

Служебный `DynamicComponentAnchor` нельзя изменять через команды даже при его явной CLR-регистрации host. Существующая пустая сущность тоже требует осторожности: если перед её первым отложенным `add` материализуется другая новая сущность, backend может собрать пустую цель. Такая очередь отклоняется заранее; инициализировать пустую существующую цель нужно до создания других целей.

## Порядок и ошибки

`add` существующего компонента и `del` отсутствующего — ошибки. Проверка учитывает предыдущие команды и немедленные изменения, выполненные callback. Каждая новая сущность должна получить компонент. Удаление последнего логического компонента запрещено: для замены сначала добавить новый, затем удалить старый. Учитываются все native/dynamic компоненты сущности, включая не объявленные в queries; технический DynamicComponentAnchor не является логическим компонентом.

Ошибка callback, любой отклонённый вызов command API, переполнение или ошибка предварительной проверки отменяют всю отложенную очередь. Это сохраняется, даже если Lua перехватил ошибку command API через `pcall`. Уже выполненные немедленные записи и `add/del` сохраняются. Политика `ScriptSystemOptions.ErrorPolicy` определяет остановку (`StopSimulation`) либо запись ошибки и продолжение следующих систем (`LogAndContinue`).

Неожиданный внутренний сбой во время самого применения останавливает Simulation при любой policy; откат уже выполненной части не обещается. Native listeners остаются синхронными, не получают атомарное событие на всю очередь и не должны reentrantly менять мир. Инварианты ёмкости/живости проверяются; гарантия безопасности и no-GC не распространяется на произвольный host callback.

## Настройки и память

Host задаёт `ScriptSystemOptions.Commands` через Simulation-scoped регистрацию options. `StructuralCommandOptions` по умолчанию ограничивает callback 1024 операциями (включая create), 256 pending entities и 256 KiB payload. Буфер общий для последовательных callback одной Simulation, состояния разных Simulation разделены. Значения проверяются при подготовке с checked arithmetic; нулевой бюджет запрещает соответствующую отправку команд. Ошибка лимита не увеличивает массивы и не переносит часть очереди на следующий callback.

Ёмкости мира, native/dynamic pools и anchor проверяются до применения по пиковому заполнению FIFO. Native pool имеет служебный нулевой слот; deleted IDs до release ещё занимают ёмкость мира. Буфер использует уже подготовленную ёмкость и отказывает при её нехватке. Host может настроить начальные размеры через `EcsWorldConfig`. Рост старых immediate APIs не запрещён этим API; предварительная проверка не должна обращаться к `world.Entities`, поскольку это может выполнять cleanup.

Цель приёмки common C#: 0 B на успешном прогретом enqueue/preflight/playback. Подготовка, fresh snapshot factories, ошибки и teardown измеряются отдельно. Lua VM и immutable wrappers pending tokens/exact values могут аллоцировать. Потолок новых canonical probes: `4096 + 1600*C + 1600*V + 544*O` B/callback, где C — команды включая create, V — копируемые scalar payload leaves, O — exact arithmetic operations. Это предел конкретных workloads; прежние typed/f32/composite allocation gates сохраняются. Фактические результаты и ограничения фиксируются в ExecPlan/evidence.

Numeric IDs существующих сущностей получают generation check при постановке/применении. Это обнаруживает замену сущности после enqueue, но не исправляет число, которое уже было устаревшим до enqueue. Pointer payload сохраняет только host-authorized opaque token; revocation повторно проверяется перед применением, а lifetime native памяти остаётся ответственностью host.

Direct entity destroy, deferred set/upsert, persistent creation receipts, schema reload/migrations, save/network и отдельные FixedUpdate-системы этим API не добавляются.
