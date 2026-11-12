# Завершить каталог типов CLR/Lua ECS bindings

- Status: ready
- Started: 2026-11-11
- Updated: 2026-11-12
- Revision: 15
- Complexity: complex

## Goal and scope

Пользователь поручил завершить расширение типов и выбрал «Весь каталог, по этапам». Существующие bool/i32/u32/f32/f64 сохраняются. Рассмотреть i8/u8/i16/u16, char, enum, точные i64/u64/decimal, вложенные unmanaged структуры, fixed buffers, opaque pointers и явно решить поддержку nint/nuint. ctx:create, пустые схемы, hot reload, save/network не являются расширением типов и не включаются автоматически.

## Requirements and behavior

Продолжить plans/lua-ecs-type-catalog-spec.md, не переутверждать реализованные scalar contracts. i64/u64/decimal нельзя проводить через double с потерей точности. Integer setters проверяют тип, целочисленность и диапазон до изменения данных. Readonly всего вложенного пути соблюдается. Fixed buffers требуют bounds checks. Указатели только opaque round-trip без dereference, арифметики и преобразования в number; provenance/lifetime ещё не выбраны.

## Architecture and integration

Подтверждено текущими исходниками: ComponentFieldDescriptor содержит пять typed accessors; DynamicComponentPool хранит отдельные T[] columns; LuaEcsContext выдаёт reusable views и DynValue scalars; ClrComponentBindingGenerator генерирует direct accessors для public полей пяти scalar kinds. Расширение затрагивает common API, schema validation, native codegen и Lua descriptors. Имеющийся общий дизайн не задаёт окончательный Lua API exact/composite/pointer values.

## Constraints

Common C# reserved success path должен оставаться 0 B, без boxing скаляров и reflection fallback. Существующий экспериментальный Lua allocation waiver не распространяется автоматически на новые workloads. Принятый ранее диапазон не обещает production latency. Проверки ownership, readonly, invalid entity и disposal сохраняются. Не изменять Shared/Client/Server boundaries или tick semantics.

Исторический checkpoint перед реализацией: branch codex/modding-0.7, HEAD 4767dca. Единственный обнаруженный untracked файл .idea/.idea.KarpikEngine/.idea/.name не относится к задаче. Старая общая task branch содержит prerequisites; новое разрешение на её использование или dependent branch не получено. Черновик хранится вне repository, до выбора законной базы task branch. На момент этого исходного checkpoint repository edits не было; дальнейшая реализация разрешена и отражена в Execution authorization/ExecPlan.

## Decisions and alternatives

