# Типизированный доступ Lua к ECS

Контракт расширения 2026-11-11; реализация принята 2026-11-12: Runner583/583, Generator78/78, independent review closed. Приёмка и ограничения — в [ExecPlan](../../../plans/modding-types-execplan.md). Это экспериментальный Lua backend. Common C# access не зависит от MoonSharp.

## Типы

Native CLR bindings генерируются для публичных instance fields unmanaged компонентов, зарегистрированных через `ClrComponentBindings.Register<T>`/`LuaComponentRegistry.Register<T>`. Getters/setters обращаются к исходным полям; nested structs не превращаются в boxed copies.

| CLR / schema | Lua |
| --- | --- |
| bool | boolean, без truthiness |
| i8/u8/i16/u16/i32/u32, char | number с проверками finite/integer/range; char — UTF-16 code unit 0…65535 |
| f32/f64 | number; f32 округляет, finite overflow запрещён; NaN/Infinity допустимы |
| i64/u64/decimal | Неизменяемое точное значение с обычными операторами |
| nint/nuint | signed/unsigned exact value с диапазоном разрядности процесса |
| enum | underlying integer; неизвестные значения/flags допустимы; wide underlying использует exact value |
| Nested unmanaged struct | Вложенный view: `v.Position.X` |
| Native C# fixed / dynamic fixed | Индексы 1…N, длина `#v.Values` |
| Pointer | `nil` либо непрозрачный token, только при opt-in host |

Managed references, string/object/managed arrays/nullable layouts, native InlineArray и автоматическое открытие private fields/properties не добавляются. `^` и битовые операции exact values не поддерживаются этим срезом.

## Точные числа

```lua
local count = types.i64("9223372036854775806")
count = count + types.i64("1")
local price = types.decimal("0.1")
local text = tostring(price)
local restored = types.decimal(text)
```

Операторы `+ - * / %`, унарный минус и сравнения сохраняют точный kind. Переполнение и деление на ноль — ошибка. Integer division усекает к нулю: -7/2 = -3. Decimal использует CLR decimal arithmetic. Arithmetic/ordering разных exact kinds — ошибка; equality разных kinds — false. Обычные Lua number/строки не приводятся автоматически.

Конструктор принимает строку, другой exact kind с явной проверкой преобразования либо уже представленное number, только если целевой тип может представить его точно. В частности, `types.decimal(0.1)` отвергается: binary double 0.1 не равен точному decimal 0.1. Использовать `types.decimal("0.1")`. Integer destination не отбрасывает дробь. Строки invariant: знак/цифры, для decimal — десятичная точка; пробелы, разделители групп и exponent syntax не принимаются. `tostring` выдаёт совместимую строку без зависимости от culture.

## Динамические схемы

```lua
components = {
    Shape = { version = 1, fields = {
        Health = "i16",
        Position = { kind = "struct", fields = { X = "f32", Y = "f32" } },
        Points = { kind = "fixed", length = 3,
            element = { kind = "struct", fields = { X = "f64", Y = "f64" } } },
        Flags = { kind = "enum", underlying = "u32" },
        Data = { kind = "pointer", type = "host:ParticleData" }
    } }
}
```

Scalar strings сохраняют старый синтаксис. Enum node задаёт underlying, без именованных members/type registry. Fixed elements — scalar (включая enum/pointer) либо struct; struct может содержать fixed fields. Прямой fixed-of-fixed не поддерживается. Размер положительный, фиксированный, checked; direct indexing 0/N+1/fractions/nonfinite/нечисловым ключом запрещён. Readonly родителя распространяется на все вложенные leaves.

Порядок полей не влияет на identity. ModSet канонизирует всё дерево, включая длины fixed, enum underlying и pointer type ID, и фиксирует версию/схему до исполнения. Циклы/неизвестные options/неподдержанные kinds отклоняются при подготовке. Динамические scalar leaves хранятся в typed dense columns; fixed elements одного leaf используют общий массив с фиксированным stride, а не managed объект на сущность.

## Live views и снимки

```lua
local pool = ctx.queries.q.components["my_mod:Shape"]
local live = pool:get(entityA)
local nested = live.Position
pool:get(entityB)
-- nested теперь обращается к B: get переиспользует один live view.

local snapshot = pool:copy(entityA)
local saved = snapshot.Position
pool:get(entityB)
-- saved остаётся частью независимых данных A.
```

