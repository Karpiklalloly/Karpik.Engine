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
- [ ] Milestone 3: упаковать Network.Codegen в SDK и генерировать самостоятельный snapshot registry.
- [ ] Milestone 4: генерировать статическую композицию module installers.
- [ ] Milestone 5: исключить reflection activation из DI и регистрации ECS-систем Static-режима.
- [ ] Milestone 6: превратить launcher-проекты в game-specific Static hosts.
- [ ] Milestone 7: убрать managed module manifest и PluginLoadContext из Static runtime, сохранив process-isolated reload.
- [ ] Milestone 8: пройти Server и Client NativeAOT acceptance, зафиксировать архитектуру ADR.

## Surprises & Discoveries

- Observation: `Network.Codegen` сейчас подключается из корневого `Directory.Build.props` только к проектам, имя которых содержит `Karpik.Engine`, поэтому не запускается ни для `Spatial2D`, ни для шаблонного `KarpikGame.Shared`.
  Evidence: `Directory.Build.props` содержит соответствующий `ItemGroup Condition`; сборка `Modules/Shared/Spatial2D/Spatial2D.csproj` не строит `Network.Codegen` как analyzer.

- Observation: `ProjectTypeDetector` обновлен для поддержки build-свойств `KarpikSide`, `KarpikProjectKind` и `KarpikCompositionMode`; определение типа проекта теперь prioritizes SDK-свойства перед определением по имени сборки.
  Evidence: `Network.Codegen/Network.Codegen/ProjectTypeDetector.cs` теперь читает `KarpikSide` из compilation symbols, установленных SDK через `CompilerVisibleProperty`.

