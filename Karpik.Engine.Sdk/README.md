# Karpik.Engine.Sdk

`Karpik.Engine.Sdk` is the MSBuild SDK used by external KarpikEngine game projects. It imports `Microsoft.NET.Sdk` and validates the raw game project graph before compilation without loading game assemblies or invoking Configurator.

Pin the package version in the game repository's `global.json`:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestPatch"
  },
  "msbuild-sdks": {
    "Karpik.Engine.Sdk": "0.6.0-local"
  }
}
```

Every project in the game solution must use the SDK and explicitly declare both its kind and side:

```xml
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
  </PropertyGroup>
</Project>
```

Runtime projects resolve their engine installation automatically before
`ResolveAssemblyReferences`. The SDK selects the single validated installation
under the platform local application-data store whose manifest provides the
exact `Karpik.Engine.Sdk` version pinned by `global.json`. An ordinary
`dotnet build` therefore does not require a process-scoped
`KarpikEngineRoot`.

`KarpikEngineRoot` remains an explicit absolute-path override for launcher,
editor, and diagnostic workflows. An invalid explicit root fails the build
instead of falling back. Automatic lookup also fails when no exact installation
exists or when multiple valid installations provide the same SDK version; it
never selects by timestamp or directory enumeration order.

Tests and portable tooling may set `KarpikLocalApplicationDataRoot` to replace
the platform local application-data root. Normal game projects must leave this
property unset.

The external game template also places the package's standard `templates/Directory.Solution.targets` file at the game solution root and commits it with the solution:

```xml
<Project>
  <Import Project="Solution.targets" Sdk="Karpik.Engine.Sdk" />
</Project>
```

This solution-scope import resolves the same version pinned by `global.json` and validates the raw `.slnx` before its `Build` target launches any child project. It is required to reject a foreign project before that project can compile. Future project creation tooling must copy this package template verbatim; games do not need a `.karpik` manifest or any `Directory.Build.*` contract.

Supported project kinds are `Runtime`, `Test`, `Tool`, `Generator`, and `Assets`. Supported sides are `Client`, `Server`, `Shared`, and `None`; runtime and test projects require a runtime side rather than `None`.

Solution builds validate every project declared in the raw `.slnx` through `Directory.Solution.targets`. Every game graph edge must be declared as an unconditional, literal, top-level `<ProjectReference Include="..." />` in the referencing `.csproj`. References introduced by imports, properties, item expressions, globs, conditions, or target-time mutation are unsupported.

Every Karpik SDK project compares its normalized evaluated direct `@(ProjectReference)` set with that static raw set. Any difference fails with `KARPIK004`; otherwise the complete raw transitive graph is validated for membership, side boundaries, and cycles. Distinct task invocations run before NuGet's recursive restore walk and at the final point before `AssignProjectConfiguration`. Both gates are declared after the underlying SDK targets: the restore gate observes `Directory.Build.targets`/consumer restore-walk mutations, and the late gate observes consumer/imported build mutations before reference resolution. Cold, incremental, and `--no-restore` builds therefore share the same contract without evaluating child projects or loading game assemblies. Invalid projects fail before compilation with stable `KARPIK...` diagnostics.
