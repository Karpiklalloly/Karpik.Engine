# Каталог типов ECS bindings и первый числовой срез

Дата: 2026-10-29, Europe/Samara. Статус: контракт и performance policy согласованы; «в этой ветке делай» явно разрешает реализацию в текущей codex/modding-0.7 без новой ветки.

Первое поручение разработчика: «Каталог и план; код после согласования». Последующее «Делай» согласовало выполнение этого среза и его performance policy; «в этой ветке делай» разрешило текущую codex/modding-0.7. Пять scalar kinds реализованы и проверены; итоговая приёмка отражена в [ExecPlan](lua-ecs-type-catalog-execplan.md), [подробных задачах](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md) и [измерениях](lua-ecs-type-catalog-evidence.md).

## Цель и исходная база

Расширить общий доступ CLR/Lua к ECS без потери точности, boxing скаляров и нового хранилища на каждый язык. Ближайшая реализация добавит только `bool`, `i32`, `u32`, `f64` к существующему `f32`. Остальные строки каталога сохраняют согласованное направление, но требуют следующих отдельных срезов.

Исследован `C:/Users/artem/RiderProjects/KarpikEngine`, ветка `codex/modding-0.7`, commit `8fae435f561b022d5b74aaa74cafc910b1f0f37a`. Локальные `main` и `origin/main` указывают на `bed23cc950ba4e0df20dac24f5f71244372ec682`; свежесть GitHub не проверялась. База bindings ещё отсутствует в этой integration branch. Эти документы пока находятся вне репозитория, чтобы не добавлять новую задачу в общую modding-ветку.

Текущая реализация подтверждена исходниками:

| Место | Нынешний контракт |
|---|---|
| `Modules/Shared/Modding/Modding.Core/Bindings/ComponentFieldDescriptor.cs` | Запись `(Name, CanWrite, IComponentField<float> F32)` |
| `Bindings/IComponentField.cs` в том же модуле | `IComponentField<TValue> where TValue : unmanaged`, `Read(int)`, `Write(int,TValue)` |
| `Bindings/ComponentSchemaRegistry.cs` | Только `SchemaScalarKind.F32`; поля канонизируются по имени, каталог фиксируется на ModSet |
| `Bindings/DynamicF32ComponentPool.cs` | Общий sparse mapping и dense entity IDs; отдельный `float[]` на поле |
| `Bindings/PreparedComponentHandle.cs` | Переиспользуемый view, `ReadF32`/`WriteF32`, проверки callback owner и readonly |
| `Modules/Shared/Modding/Modding.Lua/LuaEcsContext.cs` | `ViewDescriptor.Index/SetIndex` работают только с `DynValue.Number` и `float` |
| `src/Karpik.Engine.Core.Codegen/ClrComponentBindingGenerator.cs` | Только public instance `float` fields; неподдержанные поля дают `KEB001` |

Старые документы могут называть прежнее расположение генератора. Для этого плана фактический проект — `Core.Codegen`, тестовый проект по-прежнему `Core.Generator.Tests`.

## Подходы

Выбранный и согласованный подход — продолжить типизированный доступ и SoA.

| Подход | Сопровождение | Производительность | Безопасность |
|---|---|---|---|
| Все скаляры через `double` или `object` | Сначала меньше ветвей | Конверсии, boxing при object; лишний объём для bool/малых целых | Потеря части значений i64/u64/decimal; автоматические преобразования скрывают ошибки |
| Типизированные accessors и массивы, switch по виду поля | Небольшое расширение существующего кода; рекомендуемый вариант | 0 B в общем C# steady state; один T[] на колонку, без нового per-entity объекта | Проверка вида и диапазона до записи, безопасный managed C# |
| Байтовый arena/Unsafe и ручные offsets | Больше контрактов alignment/lifetime | Возможная экономия metadata; выигрыш здесь не измерен | Риски неверного offset и выравнивания; сейчас не оправданы |

Новые зависимости, reflection fallback, отдельный scheduler и новый публичный CLR query API не нужны. SIMD для поштучных Lua accessor-вызовов не планируется.

## Каталог и статусы