- D1 — Scope: весь каталог, отдельными проверяемыми этапами. Прямой выбор пользователя 2026-11-11.
- D2 — Сохранить typed SoA/common accessors; не заменять значения object/double. Это продолжение существующего принятого контракта. Byte arena/Unsafe не имеют измеренного преимущества для обычных scalar columns.
- D3 — Пользователь выбрал обычные операторы для точных Lua-чисел (2026-11-11: «обычные операторы»). Альтернатива explicit functions add/sub/etc. отклонена в пользу удобства кода модов. Representation, immutability, конструкторы, числовая семантика и allocation budget этим ответом отдельно не утверждены.
- D4 — Пользователь принял числовую семантику (2026-11-11: «Да»): i64/u64 операции сохраняют тип; overflow выдаёт ошибку; integer division усекает к нулю (7 / 2 = 3, -7 / 2 = -3); decimal использует CLR decimal arithmetic; между точными типами требуется явное преобразование. Альтернатива integer / с дробным результатом и переходом в другой kind отклонена в пользу предсказуемого типа результата. Constructors/conversions API, comparison details и поддерживаемый набор операторов ещё требуют конкретизации.
- D5 — Пользователь выбрал вложенные views (2026-11-11: «вложенные»): доступ view.Position.X. Альтернатива плоских путей view["Position.X"] отклонена. Генератор должен обращаться к leaf напрямую без boxed copy; readonly любого родителя запрещает запись. Representation и lifetime вложенных views ещё не выбраны.
- D6 — Пользователь принял явный снимок компонента (2026-11-11: «Да, норм» в ответ на предложение copy/copy_to). pool:copy(entity) возвращает независимые данные компонента, включая вложенные структуры; последующие get не перенаправляют вложенные views снимка. Изменения снимка не затрагивают исходную entity. Это не клонирование ECS entity. Обычный get сохраняет существующий reusable контракт: повторный get перенаправляет root и его nested access. copy создаёт независимый снимок с явной стоимостью; copy_to(entity, buffer) использует заранее созданное хранилище. Альтернатива стабильного entity-bound get не выбрана. Snapshot validity/lifetime, buffer constructor, type compatibility и стоимость Lua wrapper operations ещё требуют конкретизации; copy_to не считается доказанным no-GC до измерения. Pointer snapshot копирует только opaque token, не pointee; ownership/lifetime token предстоит определить.
- D7 — Пользователь выбрал fixed-buffer indexing 1..N (2026-11-11: «1..N»). CLR offset = index - 1. Альтернатива 0..N-1 отклонена. Предложены #view.Buffer для длины, проверки invalid/out-of-range индексов до доступа и readonly propagation; эти детали ещё не подтверждены отдельно.
- D8 — Пользователь принял pointer contract (2026-11-11: «да»): явный opt-in доверенного C# host, opaque read/compatible-field round-trip, без dereference, numeric conversion и арифметики. Host отвечает за pointee lifetime; copy копирует только opaque pointer value, не память и не продлевает её жизнь. Lua API не гарантирует валидность произвольной native памяти. Альтернатива raw-address/numeric access не выбрана. Точная совместимость pointer kinds, null representation, domain revocation и dynamic pointer schema policy требуют решения.
- D9 — Пользователь принял lifetime detached snapshots (2026-11-11: «да»): снимки можно хранить между callbacks до завершения своей Simulation. Собственное хранилище nested structs/fixed buffers не зависит от изменения/удаления исходной entity. После disposal Simulation доступ запрещён; между Simulation snapshots не передаются. Pointer lifetime независимо ограничивается host. Альтернатива callback-only snapshot отклонена в пользу полезности копии; независимый от Simulation ресурс не выбран.
- D10 — Пользователь принял scalar edge contracts (2026-11-11: «да»): char как numeric UTF-16 code unit 0..65535 без неявных string conversions; CLR enum как underlying integer, unknown/flags values разрешены в диапазоне, i64/u64 underlying используют exact kinds; nint/nuint как exact integers с диапазоном разрядности процесса, без доступа к памяти. Поддержка одиночных surrogate следует выбранному диапазону UTF-16 code unit. Dynamic enum metadata не была предложена в пользовательском вопросе и пока не утверждена. Альтернативы char-as-string, enum names-only и nint-as-double не выбраны.
- D11 — Пользователь принял exact constructors (2026-11-11: «да»): types.i64(value), types.u64(value), types.decimal(value); строки сохраняют цифры; explicit conversion из другого exact kind проверяет диапазон и запрещает потерю дробной части для integer destination; Lua number принимается только при точном преобразовании уже представленного значения, потерянные до вызова цифры не восстанавливаются. Arithmetic не приводит string/обычные Lua number неявно. Constructor syntax сохранён из предыдущего примера. Полный operator/comparison/formatting set ещё уточняется.
- D12 — Пользователь принял inline dynamic composite declarations (2026-11-11: «да»): scalar tokens сохраняются; вложенная структура {kind="struct",fields={...}}, fixed buffer {kind="fixed",element="f32",length=4}; component fields продолжают прежний формат. Альтернатива named reusable value-type registry отклонена для текущего scope в пользу простого inline дерева. Возможные element kinds, bounds/depth/cycle validation и recursive canonical schema identity требуется определить в спецификации.
- D13 — Пользователь принял snapshot buffer API (2026-11-11: «да»): pool:new_snapshot() создаёт reusable buffer; pool:copy_to(entity, buffer) перезаписывает его; buffer совместим только с тем же component и той же Simulation, incompatibility вызывает ошибку до копирования. copy создаёт и заполняет новый buffer; локальные изменения snapshot автоматически не записываются в ECS. Точная factory callback/preparation доступность, zero-init и matching rules для readonly native fields относятся к детализации контракта.
- Техническая проверка 2026-11-11: локальные main/origin/main = d8ee920, current HEAD = 4767dca; git ls-tree main не содержит ComponentFieldDescriptor.cs. Для независимой ветки от main отсутствуют prerequisites; concrete dependent-branch proposal должен войти в итоговый reviewable plan, не переключать/изменять repository сейчас. Graph metadata .ua/meta.json указывает 7224b183, граф не считается свежим относительно current checkout.
- Установленный MoonSharp 2.0.0 XML документирует IUserDataDescriptor.MetaIndex с arithmetic/comparison/length metamethods. Runtime probe /tmp/karpik-exact-probe/Program.cs, dotnet 10.0.115 Release, 2026-11-11: custom immutable i64 wrapper, cached descriptor callbacks; +,-,*,/,%,unary minus,eq,lt,le,tostring и length проверены 10 результатами, PASS. Repeated addition N=0/1/16/256/4096 после 50 warmups и 50 measured calls: 816/1344/9264/135984/2163504 B/callback, то есть 816+528*N. Это standalone MoonSharp probe, не engine binding integration, не бюджет и не p99 acceptance. Initial run не дошёл до build из-за read-only default DOTNET_CLI_HOME; повтор с DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli завершился exit 0. Команда dotnet run --project /tmp/karpik-exact-probe/Probe.csproj -c Release -m:1 -nr:false --property:NuGetAudit=false --property:RestoreIgnoreFailedSources=true с DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1, DOTNET_CLI_TELEMETRY_OPTOUT=1.

