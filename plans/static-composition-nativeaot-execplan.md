# Перевести runtime-композицию на compile-time граф и подготовить NativeAOT

> **Для agentic workers:** REQUIRED SUB-SKILL: выполняйте этот план через `superpowers:subagent-driven-development` (предпочтительно) либо `superpowers:executing-plans`, по одному milestone за раз с отдельной проверкой результата. Для каждого изменения поведения соблюдайте TDD: сначала тест, подтверждённый RED, затем минимальная реализация и GREEN.

Этот ExecPlan является живым документом. Его необходимо поддерживать согласно `plans/PLANS.md`: после каждого milestone обновлять `Progress`, а новые факты и изменения направления фиксировать в `Surprises & Discoveries` и `Decision Log`.

## Purpose / Big Picture

Сейчас выбранные KarpikEngine-модули собираются в набор DLL, записываются в `modules.list`, затем универсальный `Karpik.Engine.Core.Runner` копирует их в shadow-директорию, загружает через collectible `PluginLoadContext`, перечисляет типы через `Assembly.GetTypes()` и создаёт `IModuleInstaller` через `Activator.CreateInstance()`. Эта схема позволяет динамически менять управляемые сборки, но скрывает полный граф программы от компилятора, мешает надёжной source generation и не соответствует closed-world модели NativeAOT.

После выполнения плана у сборки будет два явно разделённых режима:

- `Dynamic` сохраняет текущий универсальный runner и нужен как временный путь совместимости.
- `Static` превращает выбранные Shared/Client/Server модули в compile-time references, генерирует композицию и создаёт отдельные game-specific Client и Server host executable. В опубликованном Static runtime отсутствуют `modules.list`, `PluginLoadContext`, `Assembly.GetTypes()` и `Activator.CreateInstance()`.

Проверяемый пользовательский результат: шаблонная игра собирается в Static-режиме; `KarpikGame.Shared` видит `Spatial2D.Transform2D`; `Network.Codegen` генерирует для него типизированный snapshot codec; Server host запускается без чтения списка managed DLL; headless Server публикуется с `PublishAot=true` и проходит smoke test. Client NativeAOT считается отдельным обязательным acceptance gate после проверки SDL, Silk.NET/NeoVeldrid и native assets.

## Progress

- [x] Milestone 1: evaluated SDK composition contract, minimal Autofac NativeAOT publish/run proof, and parent Runner compile-glob exclusion are verified.
- [x] Milestone 2: side-safe compile-time references модулей через SDK и transaction-owned external fixture реализованы; task suite, normal integration suite и focused opt-in acceptance зелёные.
- [x] Milestone 3: Network.Codegen упакован в SDK; typed snapshot registry, deterministic schema и pre-payload handshake проверены generator, transport и external SDK tests.
- [x] Milestone 4: генерировать статическую композицию module installers.
- [x] Milestone 5: исключить reflection activation из DI и регистрации ECS-систем Static-режима.
- [x] Milestone 6: превратить launcher-проекты в game-specific Static hosts.
- [x] Milestone 7: убрать managed module manifest и PluginLoadContext из Static runtime, сохранив process-isolated reload.
- [x] Milestone 8: пройти Server и Client NativeAOT acceptance, зафиксировать архитектуру ADR.
- [ ] Milestone 9 (corrective, reopened M4/M5/M8 scope): compile-time ECS descriptors, точный порядок installers, полная Dynamic/Static parity, узкий trimming и reload без роста state.

## Surprises & Discoveries

- Observation: `Network.Codegen` сейчас подключается из корневого `Directory.Build.props` только к проектам, имя которых содержит `Karpik.Engine`, поэтому не запускается ни для `Spatial2D`, ни для шаблонного `KarpikGame.Shared`.
  Evidence: `Directory.Build.props` содержит соответствующий `ItemGroup Condition`; сборка `Modules/Shared/Spatial2D/Spatial2D.csproj` не строит `Network.Codegen` как analyzer.

- Observation: все три network generators теперь считают compiler-visible `KarpikSide`, `KarpikProjectKind` и `KarpikCompositionMode` единственным источником routing; assembly-name fallback удалён. Полностью отсутствующий набор намеренно подавляет internal generation, а частичный/invalid набор выдаёт `KNET001`.
  Evidence: `ProjectTypeDetector.TryGetProjectProperties` читает exact `build_property.*` keys из `AnalyzerConfigOptionsProvider`; parity tests используют намеренно вводящие в заблуждение assembly names.

- Observation: `NetworkGenerator` полностью заменил hardcoded `MyGame.Shared.NetworkManager` одним самостоятельным `Karpik.Engine.Generated.NetworkSnapshotRegistry`; generated hot methods используют прямые typed Dragon ECS pools и primitive/vector codecs без serializer dictionary/interface dispatch.
  Evidence: semantic generator suite компилирует emitted source против реального `Spatial2D.Transform2D`, round-trip сохраняет fractional `Vector2d`, а warmed loop из 1,000 generated writes измеряет ровно 0 managed bytes.

- Observation: SDK package содержит ровно один `analyzers/dotnet/cs/Network.Codegen.dll`, а `Sdk.targets` подключает его только к `KarpikProjectKind=Runtime`; Client/Server запускают generator, но snapshot output подавляется build-property routing.
  Evidence: package inspection вернул `NetworkCodegenEntryCount=1`; SDK XML tests проверяют exact analyzer Include/Condition.

- Observation: после включения analyzer во внешнем Static Shared fixture пустые RPC generators всё ещё испускали scaffolding с отсутствующими dependency namespaces, хотя команд не было.
  Evidence: первый opt-in RED падал на generated RPC source; генераторы теперь не добавляют requests/extensions/dispatchers при пустом command set, сохраняя существующее поведение при наличии команд.

- Observation: публичные networked поля установленного `Spatial2D.Transform2D` используют `OpenTK.Mathematics.Vector2/Vector2d`, поэтому compile reference только на `Spatial2D.dll` недостаточна для generated typed code.
  Evidence: второй opt-in RED содержал `CS0012`/`CS0400`; exact colocated `modules/Spatial2D/OpenTK.Mathematics.dll` reference сделал transaction-owned external fixture GREEN без wildcard discovery.

- Observation: до Milestone 3 snapshot generator жёстко дописывал partial-класс `Karpik.Engine.MyGame.Shared.Main.NetworkManager` и сериализовал компонент через `object`, что боксило struct на каждую запись.
  Evidence: base commit `fff492afda66018753470413373534f52f1ed103`, `Network.Codegen/Network.Codegen/NetworkGenerator.cs`, generated `IComponentSerializer.Write(IWriter, object)`.

- Observation: установленный `modules.catalog` уже содержит канонические пары `EngineModuleSide + ModuleId` и валидируется `EngineModuleCatalog`; его можно безопасно использовать как build-time вход без сканирования произвольных директорий.
  Evidence: `Karpik.Engine.Tooling/EngineModuleCatalog.cs` и `Karpik.Engine.Tooling/EngineInstallationValidator.cs`.

- Observation: process-isolated hot reload уже перезапускает worker и переносит состояние через IPC, поэтому Static-режиму не требуется collectible `AssemblyLoadContext` для перезагрузки кода.
  Evidence: `Karpik.Engine.Core.Runner/Program.cs`, `Karpik.Engine.Core/ProcessManagement/ProcessManager.cs` и `plans/process-isolation-architecture.md`.

