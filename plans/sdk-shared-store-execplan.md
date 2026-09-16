# SDK shared store (payload layout v3)

This ExecPlan is a living document. It must be maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

One published engine installation is ~1.85 GB. Measured waste inside a single
installation (see M0 evidence below): 304 MB of third-party `*.pdb` files and
964 MB of byte-identical duplicates across 44 content groups
(`libMoltenVK.a` x7, `Silk.NET.*.dll` x5, `shaderc` x4, ...). Total
addressable: ~1.27 GB of 1.85 GB (69%).

This plan introduces a top-level `shared/` directory in the engine payload:
module directories keep only their primary `<module-id>.dll`, every other
managed dependency moves to `shared/` exactly once. `*.pdb` files stop being
staged. Payload layout version goes 2 -> 3; the validator keeps accepting v2
so existing installations stay usable.

Observable success: a fresh `Update-KarpikSdk.ps1` publish produces an
installation well under 1 GB, `dotnet build`/`test` of an external game
against it is green, and all gate tests below pass.

## Progress

- [x] (2026-09-16) Initial plan created.
- [x] (2026-09-16) M0 measured, shared boundary locked (see evidence below).
- [x] (2026-09-16) M1 done: `IsStagedPayloadFile` filter in `PayloadLayout`
  (prepared + repository copy sites, native staging); 13/13 non-RepositoryMode
  packager tests green incl. new `BuilderStripsSymbolFilesFromPublishedPayload`.
  4 `RepositoryMode*` failures are pre-existing fixture drift
  (`Karpik.Engine.Templates.csproj` missing from `FakeRepository`, MSB1009) —
  same names and cause on clean tree.
- [ ] M2 locked boundary (module scan 2026-09-16): module dirs contain
  top-level `*.dll` (628 files / 257 MB), `Runner.exe` x17, `*.json` x57,
  `*.ttf` x2, and only one subdir kind — `runtimes/` (5 modules). No
  satellite/culture dirs. Rule: primary stays; other top-level files move
  flat to `shared/`; `runtimes/` merges to `shared/runtimes/`; any other
  subdir throws fail-closed.
- [ ] M0: overlap matrix measured, shared boundary locked.
- [ ] M1: `*.pdb` strip implemented, tested, committed.
- [x] (2026-09-16) M2 done: `PayloadLayout.DeduplicateToShared` (primaries
  stay, other top-level files move flat to `shared/`, `runtimes/` merges to
  `shared/runtimes/`, unknown subdirs and byte-conflicts throw fail-closed,
  streaming compare). 21/21 non-RepositoryMode packager tests green incl. 3
  new (dedup, conflict, runtimes merge); exact-layout contract test now
  expects `shared/`.
- [x] (2026-09-16) M3 done: `CurrentLayoutVersion = 3`,
  `MinimumLayoutVersion = 2` (v2 installs keep validating — migration-safe);
  validator requires `shared/` and primary-only module dirs for v3, extends
  managed identity check to `shared/*.dll`. Tooling 46/46 incl. 3 new
  (v2-without-shared valid, v3-without-shared MissingDirectory,
  v3-extra-module-DLL MissingModules). Fixture fallout fixed (shared/ added):
  Sdk.Tasks root tests 5/5, Launcher 22/22, Runner RunnerArguments 4/4,
  Editor HandoffService 2/2.
- [x] (2026-09-16) M4 done: `ResolveKarpikStaticReferencesTask` collects
  `shared/*.dll` (v2-compatible: missing dir skipped); `ModuleLoader` gains
  optional `engineSharedRoot` plumbed into `PluginLoadContext`
  dependencyDirectories (native `shared/runtimes/<rid>/native` probing comes
  free via existing candidate paths); runner `Program` passes it when present;
  `Sdk.targets` comment updated. Tests: task shared-collect green,
  new loader test proves `Lib` resolves only from shared (watched honest RED:
  `FileNotFoundException` at `GetTypes()`), ModuleLoaderExplicitBundle 13/13,
  Sdk.Tasks 76+4skip, Runner 133/133.
- [ ] M5: full publish, integration tests, size acceptance, ADR handoff.

## Surprises & Discoveries

- Observation: M0 overlap matrix (install `0.6.0-dev-e3c627...`, 1845.7 MB).
  Per-dir: editor 568.0 MB / 111 files, sdk 8.4 MB / 3, modules 872.0 MB /
  804 files, native 196.8 MB / 39, runners 200.4 MB / 83.
  Module primaries total 0.3 MB in 23 files — i.e. ~871.7 MB of `modules/`
  is non-primary payload. Duplicate copies live in
  `modules + native + runners` (x4/x7 patterns); `editor/` content is
  essentially unique (its closure + PDBs).
  Evidence: `%TEMP%\opencode\overlap-matrix.ps1` throwaway run, 2026-09-16.
