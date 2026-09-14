---
title: "Source asset formats in the content pipeline"
date: "2026-09-13"
status: "accepted"
tags:
  - adr
  - content
  - textures
  - fonts
  - shaders
---

# Source asset formats in the content pipeline

> Status: accepted
> Date: 2026-09-13
> Owners: developer and Codex
> Extends: [[content-pipeline-build-contract]]

## Context

The content pipeline started with `raw-json` and later added images. The game
template also contains an MSDF font description (`.font-json`) and GLSL shader
sources (`.vert` and `.frag`). A template that enables the pipeline must build
these files without bringing a graphics device, Veldrid, shader compiler, or
Client module dependency into Core.

## Decision

`Karpik.Content.Core` supports the declared types `texture`, `font-json`, and
`shader` in addition to `raw-json`.

A headless `TextureProcessor` accepts `.png`, `.jpg`, and `.jpeg` source
paths. It uses `StbImageSharp` to verify that bytes decode as an image, but
the cooked artifact is the unchanged encoded input bytes. The processor owns
no GPU objects and has no dependency on `Graphics.Core`; runtime texture
upload through `AssetRef` is deferred.

`FontJsonProcessor` accepts `.font-json`, validates it as JSON, and publishes
the unchanged source bytes. `ShaderProcessor` accepts `.vert` and `.frag`,
rejects empty or invalid UTF-8 source, and publishes unchanged source bytes.
Neither processor compiles or reflects the source. Shader compilation is
backend-specific runtime work and is deliberately outside this build-time Core
contract.

The NuGet SDK package carries the published Content CLI and Content code
generator. SDK targets invoke the packaged CLI and load the packaged analyzer;
they do not use `KarpikRepositoryRoot` or a project reference into the engine
checkout. This makes the pipeline available to games created from the external
game template.

The SDK publishes cooked `manifest.json` and `artifacts/` from
`$(KarpikContentOutput)` into `$(TargetDir)Content` before
`BuildKarpikRuntimeBundle`. The runtime bundle therefore contains both the
cooked content contract and the template's existing source files. Keeping the
sources preserves the legacy path-based loaders until manifest-backed runtime
loaders replace them.

The game template enables this build for both Client and Server. They consume
the same game-owned source tree but cook it into independent intermediate and
runtime-bundle directories. Shared prefab schemas belong in Shared; no
Client-to-Server project reference is introduced.

The default content coordinator registers `RawJsonProcessor`,
`TextureProcessor`, `FontJsonProcessor`, and `ShaderProcessor`. Missing or
malformed inputs produce stable content diagnostics and block publication just
as any other processor error does.

The Editor and Content CLI share one Core meta-template contract:

- `.json` creates `declaredType: "raw-json"`;
- `.png`, `.jpg`, and `.jpeg` create `declaredType: "texture"`;
- `.font-json` creates `declaredType: "font-json"` and logical suffix `.font`;
- `.vert` and `.frag` create `declaredType: "shader"` and retain their stage suffix;
- other initial logical names are `game/<path-without-extension>`;
- generated `importSettings` and `dependencies` are empty.

On project open, the Editor creates missing sidecars for these supported
source extensions with create-only file semantics. Existing sidecars are
never overwritten and remain hidden from the Project tree.

`.meta` is editable authoring data. Its `assetId` is the durable identity and
must not be changed except through an explicit reference-migration workflow.
Changing `declaredType`, `logicalName`, `importSettings`, or `dependencies`
is allowed and changes the content build input. Future image import settings
such as filtering, wrapping, mipmaps, maximum size, and sRGB live under
`importSettings`; each becomes valid only when `TextureProcessor` implements
and validates its behavior.

## Consequences

Images, font descriptions, and shader source can receive stable asset
identities, pass `content validate`, and be published into the manifest. Their
cooked bytes remain readable by future runtime loaders, but this decision adds
no runtime `AssetRef<ITexture2D>`, GPU allocation, sampler, shader compiler,
or font loader.

Unknown extensions remain untouched by Editor auto-generation. They require
their own processor and extension-to-declared-type mapping before a `.meta` is
created automatically.

The cooked manifest and artifacts are present in `bin/.../Content` and the
runtime bundle, but this does not add runtime loaders for font descriptions or
shaders. They remain source-compatible legacy files until a future runtime
slice consumes their manifest entries.

## Alternatives Considered

### Add GPU texture upload now

Rejected for this slice. It would couple build-time authoring to Client
graphics lifetimes and turn a content-pipeline extension into a runtime
rendering feature.

### Store image settings now without processor behavior

Rejected. Persisting settings that do not affect validation, cooking, or
runtime behavior would make the Editor promise controls it cannot honour.

### Treat file extension as durable identity

Rejected. `assetId` in `.meta`, rather than a path or extension, continues to
be the stable reference across moves and renames.

### Compile shaders in Content.Core

Rejected. Compilation target, optimization level, and reflection format depend
on the graphics backend. Adding one to Core would leak Client rendering policy
into a headless build contract.

### Require a checkout to build content

Rejected. A game created from the SDK template is an external consumer. The
CLI and analyzer must therefore ship with the SDK package rather than resolve
engine project paths at game build time.

## Validation

- Unit-test the shared meta template for JSON, images, `.font-json`, `.vert`,
  and `.frag` mappings.
- Test valid PNG/JPEG cooking and malformed-image diagnostics.
- Test valid/invalid font JSON and valid/empty shader diagnostics.
- Test a mixed JSON/image/font/shader build produces deterministic artifacts
  and a manifest.
- Test Editor auto-generation creates sidecars once, does not overwrite an
  existing `assetId`, and excludes `.meta` files from the tree.
- Test that the game template enables the pipeline and builds its initial
  content successfully.
- Test a packed SDK and external template build: the CLI and analyzer resolve
  from the package, and the runtime bundle contains cooked manifest and
  artifacts.
- Build and validate the content tool and Editor tests.