- D14 — Пользователь принял dynamic fixed arrays со скалярами и вложенными структурами (2026-11-11: «да»): view.Points[1].X, length задаётся схемой и не меняется. Native fixed buffers сохраняют допустимые C# element kinds; поддержка native InlineArray не подразумевается. Data-oriented направление: flatten leaf fields, typed dense arrays с fixed stride для elements, nested views подготовлены заранее. Direct fixed-of-fixed syntax не обсуждался; рекурсивные составные элементы должны быть описаны единообразно в окончательном плане.
- D15 — Пользователь принял pointer completion contract (2026-11-11: «да»): null представлен nil; разрешённые type IDs регистрирует host и мод использует их имена в schema; запись допускает только совместимый opaque token того же host lifetime domain; implicit T*↔void* casts запрещены. Предложенный dynamic синтаксис {kind="pointer",type="host:ParticleData"} продолжает выбранные inline declarations. Host revocation mechanism остаётся реализационной деталью при соблюдении уже выбранного lifetime contract; snapshots pointer lifetime не продлевают.
- Предложение P13 — Exact kinds поддерживают +,-,*,/,%,unary minus и ==,~=,<,<=,>,>=; arithmetic и ordering только для одного exact kind, equality разных kinds false; tostring culture-independent round-trip. Power ^ и bitwise API не входят в начальный exact operator set, неподдержанные операции диагностируются без double fallback. Fixed views имеют #buffer, strict integer 1..N bounds checks, readonly propagation; отсутствующие/лишние descriptor options диагностируются при подготовке. Для dynamic enum достаточно explicit underlying metadata без named-members registry, numeric/flags semantics приняты ранее.

## Acceptance criteria