- Decision: M2 boundary = module dirs keep primaries only; everything else
  managed goes flat to `shared/`, module `runtimes/` native trees merge to
  `shared/runtimes/` preserving RID structure. `editor/`, `runners/*` and
  `native/` stay self-contained in this plan (they are runtime app hosts;
  teaching them `shared/` probing is follow-up work if M5 misses the size
  target).
  Rationale: modules hold ~872 MB of which 0.3 MB is primary; editor is
  unique anyway, so touching runtime hosts buys little and risks loader
  behavior.
  Date/Author: 2026-09-16 / agent.

## Decision Log

- Decision: Variant 3 (shared store), not PDB-only and not hardlinks.
  Rationale: Developer chose it after seeing measured numbers (964 MB dupes
  vs 304 MB PDB). Hardlinks were rejected implicitly: payload must stay
  ordinary files for hashing/validation simplicity.
  Date/Author: 2026-09-16 / developer.
- Decision: Validator accepts layout 2 AND 3 (`MinimumLayoutVersion = 2`).
  Rationale: strict `!= CurrentLayoutVersion` would orphan all existing
  installations at once; games pin exact SDK versions and cannot be
  republished from old source. New publishes write 3.
  Date/Author: 2026-09-16 / agent.
- Decision: M1 (PDB strip) ships first as an independent milestone.
  Rationale: orthogonal to dedup, packager-only, near-zero risk, gives an
  early measurable win (~300 MB) while M0/M2 settle the shared boundary.
  Date/Author: 2026-09-16 / agent.

## Outcomes & Retrospective

No outcome yet. Update this after each major milestone and at completion.

## Context and Orientation

Payload layout v2 (top level of an installation below
`%LocalAppData%/Karpik/Engines/<engine-version>-<content-hash>/`):
`editor/`, `sdk/`, `runners/client`, `runners/server`, `modules/<id>/`,
`native/`, plus `engine-installation.json` and `.complete`.

Key files (repository-relative):

- `Karpik.Engine.Packager/PayloadLayout.cs` — `Materialize` builds the
  staging tree from per-project `dotnet build` outputs (`CopyDirectory` per
  module/runner/editor copies full transitive closures; this is where
  duplicates originate). `StageModuleNativeAssetsForRunners` copies module
  natives into both runners.
- `Karpik.Engine.Packager/EnginePayloadBuilder.cs` — writes manifest with
  `LayoutVersion = CurrentLayoutVersion`, writes `.complete`, validates,
  atomically publishes.
- `Karpik.Engine.Tooling/EngineInstallationManifest.cs` —
  `CurrentLayoutVersion = 2`.
- `Karpik.Engine.Tooling/EngineInstallationValidator.cs` — strict layout
  gate (`!= CurrentLayoutVersion` fails), requires all six directories,
  runner/editor assemblies, >= 1 nupkg, primary `<id>.dll` per module,
  catalog match, cross-module managed identity check (same simple name must
  be same identity AND same bytes).
- `Karpik.Engine.Tooling/EngineContentHash.cs` (same file) — deterministic
  SHA-256 over all files except manifest/marker. Layout change alters the
  hash automatically; dev destination names are hash-qualified, so v2 and v3
  installs coexist side by side.
- `Karpik.Engine.Sdk/Sdk/Sdk.targets` — build-time references:
  `$(KarpikEngineRoot)\runners\server\*.dll`,
  `$(KarpikEngineRoot)\modules\<id>\<id>.dll`, `_KarpikStaticModulePayloadAssembly`
  (every non-primary DLL inside module dirs), natives from `native/`.
- `Karpik.Engine.Sdk.Tasks/ResolveKarpikStaticReferencesTask.cs` —
  `CollectPayloadAssemblies` scans top-level `*.dll` of each module dir.
- `Karpik.Engine.Core/PluginLoadContext.cs` — runtime module loading; check
  how it resolves module dependencies before moving files (M4).
- Tests: `Karpik.Engine.Packager.Tests/EnginePayloadBuilderTests.cs`
  (`PreparedPayload` fixture; `BuilderPublishesExactCompleteLayoutWithValidHash`
  asserts the exact top-level entry set — must gain `shared`),
  `Karpik.Engine.Tooling.Tests/ManifestAndValidationTests.cs`,
  `Karpik.Engine.Sdk.Tasks.Tests/ResolveKarpikStaticReferencesTaskTests.cs`.

Non-obvious terms: "primary assembly" = `modules/<id>/<id>.dll`;
"payload assemblies" = third-party DLLs beside primaries that static
compilations must reference; "prepared payload" = test fixture layout that
skips real `dotnet build`.

## Real-Time Assessment

Build/packaging-time change only. No `Update`/`FixedUpdate`/ECS `Run`/
network/render code is touched. The only runtime-adjacent surface is module
dependency probing at load/startup (`PluginLoadContext`, runner worker
startup): if M0 shows runtime probing reads module dirs, M4 adds `shared/`
probing there too — startup-only, zero per-frame cost. Packager dedup must
stream-compare (SHA-256 over file streams); do not `ReadAllBytes` multi-MB
natives into memory in a loop (existing `CopyFileMerged` does, do not copy
that pattern for large files).

