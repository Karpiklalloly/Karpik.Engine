---
title: "Content pipeline build contract"
date: "2026-08-27"
status: "accepted"
tags:
  - adr
  - architecture
  - content
---

# Content pipeline build contract

> Status: accepted
> Date: 2026-08-27
> Owners: developer and AI assistant

## Context

The existing `Modules/Shared/AssetManagement/AssetManagement.Core` is a
runtime, path-and-extension-based loader. `AssetsManager` resolves loose
files from `Content/` or `Mods/`, chooses a loader by extension, and owns the
loaded object cache. It cannot be the source of truth for stable content
identity: a rename changes its path hash, source formats are decoded at
runtime, and its cache ownership must not leak into a future content
registry.

The engine needs a reproducible, headless content build foundation before it
adds a runtime registry, GPU upload, mod overrides, streaming, or hot reload.
The foundation must run in CI without the Editor and must permit future
storage changes without changing content identity.

This work is build-time tooling. It does not run from `Update`,
`FixedUpdate`, ECS `Run`, rendering, networking, or serialization hot paths.
It may allocate and perform file I/O while building. It must not introduce
Client graphics/input dependencies into Shared or Server runtime projects.

## Decision

Create two new build-time projects:

- `Karpik.Content.Core` owns deterministic schemas, validation, source
  discovery abstractions, processor contracts, dependency analysis, build
  planning, and canonical manifest serialization. It has no CLI, service
  container, game-runtime, or renderer dependency.
- `Karpik.Content.Tool` is a headless console host for `build`, `validate`,
  `list`, and `why`. It performs filesystem orchestration and maps Core
  diagnostics to exit codes and stable console output.

Each authoring source has a sidecar `<source>.meta`. The sidecar contains a
schema version, an immutable `AssetId` encoded as a canonical lower-case GUID
in `D` format, a declared type, a logical name, and processor-specific import
settings. `AssetId`, not the source path or logical name, is the durable
identity used by later references and manifests.

The first processor is `raw-json`. It validates and cooks JSON into an
artifact; it does not create a runtime asset object or perform runtime
deserialization. Processors receive supplied input bytes and normalized meta,
then return cooked bytes, direct `AssetId` dependencies, and deterministic
diagnostics. Processors do not open files or resolve services themselves.

The canonical `ContentManifest` contains a schema version and entries with:
`AssetId`, declared type, logical name, import-settings hash, source hash,
opaque cooked-artifact locator, size, and direct dependency IDs. Logical names
are mandatory `namespace/path` strings, unique within one build input, and
are authoring/diagnostic addresses only. Mod overrides and namespace conflict
resolution are explicitly deferred.

Artifacts are content-addressed using SHA-256 over an unambiguous framing of
source bytes, canonical meta bytes, and the processor version. A build writes
to a temporary directory under the requested output root, validates the full
result, then atomically publishes it. Failed builds leave the previously
published output untouched. Identical inputs yield byte-identical manifests
and do not rewrite matching artifacts.

## Per-asset build reuse (0.7)

An ordinary build still scans and validates source/sidecar pairs, but reuses
successful processor results individually. A versioned private index,
`.karpik-content-cache.v1.json` in the output root, binds records to the
canonical published manifest. Each record identifies its manifest entry by
`AssetId` and stores a reuse key, cooked-byte checksum and successful processor
diagnostics. The index is disposable build metadata, not a runtime contract.

The reuse key includes relative source path, source contents, canonical meta,
processor type/version and target. Processors must be deterministic for their
supplied arguments and increment their version when their behavior changes.
Including the relative path conservatively reruns processing after moves,
including extension-sensitive validation, without changing the durable ID or
the public artifact recipe format.

A hit requires a matching key and artifact size/checksum. Missing, corrupt,
unsupported or manifest-mismatched indexes cause a cold build; a missing or
damaged artifact invalidates that asset's result. Cache-hit processor warnings
are replayed, and the combined metadata/processor dependency graph is validated
for both clean and incremental builds. `validate` always invokes processors.

Dependency IDs currently express references. Changing a referenced asset's
bytes does not require reprocessing an unchanged referrer, but missing,
target-incompatible and cyclic references are rejected before publication.
Processors that consume other assets' contents will need a separate explicit
build-dependency contract; this cache does not implement that future behavior.

When every selected result is reused and the candidate manifest is unchanged,
the build returns after validation without creating staging or rewriting
output. Changed builds stage all referenced artifacts, including copies of
reused artifacts, because publication removes unstaged old artifacts. The
private index is saved with an atomic file replacement only after successful
content publication. Failure to save it leaves a successful, usable output;
a subsequent build can rebuild instead of reusing results.

The public manifest schema, artifact hashing and ordinary CLI invocation remain
unchanged. Older output without this index is supported by a cold rebuild.
The legacy whole-tree fingerprint file is still emitted for older tooling,
but current builds no longer use it to bypass per-asset integrity checks.
Processor-emitted invalid references now fail validation like metadata
references. No runtime, Editor or source-meta migration is required.

This optimization reduces processor work; full source reads, checksum reads
and changed-build staging copies remain. Concurrent publishers now serialize
through the generation namespace gate described below. Source watching remains
outside the build contract.

## Immutable generations and explicit delivery

