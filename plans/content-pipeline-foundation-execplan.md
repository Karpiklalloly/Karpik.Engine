# Establish the content pipeline foundation

This ExecPlan is a living document. It must be maintained according to
`plans/PLANS.md`.

## Purpose / Big Picture

Build a reproducible, headless foundation for KarpikEngine content. A developer
will be able to place `Foo.json` and `Foo.json.meta` below a source root, run a
CLI command, and receive a canonical manifest plus content-addressed cooked
artifacts. `validate`, `list`, and `why` will expose the same model in CI and
locally without launching a game or editor.

The observable first-slice result is deterministic `raw-json` cooking and
diagnostics. Runtime loading of cooked artifacts, GPU resources, mod
overrides, streaming, and hot reload are deliberately not part of this plan.

## Progress

- [x] (2026-08-27) Design discussion completed; the proposed durable boundary
  is recorded in `docs/02_ADR/content-pipeline-build-contract.md`.
- [x] (2026-08-27) Create Core, Tool, and test projects and add them to `KarpikEngine.slnx`.
- [x] (2026-08-27) Define canonical metadata, manifest, diagnostics, and processor
  contracts.
- [x] (2026-08-27) Implement source scanning, validation, raw-json cooking, and atomic
  output publication.
- [x] (2026-08-27) Implement `build`, `validate`, `list`, and `why`.
- [x] (2026-08-27) Add deterministic, validation, and CLI acceptance tests.
- [x] (2026-08-27) Validation passed: 42 content tests green, artifact no-rewrite verified, failed-build preservation verified, CLI exit codes verified. See Outcomes & Retrospective.

## Surprises & Discoveries

- Observation: The current `AssetsManager` caches by `(path hash, asset type)`
  and resolves loose files at runtime.
  Evidence: `Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs`.

- Observation: The project already has a proposed content-pipeline board that
  selects build-time cooking, stable GUID identity, and a future slot registry.
  Evidence: `docs/04_Roadmap/kanban-content-pipeline-approach-2.md`.

- Observation: `Karpik.Engine.Tooling/AtomicDirectoryPublisher.cs` encodes engines store path `Engines/<version>` and recovery semantics that do not map directly to content output `manifest.json + artifacts/`. Reused framing/hash and path-safety utilities, but kept a content-local `ContentAtomicPublisher` with merge semantics to preserve identical artifacts without rewriting.
  Evidence: `Karpik.Engine.Tooling/EngineInstallationValidator.cs:435` PathSafety internal; `Karpik.Content.Core/ContentAtomicPublisher.cs`.

- Observation: Canonical JSON must be normalized for both importSettings and source cooking; raw-json cooking uses the same `CanonicalJson.SerializeCanonical` to ensure byte-identical cooked output for semantically equal JSON with different key order.
  Evidence: `Karpik.Content.Core/CanonicalJson.cs`, `Karpik.Content.Core/RawJsonProcessor.cs`.

- Observation: `dotnet build` without `-m:1 -nr:false` leaves MSBuild nodes alive; content tests spawn `dotnet run` for CLI tests which adds ~40s; targeted `dotnet test Karpik.Content.Tests` is 42 tests in ~3s when using `dotnet test` with built binaries but CLI tests dominate.
  Evidence: `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false` 42 passed 43s.

## Decision Log

- Decision: Use an independent `Karpik.Content.Core` plus
  `Karpik.Content.Tool`, rather than extending `AssetsManager` or beginning
  with an MSBuild task.
  Rationale: Content building must be headless, reproducible, and independent
  of runtime lifetime/ownership concerns.
  Date/Author: 2026-08-27 / developer and Codex

- Decision: Store a stable `AssetId` as a GUID in each source sidecar `.meta`.
  Rationale: Source paths and logical names are mutable authoring data and
  cannot safely identify long-lived references.
  Date/Author: 2026-08-27 / developer and Codex

- Decision: Limit the first slice to `raw-json` cooking and contract
  validation.
  Rationale: It proves the build architecture without entangling runtime,
  rendering, mod ordering, or hot-reload ownership.
  Date/Author: 2026-08-27 / developer and Codex

## Outcomes & Retrospective

Implementation completed 2026-08-27. All acceptance observations verified:

