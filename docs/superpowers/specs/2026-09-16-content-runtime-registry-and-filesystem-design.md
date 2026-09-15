# Content runtime registry and filesystem integration

## Purpose

Make generated `AssetRef<T>` values usable by ECS systems through an exported
`IContentRegistry`. Startup must register cooked-content metadata without
loading every asset. Loose development content and a future packaged archive
must share the same registry and asset-reference API.

## Decisions

- Add `IContentRegistry` to `Karpik.Content.Runtime`. Systems depend on this
  interface rather than on `ContentRegistry`.
- `ContentRegistry` is an Engine-scope singleton exported as both
  `IContentRegistry` and its concrete type. Its public contract covers
  manifest registration, `LoadAsync`, `TryGet`, and liveness checks.
- Move the existing `IFileSystem` and `PhysicalFileSystem` from
  `Modules/Shared/AssetManagement/AssetManagement.Core` to
  `Karpik.Engine.Core/FileSystem`. Existing consumers change namespace only;
  no second filesystem interface is introduced.
- `FileContentStore` receives `IFileSystem` and reads loose cooked artifacts
  through `OpenRead`, preserving its locator containment validation. It must
  not call `File.*` directly.
- A bootstrap/initialization service chooses one `IContentStore` before ECS
  frame systems run, reads `Content/manifest.json`, and calls
  `IContentRegistry.RegisterManifest`. The initial release implementation
  selects the loose `FileContentStore` only. `PackContentStore` is deferred
  until the pack format exists; it will implement the same `IContentStore`
  contract and read the archive index plus byte ranges through `IFileSystem`.

## Lifecycle

At startup, registry registration parses the manifest and creates one
`ContentSlot` per entry with `Unloaded` state. It does not open cooked
artifacts or deserialize payloads. A loading/init phase explicitly calls
`LoadAsync` for assets it needs. ECS `Begin`/`Run`/render systems only call
`TryGet`; they never begin I/O or block on loading.

## Boundaries and performance

The filesystem contract belongs in `Karpik.Engine.Core` because it already
defines engine paths (`ContentPath`, `ModsPath`) and is consumed across Shared,
Client, and Server modules. `Karpik.Content.Runtime` may reference Core for
this contract and service attributes, but it must not reference
`AssetManagement.Core` or `AssetsManager`.

Startup allocates the registry's metadata table once. `TryGet` retains its
current lock-free read path and performs no I/O or allocation. Loading,
manifest parsing, file access, and payload deserialization remain outside
real-time paths.

## Validation

- A registry unit test proves exported startup registration leaves store read
  count at zero, while `LoadAsync` performs exactly one read for concurrent
  callers.
- A `FileContentStore` test uses an `IFileSystem` test double and proves it
  reads a contained locator and rejects traversal.
- Targeted Content Runtime tests and AssetManagement/Core runner tests pass
  after the filesystem namespace move.
- A targeted build confirms `Karpik.Content.Runtime` references Core but not
  `AssetManagement.Core`.

## Deferred work

No archive format, archive index, `PackContentStore`, automatic preloading,
hot reload, or migration of the legacy `IAssetsManager` is part of this
change. Those need a concrete package-format design first.
