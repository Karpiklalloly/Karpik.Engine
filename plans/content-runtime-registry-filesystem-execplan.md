# Content Runtime Registry and Filesystem Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make generated `AssetRef<T>` values available to ECS systems through an exported, automatically initialized `IContentRegistry`, while routing loose-content reads through the shared filesystem contract.

**Architecture:** `IFileSystem` and `PhysicalFileSystem` move from the legacy AssetManagement module into `Karpik.Engine.Core`, which already owns shared engine contracts and DI attributes. `ContentRegistry` becomes an Engine singleton exported as `IContentRegistry`; as an Autofac `IStartable`, it reads only `Content/manifest.json` on container startup and registers the loose `FileContentStore`. Asset payload I/O remains demand-driven through `LoadAsync`.

**Tech Stack:** .NET 10, C#, Autofac, System.Composition, xUnit, Dragon ECS, Karpik static and dynamic composition.

**Spec:** `docs/superpowers/specs/2026-09-16-content-runtime-registry-and-filesystem-design.md`

## Global Constraints

- Do not add file I/O, task waits, locks, allocations, or payload deserialization to `Update`, `FixedUpdate`, ECS `Run`, render, or network paths.
- `IContentRegistry.TryGet` remains the lock-free, non-allocating read path after content is loaded.
- `Karpik.Content.Runtime` may reference `Karpik.Engine.Core`; it must not reference `AssetManagement.Core` or `AssetsManager`.
- Keep the current loose `Content/` layout. Archive storage, pack indexing, hot reload, and implicit preloading are explicitly out of scope.
- Use constructor injection and `[Export]` plus `[ServiceRegistration]`; do not use field/property injection or a service locator.
- Execute tests with `-m:1 -nr:false` and build the smallest owning project.

---

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

After this change a game system can accept `IContentRegistry` in its constructor. At container startup, the registry parses the cooked content manifest from the runtime `Content/` directory and records every asset as `Unloaded`; it does not open cooked artifacts. A loading phase may call `LoadAsync(ContentRefs.X)`, while ECS frame systems only call `TryGet(ContentRefs.X, out lease)`.

The existing filesystem abstraction becomes engine infrastructure rather than a legacy-asset-manager detail. `FileContentStore` therefore reads through `IFileSystem`, leaving a single seam for a later archive-backed filesystem/store.

## Progress

- [x] (2026-09-16) Design approved and implementation plan created.
- [x] (2026-09-16) Move the filesystem contract and preserve all existing consumers. Commit `e42f737`; task review clean. The runner-test command compiled and ran 108 tests, while 20 unrelated named-pipe lifecycle tests remained blocked by sandbox permissions.
- [x] (2026-09-16) Route loose content storage through `IFileSystem` and prove lazy reads. Commit `2fc2e88`; task review approved. Deferred minor: traversal test checks no `OpenRead`, but not no `Exists`.
- [x] (2026-09-16) Add the exported registry contract and automatic manifest registration. Commit `d0baf35`; task review approved. Template validation is blocked before compilation by its literal `KarpikSdkVersionPlaceholder`; startup test strengthening is deferred to final review.
- [x] (2026-09-16) Run targeted unit, integration, and build validation. Current HEAD `92bd64d`: Content Runtime 24/24, static composition generator 51/51, dynamic Autofac composition 5/5. Raw template Client/Server builds remain blocked before compilation by `KarpikSdkVersionPlaceholder`; the broader Runner test suite has pre-existing named-pipe permission failures in this sandbox.

## Surprises & Discoveries

- Observation: `Karpik.Content.Runtime` currently has no production registration or startup path; it is referenced by the SDK but only tested directly.
  Evidence: `rg` finds `ContentRegistry` only in its runtime project and unit tests.
- Observation: `ContentManifest.LoadFromFile` directly calls `File.ReadAllText`, so startup cannot honour `IFileSystem` until the manifest has a stream-based loader.
  Evidence: `Karpik.Content.Core/ContentManifest.cs`.
