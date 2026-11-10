# Generated live Content bindings

- Status: ready
- Started: 2026-11-09
- Updated: 2026-11-09
- Revision: 5
- Complexity: medium

## Goal and scope

Extend the existing ContentRefs generation flow so games can use live texture
and typed UI bindings without writing one runtime Bind call per asset. Provide
an explicit way to disable automatic binding. The first consumer is CoinRush.

## Requirements and behavior

- Continue generating the existing fixed-version refs and source paths.
- Generate live binding access by default for every bindable manifest entry
  in a participating project. An explicit false flag in an asset's .meta
  file excludes that entry. A project filter can exclude declared asset types.
- Use the same readiness, last-good, removal and re-add semantics as manual
  bindings.
- Typed JSON requires game-supplied source-generated metadata and fallback.
- Keep manual Bind and BindJson available when automation is disabled.

## Architecture and integration

The generator already correlates the cooked manifest with source .meta files
and resolves declared CLR payload types. It can derive the live binding set from
that same input. Runtime handles belong to one Simulation and are registered during its cold
startup phase, before frame consumers run. Generated reads use the existing IContentBindings.

Static composition codegen scans source declarations. It cannot discover an
Export emitted by a different Roslyn generator during the same compilation.
A source-declared partial owner supplies the stable composition entry point
for generated binding initialization. Each participating project declares
one owner, with normal Simulation lifetime and explicit typed JSON providers.
Generated members handle binding registration, reads and release. The SDK
owns the resource lifetime as it does for manual bindings.

## Constraints

- No mutable static owners. Keep live capacity bounded and frame reads
  allocation-free. Generating an accessor for an asset does not itself allocate
  a runtime slot or load an asset.
- Keep textures client-side and preserve AOT-safe typed JSON projection.
- Follow stable AssetId across versions. Keep the existing cooked manifest,
  transport and delivery semantics.

## Decisions and alternatives

- D1 — Selection: default inclusion for bindable entries in a participating
  project, as requested by the user. An explicit .meta false excludes an
  asset. This replaces opt-in flags and avoids annotating every asset.
- D2 — Disable control: a per-project build property. The user chose
  compile-time control. A startup option would require shipping and composing
  the automation code in disabled builds, and supporting both would add a
  second configuration surface. Use `KarpikAutoContentBindings=false` to
  suppress registrations. A declared owner's generated read surface stays
  compilable and returns its configured fallback or an unavailable value.
  Manual calls continue to work.
- D3 — Composition: one partial owner declaration per participating project.
  The user chose explicit ownership. Metadata-only registration would need
  coordinated changes to Static and Dynamic composition generation. The chosen
  entry point is visible to the existing Static/AOT source scan and keeps the
  same lifetime in Dynamic hosts.
- D4 — Eligibility: declared types must map to a supported payload and
  loader in the project side. A file extension alone cannot distinguish
  ordinary JSON, fonts and shader assets. Use declaredType for the proposed
  project exclusion filter. A typed JSON declaration supplies the projection
  for that ID. Unprojected JSON uses the existing RawJsonPayload contract.
  The existing font and shader paths require live adapters before they
  can participate. Do not generate texture bindings in a server project.
- D5 — Startup policy: register every eligible entry at Simulation startup.
  The user chose eager registration so the same generated access works from
  the first frame without a separate request. Cold demand registration
  would conserve slots and delay loading unused resources. Eager registration
  uses one slot per selected asset and can schedule loads even when the game
  never reads the result. Registration does not wait for resources to become
  ready. If registration fails, release all handles created by that owner
  and fail startup with context about the selected set and the failing asset.
  Keep the existing capacity limits and do not grow tables automatically.
- D6 — Filter precedence: project-wide disable wins, then excluded declared
  types and per-asset false. Typed JSON metadata overrides the default
  payload for an included asset. It does not bypass exclusions or create a
  second binding for the same ID. Proposed settings are
  `KarpikAutoContentBindings=false`, a project declared-type exclusion list,
  and `"autoBind": false` in source .meta. These settings control generation
  and registration, without changing the cooked manifest or artifact identity.

## Acceptance criteria

- AC1 — Bindable entries have generated access by default. A per-asset .meta
  exclusion and a declared-type exclusion suppress the corresponding automatic
  bindings while keeping ContentRefs.
- AC2 — Explicit typed UI JSON declaration has generated access with its
  supplied serializer metadata and fallback.
- AC3 — A project build with automation disabled creates no automatic runtime
  handles and keeps ContentRefs and manual Bind / BindJson usable.
- AC4 — Generated bindings have one Simulation owner and follow publication,
  invalid replacement, removal and restoration in CoinRush.
- AC5 — All included IDs register once during cold startup. Registration
  exceeding the shared capacity fails with a useful diagnostic and leaves
  no partial generated owner. Frame reads remain allocation-free.

## Open questions / blockers

None.

## Resume point

Planning uses design revision 5 and acceptance criteria AC1–AC5.