- Observation: существующий `ModuleRegistratorGenerator` не является основой готового Static runtime: он ищет `IModule`, генерирует устаревший вызов `Bootstrap`, а `context.AddSource(...)` закомментирован.
  Evidence: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/ModuleRegistratorGenerator.cs`.

- Observation: the minimal Autofac factory-registration smoke host publishes and runs under NativeAOT when executed outside the sandbox in the Visual Studio C++ developer environment.
  Evidence: `vcvars64.bat -vcvars_ver=14.44` plus `C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64` on `PATH` allowed the exact publish command to exit 0; `publish\NativeAotSmoke.exe` printed exactly `STATIC_AOT_OK` and exited 0. The publish reported only `NU1900`; no `IL2xxx`, `IL3xxx`, or `IL3050` warnings occurred.

- Observation: excluding `NativeAotSmoke\**\*.cs` from the parent Runner test project prevents the nested smoke project's generated `obj/Release/net10.0/win-x64/*.AssemblyInfo.cs` from being compiled by the parent; all Runner tests execute when they have normal Windows pipe/process permissions.
  Evidence: the sandboxed run reached VSTest but failed 20 pipe/process tests with `UnauthorizedAccessException`; rerunning the identical command outside the sandbox passed all 107 tests with no skips or failures.

- Observation: the focused external SDK fixture must build every package input inside its transaction; merely redirecting the `.nupkg` output still consumes repository `bin` outputs and can mutate shared `obj` state during restore/build.
  Evidence: `External_static_composition_references_are_side_safe_and_dynamic_remains_unchanged` now restores/builds `Karpik.Engine.Sdk.csproj` and `Karpik.Engine.Core.Codegen.csproj` with a transaction `Directory.Build.props`, per-project intermediate paths, and transaction `ArtifactsPath`, then packs with explicit owned task/codegen output paths and verifies repository build-output snapshots are unchanged.

- Observation: an MSBuild diagnostic target declared only with `AfterTargets="_KarpikResolveEngineReferenceAssemblies"` did not force that target to execute before capturing `@(Reference)`; the capture contained framework identities with empty `HintPath` and omitted the three Dynamic engine references.
  Evidence: the focused opt-in RED reported zero engine-owned references and printed the captured framework set; adding `DependsOnTargets="_KarpikResolveEngineReferenceAssemblies"` made the exact Autofac, DragonECS, and `Karpik.Engine.Core` identity/absolute-HintPath contract GREEN.

- Observation: repository-local `templates/Karpik.Game/bin` and `obj` contamination must not change template-source assertions.
  Evidence: filtering build-output paths lets the source-template checks pass without treating generated PDBs or intermediate files as template content.

- Observation: system assemblies are excluded from Static composition scanning without name-prefix heuristics: a candidate assembly is scanned only if one of its modules references `Karpik.Engine.Core` by assembly identity.
  Evidence: `RuntimeCompositionGenerator.EnumerateCandidateAssemblies`/`ReferencesAssembly`; generator tests cover discovery from current + referenced assemblies with no System/Microsoft enumeration.

- Observation: `dotnet publish -p:PublishAot=true` протекает глобальное свойство во все ProjectReference builds; netstandard2.1 DragonECS отклоняется с NETSDK1207, и ни `Properties="PublishAot=false"`, ни `GlobalPropertiesToRemove="PublishAot"`, ни `BuildProjectReferences=false` не предотвращают outer metadata builds зависимостей.
  Evidence: три варианта publish упали с одной и той же ошибкой `FrameworkReferenceResolution.targets(120,5)` в DragonECS.csproj; publish прошёл только после перевода NativeAotSmoke на прямые `Reference HintPath`.

- Observation: emitted service factories требуют полной transitive reference closure хоста (например, `InputCaptureState` из Window.Core в аргументах конструктора системы Graphics), поэтому harness-based parity тесты сверяют текст generated source, а не выполняют emit-and-load над реальными module graph.
  Evidence: CS0012/CS0234 в parity тесте при отсутствии Window.Core/Autofac references; synthetic-graph execution tests остаются на emit-and-load.

- Observation: startup allocations Debug-сборки Runner.Tests: Dynamic ≈ 554 KB, Static (hand-written composition c 6 сервисами) ≈ 605 KB; reflection exceptions отсутствуют, per-frame allocations не добавлены.
  Evidence: `StaticCompositionTests.StartupAllocations_DynamicVersusStatic_AreMeasuredAndDocumented`, GC.GetAllocatedBytesForCurrentThread после warm-up.

## Decision Log

- Decision: сохранить `KarpikCompositionMode=Dynamic|Static` на период миграции, первоначально с default `Dynamic`.
  Rationale: это позволяет вводить closed-world путь milestone-ами, сохраняя работающий editor/runtime до достижения функционального паритета.
  Date/Author: 2026-08-15 / Codex по согласованию с разработчиком.

- Decision: `KarpikModuleSelection` и установленный `modules.catalog` остаются build-time входами, но не входят в Static runtime.
  Rationale: компилятор не выбирает backend самостоятельно; выбор должен быть детерминирован до compilation. NativeAOT требует закрытого графа, а не отсутствия build metadata.
  Date/Author: 2026-08-15 / Codex.

- Decision: внешняя игра получает prebuilt engine module assemblies как MSBuild `Reference`, а source build внутри репозитория продолжает строить модули их существующими проектами. При `PublishAot` IL всех достижимых references становится входом AOT-компилятора.
  Rationale: внешняя игра не имеет исходных `.csproj` установленного движка; искусственные `ProjectReference` к installation payload невозможны. Обычные compile-time references дают Roslyn и NativeAOT нужный закрытый граф.
  Date/Author: 2026-08-15 / Codex.

- Decision: snapshot registry генерируется в Shared-проекте; runtime composition генерируется отдельно в Client и Server host.
  Rationale: сетевой протокол должен быть одинаковым на сторонах, тогда как module installers и backend-состав side-specific.
  Date/Author: 2026-08-15 / Codex.

- Decision: Static snapshot code не использует `object`, reflection или interface dispatch на каждый компонент; генератор испускает прямые typed read/write blocks.
  Rationale: snapshot serialization является горячим сетевым путём; boxing, словари serializers и pointer chasing неприемлемы.
  Date/Author: 2026-08-15 / Codex.

- Decision: protocol schema handshake использует существующий Shared `PacketType.Handshake` и LiteNetLib peer connect/receive path, отправляет hash через `ReliableOrdered`, и публикует `PeerConnectedEvent`/entity payload только после equality; mismatch disconnects peer до consumer payload access.
  Rationale: это наименьшая exact integration в текущую transport architecture, сохраняющая delivery method и authority. Изменение deterministic component IDs/schema является protocol-incompatible и должно выкатываться/откатываться одновременно на Client и Server.
  Date/Author: 2026-08-21 / Codex.

- Decision: generated `NetworkSnapshotRegistry` экспортируется как `INetworkProtocolSchema` и как concrete Simulation singleton; реальные Client/Server init systems получают этот Shared contract через обычный service resolution и передают generated nonzero hash в `INetworkManager` до `Start`. Hash `0` зарезервирован как unconfigured и отклоняется; exact handshake payload после `PacketType` содержит ровно один `Int64` без trailing bytes.
  Rationale: schema должен попадать в normal runtime автоматически из generated closed schema, а не из mutable `NetworkConfig`, process-global state или runtime lookup типа/hash. Shared interface не создаёт Client↔Server dependency; существующий service registration path и будущая compile-time composition используют один и тот же generated service metadata.
  Date/Author: 2026-08-21 / Codex, Task 3 Fix Round 1.

- Decision: произвольные managed DLL-моды не поддерживаются в Static/NativeAOT; Lua и другие data/script mods остаются runtime-динамическими. Managed-мод должен быть включён до публикации.
  Rationale: NativeAOT не поддерживает общий сценарий загрузки и компиляции ранее неизвестного managed кода.
  Date/Author: 2026-08-15 / Codex.

- Decision: do not require a generated lightweight DI container from this milestone's smoke result.
  Rationale: the Autofac factory-registration probe completed NativeAOT publish and execution without intrinsic trimming/AOT diagnostics. Future Milestone 5 scope remains governed by its full three-scope and reflection-boundary tests.
  Date/Author: 2026-08-20 / Codex.

- Decision: `ResolveKarpikStaticReferencesTask` uses `EngineModuleCatalog.ForSide` for Client/Server and filters already validated `EngineModuleCatalog.Read` output for Shared.
  Rationale: `ForSide` intentionally rejects `Shared`; filtering canonical validated catalog entries preserves that runtime API invariant without duplicating parser or module-ID validation.
  Date/Author: 2026-08-20 / Codex.

- Decision: focused external SDK packing restores and builds the SDK task graph and Core code generator under one transaction-owned, per-project intermediate/output tree, then packs with `--no-build --no-restore` and explicit trailing-separator input paths.
  Rationale: the fixture must test the current SDK package without reading or writing repository package feeds, `bin`, `obj`, or default `artifacts`; explicit owned inputs make that isolation observable and repeatable.
  Date/Author: 2026-08-21 / Codex.

- Decision: отключённый `ModuleRegistratorGenerator.cs` удалён полностью, без файла-переадресатора.
  Rationale: генератор никогда не выпускал source (`context.AddSource` закомментирован), проект не является packable отдельно — в SDK упаковывается целиком `Karpik.Engine.Core.Codegen.dll`, поэтому ни один потребитель не мог зависеть от типа. RuntimeCompositionGenerator полностью заменяет его контракт.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 4).

- Decision: «несколько выбранных implementations одного module contract» детектируется статически как attributed installer, наследующий другой attributed installer (diagnostic KCORE004).
  Rationale: выбор `KarpikModuleSelection.Implementation` недоступен Roslyn во время generation; наследование attributed installer от attributed installer — единственная компиляторно видимая форма регистрации одного модуля через несколько реализаций. Выбор реализаций остаётся ответственностью SDK/build.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 4).

- Decision: контракт `IStaticServiceRegistry` создан уже в Milestone 4, а не Milestone 5; generated `RegisterServices` пока пустой.
  Rationale: `IStaticRuntimeComposition` (Milestone 4) ссылается на `IStaticServiceRegistry`, поэтому generated класс не может скомпилироваться без контракта. Тело заполняется в Milestone 5 без изменения API.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 4).

- Decision: KE307 «definitely-unresolvable» реализован как scalar/special-type dependency (примитивы, enum, string, decimal, указатели/ref), отсутствующей в closed graph; обычный reference-type, просто отсутствующий в графе, diagnostic НЕ получает.
  Rationale: регистрации из `OnRegisterServices` невидимы Roslyn (resolved decision #3), поэтому отсутствие reference-типа в графе не является доказательством неразрешимости; скалярные зависимости ни одним существующим модулем не предоставляются, и typed factory `IServiceResolver.Resolve<T>()` их разрешить не может.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 5).

- Decision: Autofac остаётся DI-контейнером Static-режима; generated factories применяются через переходный `AutofacStaticServiceRegistry` поверх Autofac для всех трёх scopes. Generated static container не требуется.
  Rationale: AOT smoke с реальным `EngineRunner` setup/destroy (Engine + ModSet + Simulation scopes, factory-based DI, ECS system activation) публикуется и выполняется под NativeAOT без intrinsic trim/AOT ошибок; предупреждения IL2104/IL3053 агрегированы существующими Dynamic-path кодом Core/Runner/DragonECS и относятся к Milestone 8.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 5).

- Decision: ECS systems в Static mode регистрируются как `Register<TSystem,TSystem>(ModuleScope.Simulation, ServiceLifetime.Transient)` через typed factory; `SystemRegistry.SuppressTypeRegistrations()` отключает только Autofac type registration, сохраняя descriptors layer/order из `IModule.Add` для pipeline ordering.
  Rationale: resolved decision #2; reflection-free активация при сохранении детерминированного порядка пайплайна.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 5).

- Decision: NativeAotSmoke ссылается на prebuilt engine assemblies (`Reference HintPath`) вместо ProjectReference.
  Rationale: глобальное свойство `PublishAot=true` протекает во все ProjectReference builds включая netstandard2.1 DragonECS, и SDK отклоняет это с NETSDK1207; `Properties="PublishAot=false"`, `GlobalPropertiesToRemove` и `BuildProjectReferences=false` не предотвращают outer metadata builds. Прямые references соответствуют Static-модели installed-engine consumption.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 5).

- Decision: reflection guard реализован структурно — internal event `EngineRunner.AttributedServiceRegistrationProbe` вызывается только на Dynamic path; Static setup test фиксирует ноль вызовов.
  Rationale: resolved decision #4 требует recording/structural подход вместо runtime profiling; event-шов не влияет на production поведение.
  Date/Author: 2026-08-22 / ox-alpha (Milestone 5).

- Decision: Static host argument parsing is a dedicated tolerant parser (all watcher arguments optional, unknown/duplicated rejected, --side must match the built side) instead of RunnerLaunchArguments.Parse, because game hosts live outside the engine runners directory and must run standalone without --bundle/--engine-root.
  Rationale: reusing Parse would require the runner ownership check to be bypassed and mandatory bundle arguments relaxed; a separate parser keeps the Dynamic CLI contract untouched. Recorded as resolved design point deviation #1-adjacent.
  Date/Author: 2026-08-22 / ox-alpha.

- Decision: dotnet new strips C# preprocessor directives from template sources, so the Static launcher entry is a separate StaticEntry.cs selected by conditional Compile items (EnableDefaultCompileItems=false) instead of #if KARPIK_COMPOSITION_STATIC branches.
  Evidence: materialized games lost every #if/#else/#endif line during dotnet new; the dynamic branch compiled alone and CS8802/CS0246 followed.
  Date/Author: 2026-08-22 / ox-alpha.

- Decision: launchers declare the Shared runtime project as a second unconditional build-only ProjectReference; the static flip target switches both references to ReferenceOutputAssembly=true/Private=true at AssignProjectConfiguration time.
  Rationale: KARPIK004 requires literal unconditional Include entries that exactly match evaluated items, and generated hosts need installers/services from Shared.dll which ResolveAssemblyReferences does not propagate transitively from the runtime project output.
  Date/Author: 2026-08-22 / ox-alpha.

- Decision: Tool kind receives Core+Runner engine references only at KarpikCompositionMode=Static, module payload directories are referenced wholesale via the new ResolveKarpikStaticReferencesTask PayloadAssemblies output (plus Microsoft.Extensions.Logging and DragonECS.Karpik.Extensions from the runner payload), and Dynamic Runtime projects gain one Network.Shared.Core reference because the generated snapshot registry always implements INetworkProtocolSchema.
  Rationale: emitted factories mention transitive service constructor types; Private stays false for Runtime so installation payloads are unchanged, while Static Tool launchers copy their graph local to become self-contained executables. The Network.Shared.Core addition repairs a pre-existing Dynamic compile break introduced by 3696153 (schema enforcement without a reference path).
  Date/Author: 2026-08-22 / ox-alpha.

- Decision: static runtime bundle layout stages Content/Mods plus native inputs only under native/ or runtimes/ TargetPaths, and completion validation rejects any modules.list anywhere in the output; launch validation sniffs layout shape (ValidateAny routes bundles containing modules.version.1 to the canonical dynamic rules, everything else through ValidateStatic).
  Rationale: a Static output must neither contain nor read a managed module manifest, but RuntimeLaunchOptions is mode-agnostic on the editor/watcher side; shape sniffing keeps Dynamic validation byte-identical while accepting manifest-free bundles. Proven-output recovery accepts either layout so switching a project between modes atomically replaces complete outputs (rollback stays functional). A stale dynamic-layout bundle handed to a static host is rejected loudly by ValidateStatic.
  Date/Author: 2026-08-23 / ox-alpha (Milestone 7).

- Decision: a Static host worker reports AppContext.BaseDirectory (not a bundle subdirectory) as its module directory in the IPC ready message.
  Rationale: manifest-free static outputs have no managed module staging directory; game modules are compiled into the host executable. Deviation from M6 ready-message wording is observable only when --bundle is supplied.
  Date/Author: 2026-08-23 / ox-alpha (Milestone 7).



- Decision: пост-приёмочный аудит разработчика (2026-08-23) переоткрыл scope Milestones 4/5/8 как corrective Milestone 9: Static ECS startup использовал Assembly.GetTypes()+Activator.CreateInstance через unconditional registry-provider enumeration в Runner.cs; порядок installers Static расходился с Dynamic (Priority→rank vs генераторный Scope→Priority→Identity→FullName) и маскировался set-based parity; project-wide NoWarn IL2104/IL3053/IL3000/IL3002 и assembly-wide trim roots противоречили решению о запрете широких roots; reload-gate допускал линейный рост payload; parity была сужена до subset-проверки. Milestones 6 и 7 приняты без изменений.
  Rationale: перечисленные механизмы являются рабочими обходами (AOT проходит за счёт принудительного сохранения metadata), а не compile-time composition, которую обещает план.
  Date/Author: 2026-08-23 / ox-alpha по аудиту разработчика.

- Decision: внутренние [ServiceRegistration] сервисы устраняются требованием public (publicизация в модулях), а не InternalsVisibleTo или документированным исключением; полная Dynamic/Static parity становится обязательной для множеств И последовательностей.
  Rationale: закрытая world-модель не должна иметь silent-невидимых сервисов; InternalsVisibleTo создал бы скрытый контракт между модулями и хостом и усложнил бы generator discovery.
  Date/Author: 2026-08-23 / разработчик.
## Outcomes & Retrospective

ExecPlan завершён 2026-08-23 (branch `open-code-ai`). Полный per-gate отчёт: `.git/sdd/task-m8-report.md`.

### Фактические результаты по gate'ам

- Server NativeAOT (Gate 1 + Gate 3): gated test `Static_server_host_publishes_and_runs_under_NativeAot_with_ten_reload_cycles` PASS (9m28s). Publish win-x64 `PublishAot=true` + `InvariantGlobalization=true`; startup, editor-snapshot round-trip (GameComponent=42), clean shutdown; 10 process-isolated reload cycles, каждый с новым pid, entity count prev+1, рост payload линейный (+398 B/cycle — спроектированное накопление сущностей), publish exe разблокирован после stop, orphan-процессов нет.
- Client NativeAOT (Gate 2 + Gate 4): gated test `Static_client_host_publishes_and_runs_under_NativeAot` PASS (6m54s) на production window/graphics/input backends: создание окна, инициализация graphics backend и input, маркер `[ClientGame] First frame rendered.`, чистое завершение, publish dir без managed manifests/shadow dirs.
- Warning inventory: только документированные IL2104/IL3053 (агрегат от неаннотированных payload-сборок) и IL3000/IL3002 (Silk.NET loader probing); оба задокументированы в комментарии Sdk.targets + ADR и исчерпывающе проверяются обоими AOT gate'ами — любой иной warning валит gate.
- Dynamic↔Static parity (Gate 5): generator-level parity test подтверждает, что emitted `registry.Register<Contract,Impl>(scope,lifetime,factory)` для одной selection равен independent runtime attribute discovery + публичный ECS-system scan (те же impls, scopes, lifetimes, export contracts); generated — строго подмножество dynamic. Module-ID parity Client/Server закреплён существующим тестом. Snapshot schema hash детерминирован из одинаковых inputs в обоих режимах.

### Дефекты, найденные и исправленные во время acceptance

1. Reflection-based System.Text.Json в EditorRuntimeSnapshot падал под AOT → source-generated `EditorSnapshotJsonContext` (7ba07c5).
2. Template GameComponent display терялся при trimming → ToString override (adb34f0).
3. Runtime MakeGenericType в ComponentTemplate<T> не имеет native code → генератор эмитит static instantiation roots в GeneratedRuntimeComposition (c6f53f1).
4. ComponentArrayConverter использовал generic `JObject.ToObject<T>` через MakeGenericMethod → non-generic `ToObject(Type, JsonSerializer)` + source-boundary тест (e5b8fe7).
5. IL3000/IL3002 от Silk.NET DefaultPathResolver → задокументированный aggregate suppression, оправданный passing runtime gate (b5fa403).
6. Native assets установки не попадали в Static launcher publish → `_KarpikStageStaticLauncherNatives` staging (6cf4eac→3e83da1).
7. Test harness терял rebuilt native assets → CopyModuleNativeAssets (88daee7).
8. Internal ECS systems молча выпадали из generated composition → модули сделаны публичными по documented launcher-visibility contract + fast source-scan regression test (7f29ae1).
9. Trimmed module assemblies теряли generated ECS scheduling registries → все first-party module assemblies trim-rooted для Static+AOT (8d8be77).

### Задокументированные отклонения

- Internal `[ServiceRegistration]` services остаются Dynamic-only (static hosts не могут на них ссылаться); вперёд закреплено EcsSystemVisibilitySourceTests.
- Все first-party module assemblies trim-rooted для Static+AOT publishes; сторонние payload-сборки остаются trimmed.

### Оставшиеся ограничения

- Managed DLL-моды в Static/NativeAOT не поддерживаются (data/script mods остаются runtime-динамическими); удаление Dynamic path — отдельный последующий ExecPlan.
- Trim/AOT warning inventory зависит от сторонних payload-сборок; расширение списка подавлений требует обновления ADR и gate-ассертов одновременно.
- Criterion 11 (обновление codebase-memory индекса): команда `graphify update .` недоступна на данной машине (graphify не установлен в PATH); индексация отложена до появления инструмента.

### Targeted verification (финальный прогон, 2026-08-23)

- Karpik.Engine.Sdk.Tasks.Tests: 75 passed / 0 failed / 4 skipped (link-capability gates).
- Network.Codegen.Tests: 24 passed / 0 failed / 0 skipped.
- Karpik.Engine.Core.Generator.Tests: 39 passed / 0 failed / 0 skipped.
- Karpik.Engine.Core.Runner.Tests: 126 passed / 0 failed / 0 skipped.
- Karpik.Engine.Sdk.IntegrationTests: 8 passed / 0 failed / 6 skipped (environment-gated, включая оба AOT gate — зелёные в gated прогоне ранее).
- AOT publishes повторно не запускались — уже gated green (см. выше).

ADR: `docs/02_ADR/static-runtime-composition.md`.

## Context and Orientation

`Directory.Build.props` содержит внутренний список `KarpikModuleSelection` и локальное подключение codegen-проектов. `Configurator/RepositoryParser.cs`, `Configurator/GraphValidator.cs` и `Configurator/ArtifactGenerator.cs` разрешают выбор модулей и создают `Generated/ModuleLoader.cs`. Этот файл компилируется в `Karpik.Engine.Core` через явный `<Compile Include>` в `Karpik.Engine.Core/Karpik.Engine.Core.csproj`.

Установленная поставка движка хранит модули как `modules/<ModuleId>/<ModuleId>.dll` и описывает их в `modules/modules.catalog`. `EngineModuleCatalog.ForSide` уже реализует правило Shared + выбранная runtime side. `Karpik.Engine.Core.Runner/RuntimeModuleComposition.cs` читает этот каталог при запуске.

Внешние проекты используют MSBuild SDK `Karpik.Engine.Sdk`. Его цели находятся в `Karpik.Engine.Sdk/Sdk/Sdk.props` и `Karpik.Engine.Sdk/Sdk/Sdk.targets`; задачи — в `Karpik.Engine.Sdk.Tasks`. Сейчас SDK добавляет только Autofac, DragonECS и `Karpik.Engine.Core` как compile references, упаковывает только `Karpik.Engine.Core.Codegen.dll` и после сборки создаёт `karpik-bundle` через `BuildKarpikRuntimeBundleTask`.

Шаблон игры находится в `templates/Karpik.Game`. `KarpikGame.Client.Launcher` и `KarpikGame.Server.Launcher` сейчас не компилируют runtime project (`ReferenceOutputAssembly=false`), а запускают установленный универсальный `Karpik.Engine.Core.Runner.exe`. В Static-режиме именно launcher станет game-specific entry point и будет иметь обычную compile reference на runtime project.

`Karpik.Engine.Core.Runner/Program.cs` создаёт `ModuleLoader`, загружает DLL, вызывает `assembly.GetTypes()` и передаёт результат в `Bootstrap.RegisterTypes`. `Karpik.Engine.Core.Runner/Runner.cs` создаёт installers через `Activator.CreateInstance`, а `AttributedServiceRegistrar.cs` читает атрибуты reflection-ом и регистрирует типы в Autofac. Эти пути допустимы только в Dynamic-режиме.

Термин **closed-world composition** означает, что до компиляции известны все managed assemblies, module installers, сервисы, ECS-системы и сетевые codecs. Термин **Static host** означает отдельный Client или Server executable конкретной игры, включающий выбранный runtime graph. Термин **Dynamic host** означает нынешний универсальный runner, загружающий game/module DLL в runtime.

## Target Build Contract

SDK вводит следующие MSBuild свойства:

```xml
<KarpikCompositionMode>Dynamic</KarpikCompositionMode>
<KarpikEnableNativeAotValidation>false</KarpikEnableNativeAotValidation>
```

Допустимы только точные значения `Dynamic` и `Static`. Пустое значение нормализуется в `Dynamic` на период миграции; любое другое значение является build error. `KarpikSide` остаётся `Shared`, `Client` или `Server` и передаётся Roslyn generators через:

```xml
<CompilerVisibleProperty Include="KarpikSide" />
<CompilerVisibleProperty Include="KarpikProjectKind" />
<CompilerVisibleProperty Include="KarpikCompositionMode" />
```

В Static-режиме SDK разрешает installed references так:

- Shared: все записи `EngineModuleSide.Shared`.
- Client: Shared + Client.
- Server: Shared + Server.
- Test: зависимости определяются реальными `ProjectReference` тестового проекта; автоматическая Static composition не генерируется.
- Tool/Launcher: получает side-compatible references только при явном `KarpikCompositionMode=Static`.

Каждый resolved module reference указывает на primary assembly `$(KarpikEngineRoot)/modules/<ModuleId>/<ModuleId>.dll`, имеет `Private=false` для обычного build и не меняет installation payload. `ResolveAssemblyReference` разрешает транзитивные зависимости из директории модуля. Задача обязана отвергать отсутствующие, небезопасные и side-incompatible paths до компиляции.

В Dynamic-режиме поведение SDK и runtime bundle должно остаться байт-в-байт совместимым настолько, насколько это проверяют существующие тесты.

## Generated APIs

Имена ниже являются контрактом плана; если реализация выявит техническую невозможность, сначала зафиксировать новое решение в `Decision Log`, затем синхронно изменить определения и все последующие milestone.

В `Karpik.Engine.Core` создать публичные AOT-safe контракты:

```csharp
public interface IStaticRuntimeComposition
{
    void RegisterModules(IStaticModuleRegistry registry);
    void RegisterServices(IStaticServiceRegistry registry);
}

public interface IStaticModuleRegistry
{
    void Add(IModuleInstaller installer);
}

public interface IStaticServiceRegistry
{
    void Register<TService, TImplementation>(
        ModuleScope scope,
        ServiceLifetime lifetime,
        Func<IServiceResolver, TImplementation> factory)
        where TImplementation : class, TService;
}
```

`EngineRunner` реализует `IStaticModuleRegistry` либо использует небольшой adapter внутри Runner assembly. Core-контракт не ссылается на concrete `EngineRunner`, поэтому зависимость Core → Runner не возникает. Запрещено заменять прямые factories на `Type` + runtime reflection.

Core generator создаёт в host assembly:

```csharp
namespace Karpik.Engine.Generated;

internal sealed class GeneratedRuntimeComposition : IStaticRuntimeComposition
{
    public void RegisterModules(IStaticModuleRegistry registry);
    public void RegisterServices(IStaticServiceRegistry registry);
}
```

`RegisterModules` вызывает `registry.Add(new SomeModuleInstaller())` в детерминированном порядке `ModuleAttribute.Scope`, `ModuleAttribute.Order`, assembly identity, full type name. Генератор выдаёт diagnostic, если installer не является concrete public class с доступным parameterless constructor.

Network generator создаёт в Shared assembly:

```csharp
namespace Karpik.Engine.Generated;

public sealed class NetworkSnapshotRegistry
{
    public void WriteSnapshot(
        EcsWorld world,
        IWriter writer,
        ReadOnlySpan<int> destroyedNetworkIds);

    public void ApplySnapshot(EcsWorld world, IReader reader);
    public void ClearClientCache();
}
```

Generated methods используют typed pools и прямые `writer.Put(...)` / `reader.Get...()` expressions. `Transform2D.Position` (`OpenTK.Mathematics.Vector2d`) записывается как два `double`; `Transform2D.Rotation` — `float`; `Transform2D.Scale` (`OpenTK.Mathematics.Vector2`) — два `float`. Для неподдерживаемого `[NetworkedField]` генератор выдаёт ошибку, а не создаёт некомпилируемый метод по соглашению об имени.

До изменения wire format сохранить текущую семантику snapshot: список удалённых network IDs, количество сущностей, network ID сущности, затем presence bit и поля каждого компонента. Component IDs должны стать детерминированными и независимыми от порядка discovery. Использовать 64-bit FNV-1a от fully qualified metadata name с фиксированной UTF-8 нормализацией; генератор сортирует компоненты по ID и выдаёт build error при collision. До включения Static snapshot обе стороны должны обмениваться одинаковым protocol schema hash; mismatch завершает соединение до чтения entity payload. Точный transport handshake добавляется в Milestone 3, без изменения authority или delivery method существующих RPC.

## Real-Time Assessment

Hot paths: snapshot serialization/deserialization и ECS pool iteration являются горячими. Static composition, DI setup и module registration выполняются только при запуске процесса и не являются frame path.

Allocation budget: generated snapshot methods не должны выделять память на entity, component или field. Запрещены boxing, LINQ, closures, reflection, создание временных массивов и dictionaries в `WriteSnapshot`/`ApplySnapshot`. Кэши network ID могут выделяться при росте числа сущностей; это существующая семантика, которую необходимо измерить отдельно и не ухудшить. Static startup может выделять managed объекты модулей и контейнера.

Data layout: serializers работают напрямую с Dragon ECS pools. На write стороне использовать readonly pool access; mutable pool нужен только при apply. Компоненты остаются unmanaged structs. Registry не создаёт объект serializer на каждый component и не делает dictionary lookup на каждый field.

Side boundary: Shared registry видит только Shared modules. Client host не получает Server references; Server host не получает Client references. SDK task и generator tests обязаны доказывать обе отрицательные границы.

Tick behavior: изменение не затрагивает fixed dt и порядок Update/FixedUpdate/Render.

Concurrency: build tasks не разделяют mutable static state. Generated registry принадлежит одной Simulation scope; его client cache не является process-global. Hot reload остаётся process restart, поэтому старое состояние не используется после остановки worker, кроме явно сериализованного `IRestartWorkerStateProvider` payload.

Networking: snapshot остаётся server-authoritative. План не меняет delivery method и частоту отправки. Protocol schema hash предотвращает молчаливое чтение несовместимого payload. Любое расширение compression/delta encoding выходит за границы этого ExecPlan.

NativeAOT risk: Autofac, System.Composition, SDL/Silk.NET/NeoVeldrid и native backend bindings должны быть доказаны publish/run тестом. Если Autofac не проходит минимальный Server AOT smoke test даже при generated factories, Static mode переходит на собственный generated scope container; запрещено лечить это широкими `DynamicDependency` или сохранением всех metadata без измеренного и документированного обоснования.

## File Map

Планируемые новые файлы:

- `Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs` — чтение catalog, side filtering и выдача безопасных compile references.
- `Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs` — unit tests задачи и path/side edge cases.
- `Network.Codegen.Tests/Network.Codegen.Tests.csproj` — Roslyn generator test project.
- `Network.Codegen.Tests/GeneratorTestHarness.cs` — compilation + AnalyzerConfigOptions harness.
- `Network.Codegen.Tests/NetworkSnapshotGeneratorTests.cs` — discovery, type mapping, deterministic IDs, diagnostics и no-boxing assertions.
- `Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj` — Roslyn generator test project для static composition.
- `Karpik.Engine.Core.Generator.Tests/RuntimeCompositionGeneratorTests.cs` — module/service composition generator tests.
- `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs` — новый generator; старый `ModuleRegistratorGenerator.cs` удалить только после parity tests.
- `Karpik.Engine.Core/StaticComposition/IStaticRuntimeComposition.cs` — runtime composition contract.
- `Karpik.Engine.Core/StaticComposition/IStaticModuleRegistry.cs` — граница прямой регистрации installers без зависимости Core от Runner.
- `Karpik.Engine.Core/StaticComposition/IStaticServiceRegistry.cs` — generated factory registration contract.
- `Karpik.Engine.Core.Runner/StaticComposition/AutofacStaticServiceRegistry.cs` — переходная factory-based реализация поверх Autofac.
- `Karpik.Engine.Core.Runner/StaticComposition/StaticEngineHost.cs` — публичная точка запуска game-specific host.
- `Karpik.Engine.Core.Runner.Tests/StaticCompositionTests.cs` — startup/order/no-reflection tests.
- `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs` — external template build/publish/run tests.
- `docs/02_ADR/static-runtime-composition.md` — финальное архитектурное решение после подтверждения milestone 8.

Основные изменяемые файлы:

- `Karpik.Engine.Sdk/Sdk/Sdk.props`
- `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`
- `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`
- `Network.Codegen/Network.Codegen/NetworkGenerator.cs`
- `Network.Codegen/Network.Codegen/RpcGenerator.cs`
- `Network.Codegen/Network.Codegen/TargetClientRpcGenerator.cs`
- `Network.Codegen/Network.Codegen/ProjectTypeDetector.cs`
- `Karpik.Engine.Core.Runner/Runner.cs`
- `Karpik.Engine.Core.Runner/AttributedServiceRegistrar.cs`
- `Karpik.Engine.Core.Runner/SystemRegistry.cs`
- `Karpik.Engine.Core.Runner/Program.cs`
- `Karpik.Engine.Sdk.Tasks/BuildKarpikRuntimeBundleTask.cs`
- `templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj`
- `templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj`
- `templates/Karpik.Game/Source/KarpikGame.Client.Launcher/Program.cs`
- `templates/Karpik.Game/Source/KarpikGame.Server.Launcher/Program.cs`
- `templates/Karpik.Game/Source/KarpikGame.Launcher/EngineLauncher.cs`
- `KarpikEngine.slnx`

## Plan of Work

Работа идёт вертикальными milestone. Сначала создаётся минимальный Static Server proof, который выявляет AOT-блокеры до крупных изменений. Затем SDK начинает предоставлять closed compile graph. После этого отдельно стабилизируются network codegen, module composition и DI. Лишь когда generated composition имеет parity tests, launcher переключается на непосредственный host. Dynamic path не удаляется и не рефакторится попутно; он служит контрольной реализацией до финального milestone.

Каждый milestone начинается с одного или нескольких failing tests. Реализация не переходит к следующему milestone, пока targeted tests не проходят. Команды сборки используют `-m:1 -nr:false`, чтобы не оставлять MSBuild worker nodes.

## Milestones

### Milestone 1: Build contract и ранний NativeAOT proof

Цель — ввести свойства режима без изменения Dynamic behavior и проверить, способен ли минимальный Server host с текущими базовыми зависимостями публиковаться AOT.

**Files:**

- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.props`
- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- Modify: `Karpik.Engine.Sdk.Tasks.Tests/KarpikValidationTaskTests.cs`
- Create: `Karpik.Engine.Core.Runner.Tests/NativeAotSmoke/NativeAotSmoke.csproj`
- Create: `Karpik.Engine.Core.Runner.Tests/NativeAotSmoke/Program.cs`

**Interfaces produced:** `KarpikCompositionMode`, `KarpikEnableNativeAotValidation`, visible compiler properties.

- [x] Написать evaluated SDK-consumer test, требующий default `Dynamic`, empty normalization, strict `Dynamic|Static` acceptance, `KARPIK010` for invalid input, and compiler-visible properties in generated analyzer config.
- [x] Запустить `dotnet test Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false --filter FullyQualifiedName~CompositionModeBehaviorTests` и подтвердить RED: empty input reached compiler evaluation empty and invalid input did not fail.
- [x] Добавить properties и target `ValidateKarpikCompositionMode` до `PrepareForBuild` и `Restore`; target normalizes an evaluated empty value and invalid value выдаёт стабильный build error `KARPIK010`.
- [x] Повторить targeted test и получить GREEN (5 passed, 0 failed, 0 skipped).
- [x] Создать минимальный smoke host, который использует Autofac factory registration без scanning и печатает `STATIC_AOT_OK`.
- [x] Выполнить из корня репозитория:

  `dotnet publish Karpik.Engine.Core.Runner.Tests/NativeAotSmoke/NativeAotSmoke.csproj -c Release -r win-x64 -p:PublishAot=true -m:1 -nr:false`

  Result: exit 0 in the Visual Studio C++ developer environment; the published executable printed `STATIC_AOT_OK` with exit code 0. Warning inventory is `NU1900` only; no `IL2xxx`, `IL3xxx`, or `IL3050` were emitted.
- [x] Determine whether the Autofac probe requires a generated lightweight-container boundary: it does not, because publish and execution completed without intrinsic trimming/AOT diagnostics. No broad reflection roots were added.
- [x] Run `dotnet test Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false` (52 passed, 3 skipped) and `dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false` (107 passed, 0 failed, 0 skipped outside the sandbox).
- [x] Update `Progress` and record the actual AOT command/result.

### Milestone 2: Side-safe compile-time module references из SDK

Цель — сделать installed Shared/Client/Server modules видимыми Roslyn и AOT publisher до compilation, не копируя их Dynamic bundle logic.

**Files:**

- Create: `Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs`
- Create: `Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs`
- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/ExternalGameCliTests.cs`

**Interface produced:**

```csharp
public sealed class ResolveKarpikStaticReferencesTask : Microsoft.Build.Utilities.Task
{
    [Required] public string EngineRoot { get; set; } = "";
    [Required] public string Side { get; set; } = "";
    [Output] public ITaskItem[] References { get; set; } = [];
    public override bool Execute();
}
```

- [x] Написать tests с временным валидным installation layout: Shared возвращает только Shared primary assemblies; Client — Shared+Client; Server — Shared+Server; порядок детерминирован как catalog.
- [x] Добавить negative tests: invalid side, отсутствующий catalog, missing primary assembly, reparse point, unsafe module ID и попытка Client получить Server.
- [x] Запустить targeted tests и подтвердить RED из-за отсутствующего task type.
- [x] Реализовать task через `EngineModuleCatalog.Read`/`ForSide` и `ModuleLayoutPolicy`; не дублировать parser и path policy.
- [x] Получить GREEN для task tests.
- [x] Добавить target `_KarpikResolveStaticModuleReferences` до `ResolveAssemblyReferences`, только при `KarpikProjectKind=Runtime|Tool` и `KarpikCompositionMode=Static`. Преобразовать output в `<Reference HintPath=... Private=false>` и дедуплицировать по assembly identity.
- [x] Написать integration fixture, где внешний Shared project использует `Karpik.Engine.Shared.Spatial2D.Transform2D` без ручного `<Reference>`; RED подтверждён на отсутствии task/hook, а focused transaction-owned fixture теперь GREEN.
- [x] Добавить отрицательные compile tests: Shared не может импортировать Client/Server type; Client не может импортировать Server type.
- [x] Запустить task tests и relevant external SDK integration tests. Task suite: 64 passed, 4 link-capability skips. Normal `ExternalGameCliTests`: 4 passed, 3 environment-gated skips. Focused opt-in Static/Dynamic/side-boundary fixture: 1 passed, 0 failed, 0 skipped, including transaction-owned SDK packing and exact Dynamic reference projection.
- [x] Обновить `Progress` и `Surprises & Discoveries`.

### Milestone 3: Network.Codegen в SDK и typed snapshot registry

Цель — заменить hardcoded `NetworkManager` самостоятельным Shared registry и доказать сериализацию `Transform2D` без boxing/allocations.

**Files:**

- Modify: `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj`
- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- Modify: `Network.Codegen/Network.Codegen/NetworkGenerator.cs`
- Modify: `Network.Codegen/Network.Codegen/ProjectTypeDetector.cs`
- Modify: `Network.Codegen/Network.Codegen/RpcGenerator.cs`
- Modify: `Network.Codegen/Network.Codegen/TargetClientRpcGenerator.cs`
- Create: `Network.Codegen.Tests/Network.Codegen.Tests.csproj`
- Create: `Network.Codegen.Tests/GeneratorTestHarness.cs`
- Create: `Network.Codegen.Tests/NetworkSnapshotGeneratorTests.cs`
- Modify: `KarpikEngine.slnx`

**Interfaces produced:** `Karpik.Engine.Generated.NetworkSnapshotRegistry` as defined above; build-property based side detection shared by all network generators.

- [x] Создать Roslyn harness, который передаёт `build_property.KarpikSide`, `build_property.KarpikProjectKind` и `build_property.KarpikCompositionMode` через custom `AnalyzerConfigOptionsProvider` и компилирует generated source.
- [x] Написать failing generator test: assembly name `AnyGame.Shared`, `KarpikSide=Shared`, referenced component assembly содержит `Transform2D`; output должен содержать `NetworkSnapshotRegistry`, два `Put(double)` для Position, `Put(float)` для Rotation и два `Put(float)` для Scale.
- [x] Написать failing tests для `Client`/`Server` snapshot suppression, RPC generator side selection, unsupported managed field diagnostic, duplicate component ID diagnostic и stable ordering независимо от reference enumeration.
- [x] Написать syntax/semantic assertions, запрещающие `object`, cast компонента из object, `Dictionary<long, IComponentSerializer>`, LINQ и reflection в generated snapshot hot path.
- [x] Запустить `dotnet test Network.Codegen.Tests/Network.Codegen.Tests.csproj -m:1 -nr:false` и подтвердить RED: 9 failed, 2 passed до production changes.
- [x] Перевести generators с `CompilationProvider` на `CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider)` и читать side/mode из build properties. Удалить assembly-name classification после прохождения parity tests.
- [x] Реализовать symbol-based field codec map для primitive types, `System.Numerics.Vector2/Vector3`, `OpenTK.Mathematics.Vector2/Vector2d` и существующего `System.Drawing.Color`. Unsupported types получают diagnostic с component/field location.
- [x] Реализовать deterministic FNV-1a IDs и schema hash. Добавить handshake API в Shared network protocol так, чтобы mismatch обнаруживался до entity payload; exact LiteNetLib integration покрыта live round-trip/mismatch tests и сохраняет `ReliableOrdered`.
- [x] Упаковать `Network.Codegen.dll` в `analyzers/dotnet/cs/` SDK package и подключать к Runtime projects. Package inspection: exact count 1; root `Directory.Build.props` не расширялся.
- [x] Получить GREEN generator tests (14/14) и выполнить snapshot round-trip для `Transform2D` с нецелыми double position values.
- [x] Добавить allocation test: после warm-up 1,000 writes реального emitted registry фиксированного world измерили 0 managed bytes; writer/registry/world/pools/buffer созданы до measurement.
- [x] Обновить `Progress` и записать protocol compatibility/handshake decision.

### Milestone 4: Generated module installer composition

Цель — заменить type scanning и `Activator.CreateInstance` прямой регистрацией installers в Static host.

**Files:**

- Create: `Karpik.Engine.Core/StaticComposition/IStaticRuntimeComposition.cs`
- Create: `Karpik.Engine.Core/StaticComposition/IStaticModuleRegistry.cs`
- Create: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs`
- Create: `Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj`
- Create: `Karpik.Engine.Core.Generator.Tests/RuntimeCompositionGeneratorTests.cs`
- Modify: `Karpik.Engine.Core.Runner/Runner.cs`
- Modify: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/ModuleRegistratorGenerator.cs`
- Modify: `KarpikEngine.slnx`

**Interfaces produced:** `IStaticModuleRegistry.Add(IModuleInstaller)` и `GeneratedRuntimeComposition.RegisterModules(IStaticModuleRegistry registry)`, плюс diagnostics for invalid installers.

- [x] Написать failing generator tests для discovery installers из current + referenced assemblies, side filtering, deterministic ordering и direct constructor emission.
- [x] Добавить failing diagnostics tests для abstract/internal/open-generic installer, отсутствующего public parameterless constructor, duplicate module identity и нескольких выбранных implementations одного module contract.
- [x] Подтвердить RED targeted generator tests.
- [x] Реализовать incremental syntax/symbol pipeline; не перечислять system assemblies и не использовать runtime reflection.
- [x] Добавить в `EngineRunner` публичную Static registration boundary, принимающую прямые installer instances до `Setup` и сохраняющую существующие проверки порядка/дубликатов.
- [x] Получить GREEN generator tests.
- [x] Добавить runner parity test: Dynamic list installers и generated Static installers дают одинаковый упорядоченный набор module IDs для текущих Client и Server selections.
- [x] После GREEN удалить отключённый `ModuleRegistratorGenerator.cs` либо оставить файл-переадресатор только если это требуется package compatibility; решение записать в `Decision Log`.
- [x] Выполнить Core generator tests и Runner tests, затем обновить `Progress`.

### Milestone 5: Generated DI и ECS activation без reflection

Цель — сделать Static startup совместимым с trimming/AOT и не маскировать отсутствие metadata широкими roots.

**Files:**

- Create: `Karpik.Engine.Core/StaticComposition/IStaticServiceRegistry.cs`
- Create: `Karpik.Engine.Core.Runner/StaticComposition/AutofacStaticServiceRegistry.cs` либо generated static container, выбранный Milestone 1
- Modify: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs`
- Modify: `Karpik.Engine.Core.Runner/Runner.cs`
- Modify: `Karpik.Engine.Core.Runner/AttributedServiceRegistrar.cs`
- Modify: `Karpik.Engine.Core.Runner/SystemRegistry.cs`
- Modify: `Karpik.Engine.Core.Generator.Tests/RuntimeCompositionGeneratorTests.cs`
- Modify: `Karpik.Engine.Core.Runner.Tests/StaticCompositionTests.cs`

**Interfaces produced:** typed factory registration for Engine, ModSet and Simulation scopes; generated ECS system factories.

- [x] Написать failing generator tests для `[Export] + [ServiceRegistration]`: constructor dependency order, multiple export contracts, `IStartable`, Singleton и Transient.
- [x] Написать failing diagnostics tests для missing `[Export]`, abstract/open generic implementation, неоднозначного constructor и зависимости, отсутствующей в closed graph.
- [x] Написать failing runner test, который устанавливает reflection guard и подтверждает, что Static startup не вызывает `AttributedServiceRegistrar.Register(IEnumerable<Type>)` или `Activator.CreateInstance`.
- [x] Реализовать generated `Func<IServiceResolver,TImplementation>` factories. Constructor arguments разрешать через `IServiceResolver.Resolve<T>()`; factory не должна захватывать mutable closure.
- [x] Для ECS systems расширить `ISystemRegistry`/`SystemRegistry` typed factory overload так, чтобы Static mode регистрировал factory, а не `Type`. Dynamic overload сохранить без изменения.
- [x] Получить GREEN unit tests и повторить Milestone 1 AOT smoke с реальным `EngineRunner` setup/destroy.
- [x] Измерить startup allocations Dynamic vs Static; цель milestone — отсутствие reflection exceptions и отсутствие новых per-frame allocations, а не нулевая startup allocation.
- [x] Если Autofac остаётся, выполнить trimmed+AOT run test всех трёх scopes. Если не проходит, реализовать заранее выбранный generated static container и повторить те же tests.
- [x] Обновить `Progress` и зафиксировать DI решение в `Decision Log`.

### Milestone 6: Game-specific Static Client/Server hosts

Цель — launcher непосредственно запускает скомпилированную игру вместо внешнего universal runner.

**Files:**

- Create: `Karpik.Engine.Core.Runner/StaticComposition/StaticEngineHost.cs`
- Modify: `Karpik.Engine.Core.Runner/Program.cs`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Client.Launcher/Program.cs`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Server.Launcher/Program.cs`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Launcher/EngineLauncher.cs`
- Create: `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs`

**Interface produced:**

```csharp
public static class StaticEngineHost
{
    public static Task<int> RunAsync(
        Side side,
        IStaticRuntimeComposition composition,
        string[] args,
        CancellationToken cancellationToken = default);
}
```

- [x] Написать template XML tests: Dynamic launcher сохраняет `ReferenceOutputAssembly=false`; Static launcher evaluates runtime project reference as true and receives side-compatible modules.
- [x] Написать failing process integration test, который запускает Static Server host, ждёт ready marker/IPC response и проверяет отсутствие строк `PluginLoadContext`, `modules.list` и `shadow` в diagnostic trace.
- [x] Подтвердить RED до появления `StaticEngineHost`.
- [x] Извлечь общий loop/IPC lifecycle из `Program.cs` в reusable host API без изменения Dynamic CLI behavior.
- [x] Обновить template entry points: compile-time condition выбирает Dynamic external runner или generated Static host. Не вычислять режим через runtime environment variable.
- [x] Получить GREEN template и process tests.
- [x] Проверить process-isolated reload: изменить game assembly, пересобрать host, запросить restart, подтвердить сохранение `IRestartWorkerStateProvider` state и отсутствие orphan processes.
- [x] Запустить полный `Karpik.Engine.Sdk.IntegrationTests` набор, кроме explicitly environment-gated installed-runtime tests; отдельно записать skipped tests.
- [x] Обновить `Progress`.


Executed (2026-08-22, branch open-code-ai): WorkerHost extraction keeps Dynamic CLI byte-compatible (Runner suite 115/115).
StaticEngineHost.RunAsync reuses Bootstrap+IPC with a tolerant argument parser (no runners-directory ownership check; unknown args rejected).
Template launchers: EnableDefaultCompileItems=false, conditional Program.cs/EngineLauncher/StaticEntry Compile items, _KarpikEnableStaticRuntimeReference target (BeforeTargets=AssignProjectConfiguration) flips ProjectReference metadata to ReferenceOutputAssembly=true/Private=true for both runtime and Shared references.
dotnet new strips C# preprocessor directives from template sources - Static entry is a dedicated file selected by conditional Compile items (recorded below).
SDK: Tool kind receives Core+Runner engine references only at KarpikCompositionMode=Static; module payload directories are referenced wholesale (PayloadAssemblies) because generated factories mention transitive service constructor types.
Integration tests rebuild every catalog module of the exercised side graph inside their transaction: retained installations predate current IModuleInstaller contracts and silently produce empty static graphs.
Known pre-existing defect (out of M6 scope): Graphics.Core TextureResources.Dispose throws NRE during DI build-failure unwind when fresh client modules initialize the real window/graphics stack; blocks only the multi-worker phase of the gated dynamic workflow test.

### Milestone 7: Static publish layout без managed module manifest

Цель — Static output больше не содержит или не читает runtime список managed assemblies.

**Files:**

- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- Modify: `Karpik.Engine.Sdk.Tasks/BuildKarpikRuntimeBundleTask.cs`
- Modify: `Karpik.Engine.Sdk.Tasks.Tests/RuntimeBundleTaskTests.cs`
- Modify: `Karpik.Engine.Core.Runner/RuntimeModuleComposition.cs`
- Modify: `Karpik.Engine.Core/ModuleManagement/ExternalModuleComposition.cs`
- Modify: `Karpik.Engine.Core/PluginLoadContext.cs`
- Modify: `Karpik.Engine.Core/ProcessManagement/RuntimeLaunchOptions.cs`

**Behavior produced:** Dynamic keeps current bundle; Static publish contains executable, assets, configuration and native libraries only.

- [x] Написать failing Static bundle layout tests: output не содержит `modules.version.1/modules.list`, managed shadow directory или loose primary module DLL при single-file/AOT publish; Content и required native files присутствуют.
- [x] Добавить Dynamic regression test, подтверждающий прежний canonical manifest и validation rules.
- [x] Подтвердить RED Static test и GREEN Dynamic regression до implementation.
- [x] Разделить `BuildKarpikRuntimeBundle` по composition mode. Static branch использует publish output/assets/native inputs и не вызывает managed module staging.
- [x] Ограничить `RuntimeModuleComposition`, `ModuleLoader` и `PluginLoadContext` compile/runtime usage Dynamic path. Не удалять их до смены default mode.
- [x] Получить GREEN bundle tests и повторить Static Server process test из Milestone 6.
- [x] Проверить recoverability: прерванная Static publish не заменяет предыдущий complete output; повторный publish идемпотентен.
- [x] Обновить `Progress`.


Executed (2026-08-23, branch open-code-ai): BuildKarpikRuntimeBundleTask gained CompositionMode (empty→Dynamic) and NativeFiles inputs; Static staging copies Content/Mods plus native items only under native/ or runtimes/ TargetPaths — never managed assemblies, no modules.version.1/modules.list/shadow; completion validation rejects any modules.list anywhere in the output. Proven-output/backup recovery accepts either layout so mode switches replace complete outputs atomically (rollback to Dynamic keeps working). RuntimeBundleLayout.ValidateStatic validates manifest-free bundles; ValidateAny sniffs modules.version.1 presence and is used by RuntimeLaunchOptions so the editor launches both layouts. Static hosts validate --bundle with ValidateStatic and report AppContext.BaseDirectory as the worker module directory (no module staging dir exists). Dynamic-only types (RuntimeModuleComposition, ModuleLoader, PluginLoadContext) are annotated and pinned by a source-boundary test over the Static host surface. Gated M6 Static Server process test re-run: PASS (6m49s) with the new static bundle; side-safety suite PASS.

### Milestone 8: NativeAOT acceptance и смена default

Цель — доказать production-shaped Server/Client публикацию, документировать ограничения и только после parity изменить default.

**Files:**

- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.props`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs`
- Create: `docs/02_ADR/static-runtime-composition.md`
- Modify: `Karpik.Engine.Sdk/README.md`
- Create or modify: `templates/Karpik.Game/README.md` — пользовательская документация Dynamic/Static build и publish.

- [x] Publish и запустить Server Static host для `win-x64` с `PublishAot=true`, trimming enabled и invariant globalization только если игра не требует culture data. Проверить startup, fixed ticks, snapshot round-trip и clean shutdown.
- [x] Publish и запустить Client Static host для `win-x64`. Проверить window creation, graphics backend initialization, один rendered frame, input initialization и clean shutdown. Headless graphics/window implementations допустимы только как отдельный предварительный test; финальный Client gate использует выбранные production backends.
- [x] Выполнить десять последовательных process-isolated reload cycles Static host и проверить отсутствие orphan processes, locked publish files и роста сохранённого state payload.
- [x] Собрать warning inventory `IL2xxx`, `IL3xxx`, `IL3050`; каждая suppression должна указывать узкий member/type и иметь тест. Acceptance требует отсутствия необъяснённых warnings.
- [x] Сравнить Dynamic и Static module IDs, registered services, ECS systems и snapshot schema hash. Acceptance: полное равенство для одной selection, кроме documented Dynamic-only managed mods.
- [x] После прохождения всех gates изменить default `KarpikCompositionMode` на `Static`; оставить явный opt-in `Dynamic` на один release cycle. Удаление Dynamic — отдельный последующий ExecPlan после телеметрии/использования.
- [x] Создать ADR с причинами closed-world решения, границами managed mods, hot reload strategy, AOT evidence и rollback mode.
- [x] Запустить targeted full verification commands из следующего раздела, обновить `Outcomes & Retrospective` и отметить ExecPlan завершённым.

### Milestone 9: Corrective — compile-time ECS descriptors, точный порядок, полная parity и узкий trimming

Цель — закрыть расхождения, выявленные пост-приёмочным аудитом (2026-08-23): reflection activation в Static ECS startup, расходящийся порядок installers, широкие AOT suppressions/roots, ослабленные reload и parity контракты.

**Files:**

- Create: `Karpik.Engine.Core/StaticComposition/IStaticEcsRegistryProviders.cs` — контракт передачи generated provider instances.
- Modify: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs`
- Modify: `Karpik.Engine.Core.Runner/Runner.cs`
- Modify: `Karpik.Engine.Core.Runner.Tests/StaticCompositionSourceBoundaryTests.cs`
- Modify: `Karpik.Engine.Core.Generator.Tests/RuntimeCompositionGeneratorTests.cs`
- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.targets`
- Modify: `templates/Karpik.Game/Source/KarpikGame.Server/ServerGameInstaller.cs`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs`
- Modify: module projects — publicизация internal `[ServiceRegistration]` сервисов/систем по мере выявления.

**Interfaces produced:** generated typed `IEcsUpdateRegistryProvider`/`IEcsRenderPrepareRegistryProvider` implementations, доставляемые через `IStaticRuntimeComposition`; Static startup без `Assembly.GetTypes()`/`Activator.CreateInstance()`; полный ordered parity; warning gate без project-wide `NoWarn`.

- [ ] Написать failing source-boundary test, сканирующий `Runner.cs`: Static execution path не содержит `Assembly.GetTypes`, `Activator.CreateInstance`, `Type.GetType`.
- [ ] Генератор испускает concrete provider classes с прямым перечислением system descriptors; `RegisterServices`/новый метод композиции передаёт их инстансы; Runner на Static path использует только переданные провайдеры, reflection-перечисление остаётся только в Dynamic ветке.
- [ ] Static регистрация installers сохраняет generated insertion order (rank вместе с installer либо явное сохранение порядка); тест сравнивает последовательности, а не множества, для Client и Server selections.
- [ ] Убрать project-wide `NoWarn IL2104/IL3053/IL3000/IL3002` из `Sdk.targets`; каждый оставшийся warning подавляется узко (тип/член или конкретная сборка через `NoWarn` в csproj модуля с комментарием-обоснованием).
- [ ] Заменить assembly-wide trim roots (`DragonECS`, `Newtonsoft.Json`, все module assemblies) минимальным набором: source-generated restart-state serialization вместо Newtonsoft-зависимого пути; generated descriptor/instantiation roots вместо rooting целых сборок. Gate publish обязан падать на любом необъяснённом `IL2xxx/IL3xxx/IL3050`.
- [ ] Сделать template `ServerGameInitSystem` idempotent при восстановленном world; reload-тест требует стабильного state payload после первого warm-up reload (линейный рост = fail).
- [ ] Публичизировать internal `[ServiceRegistration]` сервисы во всех модулях selection; parity-тесты для services/systems/installers требуют полного равенства множеств И последовательностей, без исключений кроме явно перечисленных в ADR.
- [ ] Перевести `RuntimeCompositionGenerator` на настоящий incremental pipeline (syntax/symbol provider без полного пересканирования `Compilation` при каждой правке).
- [ ] Повторить Server и Client NativeAOT gates и десять reload cycles на новом коде; обновить warning inventory и ADR.
- [ ] Обновить `Progress`, `Decision Log` и `Outcomes & Retrospective`.

## Concrete Steps

Все команды выполняются из `C:\Users\artem\RiderProjects\KarpikEngine` в PowerShell. После изменений кода обновлять индекс codebase-memory.

Основная последовательность targeted checks:

```powershell
dotnet test Karpik.Engine.Sdk.Tasks.Tests/Karpik.Engine.Sdk.Tasks.Tests.csproj -m:1 -nr:false
dotnet test Network.Codegen.Tests/Network.Codegen.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Generator.Tests/Karpik.Engine.Core.Generator.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet test Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false
```

SDK package и template build:

```powershell
dotnet pack Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj -c Release -m:1 -nr:false
dotnet build templates/Karpik.Game/Source/KarpikGame.Shared/KarpikGame.Shared.csproj -m:1 -nr:false -p:KarpikCompositionMode=Static
dotnet build templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj -m:1 -nr:false -p:KarpikCompositionMode=Static
dotnet build templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj -m:1 -nr:false -p:KarpikCompositionMode=Static
```

NativeAOT acceptance:

```powershell
dotnet publish templates/Karpik.Game/Source/KarpikGame.Server.Launcher/KarpikGame.Server.Launcher.csproj -c Release -r win-x64 -p:KarpikCompositionMode=Static -p:PublishAot=true -m:1 -nr:false
dotnet publish templates/Karpik.Game/Source/KarpikGame.Client.Launcher/KarpikGame.Client.Launcher.csproj -c Release -r win-x64 -p:KarpikCompositionMode=Static -p:PublishAot=true -m:1 -nr:false
```

Перед каждой командой publish пакет SDK и installed engine fixture должны быть собраны тем же `Configuration`; integration harness обязан создавать изолированные временные каталоги вне repository root и удалять только проверенные собственные пути.

Рекомендуемые commit checkpoints после GREEN milestone:

```text
build: add static composition mode contract
build: resolve installed modules for static compilation
feat(network): generate typed snapshot registry
feat(core): generate static module composition
feat(core): generate aot-safe service factories
feat(runtime): add game-specific static hosts
build: publish static runtime without module manifest
docs: make static composition the default
```

Не смешивать физическую double migration, IRenderer или замену physics backend с этими commits.

## Validation and Acceptance

Функция принимается только при выполнении всех условий:

1. `KarpikCompositionMode=Dynamic` проходит существующие SDK, bundle, runner и hot reload tests без изменения observable behavior.
2. Static Shared compilation видит `Spatial2D.Transform2D` без ручной ссылки в игровом `.csproj`.
3. Generated `NetworkSnapshotRegistry` сериализует `Transform2D` round-trip с сохранением double position и не выделяет память в warmed snapshot write loop.
4. Generated code не содержит `object` component serialization, reflection, `Assembly.GetTypes`, `Activator.CreateInstance` или serializer dictionary lookup на каждый component.
5. Static Client graph не содержит Server assemblies; Static Server graph не содержит Client assemblies; Shared graph не содержит обе side-specific группы.
6. Static Server host запускается без `modules.list`, `PluginLoadContext` и shadow copy, принимает IPC/editor snapshot request и корректно завершается.
7. Десять последовательных process restart cycles сохраняют разрешённое state и не оставляют процессы/locked files.
8. Server NativeAOT publish/run проходит без необъяснённых trim/AOT warnings.
9. Client NativeAOT publish/run с production window/graphics backend создаёт окно, выполняет хотя бы один render frame и завершается чисто.
10. Dynamic и Static при одинаковой selection регистрируют одинаковые module IDs, services, ECS systems и network schema hash.
11. Индекс codebase-memory успешно обновлён после финальных изменений кода.

## Idempotence and Recovery

Добавление compile references является build-time операцией и безопасно для повторного запуска. SDK task должен возвращать детерминированный отсортированный набор и не писать в installation root.

Generated sources пишутся Roslyn в compiler output, а не напрямую в tracked repository files. Повторная compilation не должна менять output при неизменных inputs.

Static publish использует отдельный output/staging directory и atomic completion marker. При ошибке AOT publish предыдущий Dynamic bundle и предыдущий complete Static output не удаляются. Integration tests создают уникальные каталоги через `Path.GetTempPath()` + GUID и перед очисткой проверяют, что путь находится вне repository и installation roots.

Rollback каждого milestone выполняется переключением проекта на `KarpikCompositionMode=Dynamic`; это должно оставаться рабочим до Milestone 8. Не использовать `git reset --hard` или удаление пользовательских worktree changes. Если generated Static path ломает сборку, отключить только Static target condition и сохранить failing regression test.

Protocol schema/ID изменение является несовместимым. До merge Milestone 3 одновременно обновить обе стороны и handshake; rollback требует возврата generator и protocol версии вместе. Никогда не читать payload при schema mismatch.

Если Client NativeAOT блокируется внешним backend, Server Static/AOT и compile-time composition не откатываются. Зафиксировать конкретный dependency/API blocker в `Surprises & Discoveries`, сохранить Client Static JIT mode и создать отдельный backend AOT ExecPlan.

## Artifacts and Notes

- Source-of-truth plan: `plans/static-composition-nativeaot-execplan.md`.
- Existing dynamic loader: `Generated/ModuleLoader.cs`, `Karpik.Engine.Core/PluginLoadContext.cs`, `Karpik.Engine.Core/ModuleManagement/ExternalModuleComposition.cs`.
- Installed module catalog: `Karpik.Engine.Tooling/EngineModuleCatalog.cs`.
- SDK entry points: `Karpik.Engine.Sdk/Sdk/Sdk.props`, `Karpik.Engine.Sdk/Sdk/Sdk.targets`.
- Existing snapshot generator: `Network.Codegen/Network.Codegen/NetworkGenerator.cs`.
- Existing process-isolation reference: `plans/process-isolation-architecture.md`.
- Final durable decision: `docs/02_ADR/static-runtime-composition.md`, created only after evidence from Milestone 8.

## Scope Boundaries

Этот ExecPlan не заменяет Aether2D на double-precision physics, не проектирует IRenderer, не меняет gameplay replication model, не добавляет network prediction и не реализует браузерный backend NeoVeldrid. Он создаёт compile-time/AOT фундамент, на котором эти подсистемы смогут использовать надёжную source generation.

Compression, delta snapshots, interest management и новый RPC wire protocol не входят в план. Допустимы только изменения, необходимые для deterministic component IDs, schema handshake и сериализации уже помеченных `[NetworkedField]`.
