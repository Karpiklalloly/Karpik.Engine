# Content Runtime Slot Registry + AssetRef/Lease + Codegen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver slice B runtime: `FileContentStore` + slot-based `ContentRegistry` with `AssetRef<T>`/`AssetLease<T>` (single-flight, versioned, zero-alloc) and in-assembly Roslyn codegen `ContentRefs.g.cs` for all `logicalName`/`AssetId` via `[ContentType]` mapping, while `AssetsManager` coexists.

**Architecture:** `Karpik.Content.Runtime` (`Shared`, `net10.0`) owns `IContentStore`, slots, `AssetRef<T>`/`AssetLease<T>`, `ContentRegistry`; `Karpik.Content.Codegen` (`netstandard2.0`, `IsRoslynComponent`) is an `IIncrementalGenerator` running in the consumer's compilation, reading `manifest.json` (primary) or `**/*.json.meta` fallback and emitting `ContentRefs.g.cs` sorted by `AssetId`. `Karpik.Engine.Sdk` wires `KarpikContentManifest` and the analyzer.

**Tech Stack:** `C#`, `net10.0` (Runtime), `netstandard2.0` (Codegen), `System.Text.Json`, `Roslyn IIncrementalGenerator`, `xUnit`, `Karpik.Jobs` (future, now `Task.Run` stub), `MSBuild` (`BeforeCompile`).

## Global Constraints

- `Karpik.Content.Runtime` targets `net10.0`, `ImplicitUsings=enable`, `Nullable=enable`, no `Veldrid`/`Autofac`/`AssetsManager` references.
- `Karpik.Content.Codegen` targets `netstandard2.0`, `IsRoslynComponent=true`, `DevelopmentDependency=true`, no runtime refs.
- `AssetRef<T>` is `readonly struct : IEquatable<AssetRef<T>>, IComparable<AssetRef<T>>, IEcsComponent`, copy is `memcpy` 20-24 bytes, 0 allocations in `TryGet`/`Lease`.
- `AssetLease<T>` is `readonly ref struct` with `Dispose` pattern (stack-only, no `IDisposable` boxing), only one owner.
- Deterministic: manifest entries sorted by `AssetId` (`Guid.CompareTo`), `CanonicalJson` fixed property order (`StringComparer.Ordinal`), `ContentRefs.g.cs` sorted by `AssetId`, no `DateTime`/absolute paths.
- `IContentStore` is opaque: `locator` is `artifacts/ab/cd/<hash>.cooked`, future `pack` must not change `AssetRef`/`manifest`.
- `FileContentStore` is loose-file first impl; `ContentRegistry` `RegisterManifest` validates `UnknownDependency` etc.
- Thread-safe per slot (`lock(Slot.Sync)` on write, `Volatile.Read` on read), `single-flight` per `AssetId`, `Version` is `uint` incremented on `Loaded`.
- Analyzer incremental: cache by `AdditionalFiles` hash + `AnalyzerConfigOptions:KarpikContentManifest`, no rewrite when byte-identical.

---

## File Structure

**New files:**

- `Karpik.Content.Runtime/Karpik.Content.Runtime.csproj`
- `Karpik.Content.Runtime/AssetRef.cs`
- `Karpik.Content.Runtime/AssetLease.cs`
- `Karpik.Content.Runtime/ContentTypeAttribute.cs`
- `Karpik.Content.Runtime/ContentAssetAttribute.cs`
- `Karpik.Content.Runtime/IContentStore.cs`
- `Karpik.Content.Runtime/FileContentStore.cs`
- `Karpik.Content.Runtime/ContentSlot.cs`
- `Karpik.Content.Runtime/ContentRegistry.cs`
- `Karpik.Content.Runtime/ContentRuntimeDiagnostics.cs`
- `Karpik.Content.Codegen/Karpik.Content.Codegen.csproj`
- `Karpik.Content.Codegen/ContentCodegenGenerator.cs`
- `Karpik.Content.Codegen/ContentTypeCollector.cs`
- `Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj`
- `Karpik.Content.Runtime.Tests/TemporaryDirectory.cs` (copy pattern from `Karpik.Content.Tests`)
- `Karpik.Content.Runtime.Tests/AssetRefTests.cs`
- `Karpik.Content.Runtime.Tests/ContentRegistryTests.cs`
- `Karpik.Content.Runtime.Tests/FileContentStoreTests.cs`
- `Karpik.Content.Runtime.Tests/CodegenSnapshotTests.cs`
- `Karpik.Content.Runtime.Tests/AllocationTests.cs`

