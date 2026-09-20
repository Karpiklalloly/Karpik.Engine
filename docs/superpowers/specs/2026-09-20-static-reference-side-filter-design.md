# Side-safe static module payload references

## Goal

Make static Client and Server builds consume only the module assemblies that
belong to their selected side. A module primary assembly that is duplicated in
the payload-layout-v3 `shared/` directory must not re-enter the compiler graph
when its catalog side is incompatible with the current build. Ordinary shared
dependency DLLs must remain available because generated static factories can
mention their public types.

The regression must be expressed in terms of catalog sides and payload
classification, not names of concrete engine modules.

## Current failure

`ResolveKarpikStaticReferencesTask` selects primary module references with
`EngineModuleCatalog.ForSide`, but then appends every top-level DLL in
`<engine>/shared/` to `PayloadAssemblies`. Payload layout v3 can contain copies
of module primary assemblies there. Those copies bypass the catalog-side
selection and become Roslyn references, so the static composition generator
can discover client installers while compiling a Server host.

The generator's assembly metadata filter is not sufficient: built-in module
projects are not consumers of `Karpik.Engine.Sdk` and therefore do not
necessarily carry `KarpikSide` assembly metadata.

## Design

Keep the catalog as the single source of truth for module side. While resolving
the catalog, derive the set of primary module assembly file names from all
catalog entries. When scanning the top-level `shared/` directory, ignore files
whose names identify catalog primary assemblies. Selected primary assemblies
are already returned through `References` from their canonical module
directories; incompatible primaries are thereby excluded from the compiler
payload. Non-primary shared dependencies continue through the existing path.

The filtering remains filesystem-safe and deterministic:

- use the existing validated catalog and module-layout filename policy;
- compare only canonical file names with the existing case-insensitive path
  semantics;
- retain existing reparse-point, runner-assembly, and deduplication checks;
- do not inspect or load assembly metadata to infer side;
- do not add runtime allocations or code to frame, ECS, network, or render hot
  paths; this runs only during MSBuild static reference resolution.

## Test contract

Add a task-level regression test using generated temporary assemblies and
generic catalog IDs:

1. Add one shared catalog entry and one incompatible-side catalog entry.
2. Place a copy of the incompatible primary assembly in `shared/`.
3. Place a separately named ordinary dependency DLL in `shared/`.
4. Resolve references for the opposite runtime side.
5. Assert the incompatible primary is absent from `PayloadAssemblies` and the
   ordinary dependency is present.

The test must not mention `Graphics.Core`, `Window.Core`, or any other product
module name. Existing tests for side selection, payload collection, duplicate
identity, and invalid catalogs remain unchanged.

## Scope and non-goals

This change does not alter the Roslyn generator's existing metadata filter,
module catalog format, payload layout, runtime loader, or DI registrations. It
does not restore headless client-service bindings. Packaging and external-game
SDK pin updates are follow-up delivery steps after the source and regression
test are green.

## Acceptance

- The new generic regression test fails before the task change and passes after
  it.
- Existing `Karpik.Engine.Sdk.Tasks.Tests` pass.
- The engine solution builds with single-node MSBuild settings.
- A newly packaged SDK no longer adds incompatible module primary DLLs to a
  Server static compilation.
