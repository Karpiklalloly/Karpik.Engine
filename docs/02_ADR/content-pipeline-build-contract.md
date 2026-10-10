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

### Runtime compatibility for path-based loaders

`AssetsManager` keeps its existing extension-selected loaders and object cache,
but can open a manifest artifact through `IContentStore.OpenRead`. The registry
indexes exact logical names to artifact locators together with their registered
store; legacy relative paths such as
`Shaders/2D.vert` also resolve against the `game/` namespace. Loader selection
and the asset path use the requested name, not the `.cooked` locator. Existing
physical files and loose files under `Content/` remain supported; this bridge
does not add automatic mod overrides or change durable `AssetId` identity.

The registry initializes its manifest on the first name lookup if an Autofac
startable requests content before `ContentRegistry.Start`. Later `Start` calls
do not clear loaded content. Manifest I/O and name normalization occur only on
initialization or an asset cache miss, not on cached reads.

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