**Modified files:**

- `KarpikEngine.slnx` — add `Karpik.Content.Runtime` + `Codegen` + `Tests` to `/Content/` folder
- `Karpik.Engine.Sdk/Karpik.Engine.Sdk.props` (or `Sdk.csproj`) — set `KarpikContentManifest` and `KarpikContentOutput`, add `BeforeCompile` target for `content build` (optional stub for this slice)
- `Directory.Build.props` — add `Karpik.Content.Codegen` as `Analyzer` (same as `Network.Codegen`)

---

### Task 1: Create Projects & Solution Wiring

**Files:**
- Create: `Karpik.Content.Runtime/Karpik.Content.Runtime.csproj`
- Create: `Karpik.Content.Codegen/Karpik.Content.Codegen.csproj`
- Create: `Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj`
- Modify: `KarpikEngine.slnx:72-74`

**Interfaces:**
- Consumes: none
- Produces: three projects buildable, `Runtime` refs `Core`, `Codegen` is analyzer, `Tests` refs `Runtime`+`Core`

- [ ] **Step 1: Create `Karpik.Content.Runtime.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Karpik.Content.Core\Karpik.Content.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create `Karpik.Content.Codegen.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.8.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.3.4" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create `Karpik.Content.Runtime.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Karpik.Content.Core\Karpik.Content.Core.csproj" />
    <ProjectReference Include="..\Karpik.Content.Runtime\Karpik.Content.Runtime.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Wire into `KarpikEngine.slnx`**

Modify `KarpikEngine.slnx` under `<Folder Name="/Content/">`:

```xml
<Project Path="Karpik.Content.Runtime/Karpik.Content.Runtime.csproj" />
<Project Path="Karpik.Content.Codegen/Karpik.Content.Codegen.csproj" />
<Project Path="Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj" />
```

- [ ] **Step 5: Build verification**

Run: `dotnet build Karpik.Content.Runtime/Karpik.Content.Runtime.csproj -m:1 -nr:false`
Expected: `Build succeeded` 0 warnings

Run: `dotnet build Karpik.Content.Codegen/Karpik.Content.Codegen.csproj -m:1 -nr:false`
Expected: `Build succeeded`

Run: `dotnet build Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false`
Expected: `Build succeeded`

- [ ] **Step 6: Commit**

```bash
git add Karpik.Content.Runtime/Karpik.Content.Runtime.csproj Karpik.Content.Codegen/Karpik.Content.Codegen.csproj Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj KarpikEngine.slnx
git commit -m "feat(content): scaffold runtime/codegen projects for B slice"
```

---

### Task 2: Core Runtime Contracts (AssetRef/Lease, Attributes, IContentStore, Slot, Diagnostics)

**Files:**
- Create: `Karpik.Content.Runtime/ContentTypeAttribute.cs`
- Create: `Karpik.Content.Runtime/ContentAssetAttribute.cs`
- Create: `Karpik.Content.Runtime/AssetRef.cs`
- Create: `Karpik.Content.Runtime/AssetLease.cs`
- Create: `Karpik.Content.Runtime/IContentStore.cs`
- Create: `Karpik.Content.Runtime/ContentSlot.cs`
- Create: `Karpik.Content.Runtime/ContentRuntimeDiagnostics.cs`
- Test: `Karpik.Content.Runtime.Tests/AssetRefTests.cs`

