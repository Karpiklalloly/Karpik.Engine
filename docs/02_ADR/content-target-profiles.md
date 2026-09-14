---
title: "Target-aware content build profiles"
date: "2026-09-14"
status: "accepted"
tags:
  - adr
  - architecture
  - content
---

# Target-aware content build profiles

> Status: accepted
> Date: 2026-09-14
> Owners: developer and Codex
> Related: [[content-pipeline-build-contract]]

## Context

The game template currently cooks its complete `Content/` tree independently
for Client and Server. This makes both runtime bundles self-contained, but it
cannot prevent a Server manifest from containing Client-only assets such as
textures, shaders, or audio. Folder names are useful authoring conventions,
but they cannot be the delivery contract: moving a file must not silently
change where it ships.

The pipeline also needs a safe foundation for future processor-specific Client
and Server variants. For example, a future texture processor may use different
settings for each runtime. The current output format is already a manifest and
content-addressed artifacts; no requirement yet calls for a monolithic
`content.pack` file.

## Decision

`Karpik.Content.Core` defines a flags `AssetTarget` with `Client`, `Server`,
and `Shared = Client | Server`. Every asset sidecar gains an optional canonical
`targets` array. Its absent value means `Shared` for backward compatibility;
the Editor and `content create` emit both targets for new sidecars.

The SDK invokes the CLI with its runtime `KarpikSide`. The CLI exposes the
same choice as `--target Client|Server`, and Core receives it through an
immutable content build profile. A target build includes an asset only when
its `targets` contains the selected target. It writes the existing
`manifest.json` plus `artifacts/` layout into that runtime's ordinary output;
there is no `content.pack` in this decision.

The build profile reaches processors through a `ContentProcessorContext`.
Current processors produce the same bytes for both targets, but the selected
target is nevertheless included in the canonical recipe hash and manifest
identity. Future processors may therefore produce target-specific artifacts
without cache collisions or an API redesign.

Validation is target-aware:

- `assetId` remains globally unique across the whole source tree;
- `logicalName` must be unique only among assets selected into one target
  manifest, so disjoint Client-only and Server-only assets may use the same
  logical name;
- a selected asset's direct dependency must also be selected for that target;
- an asset available to both targets may only depend on an asset available to
  both targets. Client-only and Server-only assets may depend on their own
  target or a shared asset.

`Content/Client`, `Content/Server`, and `Content/Shared` remain recommended
folders for authoring, not special build roots. A sidecar's `targets` is the
only source of truth.

## Consequences

Client and Server bundles carry separate manifests and separate content hashes
even when their current cooked bytes are identical. This deliberately spends
some build and storage duplication to keep each bundle independently
deployable and to reserve a stable extension point for future variants.

Asset source, `assetId`, and `logicalName` stay stable across targets. A
gameplay definition needing different authority and presentation data should
normally be modelled as separate Server and Client assets with a shared game
identifier, rather than one opaque JSON file whose runtime interpretation is
side-dependent.

No runtime loader, ECS system, renderer, pack archive, or hot-path code is
changed. All target selection, dependency checks, and cooking happen during
Editor/CLI/MSBuild work.

## Alternatives Considered

### Folder include globs in project files

Rejected. They make delivery depend on filesystem placement and duplicate
selection policy between Client and Server project files.

### One shared asset compiled differently without target in its identity

Rejected. It permits cache collisions and makes an `assetId` ambiguous. The
target must be part of the recipe before any processor gains such behavior.

### Add `content.pack` now

Rejected for this slice. The existing manifest/artifact contract already
delivers target-specific outputs. A pack is justified later by measured
startup, streaming, signing, or distribution requirements.

## Validation

- Unit-test absent, Client-only, Server-only, and Shared `targets` parsing.
- Build each target from one source tree and assert its manifest contains only
  selected assets.
- Test allowed and forbidden dependency edges for both target builds.
- Test same logical name in disjoint targets succeeds and an overlapping name
  fails.
- Test artifact locators differ by target even when processor output bytes are
  equal.
- Test the SDK passes `KarpikSide` to the packaged CLI and each template
  runtime bundle contains its own cooked manifest.