`get/add` views требуют активный callback своей системы и присутствие компонента при чтении/записи. Они не являются snapshots или generation-safe entity handles. Удалённый int-ID может позднее принадлежать другой entity, как в предыдущем контракте.

`copy(entity)` создаёт detached snapshot одного компонента; это не clone entity. Nested/fixed данные копируются. Изменение/удаление source не меняет snapshot; изменение snapshot автоматически не записывается в ECS. Snapshot можно хранить между callbacks до disposal своей Simulation. CLR readonly leaves сохраняют readonly; query readonly запрещает запись в ECS, а не локальное изменение mutable данных отделённой копии.

```lua
-- Создать при первом callback, затем сохранить для следующих вызовов системы:
buffer = buffer or pool:new_snapshot()
pool:copy_to(entityA, buffer)
local retainedPosition = buffer.Position
pool:copy_to(entityB, buffer)
-- retainedPosition остаётся тем же view буфера и теперь содержит скопированные данные B.
```

`new_snapshot()` создаёт default buffer и требует active callback pool. `copy_to` принимает root buffer того же компонента/binding/schema и Simulation; несовместимость проверяется до копирования. Root/nested identities сохраняются, существующий buffer возвращается как результат. Создание snapshots и Lua wrappers явно аллоцирует. Common повторный `CopyTo` после подготовки не аллоцирует; no-GC для всего Lua callback не обещается.

Common C# API: `PreparedComponentHandle.NewSnapshot()`, `Copy(entity)`, `CopyTo(entity, snapshot)`; `ComponentSnapshot` и live view реализуют typed `IComponentDataView.Read<T>/Write<T>`. `ComponentFieldDescriptor.Path` содержит отдельные name/index segments, поэтому точка внутри настоящего field name не смешивается с nested path. Старые scalar APIs и F32 constructor/init/with/Deconstruct сохраняются.

## Непрозрачные указатели

C# host должен до `ComponentQueryPreparer.Initialize/Prepare` настроить Simulation-scoped `ComponentPointerRegistry`: `CreateDomain()`, `RegisterType(id, nativePointerType, domain)`. ResolveType/ResolveNativeType дают prepared handles. Registry передаётся через constructor DI; обычная регистрация сервиса — `[Export]` и `[ServiceRegistration]`. Host задаёт подготовку/явную factory-регистрацию, чтобы opt-in был готов до подготовки Lua queries; service locator/field injection не нужны.

Пустой registry запрещает native/dynamic pointer fields при подготовке. Type ID привязан к одному CLR pointer type и lifetime domain. Совместимые token assignment требуют тот же type/registry/domain generation. `T*` и `void*` не преобразуются автоматически. Null — nil; его можно записать для очистки уже отозванного типа, пока сама Simulation/registry жива.

`Revoke(domain)` инвалидирует сохранённые non-null tokens, включая tokens внутри snapshots. Host отзывает domain перед освобождением памяти. Проверка token не доказывает жизнь произвольной native памяти, snapshot её не копирует и не продлевает lifetime. Lua не получает адрес, dereference, arithmetic или создание pointer из числа/строки. Для host-only raw address используются проверенные `Capture/ReadAddress`; hot path использует заранее разрешённые handles без строкового поиска.

## Производительность и границы

Common read/write/nested/fixed access и warmed copy_to: 0 B. Preparation, snapshot creation, formatting/parsing, growth и errors измеряются отдельно. Lua VM и immutable exact/token wrappers выделяют память: backend экспериментальный.

Новый согласованный canonical ceiling: `4096 + 1600*N*L + 544*N*O` B/callback. N — число entities; L — число scalar leaves, копируемых read→write по одному разу на entity (fixed elements считаются отдельно); O — exact arithmetic operations/entity. Это budget конкретных probes, не произвольной Lua программы. Старые f32/mixed-five gates сохраняются. Время/Gen0/memory peaks — измерения, не production latency SLA.

Save/network/schema reload, state migration, индивидуальные FixedUpdate/RenderPrepare и создание сущностей не добавлены расширением типов. Отдельный последующий срез создания и отложенных add/del описан в [структурных командах](structural-commands.md) и его [ExecPlan](../../../plans/lua-structural-commands-execplan.md). NativeAOT/editor hot reload/manual game требуют отдельных проверок.