- Observation: static composition emits factories for types with both `[Export]` and `[ServiceRegistration]`, and mirrors Autofac `IStartable` registration.
  Evidence: `Karpik.Engine.Core.Generator/Karpik.Engine.Core.Codegen/RuntimeCompositionGenerator.cs` and `Karpik.Engine.Core.Runner/StaticComposition/AutofacStaticServiceRegistry.cs`.

## Decision Log

- Decision: Put `IFileSystem` and `PhysicalFileSystem` under `Karpik.Engine.Core/FileSystem` with namespace `Karpik.Engine.Core.FileSystem`.
  Rationale: the contract contains engine locations (`ContentPath`, `ModsPath`) and is consumed by Shared, Client, and Server code. A new module or a duplicate content filesystem contract would add needless graph surface.
  Date/Author: 2026-09-16 / developer and Codex.
- Decision: Add `IContentRegistry` while retaining `ContentRegistry` as its only implementation.
  Rationale: systems should consume a contract; there is no factory, second registry, or configurable policy.
  Date/Author: 2026-09-16 / developer and Codex.
- Decision: Use `IStartable` for one-time manifest registration and retain lazy payload loading.
  Rationale: it runs before systems and does not require a synthetic ECS system that exists only to do bootstrap I/O.
  Date/Author: 2026-09-16 / Codex.

## Outcomes & Retrospective

Delivered an injectable, metadata-only `IContentRegistry`; systems can depend on its interface while demand-loading remains outside ECS frame paths. `IFileSystem` now belongs to Engine Core and backs loose content reads. Static and Dynamic composition both discover Core exports, including the filesystem service required to activate content services. No archive format was introduced.

Validation on `92bd64d`: Content Runtime 24/24, RuntimeCompositionGeneratorTests 51/51, and AutofacCompositionTests 5/5. Network-disabled NuGet audit warnings, pre-existing analyzer warnings, a raw uninstantiated template SDK placeholder, and sandbox named-pipe failures prevented clean broad/template evidence; none are caused by this change.

## Context and Orientation

`Modules/Shared/AssetManagement/AssetManagement.Core/IFileSystem.cs` currently defines root/content/mod paths and stream plus directory operations. Its `PhysicalFileSystem` is already an Engine singleton exported through `System.Composition`. `AssetsManager`, ECS template loading, Lua mods, templates, and runner tests consume it.

`Karpik.Content.Runtime/ContentRegistry.cs` holds the manifest-derived `ContentSlot` dictionary. `RegisterManifest` only creates slots. `LoadAsync` performs single-flight asynchronous store reads and `TryGet` makes a lock-free read. `Karpik.Content.Runtime/FileContentStore.cs` currently accesses `File.*` directly. `Karpik.Content.Core/ContentManifest.cs` parses canonical manifest JSON but its file loader also accesses `File` directly.

The runtime composition generator requires exported services to carry both `[Export]` and `[ServiceRegistration]`. An `IStartable` is registered in dynamic and static composition before ECS systems are resolved. `IFileSystem.ContentPath` is the runtime loose-content root; the SDK already copies cooked output to `$(TargetDir)Content`.

## Real-Time Assessment

No edited code runs in a frame loop by design. Container startup parses one manifest and allocates one slot per manifest entry; this cold-path cost is intentional. `LoadAsync` file I/O, Task creation, JSON decoding, and its per-slot write lock remain loading-path work. `TryGet` remains a dictionary lookup plus volatile reads and lease construction; it reads no files and takes no lock. No ECS component layout or fixed timestep changes occur. The change remains Shared/Client/Server neutral and does not create an AssetManagement dependency in Content Runtime.

## Plan of Work

First move the existing filesystem types into Core and update all compilation units to the new namespace. Then extend `ContentManifest` with a stream-based loader so runtime startup and loose-store reads share `IFileSystem`. Add the small registry interface, attribute-driven singleton registrations, and an `IStartable` implementation that opens only `Content/manifest.json`; inject `IContentStore` so a later package profile can select a different store without changing systems. Finally replace the store's direct `File` calls with `IFileSystem` streams and add regression tests for startup laziness and path safety.

## Milestones

### Milestone 1: Filesystem contract is Core infrastructure