«Следующий срез» означает предложение реализации после утверждения. «Позже» не означает отказ от возможности.

| CLR / schema token | Представление в Lua и правило записи | Статус |
|---|---|---|
| `bool` / `bool` | Только boolean; без truthiness и числовых 0/1 | Реализовано |
| `sbyte`, `byte` / `i8`, `u8` | number; конечное целое в диапазоне CLR-типа | Позже, тот же строгий числовой контракт |
| `short`, `ushort` / `i16`, `u16` | number; конечное целое в диапазоне CLR-типа | Позже |
| `int` / `i32` | number; целое от −2147483648 до 2147483647 включительно | Реализовано |
| `uint` / `u32` | number; целое от 0 до 4294967295 включительно | Реализовано |
| `long`, `ulong` / `i64`, `u64` | Точное значение с конструированием из строки и арифметикой без double; number допускается только без потери уже представленного им значения и с проверкой диапазона | Принятое продолжение; отдельный контракт API/стоимости |
| `float` / `f32` | number; округление double→float, finite overflow отклоняется; NaN и ±Infinity допустимы | Реализованный экспериментальный контракт, сохранить |
| `double` / `f64` | number; прямой double, без промежуточного float; NaN и ±Infinity допустимы | Реализовано |
| `char` / `char` | Предложение: number-код UTF-16 unit 0…65535, включая одиночный surrogate; строковые преобразования только явно | Позже; это предложение, не утверждённый API |
| `decimal` / `decimal` | Точное значение из строки; вычисления без double; смешанные точные типы требуют явного преобразования | Принятое продолжение; rounding/overflow/operator API задаст отдельный срез |
| `enum` | Underlying integer с проверкой диапазона; неизвестные именованные значения и flags-комбинации не запрещаются `Enum.IsDefined`; i64/u64 underlying требуют точного представления | Позже; CLR-first, dynamic enum metadata отдельно |
| Вложенный unmanaged struct | Generated доступ по полям без boxed copy; readonly всего пути учитывается | Принятое продолжение; синтаксис путей/вложенных views пока не выбран |
| `fixed T[N]` | Типизированный элемент, обязательный bounds check, без выдачи адреса | Позже; синтаксис индексации и допустимые element kinds отдельно |
| `T*`, `void*` | Opaque round-trip, без арифметики и dereference; адрес не преобразуется в Lua number | Принятое продолжение; provenance/lifetime и доверенный host отдельно |
| `nint`, `nuint` | Требуют явного учёта разрядности процесса и точного числового контракта | Вне первого среза; решение о поддержке отдельно |
| Managed references, `string`, `object`, массивы в CLR-компоненте, nullable/reference-rich layouts | Не преобразуются скрытно; нарушение unmanaged ECS-контракта диагностируется | Не поддерживаются этим направлением хранения |

Пустые dynamic schemas/markers и `ctx:create` остаются отдельными операциями продолжения. Они не блокируют этот числовой срез и не добавляются в него.

## Точный контракт ближайшего среза

### Входы и преобразования

`bool` getter возвращает `DataType.Boolean`, setter принимает только `DataType.Boolean`. `nil`, строки, userdata, числа и таблицы отклоняются. Lua-истинность значения здесь не применяется.

`i32/u32` getter возвращает `DataType.Number`. Setter сначала требует `DataType.Number`, затем `double.IsFinite(n)`, `n == Math.Truncate(n)` и включительный диапазон. Только после этого выполняется checked cast к `int`/`uint`. Округления, усечения и wraparound нет. `-0.0` принимается как 0. Значение `2147483647.5` отклоняется, хотя обычный C# cast мог бы усечь дробь.

`f64` getter/setter передают double напрямую. Значение `16777217.0` и `1.0 + 2^-40` должны пережить round-trip: это выявляет случайный маршрут через float. `double.MaxValue`, NaN и ±Infinity допустимы. Числовые строки и booleans не конвертируются. Для f32 сохраняются существующие overflow/underflow и rounding-семантика. Для f32/f64 сохраняется знак нуля; точные payload bits NaN и текстовое форматирование числа не обещаются.