- Valid `raw-json` source trees create one canonical `manifest.json` and content-addressed artifacts under `artifacts/ab/cd/<hash>.cooked`. Manifest entries ordered by `AssetId`, UTF-8 without BOM, fixed property order.
- Rebuilding unmodified input produces byte-identical manifest (`SHA256 D2E9...` in fixture) and does not rewrite matching artifacts (timestamps preserved via `ContentAtomicPublisher` merge).
- Moving a source while keeping its `.meta` leaves `AssetId` unchanged (proven by `Build_MovePreservesAssetId` which moves `a.json` to `subdir/moved.json` and asserts manifest still contains same GUID).
- Duplicate IDs (`KCO004`), duplicate logical names (`KCO005`), malformed meta (`KCO003`), invalid JSON (`KCO011`), unknown type (`KCO008`), traversal (`KCO006`/`KCO015`), missing dependency (`KCO012`), and cycle (`KCO014`) fail with stable non-zero diagnostics and sorted output.
- A failing build preserves previously published output (backup/restore via `.replacement`, verified by `Build_FailedPreservesPreviousOutput`).
- `build` (0 success, 2 validation), `validate` (0 success, 2 validation), `list` (sorted, 0), `why` (0 success, 2 missing) all return stable exit codes: 0 success, 1 usage, 2 validation, 3 unexpected. Commands are deterministic and do not mutate output for `validate`/`list`/`why`.
- No `Client`, `Server`, `ECS`, graphics, or `AssetManagement.Core` production file changed (`git diff --stat` shows only `Karpik.Content.*`, `KarpikEngine.slnx`, docs/plans).

Build verification:
- `dotnet build Karpik.Content.Core -m:1 -nr:false` warning-free.
- `dotnet build Karpik.Content.Tool -m:1 -nr:false` warning-free (produces `content.dll`).
- `dotnet test Karpik.Content.Tests -m:1 -nr:false` 42 passed 0 failed.

ADR `docs/02_ADR/content-pipeline-build-contract.md` status moved to `accepted` after validation.

Next slice: runtime `IContentStore` and slot registry, as per `docs/04_Roadmap/kanban-content-pipeline-approach-2.md`.

## Context and Orientation

`Modules/Shared/AssetManagement/AssetManagement.Core` is the existing Engine
runtime loader. It selects `IAssetLoader` implementations by extension and
opens source files from `Content/` or `Mods/`. It remains untouched by this
plan except as a migration reference; none of its `Asset`, `AssetHandle<T>`,
or `IAssetsManager` types are the new content contract.

Create the following projects and source groups:

- `Karpik.Content.Core/Karpik.Content.Core.csproj`: pure build model and
  orchestration library.
- `Karpik.Content.Tool/Karpik.Content.Tool.csproj`: console front end.
- `Karpik.Content.Tests/Karpik.Content.Tests.csproj`: focused unit, golden,
  filesystem, and CLI tests.

The Core model must define these concepts in plain C# types:

- `AssetId`: a value type wrapping a GUID; all formatting uses lower-case `D`.
- `AssetMeta`: meta schema version, ID, declared type, `namespace/path` logical
  name, and JSON import-settings payload.
- `ContentManifestEntry`: ID, type, logical name, import-settings hash, source
  hash, opaque artifact locator, byte size, and direct dependencies.
- `ContentDiagnostic`: deterministic code, severity, source-relative path, and
  message. No absolute path appears in a manifest or hash input.
- `IContentProcessor`: receives source bytes plus normalized `AssetMeta` and
  returns cooked bytes, direct dependencies, and diagnostics. It does not
  access the filesystem.

For the first slice, `raw-json` is the only declared type. It parses JSON for
validity, preserves a canonical cooked representation, has an explicit
processor version, and reports no automatic dependencies. Dependencies may be
declared explicitly in meta so cycle and missing-ID validation can be tested
before richer processors exist.

`Karpik.Content.Tool` exposes these commands:

```text
content build    --source <dir> --output <dir> --namespace <id>
content validate --source <dir> --namespace <id>
content list     --manifest <path>
content why      --manifest <path> <asset-guid>
```

The `namespace` CLI value is the expected leading segment for every logical
name in that build. It does not implement mod priority or overriding.

## Real-Time Assessment

This plan creates build-time code only. No edited or added code may execute in
`Update`, `FixedUpdate`, ECS `Run`, rendering, network pumps, or serialization
hot paths. Build-time scanning, JSON parsing, hashing, diagnostics, and file
I/O may allocate. No ECS data layout, fixed-tick behavior, Client/Server
runtime boundary, or runtime synchronization mechanism changes in this slice.

The new Core project must not reference Client graphics/input assemblies,
Server assemblies, `AssetsManager`, Autofac, or service-locator APIs. The
future runtime registry is a separate plan and must treat the manifest locator
as opaque.

## Plan of Work

### Milestone 1: Create deterministic contracts

Create the three projects and add their project/test entries to
`KarpikEngine.slnx`. Keep Core free of runtime project references. Add the
value types, JSON parsing/serialization boundary, processor result, and
diagnostic code catalog. Specify a canonical JSON writer: fixed property order,
entries ordered by `AssetId`, UTF-8 output, invariant numeric formatting, and
no timestamps or absolute paths.

Add tests that show a `.meta` GUID is normalized, malformed meta fails with a
stable code, and differently ordered in-memory inputs serialize into identical
manifest bytes.

### Milestone 2: Validate sources and cook raw JSON

Implement source scanning of each source file with a required adjacent
`.meta`, using only normalized source-relative paths. Reject source or output
traversal, duplicate IDs, duplicate logical names, unsupported declared types,
unknown dependency IDs, dependencies outside the requested namespace, and
dependency cycles.