Every successful publication has an immutable `.generations/<id>/` copy of its
manifest and artifacts. The ordinary root manifest/artifact tree remains a
compatible projection; `.karpik-current-generation` identifies the authoritative
generation. A private descriptor records schema, namespace, target, manifest hash
and whether the identity is known. The public manifest and artifact recipe do not
change.

A permanent `.karpik-generations.gate` serializes build reuse, publication,
acquisition and collection. An owner creates a unique open-handle claim under
that gate. Collection probes claims under the same gate, prunes proven-stale
claims and deletes only validated, non-current, unowned generations. Separate
claim handles support multiple processes and independent owners in one process;
process death releases the OS ownership. PID reuse and timestamp expiry are not
ownership evidence. Unknown schema, corruption, access errors or unsupported
filesystem semantics retain data. `ContentGeneration.GetCollectionStatus`
explains cleanup support; builds report `KCO025` when collection is unavailable.

The immutable generation exists before the legacy projection is changed. The
current marker is switched last. Recovery restores an interrupted projection
from its prior immutable anchor; abandoned candidates can be reclaimed once safe.
Valid legacy output is copied and validated into an unknown-identity anchor
before its first destructive migration. A validated build produces known
namespace/target metadata. A no-op build verifies the immutable copy as well as
the ordinary cache/projection; damaged copies are repaired by publishing a new
generation rather than rewriting captured bytes.

`ContentBuildOptions.RetainGeneration` requests a delivery lease returned as
`ContentBuildResult.PublishedGeneration`; dispose the result when delivery no
longer needs it. Acquisition occurs before the publisher releases its gate, so a
newer build cannot collect the result in the build-to-notification interval.
Ordinary builds do not require callers to manage that lease. A retry acquires its
own `ContentGeneration` lease and fails clearly if an unused generation has
already been collected.

`content build --notify <instance>` and `content notify` use a bounded same-user
local transport. Build and delivery outcomes are separate: publication succeeds
even if delivery fails. Delivery waits for applied, superseded or failed;
timeout/disconnection reports unknown without cancelling accepted application.
See [targeted reload usage](../content-reload.md) for commands and exit codes.

## Alternatives Considered

### Extend `AssetsManager`

Rejected. It would keep paths, loose-file lookup, runtime decode, ref-counted
payload ownership, and build concerns in one Engine-scoped runtime service.
That prevents reliable renames and makes future registry/storage work harder.

### Implement only an MSBuild task

Rejected for the first slice. MSBuild integration is useful later, but it is
not a sufficient developer or CI diagnostic surface. A standalone CLI can be
called by MSBuild, the Editor, and CI without moving content logic into a
build-host-specific task.

### Add a Core library without a CLI

Rejected. CI and developers would immediately need separate command hosts.
The small CLI is the required stable operational surface.

### Use source paths or logical names as identity

Rejected. Both change during ordinary authoring. Only the GUID stored in
`.meta` survives moves and renames without rewriting references.

## Consequences

The first implementation adds no runtime registry and does not migrate
graphics, ECS, or gameplay consumers. Existing `AssetManagement.Core` stays
in place until a later vertical slice can load cooked content through a
separate runtime contract.

Build tooling receives a narrow, testable contract and deterministic output.
Canonical JSON must use fixed property/entry order and must exclude timestamps,
absolute paths, locale-sensitive formatting, and machine-specific data.

The initial filesystem layout is an implementation detail behind the manifest
locator. A later `IContentStore` can replace loose cooked files with packs or
chunks while preserving `AssetId`, manifest entries, and future `AssetRef<T>`
consumers.

## Comparing published manifests

`ContentManifestDiff.Compare(previous, current)` provides a deterministic
build/control-plane primitive for later successful-build notifications. It
returns one `ContentAssetChange` per added, updated or removed `AssetId`, in
ascending ID order. A null previous manifest represents the first publication;
an empty current manifest removes all previously present IDs.

An update means any canonical manifest-entry field changed: declared type,
logical name, source/import hashes, artifact locator, size or dependencies.
Entry order and dependency order are ignored, matching canonical manifest
serialization. Dependency multiplicity is preserved. The comparison copies
working arrays and never reorders its inputs. Unsupported schemas, duplicate
IDs and null entries/collections are rejected rather than silently producing
an ambiguous delta. Full source/dependency-graph validation remains the build
pipeline's responsibility.

The change record is a readonly value type containing only an `AssetId` and
change kind. Comparison allocates outside frame hot paths and has no runtime,
graphics, network or Editor dependency. It does not publish events, select
targets, invalidate unchanged referrers or replace live resources. A caller
must compare manifests for the same content namespace and build target; these
attributes are not stored in the public manifest schema. Failed builds must
not be treated as successful publications. Delivery, generation identifiers,
resource ownership and runtime application are separate contracts.

This helper changes no manifest schema, artifact recipe, CLI invocation or
existing consumer behavior. It is independent of the per-asset cache work.

## Validation

- Unit-test `.meta` parsing, GUID normalization, duplicate IDs, declared type
  validation, logical-name uniqueness, and path traversal rejection.
- Golden-test canonical manifest and raw-json cooked output.
- Verify byte-identical output and no artifact rewrites on repeated builds.
- Verify cycles and missing dependencies fail validation with stable
  diagnostics.
- Verify a failed build preserves a previously published output directory.
- Exercise all four CLI commands and their documented exit codes.

## Links

- Previous design board: [[../04_Roadmap/kanban-content-pipeline-approach-2]]