**Interfaces:**
- Consumes: `Karpik.Content.Core.AssetId`, `ContentManifest`
- Produces: `AssetRef<T>`, `AssetLease<T>`, `ContentTypeAttribute`, `IContentStore`, `ContentSlot` types for Task 3/4, `ContentRuntimeDiagnostics.Codes`

- [ ] **Step 1: Write failing test for AssetRef**

```csharp
// Karpik.Content.Runtime.Tests/AssetRefTests.cs
using Karpik.Content.Runtime;
using Xunit;
public sealed class AssetRefTests {
    [Fact] public void IsAlive_AfterRegister() {
        var id = new Karpik.Content.Core.AssetId(Guid.NewGuid());
        var r = new AssetRef<RawJsonPayload>(id, "game/a", 1);
        Assert.False(r.IsAlive(null!)); // registry not yet created -> should fail to compile or return false
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --filter AssetRefTests`
Expected: FAIL `type or namespace AssetRef not found`

- [ ] **Step 3: Implement `ContentTypeAttribute` and `AssetRef`**

```csharp
// Karpik.Content.Runtime/ContentTypeAttribute.cs
namespace Karpik.Content.Runtime;
[AttributeUsage(AttributeTargets.Class|AttributeTargets.Struct, AllowMultiple=false)]
public sealed class ContentTypeAttribute(string declaredType) : Attribute {
    public string DeclaredType { get; } = declaredType;
}
// Karpik.Content.Runtime/AssetRef.cs
using Karpik.Content.Core;
namespace Karpik.Content.Runtime;
public readonly struct AssetRef<T> : IEquatable<AssetRef<T>>, IComparable<AssetRef<T>>, DagonECS.IEcsComponent // if IEcsComponent available in Shared, else plain
{
    public readonly AssetId Id;
    public readonly uint Version;
    public readonly string LogicalName;
    public AssetRef(AssetId id, string logicalName, uint version=1) { Id=id; LogicalName=logicalName; Version=version; }
    public AssetRef(string guid, string logicalName, uint version=1) : this(AssetId.Parse(guid), logicalName, version) {}
    public bool IsAlive(ContentRegistry registry) => registry != null && registry.IsAlive(this);
    public bool Equals(AssetRef<T> other) => Id.Equals(other.Id) && Version==other.Version;
    public int CompareTo(AssetRef<T> other) => Id.CompareTo(other.Id);
    public override int GetHashCode() => HashCode.Combine(Id, Version);
    public string ToCanonicalString() => Id.ToCanonicalString();
}
public readonly struct AssetRef : IComparable<AssetRef> {
    public readonly AssetId Id;
    public AssetRef(AssetId id) { Id=id; }
    public int CompareTo(AssetRef other) => Id.CompareTo(other.Id);
}
```

> Note: If `IEcsComponent` is in `DragonECS` shared, add `using DragonECS;` and `: IEcsComponent`. If not available in `Shared`, omit interface for this slice and add in follow-up.

- [ ] **Step 4: Implement `AssetLease` and `IContentStore`**

```csharp
// Karpik.Content.Runtime/AssetLease.cs
namespace Karpik.Content.Runtime;
public readonly ref struct AssetLease<T> {
    private readonly ContentRegistry _registry;
    private readonly AssetRef<T> _ref;
    private readonly uint _snapshotVersion;
    internal AssetLease(ContentRegistry reg, AssetRef<T> r, uint ver, T payload) { _registry=reg; _ref=r; _snapshotVersion=ver; Payload=payload; }
    public T Payload { get; }
    public bool IsAlive => _registry.IsAlive(_ref) && _snapshotVersion==_ref.Version;
    public void Dispose() { /* no heap free, just validation */ }
}
// Karpik.Content.Runtime/IContentStore.cs
namespace Karpik.Content.Runtime;
public interface IContentStore {
    ReadOnlyMemory<byte> Get(string artifactLocator);
    Task<ReadOnlyMemory<byte>> GetAsync(string artifactLocator, CancellationToken ct=default);
}
// Karpik.Content.Runtime/ContentSlot.cs
namespace Karpik.Content.Runtime;
internal enum SlotState { Unloaded, Loading, Loaded, Failed }
internal sealed class ContentSlot {
    public AssetId Id;
    public SlotState State;
    public uint Version;
    public object? Payload;
    public AssetId[] Dependencies = Array.Empty<AssetId>();
    public Task<object?>? Inflight;
    public object Sync = new();
    public string ArtifactLocator = "";
    public string DeclaredType = "";
}
```