Неверная запись не меняет целевое поле. Ранее успешные записи и уже сделанный `add` сохраняются: транзакционного rollback callback нет. Существующие `StopSimulation` и `LogAndContinue` действуют как раньше.

Ошибка должна назвать mod/system/query/component, поле и ожидаемый kind. Проверки owner, активного callback, readonly, наличия компонента, валидного entity ID и disposal остаются во всех новых typed путях, включая Release.

### Общий API и хранилище

Добавить `SchemaScalarKind.Bool`, `I32`, `U32`, `F64`; зафиксировать старое значение `F32 = 0`. Предлагаемые новые значения: Bool=1, I32=2, U32=3, F64=4. Registry принимает только эти пять значений, а не любой cast enum. Lua tokens регистрозависимы: `bool`, `i32`, `u32`, `f32`, `f64`. Поле с одинаковым именем, но другим kind считается изменением схемы и требует нового ModSet; изменение version не разрешает менять схему внутри живого ModSet.

`IComponentField<TValue>` остаётся существующим typed контрактом. `ComponentFieldDescriptor` получает read-only `Kind` и пять overloads конструктора `(string Name, bool CanWrite, IComponentField<T> field)` для T=bool/int/uint/float/double. Нулевой accessor запрещён. Существующая float-сигнатура с named argument `F32` сохраняется. Дескриптор владеет ровно одним логически активным typed accessor; допустимы пять nullable typed backing slots, заданных только конструкторами. Это metadata на поле, не память на entity. `F32` остаётся guarded non-null getter для старых вызовов; аналогичные getters: `Bool`, `I32`, `U32`, `F64`. Wrong-kind getter бросает `InvalidOperationException`, не reinterpret-cast. Никаких `object`-контейнеров скаляров.

В `PreparedComponentView` сохранить `ReadF32(int)`/`WriteF32(int,float)` и добавить `ReadBool(int)`/`WriteBool(int,bool)`, `ReadI32(int)`/`WriteI32(int,int)`, `ReadU32(int)`/`WriteU32(int,uint)`, `ReadF64(int)`/`WriteF64(int,double)` и `GetFieldKind(int)`. Последний тоже проверяет callback gate. Каждый setter проверяет `CanWrite`, каждый accessor — соответствие kind.

Внутренний `DynamicF32ComponentPool` заменить одним `DynamicComponentPool`, сохранив общие mapping/entities/count. В нём private `Column<T>` владеет одним T[] и реализует `IComponentField<T>`; массив небольшого внутреннего column-интерфейса позволяет выполнить resize, default reset, swap-remove и clear для всех полей. Column создаётся startup-switch по schema kind, а не через reflection. Read/Write используют typed массив и актуальный sparse mapping. У bool — bool[], у int — int[], у uint — uint[], у float — float[], у double — double[]. Пер-полевой интерфейсный вызов на structural mutation допустим, дополнительный объект на entity — нет. Старый отдельный float-пул не сохранять параллельно.

Генератор сопоставляет Roslyn SpecialType с пятью видами и испускает закрытый `IComponentField<T>`, чтение исходного поля и прямую запись через `MutableComponent`. `KEB001` остаётся для неподдержанных публичных полей, fixed buffers и остальных прежних запретов. Из теста, где `int` был unsupported, убрать именно это устаревшее ожидание и заменить его на действительно неподдержанный `long`.

Lua `ViewDescriptor` делает switch по `GetFieldKind`. Boolean — через `NewBoolean`/`Boolean`, остальные четыре вида — через `NewNumber`/`Number`, с описанной выше проверкой setter. Существующий `object obj` в MoonSharp IUserDataDescriptor ABI не является разрешением boxing значений компонента; сам ABI не переписывать.

### Lifetime, совместимость и границы

Engine хранит bindings, ModSet — неизменяемые schemas, Simulation — mutable typed columns и queries. Новые типы не меняют ComponentCallbackScope, int-ID модель, snapshot semantics, inferred scheduling keys, DI scopes или запрет конкурирующего исполнения одной VM. После swap-remove и growth старый reusable view должен адресовать правильный entity через mapping. После destroy старые views/columns недоступны и освобождаются.