- AC1 — Существующие пять scalar kinds и совместимость API сохранены.
- AC2 — Поддержанные новые kinds проходят native/dynamic round-trip и negative cases без потери точности или частичной неверной записи.
- AC3 — Generated accessors сохраняют readonly и проверки boundary/lifetime.
- AC4 — Common reserved success path 0 B; Lua workloads имеют измеренный и явно принятый budget.
- AC5 — Каждый этап проверяется целевыми regression tests; end-to-end Static/Dynamic × Deterministic/Parallel и независимый review после integration.
- AC6 — Exact constructors/operators/comparisons/formatting выполняют принятый числовой контракт на boundaries и generated cases.
- AC7 — Nested/fixed views сохраняют 1..N indexing, readonly propagation, no boxed copies и reusable live-view semantics.
- AC8 — copy/new_snapshot/copy_to дают detached data, strict compatibility, стабильные buffer/nested identities и Simulation disposal guards.
- AC9 — Pointer host opt-in/type/domain/null/revocation checks не дают Lua доступ к raw address или dereference.
- AC10 — Recursive schemas canonical, frozen и валидируются до запуска; unknown/cyclic/overflowing layouts не обходят проверки.

## Open questions / blockers

None. Реализация, dependent branch и новый экспериментальный Lua budget ещё не утверждены; они включены в одно итоговое предложение плана.

## Resume point

Planning: source design revision 15. Принятые user contracts — D1–D16; обязательная техническая детализация и предлагаемый performance waiver ниже. Не начинать implementation без единого утверждения плана.

## Итоговый контракт для implementation plan

Этот раздел завершает ранние вопросы выше. Он сохраняет выбор пользователя D1–D15 и добавляет D16: пользователь 2026-11-11 принял +,-,*,/,%, unary minus, все сравнения, same-kind arithmetic/ordering, false для mixed-kind equality, round-trip tostring, без ^/bitwise в этом срезе. Неподдержанные операции не получают double fallback. Exact values в Lua имеют value semantics: сохранённое число не меняется при следующем getter/вычислении. Нельзя переиспользовать mutable numeric wrapper, перенаправляя сохранённое значение.

Технические следствия принятых контрактов, включаемые в итоговый план:

- Snapshot factory работает при активном callback своего pool, как остальные pool operations. Буфер можно сохранить и повторно использовать в следующих callbacks; создание буфера во время подготовки из несуществующего startup ctx не обещается. Для измерения warmed copy_to первоначальная factory и рост исключаются и измеряются отдельно. Snapshot data доступны в пределах своей Simulation, независимо от наличия исходной entity; live ECS permission gate не подменяется snapshot factory. Readonly CLR leaf/parent сохраняется и у snapshot; query readonly запрещает запись в ECS, но не превращает отделённую mutable data copy в доступ к ECS. Snapshot не позволяет implicit writeback.
- Буфер совместим по Simulation identity, component ID, CLR binding identity либо canonical dynamic schema version+layout. Похожие layouts разных компонентов не совместимы. copy_to проверяет несовместимость до изменения buffer, сохраняет identity буфера/nested views и не создаёт views на каждый вызов. new_snapshot создаёт default data. Snapshot другой Simulation отвергается даже при совпадении layouts.
- Fixed view поддерживает #buffer. Индексы только конечные целые 1..N, проверка до доступа. Строки/booleans/fractions, 0, N+1, NaN/Infinity отклоняются. Element kind: scalar или inline struct; struct может содержать fixed fields. Direct fixed-of-fixed declaration не входит в выбранный scalar/struct набор. Размер положительный, целочисленный, с checked layout-size arithmetic; cyclic declarations и unsupported managed references отклоняются при подготовке. Dynamic enum описывает underlying integer kind, без named-members/type registry: {kind="enum",underlying="u32"}. Native enum сохраняет CLR identity, getters/setters работают в принятой underlying numeric семантике.
- Inline recursive schemas канонизируются по ordinal field names; identity включает node kind, все children, длины buffers, underlying enum kind и host pointer type ID. Version и layout фиксируются в ModSet, schema reload/migrations не входят. Recursive input обходится с cycle validation и итеративной проверкой/подготовкой, чтобы malformed data не вызывали неконтролируемый stack overflow; arbitrary product-specific depth cap не вводится незаметно.
- Native nested field access генерируется до leaf, без boxed struct copy; readonly любого узла учитывается. Native fixed pointer/ref используется только внутри accessor, не сохраняется между вызовами; bounds и world/component lifetime checks остаются в Release. Native InlineArray и private/property discovery не добавляются автоматически.
- nint/nuint отображаются в Lua через signed/unsigned exact values, с дополнительной process-width проверкой на запись. Enum-backed i64/u64 используют соответствующие exact values. char остаётся numeric UTF-16 code unit; managed strings не становятся допустимыми ECS fields.
- Pointer token содержит unmanaged address/type/domain-generation metadata; common columns не содержат managed pointer-token references. Host регистрирует type/domain и отвечает за native lifetime; source pointer read не доказывает жизнь pointee. Domain revocation инвалидирует сохранённые tokens, в том числе внутри snapshots; Lua не получает raw address через tostring/constructors. Token допустим только в своей Simulation/домене и с host-разрешённой совместимостью типа. nil разрешён только pointer fields, не integer kinds. Host opt-in должен быть проверен до запуска модовой системы, а revoked-token assignment — до записи.