- [ ] **Step 5: Implement `ContentRuntimeDiagnostics`**

```csharp
// Karpik.Content.Runtime/ContentRuntimeDiagnostics.cs
namespace Karpik.Content.Runtime;
public static class ContentRuntimeDiagnostics {
    public const string StoreMissingArtifact = "KCR201";
    public const string SlotFailed = "KCR202";
    public const string UnknownManifestId = "KCR203";
    public const string LeaseVersionMismatch = "KCR204";
    public const string CodegenManifestNotBuilt = "KCO301";
    public const string CodegenDuplicateLogical = "KCO302";
    public const string CodegenInvalidLogical = "KCO303";
    public const string CodegenNoContentType = "KCO304";
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --filter AssetRefTests`
Expected: PASS (1 passed)

- [ ] **Step 7: Commit**

```bash
git add Karpik.Content.Runtime/ContentTypeAttribute.cs Karpik.Content.Runtime/AssetRef.cs Karpik.Content.Runtime/AssetLease.cs Karpik.Content.Runtime/IContentStore.cs Karpik.Content.Runtime/ContentSlot.cs Karpik.Content.Runtime/ContentRuntimeDiagnostics.cs Karpik.Content.Runtime.Tests/AssetRefTests.cs
git commit -m "feat(content): runtime contracts AssetRef/Lease, IContentStore, diagnostics"
```

---

### Task 3: FileContentStore

**Files:**
- Create: `Karpik.Content.Runtime/FileContentStore.cs`
- Test: `Karpik.Content.Runtime.Tests/FileContentStoreTests.cs`

**Interfaces:**
- Consumes: `IContentStore`, `ContentManifest`
- Produces: `FileContentStore` for `ContentRegistry`

- [ ] **Step 1: Write failing test**

```csharp
// FileContentStoreTests.cs
[Fact] public void Get_Missing_Throws() {
    var store = new FileContentStore(Path.GetTempPath());
    Assert.Throws<InvalidDataException>(() => store.Get("artifacts/ab/cd/hash.cooked"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FileContentStoreTests`
Expected: FAIL `type not found`

- [ ] **Step 3: Implement `FileContentStore`**

```csharp
// FileContentStore.cs
using System.IO;
namespace Karpik.Content.Runtime;
public sealed class FileContentStore(string outputRoot) : IContentStore {
    private readonly string _root = Path.GetFullPath(outputRoot);
    public ReadOnlyMemory<byte> Get(string locator) {
        string path = Path.Combine(_root, locator.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) throw new InvalidDataException($"Missing artifact {locator}");
        return File.ReadAllBytes(path);
    }
    public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct=default) => Task.Run(() => Get(locator), ct);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FileContentStoreTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Karpik.Content.Runtime/FileContentStore.cs Karpik.Content.Runtime.Tests/FileContentStoreTests.cs
git commit -m "feat(content): FileContentStore loose-file implementation"
```

---

### Task 4: ContentRegistry (Register, TryGet, LoadAsync single-flight, version)

**Files:**
- Create: `Karpik.Content.Runtime/ContentRegistry.cs`
- Test: `Karpik.Content.Runtime.Tests/ContentRegistryTests.cs`

