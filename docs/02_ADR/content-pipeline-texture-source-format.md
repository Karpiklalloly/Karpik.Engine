---
title: "Texture source assets in the content pipeline"
date: "2026-09-13"
status: "accepted"
tags:
  - adr
  - content
  - textures
---

# Texture source assets in the content pipeline

> Status: accepted
> Date: 2026-09-13
> Owners: developer and Codex
> Extends: [[content-pipeline-build-contract]]

## Context

The content pipeline currently has only `raw-json`. As a result, the Editor
can create useful sidecars only for JSON, even though the existing loose-file
graphics loader already accepts PNG and JPEG image inputs. The first texture
slice must make image authoring and headless content builds work without
bringing a graphics device, Veldrid, or Client module dependency into Core.

## Decision

`Karpik.Content.Core` will support one new declared type: `texture`.

A headless `TextureProcessor` accepts `.png`, `.jpg`, and `.jpeg` source
paths. It uses `StbImageSharp` to verify that bytes decode as an image, but
the cooked artifact is the unchanged encoded input bytes. The processor owns
no GPU objects and has no dependency on `Graphics.Core`; runtime texture
upload through `AssetRef` is deferred.

The default content coordinator registers both `RawJsonProcessor` and
`TextureProcessor`. Missing or malformed image inputs produce stable content
diagnostics and block publication just as any other processor error does.

The Editor and Content CLI share one Core meta-template contract:

- `.json` creates `declaredType: "raw-json"`;
- `.png`, `.jpg`, and `.jpeg` create `declaredType: "texture"`;
- the initial logical name is `game/<path-without-extension>`;
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

Images can receive stable asset identities, pass `content validate`, and be
published into the manifest. Their current cooked bytes remain readable by a
future runtime image loader, but no runtime `AssetRef<ITexture2D>`, GPU
allocation, sampler, or import-setting behavior is introduced here.

Unknown extensions remain untouched by Editor auto-generation. They require
their own processor and extension-to-declared-type mapping before a `.meta`
is created automatically.

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

## Validation

- Unit-test the shared meta template for JSON, PNG, JPG, and JPEG mappings.
- Test valid PNG/JPEG cooking and malformed-image diagnostics.
- Test a mixed JSON/image build produces deterministic artifacts and a
  manifest.
- Test Editor auto-generation creates sidecars once, does not overwrite an
  existing `assetId`, and excludes `.meta` files from the tree.
- Build and validate the content tool and Editor tests.