The project compiles with `IFileSystem` and `PhysicalFileSystem` physically located in `Karpik.Engine.Core/FileSystem`; all previous consumers import the new namespace and no source defines either type under AssetManagement.

### Milestone 2: Registry is a DI service with lazy startup metadata

`IContentRegistry` is exported through the existing composition model. `ContentRegistry.Start()` reads a manifest through the injected filesystem, registers metadata with the injected store, and makes no artifact read. Its public `LoadAsync`, `TryGet`, and `IsAlive` behaviour is unchanged.

### Milestone 3: Loose content uses the shared filesystem seam

`FileContentStore` implements and exports `IContentStore`, receives `IFileSystem`, validates locator containment below `ContentPath`, and reads artifacts through `OpenRead`. Tests prove normal reads, traversal rejection, and one-read single-flight loading.

## Concrete Steps

### Task 1: Move filesystem ownership to Core

**Files:**
- Create: `Karpik.Engine.Core/FileSystem/IFileSystem.cs`
- Create: `Karpik.Engine.Core/FileSystem/PhysicalFileSystem.cs`
- Delete: `Modules/Shared/AssetManagement/AssetManagement.Core/IFileSystem.cs`
- Delete: `Modules/Shared/AssetManagement/AssetManagement.Core/PhysicalFileSystem.cs`
- Modify: every file found by `rg -l '\bIFileSystem\b|\bPhysicalFileSystem\b' --glob '*.cs'` to import `Karpik.Engine.Core.FileSystem`.
- Test: `Karpik.Engine.Core.Runner.Tests/AssetsManagerRuntimeBundleTests.cs` and `Karpik.Engine.Core.Runner.Tests/ShaderAssetLoadingTests.cs` compile unchanged except namespace imports.

**Consumes:** existing `IFileSystem` signatures and `PhysicalFileSystem` behaviour.

**Produces:** `Karpik.Engine.Core.FileSystem.IFileSystem` and `Karpik.Engine.Core.FileSystem.PhysicalFileSystem` with the same public API and Engine singleton exports.

- [ ] **Step 1: Write the failing namespace-move test**

Add a compilation reference in the smallest existing Core-runner test that constructs the Core-qualified filesystem:

```csharp
var fileSystem = new Karpik.Engine.Core.FileSystem.PhysicalFileSystem();
Assert.NotNull(fileSystem);
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `C:\Users\artem\RiderProjects\KarpikEngine`:

```powershell
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore
```

Expected: compilation fails because `Karpik.Engine.Core.FileSystem.PhysicalFileSystem` does not exist.

- [ ] **Step 3: Move the two source files without changing their API**

Place the interface and physical implementation in `Karpik.Engine.Core/FileSystem`, set their namespace to `Karpik.Engine.Core.FileSystem`, retain both existing `[Export]` attributes and `[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]`, then update imports throughout the repository. Remove the two old definitions so exactly one public `IFileSystem` remains.

- [ ] **Step 4: Run the test to verify it passes**

Run the Task 1 command again.

Expected: exit code 0; the two existing asset-manager runner tests compile and execute with the Core filesystem type.

- [ ] **Step 5: Commit the focused migration**

```powershell
git add Karpik.Engine.Core Modules Karpik.Engine.Core.Runner.Tests templates
git commit -m "refactor: move filesystem contract to engine core"
```

### Task 2: Add a stream manifest loader and filesystem-backed loose store

**Files:**
- Modify: `Karpik.Content.Core/ContentManifest.cs`
- Modify: `Karpik.Content.Runtime/FileContentStore.cs`
- Modify: `Karpik.Content.Runtime/Karpik.Content.Runtime.csproj`
- Create or modify: `Karpik.Content.Runtime.Tests/FileContentStoreTests.cs`

**Consumes:** `IFileSystem.OpenRead`, `IFileSystem.ContentPath`, and existing `ContentManifest.Parse(string)`.

**Produces:** `ContentManifest.Load(Stream, List<ContentDiagnostic>? diagnostics = null)` and `FileContentStore(IFileSystem fileSystem)` with no `File.*` calls.

- [ ] **Step 1: Write failing tests for stream-only access**

Introduce a minimal `IFileSystem` fake whose `OpenRead` returns a `MemoryStream` and whose `Exists` records its argument. Add tests equivalent to:

```csharp
[Fact]
public void Get_ContainedLocator_ReadsThroughFileSystem()
{
    var fs = new FakeFileSystem("C:/game/Content", ["artifacts/a.cooked", "data"]);
    var store = new FileContentStore(fs);
    Assert.Equal("data", Encoding.UTF8.GetString(store.Get("artifacts/a.cooked").Span));
}