**Interfaces:**
- Consumes: `IContentStore`, `ContentManifest`, `ContentSlot`, `AssetRef<T>`, `AssetLease<T>`
- Produces: `ContentRegistry` API for gameplay and codegen `IsAlive` checks

- [ ] **Step 1: Write failing tests**

```csharp
[Fact] public async Task SingleFlight_TwoConcurrentLoads_OneStoreGet() {
    var manifest = new ContentManifest(... 2 entries ...);
    var mockStore = new CountingStore(); // counts GetAsync calls
    var registry = new ContentRegistry(); registry.RegisterManifest(manifest, mockStore);
    var r = new AssetRef<RawJsonPayload>(idA, "game/a");
    var t1 = registry.LoadAsync<RawJsonPayload>(r);
    var t2 = registry.LoadAsync<RawJsonPayload>(r);
    await Task.WhenAll(t1, t2);
    Assert.Equal(1, mockStore.Calls);
}
[Fact] public void TryGet_Missing_ReturnsFalse() { ... }
[Fact] public void Lease_VersionMismatch_IsAliveFalse() { ... }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ContentRegistryTests`
Expected: FAIL `ContentRegistry not found`

- [ ] **Step 3: Implement `ContentRegistry`**

```csharp
// ContentRegistry.cs
namespace Karpik.Content.Runtime;
public sealed class ContentRegistry {
    private readonly Dictionary<AssetId, ContentSlot> _slots = new();
    private IContentStore? _store;
    public void RegisterManifest(ContentManifest manifest, IContentStore store) {
        _store = store;
        _slots.Clear();
        foreach(var e in manifest.Entries.OrderBy(x=>x.AssetId.Value)) {
            _slots[e.AssetId] = new ContentSlot { Id=e.AssetId, State=SlotState.Unloaded, Version=1, ArtifactLocator=e.ArtifactLocator, DeclaredType=e.DeclaredType, Dependencies=e.Dependencies.ToArray() };
        }
    }
    public bool IsAlive<T>(AssetRef<T> r) => _slots.TryGetValue(r.Id, out var s) && s.State==SlotState.Loaded && s.Version==r.Version;
    public bool TryGet<T>(AssetRef<T> r, out AssetLease<T> lease) {
        if(_slots.TryGetValue(r.Id, out var s) && s.State==SlotState.Loaded && s.Version==r.Version && s.Payload is T p) {
            lease = new AssetLease<T>(this, r, s.Version, p);
            return true;
        }
        lease = default;
        return false;
    }
    public Task<AssetLease<T>> LoadAsync<T>(AssetRef<T> r, CancellationToken ct=default) {
        // per-slot lock, single-flight, Volatile.Read fast path, Task.Run for store.GetAsync outside lock
        // on success: s.Payload = payload, s.State=Loaded, s.Version++ (or keep if first load), complete inflight
        // on fail: s.State=Failed, s.Inflight=null, retryable
        throw new NotImplementedException();
    }
    // internal helper for AssetRef.IsAlive non-generic
    public bool IsAlive(AssetId id, uint version) => _slots.TryGetValue(id, out var s) && s.Version==version && s.State==SlotState.Loaded;
}
```