Implement `raw-json` validation and deterministic cooking. Define its version
as an explicit constant that participates in artifact hashing. Frame hash
inputs with field name and byte length before bytes so concatenation is
unambiguous. Write artifact files under a locator derived from the SHA-256
digest. Reuse an existing artifact only after verifying its bytes hash to the
locator digest.

Add golden tests for valid JSON, invalid JSON, hash changes after source/meta
or processor-version changes, missing/cyclic declared dependencies, and
duplicate identity/name diagnostics.

### Milestone 3: Publish atomically and expose the CLI

Implement a build coordinator that collects all diagnostics before publication.
On any error it produces no new output. On success it creates the complete
candidate output in a temporary sibling directory below the requested output
root and atomically replaces the published directory. Reuse the existing
`Karpik.Engine.Tooling/AtomicDirectoryPublisher.cs` only if its semantics and
dependency direction permit it; otherwise keep a small content-local publisher
with equivalent recovery behavior.

Wire the Tool commands to Core. `build` validates then publishes; `validate`
does not create artifacts; `list` displays canonical manifest entries ordered
by `AssetId`; `why` reports the dependency path from the named entry to its
direct/transitive dependencies and exits non-zero for a missing ID. Define
stable success, validation-failure, usage-failure, and unexpected-failure exit
codes.

Add filesystem integration tests that seed a prior published output, inject a
failing source, and prove the prior manifest/artifact bytes remain unchanged.
Add CLI process tests for each command, output ordering, and exit code.

### Milestone 4: Verify and document the working slice

Run the smallest project tests, then the full `Karpik.Content.Tests` suite.
Run two `build` invocations over the same fixture and compare every output byte
and artifact modification behavior. Run `validate`, `list`, and `why` against
the fixture from the command line. Update the Progress, Surprises, and
Outcomes sections with actual command output summaries. Mark the ADR accepted
only after those validations prove the contract.

## Concrete Steps

Work from `C:\Users\artem\RiderProjects\KarpikEngine`.

1. Create the three projects listed in Context and add them to
   `KarpikEngine.slnx`. Follow the existing repository package/version and
   test-project conventions; do not invent a new DI/module registration path.
2. Start each behavior from a failing targeted test in
   `Karpik.Content.Tests`, then make the smallest Core change that passes it.
3. Use `dotnet test Karpik.Content.Tests\Karpik.Content.Tests.csproj -m:1 -nr:false`
   after each implementation milestone. Expected result: all content tests
   pass and no unrelated project is rebuilt unnecessarily.
4. Build the console host with
   `dotnet build Karpik.Content.Tool\Karpik.Content.Tool.csproj -m:1 -nr:false`.
   Expected result: a warning-free successful build.
5. Run each command against a checked-in test fixture. Expected result:
   deterministic manifest/artifact bytes for `build`, no filesystem mutation
   for `validate`, sorted output for `list`, and a reproducible dependency path
   for `why`.
6. Before closing the plan, rerun both commands from steps 3 and 4, compare two
   clean output directories byte-for-byte, update this document, and change the
   ADR status from `proposed` to `accepted` only if acceptance passes.

## Validation and Acceptance

Acceptance requires all of the following observations:

- A valid source tree of `raw-json` files creates one canonical manifest and
  content-addressed artifacts.
- Rebuilding unmodified input produces byte-identical output and does not
  rewrite verified matching artifacts.
- Moving a source while keeping its `.meta` leaves its `AssetId` unchanged;
  no runtime behavior is required to prove this.
- Duplicate IDs, duplicate logical names, malformed meta, invalid JSON,
  unknown type, traversal, missing dependency, and dependency cycle fail with
  stable non-zero diagnostics.
- A failing build cannot corrupt or partially replace an earlier valid output.
- `build`, `validate`, `list`, and `why` all return their specified exit code
  and deterministic output order.
- No Client, Server, ECS, graphics, or runtime AssetManagement production file
  is changed by this first slice.

## Idempotence and Recovery

`validate`, `list`, and `why` are read-only and safe to rerun. `build` is
idempotent for identical inputs: it may recreate a temporary directory but
must publish byte-identical output and retain verified artifacts. A failed
build deletes only its own temporary candidate and preserves the last valid
published output. If publication fails due to an interrupted replacement,
retain the previous output or restore it before returning an error; record the
recovery operation in a diagnostic.

Do not migrate existing `AssetsManager` consumers in this plan. If project
creation or solution wiring proves incompatible with the repository's current
project model, stop after documenting the evidence in Surprises and revise the
plan before adding a workaround dependency.

## Artifacts and Notes

- Proposed ADR: `docs/02_ADR/content-pipeline-build-contract.md`
- Active plan: `plans/content-pipeline-foundation-execplan.md`
- Existing runtime loader retained for later migration:
  `Modules/Shared/AssetManagement/AssetManagement.Core/AssetsManager.cs`
- Existing content-pipeline design board:
  `docs/04_Roadmap/kanban-content-pipeline-approach-2.md`