- Observation: `NetworkGenerator` теперь генерирует два source файла: `NetworkManager.g.cs` (старый формат для Dynamic-режима) и `NetworkSnapshotRegistry.g.cs` (новый NativeAOT-safe формат сTyped serializer'ами и без `object`/dictionary).
  Evidence: `Network.Codegen/Network.Codegen/NetworkGenerator.cs` теперь содержит метод `GenerateSnapshotRegistrySource` создающий `NetworkSnapshotRegistry` с typed Write/Read methods, deterministic component IDs и без boxing/allocations в горячем пути.

- Observation: SDK `Sdk.csproj` обновлен для включения `Network.Codegen.dll` как analyzers/dotnet/cs package, что обеспечивает его автоматическое подключение к проектам с `KarpikSide=Shared|Client|Server`.
  Evidence: `Karpik.Engine.Sdk/Karpik.Engine.Sdk.csproj` теперь ссылается на `Network.Codegen` project и упаковывает его DLL в SDK package.

- Observation: snapshot generator жёстко дописывает partial-класс `Karpik.Engine.MyGame.Shared.Main.NetworkManager` и сериализует компонент через `object`, что боксит struct на каждую запись.
  Evidence: `Network.Codegen/Network.Codegen/NetworkGenerator.cs`, методы `GenerateSource` и сгенерированный `IComponentSerializer.Write(IWriter, object)`.

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

## Outcomes & Retrospective

Реализация ещё не начата. После каждого milestone сюда добавлять фактический результат, измерения, оставшиеся ограничения и ссылки на созданные ADR.

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

- [ ] Создать Roslyn harness, который передаёт `build_property.KarpikSide`, `build_property.KarpikProjectKind` и `build_property.KarpikCompositionMode` через custom `AnalyzerConfigOptionsProvider` и компилирует generated source.
- [ ] Написать failing generator test: assembly name `AnyGame.Shared`, `KarpikSide=Shared`, referenced component assembly содержит `Transform2D`; output должен содержать `NetworkSnapshotRegistry`, два `Put(double)` для Position, `Put(float)` для Rotation и два `Put(float)` для Scale.
- [ ] Написать failing tests для `Client`/`Server` snapshot suppression, RPC generator side selection, unsupported managed field diagnostic, duplicate component ID diagnostic и stable ordering независимо от reference enumeration.
- [ ] Написать syntax/semantic assertions, запрещающие `object`, cast компонента из object, `Dictionary<long, IComponentSerializer>`, LINQ и reflection в generated snapshot hot path.
- [ ] Запустить `dotnet test Network.Codegen.Tests/Network.Codegen.Tests.csproj -m:1 -nr:false` и подтвердить RED по ожидаемым причинам.
- [ ] Перевести generators с `CompilationProvider` на `CompilationProvider.Combine(context.AnalyzerConfigOptionsProvider)` и читать side/mode из build properties. Удалить assembly-name classification после прохождения parity tests.
- [ ] Реализовать symbol-based field codec map для primitive types, `System.Numerics.Vector2/Vector3`, `OpenTK.Mathematics.Vector2/Vector2d` и существующего `System.Drawing.Color`. Unsupported types получают diagnostic с component/field location.
- [ ] Реализовать deterministic FNV-1a IDs и schema hash. Добавить handshake API в Shared network protocol так, чтобы mismatch обнаруживался до entity payload; exact transport integration покрыть round-trip test без изменения delivery method.
- [ ] Упаковать `Network.Codegen.dll` в `analyzers/dotnet/cs/` SDK package и подключать к Runtime projects. Не подключать второй экземпляр через `Directory.Build.props`; внутренние module builds сохраняют только Core codegen, пока network component manifests не потребуются отдельно.
- [ ] Получить GREEN generator tests и выполнить snapshot round-trip для `Transform2D` с нецелыми double position values.
- [ ] Добавить allocation test: после warm-up 1,000 snapshot writes фиксированного world не выделяют managed bytes в цикле; сам writer и world создаются до измерения.
- [ ] Обновить `Progress`; при wire format изменении записать protocol compatibility decision.

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

- [ ] Написать failing generator tests для discovery installers из current + referenced assemblies, side filtering, deterministic ordering и direct constructor emission.
- [ ] Добавить failing diagnostics tests для abstract/internal/open-generic installer, отсутствующего public parameterless constructor, duplicate module identity и нескольких выбранных implementations одного module contract.
- [ ] Подтвердить RED targeted generator tests.
- [ ] Реализовать incremental syntax/symbol pipeline; не перечислять system assemblies и не использовать runtime reflection.
- [ ] Добавить в `EngineRunner` публичную Static registration boundary, принимающую прямые installer instances до `Setup` и сохраняющую существующие проверки порядка/дубликатов.
- [ ] Получить GREEN generator tests.
- [ ] Добавить runner parity test: Dynamic list installers и generated Static installers дают одинаковый упорядоченный набор module IDs для текущих Client и Server selections.
- [ ] После GREEN удалить отключённый `ModuleRegistratorGenerator.cs` либо оставить файл-переадресатор только если это требуется package compatibility; решение записать в `Decision Log`.
- [ ] Выполнить Core generator tests и Runner tests, затем обновить `Progress`.

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

- [ ] Написать failing generator tests для `[Export] + [ServiceRegistration]`: constructor dependency order, multiple export contracts, `IStartable`, Singleton и Transient.
- [ ] Написать failing diagnostics tests для missing `[Export]`, abstract/open generic implementation, неоднозначного constructor и зависимости, отсутствующей в closed graph.
- [ ] Написать failing runner test, который устанавливает reflection guard и подтверждает, что Static startup не вызывает `AttributedServiceRegistrar.Register(IEnumerable<Type>)` или `Activator.CreateInstance`.
- [ ] Реализовать generated `Func<IServiceResolver,TImplementation>` factories. Constructor arguments разрешать через `IServiceResolver.Resolve<T>()`; factory не должна захватывать mutable closure.
- [ ] Для ECS systems расширить `ISystemRegistry`/`SystemRegistry` typed factory overload так, чтобы Static mode регистрировал factory, а не `Type`. Dynamic overload сохранить без изменения.
- [ ] Получить GREEN unit tests и повторить Milestone 1 AOT smoke с реальным `EngineRunner` setup/destroy.
- [ ] Измерить startup allocations Dynamic vs Static; цель milestone — отсутствие reflection exceptions и отсутствие новых per-frame allocations, а не нулевая startup allocation.
- [ ] Если Autofac остаётся, выполнить trimmed+AOT run test всех трёх scopes. Если не проходит, реализовать заранее выбранный generated static container и повторить те же tests.
- [ ] Обновить `Progress` и зафиксировать DI решение в `Decision Log`.

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

- [ ] Написать template XML tests: Dynamic launcher сохраняет `ReferenceOutputAssembly=false`; Static launcher evaluates runtime project reference as true and receives side-compatible modules.
- [ ] Написать failing process integration test, который запускает Static Server host, ждёт ready marker/IPC response и проверяет отсутствие строк `PluginLoadContext`, `modules.list` и `shadow` в diagnostic trace.
- [ ] Подтвердить RED до появления `StaticEngineHost`.
- [ ] Извлечь общий loop/IPC lifecycle из `Program.cs` в reusable host API без изменения Dynamic CLI behavior.
- [ ] Обновить template entry points: compile-time condition выбирает Dynamic external runner или generated Static host. Не вычислять режим через runtime environment variable.
- [ ] Получить GREEN template и process tests.
- [ ] Проверить process-isolated reload: изменить game assembly, пересобрать host, запросить restart, подтвердить сохранение `IRestartWorkerStateProvider` state и отсутствие orphan processes.
- [ ] Запустить полный `Karpik.Engine.Sdk.IntegrationTests` набор, кроме explicitly environment-gated installed-runtime tests; отдельно записать skipped tests.
- [ ] Обновить `Progress`.

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

- [ ] Написать failing Static bundle layout tests: output не содержит `modules.version.1/modules.list`, managed shadow directory или loose primary module DLL при single-file/AOT publish; Content и required native files присутствуют.
- [ ] Добавить Dynamic regression test, подтверждающий прежний canonical manifest и validation rules.
- [ ] Подтвердить RED Static test и GREEN Dynamic regression до implementation.
- [ ] Разделить `BuildKarpikRuntimeBundle` по composition mode. Static branch использует publish output/assets/native inputs и не вызывает managed module staging.
- [ ] Ограничить `RuntimeModuleComposition`, `ModuleLoader` и `PluginLoadContext` compile/runtime usage Dynamic path. Не удалять их до смены default mode.
- [ ] Получить GREEN bundle tests и повторить Static Server process test из Milestone 6.
- [ ] Проверить recoverability: прерванная Static publish не заменяет предыдущий complete output; повторный publish идемпотентен.
- [ ] Обновить `Progress`.

### Milestone 8: NativeAOT acceptance и смена default

Цель — доказать production-shaped Server/Client публикацию, документировать ограничения и только после parity изменить default.

**Files:**

- Modify: `Karpik.Engine.Sdk/Sdk/Sdk.props`
- Modify: `Karpik.Engine.Sdk.IntegrationTests/StaticCompositionCliTests.cs`
- Create: `docs/02_ADR/static-runtime-composition.md`
- Modify: `Karpik.Engine.Sdk/README.md`
- Create or modify: `templates/Karpik.Game/README.md` — пользовательская документация Dynamic/Static build и publish.

- [ ] Publish и запустить Server Static host для `win-x64` с `PublishAot=true`, trimming enabled и invariant globalization только если игра не требует culture data. Проверить startup, fixed ticks, snapshot round-trip и clean shutdown.
- [ ] Publish и запустить Client Static host для `win-x64`. Проверить window creation, graphics backend initialization, один rendered frame, input initialization и clean shutdown. Headless graphics/window implementations допустимы только как отдельный предварительный test; финальный Client gate использует выбранные production backends.
- [ ] Выполнить десять последовательных process-isolated reload cycles Static host и проверить отсутствие orphan processes, locked publish files и роста сохранённого state payload.
- [ ] Собрать warning inventory `IL2xxx`, `IL3xxx`, `IL3050`; каждая suppression должна указывать узкий member/type и иметь тест. Acceptance требует отсутствия необъяснённых warnings.
- [ ] Сравнить Dynamic и Static module IDs, registered services, ECS systems и snapshot schema hash. Acceptance: полное равенство для одной selection, кроме documented Dynamic-only managed mods.
- [ ] После прохождения всех gates изменить default `KarpikCompositionMode` на `Static`; оставить явный opt-in `Dynamic` на один release cycle. Удаление Dynamic — отдельный последующий ExecPlan после телеметрии/использования.
- [ ] Создать ADR с причинами closed-world решения, границами managed mods, hot reload strategy, AOT evidence и rollback mode.
- [ ] Запустить targeted full verification commands из следующего раздела, обновить `Outcomes & Retrospective` и отметить ExecPlan завершённым.

## Concrete Steps

Все команды выполняются из `C:\Users\artem\RiderProjects\KarpikEngine` в PowerShell. После изменений кода обновлять graphify командой `graphify update .`.

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
11. `graphify update .` успешно обновляет knowledge graph после финальных code changes.

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