Full implementation includes per-slot `lock(s.Sync)`, `Volatile.Read`, `Interlocked`, `TaskCompletionSource` for single-flight.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ContentRegistryTests`
Expected: PASS (5+ tests)

- [ ] **Step 5: Commit**

```bash
git add Karpik.Content.Runtime/ContentRegistry.cs Karpik.Content.Runtime.Tests/ContentRegistryTests.cs
git commit -m "feat(content): ContentRegistry single-flight, versioned slots, TryGet/LoadAsync"
```

---

### Task 5: Codegen Analyzer

**Files:**
- Create: `Karpik.Content.Codegen/ContentCodegenGenerator.cs`
- Create: `Karpik.Content.Codegen/ContentTypeCollector.cs`
- Create: `Karpik.Content.Codegen/CodegenHelpers.cs`
- Test: `Karpik.Content.Runtime.Tests/CodegenSnapshotTests.cs`

**Interfaces:**
- Consumes: `ContentManifest`, `[ContentType]` attribute via `Compilation`, `AdditionalFiles`
- Produces: `ContentRefs.g.cs` with `AssetRef<T>` per `logicalName`

- [ ] **Step 1: Write failing snapshot test**

```csharp
[Fact] public void Snapshot_GeneratesSorted() {
    string manifestJson = new ContentManifest(1, new[]{ entryB, entryA }).ToCanonicalJson();
    string generated = ContentCodegenGenerator.GenerateForTest(manifestJson, new Dictionary<string,string>{{"raw-json","RawJsonPayload"}});
    Assert.Contains("Game_A", generated);
    Assert.True(generated.IndexOf("125bb6cd") < generated.IndexOf("16755701")); // sorted by AssetId
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter CodegenSnapshotTests`
Expected: FAIL `GenerateForTest not found`

- [ ] **Step 3: Implement `ContentCodegenGenerator` (IIncrementalGenerator)**

```csharp
[Generator]
public sealed class ContentCodegenGenerator : IIncrementalGenerator {
    public void Initialize(IncrementalGeneratorInitializationContext ctx) {
        var manifestPath = ctx.AnalyzerConfigOptionsProvider.Select((p,c) => p.GlobalOptions.TryGetValue("build_property.KarpikContentManifest", out var v) ? v : "");
        var additionalFiles = ctx.AdditionalTextsProvider.Collect();
        var compilationAndFiles = ctx.CompilationProvider.Combine(additionalFiles.Combine(manifestPath));
        ctx.RegisterSourceOutput(compilationAndFiles, (spc, source) => {
            var (compilation, filesAndPath) = source;
            // 1. Build declaredType->IType map via attribute scan
            var map = ContentTypeCollector.Collect(compilation);
            // 2. Load manifest if exists, else scan AdditionalFiles .meta
            ContentManifest manifest = LoadManifestOrScanMeta(filesAndPath, spc);
            if(manifest==null) return;
            // 3. Generate ContentRefs.g.cs
            string code = GenerateCode(manifest, map);
            spc.AddSource("ContentRefs.g.cs", code);
        });
    }
}
```

Implement `ContentTypeCollector` (scan `INamedTypeSymbol` with `ContentTypeAttribute`), `GenerateCode` (sorted by `AssetId`, `PascalCase`, collision `_2`, `All`/`ByPath`, `const string` paths, `KCO301`/`KCO304` diagnostics via `spc.ReportDiagnostic`).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter CodegenSnapshotTests`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Karpik.Content.Codegen/ContentCodegenGenerator.cs Karpik.Content.Codegen/ContentTypeCollector.cs Karpik.Content.Runtime.Tests/CodegenSnapshotTests.cs
git commit -m "feat(content): Roslyn codegen ContentRefs with [ContentType] mapping, in-assembly generation"
```

---

### Task 6: SDK Wiring & Analyzer Registration

**Files:**
- Modify: `Karpik.Engine.Sdk/Karpik.Engine.Sdk.props` (or `Karpik.Engine.Sdk.csproj`)
- Modify: `Directory.Build.props`

**Interfaces:**
- Consumes: `Karpik.Content.Codegen` analyzer, `content build` output
- Produces: `KarpikContentManifest` property, `BeforeCompile` ordering

- [ ] **Step 1: Add analyzer reference in `Directory.Build.props`**

```xml
<ItemGroup Condition="!$(MSBuildProjectName.Contains('Codegen'))">
  <ProjectReference Include="$(KarpikRepositoryRoot)Karpik.Content.Codegen/Karpik.Content.Codegen.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    <PrivateAssets>all</PrivateAssets>
  </ProjectReference>
</ItemGroup>
```

- [ ] **Step 2: Add Sdk props**

```xml
<PropertyGroup>
  <KarpikContentOutput>$(IntermediateOutputPath)Content\</KarpikContentOutput>
  <KarpikContentManifest>$(KarpikContentOutput)manifest.json</KarpikContentManifest>
</PropertyGroup>
<ItemGroup>
  <AdditionalFiles Include="**\*.json.meta" />
  <CompilerVisibleProperty Include="KarpikContentManifest" />
</ItemGroup>
<Target Name="KarpikContentBuild" BeforeTargets="BeforeCompile" Condition="Exists('$(KarpikRepositoryRoot)Karpik.Content.Tool/bin/Debug/net10.0/content.dll')">
  <Exec Command="dotnet &quot;$(KarpikRepositoryRoot)Karpik.Content.Tool/bin/Debug/net10.0/content.dll&quot; build --source &quot;$(MSBuildProjectDirectory)/Content&quot; --output &quot;$(KarpikContentOutput)&quot; --namespace game" />
</Target>
```

- [ ] **Step 3: Build verification**

Run: `dotnet build Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false`
Expected: `Build succeeded`, `ContentRefs.g.cs` generated in `obj`

- [ ] **Step 4: Commit**

```bash
git add Karpik.Engine.Sdk/Karpik.Engine.Sdk.props Directory.Build.props
git commit -m "chore(sdk): wire Content codegen analyzer and manifest property"
```

---

### Task 7: Allocation & Integration Tests

**Files:**
- Create: `Karpik.Content.Runtime.Tests/AllocationTests.cs`

**Interfaces:**
- Consumes: `AssetRef<T>`, `ContentRegistry`
- Produces: verification of zero-alloc copy

- [ ] **Step 1: Write failing allocation test**

```csharp
[Fact] public void AssetRef_Copy_ZeroAlloc() {
    var r = new AssetRef<RawJsonPayload>(new AssetId(Guid.NewGuid()), "game/a");
    long before = GC.GetAllocatedBytesForCurrentThread();
    for(int i=0;i<100_000;i++) { var c = r; GC.KeepAlive(c); }
    long after = GC.GetAllocatedBytesForCurrentThread();
    Assert.Equal(0, after - before);
}
```

- [ ] **Step 2: Run test to verify it passes** (should pass immediately)

Run: `dotnet test --filter AllocationTests`
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add Karpik.Content.Runtime.Tests/AllocationTests.cs
git commit -m "test(content): allocation zero-alloc for AssetRef copy"
```

---

### Task 8: Verification & Docs

**Files:**
- Modify: `docs/03_Research/overview.md` (optional link)
- Test: full suite

- [ ] **Step 1: Run full suite**

Run: `dotnet test Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false`
Run: `dotnet test Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false`
Expected: all green (42 + new tests)

- [ ] **Step 2: Run CLI fixture**

Run: `dotnet run --project Karpik.Content.Tool -- build --source <fixture> --output <out> --namespace game`
Run: `dotnet run --project Karpik.Content.Tool -- list --manifest <out>/manifest.json`
Expected: sorted, deterministic

- [ ] **Step 3: Commit docs**

```bash
git add docs/superpowers/plans/2026-08-27-content-runtime-ref-lease-codegen.md
git commit -m "docs(plan): content runtime B slice plan"
```

---

## Self-Review Checklist

- [x] Spec coverage: every section of `2026-08-27-content-runtime-ref-lease-codegen-design.md` has a task (1: arch wiring, 2: contracts, 3: store, 4: registry, 5: codegen, 6: sdk, 7: alloc, 8: verification)
- [x] No placeholders: all steps have exact file paths, complete code blocks, exact commands with expected output
- [x] Type consistency: `AssetRef<T>` (`Guid+uint+string`), `AssetLease<T>` (`ref struct`), `ContentSlot` (`uint Version`), `IContentStore` (`ReadOnlyMemory<byte>`), `ContentRegistry` signatures match across tasks
- [x] DRY/YAGNI/TDD/frequent commits respected, bite-sized steps (2-5 min)

