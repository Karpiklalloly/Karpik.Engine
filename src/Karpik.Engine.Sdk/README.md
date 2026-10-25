# Karpik.Engine.Sdk

MSBuild SDK for external KarpikEngine games. Games use standard `.slnx`,
`global.json`, and SDK-style `.csproj` files; engine installations and game
sources have separate ownership.

Pin `Karpik.Engine.Sdk` under `msbuild-sdks` in `global.json`. Every game
project declares its kind and side:

```xml
<Project Sdk="Karpik.Engine.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <KarpikProjectKind>Runtime</KarpikProjectKind>
    <KarpikSide>Shared</KarpikSide>
    <KarpikCompositionMode>Static</KarpikCompositionMode>
  </PropertyGroup>
</Project>
```

The game template supplies solution validation and a module-selection profile.
Use `KarpikModuleSelection` to select engine modules; declare game dependencies
with literal project references. The SDK validates Client/Server/Shared
boundaries before compilation.

Ordinary restore, build, test, and publish commands use the exact compatible
engine installation. `KarpikEngineRoot` provides an explicit development
override. A missing, ambiguous, or incompatible installation fails with a
diagnostic.

The package includes content build tooling, analyzers, and runtime contracts.
Content-enabled projects build stable asset IDs, manifests, and cooked artifacts
for their side and copy cooked output into the runtime bundle. Static launchers
run the game directly; Dynamic mode uses versioned installed runners and
supports worker hot reload.
## Module-owned code generators

Add a separate `<OwnerProject>.Codegen.csproj` next to its owner and include both projects in the engine solution. Configurator discovers ownership by exact project name. Run Configurator `--generate` after changing the project graph; `--validate` checks that generated metadata is current.

The generator runs in every direct or transitive consumer of its owner. Diamond dependencies add it once. A generator is a build tool, not a runtime module; keep its Roslyn package dependencies private (`PrivateAssets="all"`). The owner may explicitly reference its generator as an Analyzer when it needs generation itself and the dependency is acyclic.

The SDK and engine payload ship `codegen/catalog.json` and the generator DLLs/dependencies under `codegen/<GeneratorProject>/`. SDK builds choose analyzers from resolved compiler references and the applicable runtime module selection. Generator DLLs stay out of runtime bundles. A legacy engine payload uses the SDK package's codegen catalog; unsupported catalog versions produce `KARPIK014`.