### Performance policy, предлагаемая к единому утверждению плана

Common prepared C# read/write/nested/indexed access и copy_to после reserve/warmup — 0 B, включая pointer validation и точную арифметику CLR. copy/new_snapshot и capacity growth создают память явно и измеряются отдельно; нельзя включать их в обещание no-GC. Lua остаётся экспериментальным, без production latency гарантии.

Для новых canonical Lua probes предлагается потолок **4096 + 1600*N*L + 544*N*O B/callback**, где N=0/1/16/256/4096 сущностей, L — число scalar leaves, копируемых read→write по одному разу на entity (fixed elements считаются отдельно), O — число exact arithmetic operations на entity. Это продолжение прежнего f32 экспериментального потолка 4096+1600*N для L=1,O=0; 544 B/op — предлагаемый потолок конкретного exact-add probe, измерившего 528 B/op. Это новый предлагаемый waiver, не уже полученное разрешение. Он действует только на описанные workloads без formatting/string constructors/snapshot allocation внутри loop; произвольная Lua программа таким ceiling не ограничена. Existing f32/typed gates не повышаются. Дополнительно direct MoonSharp matched workload отделяет VM стоимость от wrapper overhead; bytes/операцию, p50/p95/p99, Gen0 и memory peaks сохраняются как evidence. Если любой canonical workload не укладывается, сначала устранять overhead; не повышать потолок молча. Изменение waiver требует отдельного scoped решения пользователя.

### Состояние дизайна

Все in-scope вопросы получили конкретный контракт для единого reviewable плана. Ранее перечисленные Q1a–Q7 закрыты выбранными пользователем решениями либо техническими следствиями выше; branch-base разрешение и новый экспериментальный budget входят в единое окончательное утверждение плана и ещё не получены. Это не утверждение реализации.

Следующий шаг: составить /tmp/karpik-modding-types-execplan.md, revision 1, с source design revision 15 и acceptance AC1–AC10. После явного разрешения текущей ветки документы переносятся в plans/modding-types-design.md и plans/modding-types-execplan.md с исправлением relative links.

## Execution authorization — 2026-11-11

Пользователь ответил «Делай в этой ветке» на единое предложение плана, budget и dependent branch. Это утверждает реализацию и предложенный experimental budget, меняя branch choice: использовать текущую codex/modding-0.7, без дочерней ветки. Остальные design contracts revision 15 сохраняются.