Существующие f32 Lua-моды и F32 constructor/getter/view-вызовы сохраняются. Дескриптор остаётся record; сохранены named F32 constructor argument, Deconstruct, equality/cloning metadata и legacy `F32 init/with`. Init разрешён только при Kind==F32 и ненулевом accessor, поэтому не создаёт противоречивую mixed-kind metadata. Record ToString форматирует только Name/CanWrite/Kind, не вызывает все typed getters. Эти контракты закреплены отдельными regression tests. Для новых providers host и модули пересобираются одним SDK; совместимость произвольной старой SDK-сборки с новыми kinds не заявляется.

Никаких новых Client/Server зависимостей. Игровой fixed dt, editor/IPC, serialization, networking, schema migration и state restore не меняются.

## Приёмка и performance gate

Общий C# typed путь после подготовки и достаточной capacity должен выделять 0 B. Startup descriptors/arrays, рост capacity и errors измеряются отдельно. Не добавлять locks, LINQ, замыкания, temporary arrays и reflection в success callback path.

Исключение существующего ADR разрешало конкретный экспериментальный f32 workload, исторически измеренный как `1984 + 1464*N B/callback`, с потолком `4096 + 1600*N`. Это не доказательство текущих метрик и не разрешение нового бюджета для произвольного смешанного workload.

Согласованная policy следующего среза: прежний f32 allocation test сохраняет свой потолок; каждый новый однородный сценарий «один native get/read → один dynamic add/write» измеряется отдельно с теми же N и потолком. Чтение/запись пяти полей не сравнивать с бюджетом одной пары. Для mixed-five сценария на каждую entity выполнить один native get, один dynamic add и пять field read/write пар; f32 baseline делает те же операции с одним компонентом из пяти float-полей. Нельзя заменить его пятью отдельными get/add парами. При каждом N требуется `B/callback(mixed-five) <= B/callback(five-f32 baseline)`; значения performance fixture должны быть допустимы в обоих представлениях. p50/p95/p99 — диагностические измерения без нового абсолютного latency gate. При превышении сохранить evidence и остановить milestone до решения разработчика; не поднимать лимит автоматически.

Граничные примеры, полный список проверок и точные команды приведены в [подробном плане](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md). NativeAOT, ручная игра и editor hot reload не считаются проверенными автоматически после unit tests.

## Открытые решения и последующие этапы

Контракт и performance policy согласованы «Делай», текущая ветка — явным «в этой ветке делай». Новая ветка не создаётся. Сохраняется запрет публикации базового Lua/ECS-плана: изменения локальные, без push/PR. Точные числа, nested/fixed/pointer API остаются отдельными следующими этапами.

За пределами первого среза остаются operator/overflow API точных чисел, char-представление, nested paths, fixed-buffer indexing, opaque-pointer lifetime и dynamic enum metadata. Они перечислены с конкретным статусом; не требуется решать их для bool/i32/u32/f64.

Возможные learnings для будущего `docs/knowledge`, только после подтверждения: разделять каталог возможностей и реализованный срез; проверять дробность до integer cast; сравнивать allocation budgets при одинаковом числе операций. Эти заметки автоматически не созданы.

## Источники

В исследованном checkout: `plans/lua-ecs-bindings-spec.md` (разделы о типах и точных числах), `docs/02_ADR/lua-ecs-common-bindings.md`, `plans/lua-ecs-bindings-execplan.md`, `AGENTS.md`, `plans/PLANS.md` и перечисленные выше исходники. Граф `.ua` создан для commit 6f20dc8 от 2026-10-01 и не содержит новых bindings; использован только для проверки пригодности навигации. QMD search по Lua ECS bindings не вернул документов; решения сверены с owning files напрямую.

Справочные первоисточники: [MoonSharp mapping](https://www.moonsharp.org/mapping.html) описывает общие автоматические преобразования; здесь выбран более строгий явный контракт. [C# numeric conversions](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/numeric-conversions) объясняет усечение double→integer и возможную потерю точности целых при переводе в double; это причина отдельной проверки дробности и точных long/decimal представлений. Новые зависимости и поддержка будущих версий .NET из этих страниц не выводятся.