## Plan of Work

M0 (read-only): per-directory overlap matrix over one real installation:
bytes unique to `editor/`, `runners/*`, `modules/`, `native/` vs shared
between them; list of file names present in >1 module dir; primary DLL
total. Lock the boundary in Decision Log: exactly which file classes move to
`shared/` (proposal: module non-primaries always; runner/editor app hosts
stay self-contained unless matrix shows otherwise).

M1: `PayloadLayout` drops `*.pdb` while staging (both repository and
prepared-payload paths go through the same copy helpers — put the filter in
`CopyDirectory`/`CopyFileMerged` call sites used for payload staging, not in
the generic helpers if they serve other purposes; check call sites).
Test: prepared payload containing a `.pdb` publishes without it; validator
still green.

M2: new `PayloadLayout.DeduplicateToShared(stagingRoot)` (name TBD) run
after `StageModuleNativeAssetsForRunners`, before manifest/hash: move every
module non-primary managed DLL to flat `shared/` (first content wins;
same-name-different-bytes throws `InvalidDataException`, mirroring the
validator identity rule); leave primaries and `modules.catalog` in place.
Deterministic order (ordinal sort). Unit tests on prepared payloads:
shared content, module dirs primary-only, byte-conflict throws.

M3: `CurrentLayoutVersion = 3`, validator requires `shared/` for v3,
module-dir strictness for v3 (primary only), managed identity check extended
to `shared/`, accept v2 with old rules. Tooling tests: v3 valid, v2 still
valid, v3-with-extra-module-DLL invalid, v3-without-shared invalid.

M4: `ResolveKarpikStaticReferencesTask` collects payload assemblies from
`shared/` (module-dir scan stays as fallback for v2 installs);
`Sdk.targets` comment updates; runtime probing (`PluginLoadContext` and
anything else M0/M4 investigation finds reading module dirs at runtime)
gains `shared/`. Launcher/editor version selection is path-based
(`editor/Karpik.Editor.dll`) — unchanged.

M5: real publish via `_scripts/Update-KarpikSdk.ps1`, external game
build+test against it, size acceptance, update
`docs/02_ADR/versioned-engine-sdk-and-external-game-projects.md` (payload
layout section) and close the plan.

## Milestones

M0: evidence table in this plan (per-dir sizes, shared-bytes matrix,
primary total). No code.
M1: `dotnet test Karpik.Engine.Packager.Tests --filter PreparedPayload|pdb`
green; real-publish editor dir contains no `.pdb`.
M2: new unit tests green; prepared-payload publish output has `shared/` with
deduped content and primary-only module dirs.
M3: `dotnet test Karpik.Engine.Tooling.Tests` green; mixed v2/v3 fixture
matrix green.
M4: `dotnet test Karpik.Engine.Sdk.Tasks.Tests` green; runtime smoke
(launcher -> editor -> game, Dynamic mode) green.
M5: full solution gate for touched projects + measured install size recorded
in Outcomes; ADR updated.

Every implementation milestone ends with validation recorded in Progress
before the next starts.

## Concrete Steps

Working directory for all commands: `C:\Users\artem\RiderProjects\KarpikEngine`.
Prefer single-node builds: `dotnet build <project> -m:1 -nr:false`.
Targeted tests: `dotnet test <Tests>.csproj -m:1 -nr:false --filter "<name>"`.
Known baseline (2026-09-16): 4 `EnginePayloadBuilderTests.RepositoryMode*`
failures are pre-existing on a clean tree; do not chase them in this plan,
but do not add new failures.

## Validation and Acceptance

- Size: fresh installation < 1 GB (µµtarget refined after M0; hard floor:
  strictly smaller than the same-source v2 publish).
- Correctness: validator green on the new install; hash recompute matches
  manifest; external game restore/build/test/publish green in Static and
  Dynamic modes.
- Compatibility: a v2 installation still validates and still builds a game
  pinned to its SDK version.

## Idempotence and Recovery

Plan steps are rerunnable; publish is atomic (staging + `.complete`).
Rollback of code: revert commits, republish — new installs get new content
hashes, old installs untouched. If a v3 install misbehaves, games stay on
their pinned v2 installs until a fixed v3 is published. Retention script
(`_scripts/Clear-OldKarpikSdks.ps1`) is layout-agnostic (directory-level)
and needs no change.

## Artifacts and Notes

- Measurement scripts live in `%TEMP%\opencode` (throwaway, not committed).
- Baseline numbers (2026-09-16, install
  `0.6.0-dev-e3c627...`, 1845.7 MB / 1042 files): PDB 303.6 MB / 6 files;
  >1 MB duplicates 964.2 MB / 44 groups.