[Fact]
public void Get_Traversal_ThrowsBeforeOpeningFile()
{
    var fs = new FakeFileSystem("C:/game/Content", []);
    Assert.Throws<InvalidDataException>(() => new FileContentStore(fs).Get("../secret"));
    Assert.Equal(0, fs.OpenReadCalls);
}
```

Add a `ContentManifest.Load(Stream)` test that parses a canonical manifest held solely in `MemoryStream`.

- [ ] **Step 2: Run tests to verify they fail**

```powershell
dotnet test Karpik.Content.Runtime.Tests\Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~FileContentStoreTests|FullyQualifiedName~ContentManifest"
```

Expected: compilation fails because the store has no `IFileSystem` constructor and manifest has no stream loader.

- [ ] **Step 3: Implement the smallest shared-stream path**

Implement `ContentManifest.Load(Stream, ...)` with a `StreamReader` and delegate to `Parse`. Keep `LoadFromFile` as a build-tool convenience that opens a stream and delegates to `Load`.

Change `FileContentStore` to retain injected `IFileSystem`; combine `fileSystem.ContentPath` with the normalized locator, validate the resulting full path remains below `ContentPath`, test `fileSystem.Exists`, then read via `fileSystem.OpenRead`. `GetAsync` may perform the same read on the existing background Task path because this is loading-only code. Add a project reference from Content Runtime to `Karpik.Engine.Core`; do not reference AssetManagement.

- [ ] **Step 4: Run tests to verify they pass**

Run the Task 2 command again.

Expected: all selected tests pass, including direct proof that traversal never opens a stream.

- [ ] **Step 5: Commit the storage seam**

```powershell
git add Karpik.Content.Core Karpik.Content.Runtime Karpik.Content.Runtime.Tests
git commit -m "refactor: route loose content through filesystem"
```

### Task 3: Export the registry contract and initialize manifest metadata at startup

**Files:**
- Create: `Karpik.Content.Runtime/IContentRegistry.cs`
- Modify: `Karpik.Content.Runtime/ContentRegistry.cs`
- Modify: `Karpik.Content.Runtime/FileContentStore.cs`
- Modify: `Karpik.Content.Runtime/Karpik.Content.Runtime.csproj`
- Modify: `Karpik.Content.Runtime.Tests/ContentRegistryTests.cs`

**Consumes:** `IContentStore`, `IFileSystem`, `ContentManifest.Load(Stream)`, Autofac `IStartable`, and Core composition attributes.

**Produces:** an exported `IContentRegistry` singleton and `IContentStore` loose singleton; `ContentRegistry.Start()` registers `Content/manifest.json` without requesting any artifact locator.

- [ ] **Step 1: Write the failing startup-laziness test**

Add a fake filesystem containing only `manifest.json` and a counting `IContentStore`. Construct the intended registry with both dependencies and call `Start`:

```csharp
[Fact]
public void Start_RegistersManifest_WithoutReadingArtifacts()
{
    var store = new CountingStore();
    var registry = new ContentRegistry(new FakeFileSystem(manifestJson), store);

    registry.Start();

    Assert.Equal(0, store.Calls);
    Assert.False(registry.TryGet(new AssetRef<RawJsonPayload>(knownId), out _));
}
```

Add a reflection assertion that `ContentRegistry` has exports for `IContentRegistry` and `ContentRegistry`, plus an Engine/singleton `ServiceRegistrationAttribute`.

- [ ] **Step 2: Run the test to verify it fails**

```powershell
dotnet test Karpik.Content.Runtime.Tests\Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore --filter "FullyQualifiedName~ContentRegistryTests"
```

Expected: compilation fails because `IContentRegistry`, the injected constructor, and `Start` do not exist.

- [ ] **Step 3: Implement the exported contract and cold-path startup**

Define `IContentRegistry` with the existing public members:

```csharp
void RegisterManifest(ContentManifest manifest, IContentStore store);
bool IsAlive<T>(AssetRef<T> asset);
bool IsAlive(AssetId id, uint version);
bool TryGet<T>(AssetRef<T> asset, out AssetLease<T> lease);
Task LoadAsync<T>(AssetRef<T> asset, CancellationToken ct = default);
```

Have `ContentRegistry` implement `IContentRegistry` and `IStartable`; inject `IFileSystem` and `IContentStore`. Add `[Export(typeof(IContentRegistry))]`, `[Export(typeof(ContentRegistry))]`, and `[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]`. In `Start`, combine `fileSystem.ContentPath` and `manifest.json`, open it through `IFileSystem.OpenRead`, call `ContentManifest.Load(stream)`, then call `RegisterManifest(manifest, store)`. Do not call `store.Get` or `store.GetAsync`.

Export `FileContentStore` as `IContentStore` with the same Engine singleton attributes. Add explicit Content Runtime package references needed for its public composition attributes and `IStartable`; keep them version-aligned with `Karpik.Engine.Core`.

- [ ] **Step 4: Run targeted tests to verify they pass**

Run the Task 3 command again.

Expected: registry tests pass; startup records manifest entries but makes zero artifact-store calls. Existing concurrent-load test still proves exactly one store read for two callers.

- [ ] **Step 5: Build static and dynamic composition consumers**

```powershell
dotnet build templates\Karpik.Game\Source\KarpikGame.Server\KarpikGame.Server.csproj -m:1 -nr:false --no-restore
dotnet build templates\Karpik.Game\Source\KarpikGame.Client\KarpikGame.Client.csproj -m:1 -nr:false --no-restore
```

Expected: both builds exit 0 and emit no `KE304`–`KE307` diagnostics.

- [ ] **Step 6: Commit the registry integration**

```powershell
git add Karpik.Content.Runtime Karpik.Content.Runtime.Tests
git commit -m "feat: export initialized content registry"
```

## Validation and Acceptance

Run from `C:\Users\artem\RiderProjects\KarpikEngine` after all tasks:

```powershell
dotnet test Karpik.Content.Runtime.Tests\Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore
dotnet test Karpik.Engine.Core.Runner.Tests\Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false --no-restore
dotnet build templates\Karpik.Game\Source\KarpikGame.Server\KarpikGame.Server.csproj -m:1 -nr:false --no-restore
dotnet build templates\Karpik.Game\Source\KarpikGame.Client\KarpikGame.Client.csproj -m:1 -nr:false --no-restore
```

Acceptance requires every command to exit 0. Tests must demonstrate: the registry can be constructor-injected through `IContentRegistry`; `Start` reads only the manifest and makes zero store artifact reads; duplicate `LoadAsync` calls read one artifact; `TryGet` before load returns false; loose storage uses `IFileSystem`; and `../` locators are rejected before opening a stream.

## Idempotence and Recovery

All tests and builds are read-only except normal `bin/obj` outputs and may be rerun. Moving the filesystem types is source-only and can be reverted as one commit if a non-content consumer fails. `IStartable` startup must fail loudly for a missing/corrupt manifest rather than silently running with an empty registry; recover by rebuilding/copying `Content/manifest.json`, then rerun the affected build. Do not delete output roots, user content, SDK installations, or bundles during this work.

## Artifacts and Notes

- Approved design: `docs/superpowers/specs/2026-09-16-content-runtime-registry-and-filesystem-design.md`
- Existing content build contract: `docs/02_ADR/content-pipeline-build-contract.md`
- Future archive storage uses the retained `IContentStore` seam. Add `PackContentStore` only with a specified archive header/index, bounded reads, corruption checks, and package-format tests.
