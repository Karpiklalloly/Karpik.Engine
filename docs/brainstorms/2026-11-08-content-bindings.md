# Live Content bindings for presentation assets

- Status: ready
- Started: 2026-11-08
- Updated: 2026-11-08
- Revision: 4
- Complexity: complex (auto)
- Development mode: tdd (auto; implementation has not started)
- Task branch: `codex/content-bindings`
- Parent branch: `codex/content-reload-notifications`
- Parent baseline: `95b976f`

## Goal and scope

Provide an explicit reusable runtime API, shipped with the SDK, that follows
published Content versions and updates presentation assets without restarting
the game. CoinRush UI strings and 2D textures are the motivating acceptance
scenario. The game selects which assets may update; physics and match state must
not silently adopt new authoring data. Shader/font adapters and source-file
watching are not part of the requested initial UI/texture scenario.

The user authorized a dependent branch and PR targeting Content Reload on
2026-11-08. This is necessary because change notifications and the supporting
snapshot/resource lifetime implementation are not yet on main. After the parent
merges, retarget the dependent PR and check its diff for inherited changes.

## Requirements and behavior

- An opt-in binding follows an AssetId; existing AssetRef<T> continues to identify
  one registry-local version.
- Common runtime code handles update tracking and loading; Graphics handles native
  resource use; the game chooses presentation meaning and its UI schema.
- No frame/fixed-tick/render allocations, blocking waits, I/O, decoding, arbitrary
  callbacks or driver calls. Use bounded storage and cold preparation.
- ECS components contain value handles, not textures, tasks, delegates or UI
  object graphs. Services use constructor injection and explicit owner lifetimes.
- On a failed replacement, keep the last successfully presented value. On
  removal, release the selected value and present the configured fallback or
  an empty value that the renderer can hide. Initial missing data has no last
  good value and therefore uses fallback/empty presentation.
- The remaining architecture below uses engineering defaults constrained by
  the project's ECS, DI, bounded-latency and ownership rules. The implementation
  plan is the single approval gate for this complete baseline.
- Presentation bindings update independently when their replacement is ready;
  no atomic presentation groups are required in the initial API. This does not
  turn the registry's atomic manifest validation/publication into partial success.
- Provide an opt-in typed JSON binding accepting AssetId and JsonTypeInfo<T>.
  Deserialize on cold workers; publish immutable-by-convention values at the
  frame boundary. Source-generated JSON metadata belongs to the binding owner,
  not a static or Engine-lifetime gameplay cache.

## Architecture and integration

Verified current implementation:

- `ContentRegistry.PrepareAsync` prepares replacements for payloads observed
  loaded, including texture upload through a stable payload loader. Failed
  preparation leaves the current snapshot intact.
- `ContentReloadCoordinator.PublishBoundary` commits at a quiescent host boundary
  and publishes a borrowed change span for simulation Begin.
- `ContentRegistry.TryResolve<T>` resolves the current version; TryGet reads a
  ready payload. LoadAsync is cold work and may lock/allocate.
- `ContentResourceLifetime.TryRetain` rejects new claims after retirement.
  RetainExisting transfers an already-owned claim. The ThreadBuffer drops a
  texture draw if its first claim cannot be acquired.
- Therefore, keeping a managed reference or even a consumer pin on an old
  texture does not by itself let fresh frame buffers draw it after retirement.
  Binding replacement/fallback and renderer ownership must be designed together.
- Registry update Completion awaits old-resource retirement before allowing
  another preparation. Indefinitely retaining an old texture can also stall
  later reloads and shutdown.
- AssetLease<T> is borrowed; Dispose is a no-op, not a resource pin.
- Already-loaded textures are decoded/uploaded by registry preparation before
  publication. A binding can adopt that prepared payload at the safe frame
  boundary, without scheduling a second load or extending retired-texture use.
  Initial/in-flight loads and JSON-to-UI projection still need cold scheduling
  and checks that a result belongs to the latest requested binding version.
- CoinRush ClientDrawSystem loads UI once and uses legacy path-based
  IAssetsManager texture loading. ClientDrawState currently stores managed
  resources/jobs/UI in an ECS component; the integration must remove this
  pattern for the assets it migrates.

Relevant contracts: `docs/02_ADR/content-runtime-snapshots.md`,
`docs/content-reload.md`, `src/Karpik.Content.Runtime/ContentRegistry.cs`,
`ContentReloadCoordinator.cs`, `ContentResourceLifetime.cs`,
`Modules/Client/Graphics/Graphics.Core/Buffers/ThreadBuffer.cs` and
`Modules/Client/Graphics/Graphics.OpenGL/AssetManagement/ContentTextureLoader.cs`.

## Constraints

Preserve Client/Server/Shared isolation, fixed-dt game logic, registry snapshot
semantics, and GPU shutdown ordering. Do not expose backend graphics types in
Shared runtime or let engine singletons retain disposed gameplay owners or
reloadable Types/delegates. Existing content transport applied means registry
publication; any extension to its acknowledgement contract requires discussion.

The available `.ua` graph was analyzed at `7224b18` on 2026-11-01 and predates
the parent feature. Current source and its ADRs are the authority for this task.
No matching QMD connector is available; relevant documentation was read directly.

## Decisions and alternatives

- D1 — Explicit live bindings: **accepted in the preceding discussion**.
  - Alternative: silently make AssetRef<T> follow the latest version.
  - Rationale: automatic replacement would break fixed-version semantics and
    could alter gameplay/physics unexpectedly.
- D2 — Reusable runtime feature shipped with SDK: **accepted**.
  - Alternative: duplicate change-handling/loading/lifetime code in each game.
  - Rationale: centralize safety and scheduling while keeping game-specific
    interpretation and opt-in policy in the game.
- D3 — Dependent branch/PR: **explicitly authorized by the user**.
  - Alternative: merge the parent feature before starting this work.
  - Rationale: develop the new independently reviewable feature against its real
    prerequisite without merging the parent prematurely.
- D4 — Failure/removal presentation: **explicitly selected by the user**.
  - Failure keeps the last good value; removal switches to fallback/empty.
  - Alternative: retain the last value after both failure and removal.
  - Rationale: do not blank a working UI on a failed replacement, but make
    explicit deletion observable and avoid indefinitely retaining deleted GPU
    resources or delaying registry retirement. Ownership must ensure that a
    retained last-good value remains safe to use; raw managed references alone
    do not satisfy this rule.
- D5 — Independent presentation readiness: **explicitly selected by the user**.
  - Apply each binding's ready replacement at a safe boundary without waiting
    for unrelated presentation bindings.
  - Alternative: atomic presentation groups that wait for all members.
  - Rationale: UI changes should not wait for a slow texture or unrelated
    projection. Keep existing atomic registry validation and transport `applied`
    semantics; presentation readiness is a separate binding observation.
- D6 — Typed JSON convenience: **explicitly selected by the user**.
  - Offer a JSON binding accepting AssetId, JsonTypeInfo<T> and an optional
    last-resort fallback value. The registry continues loading RawJsonPayload;
    the binding projects that payload to the game-specific T on a cold worker.
  - Alternative: expose only RawJsonPayload and require per-game projection,
    completion scheduling and error handling.
  - Rationale: remove repeated game code while keeping AOT-safe serialization
    and avoiding registration of gameplay decoders as Engine payload loaders.
- D7 — Value handles and scoped ownership: **engineering selection**.
  - Public ContentBinding<T> is an unmanaged value handle containing owner,
    slot and reuse-generation identity. It contains no service/payload reference
    and may be stored in ECS. An injected IContentBindings Simulation singleton
    owns dense slots, projections, fallback values and cold work.
  - Cold Bind<T>/BindJson<T> create bindings; Release invalidates a handle.
    TryGet returns the current borrowed value (including last-good/fallback);
    a value-type status snapshot reports Pending, Ready, Missing or Failed,
    value availability, fallback use and requested/presented version. Stale,
    foreign-owner and released handles are rejected without hot-path exceptions.
  - Values are borrowed for the current presentation frame; callers must not
    dispose registry-owned payloads or retain raw values across boundaries.
    Shared handles are not reference-counted by struct copies; owner/Release
    controls lifetime. A copied handle becomes invalid when released.
  - Alternative: one disposable reference-type binding object per consumer.
    Simpler syntax, but introduces managed ECS references or a second indirection
    system and obscures ownership when copied into entity state.
  - Rationale: stable dense handles satisfy project invariants and support cheap
    reads without a dictionary lookup or allocation per binding read. Backend
    types remain in Client; shared bindings know only IContentTrackedResource.
- D8 — Safe-boundary adoption, not a second publication system: **engineering selection**.
  - Stable host integration advances bindings after registry publication and
    before gameplay/render preparation, including initial loading without reload
    opt-in. Existing Static and Dynamic hosts provide this automatically;
    manually composed hosts follow a documented explicit boundary contract.
  - Already-loaded textures use the replacement prepared/uploaded by the registry.
    Adopt it at that boundary before any new frame can draw a retired payload.
    Release the prior binding claim; pending frame/command claims remain intact.
  - For a ready texture, preparation failure preserves the existing root/value.
    Missing/in-flight slots queue cold loading and show fallback until ready;
    they never keep drawing an already-retired raw texture. Remove switches to
    fallback/empty at the boundary and releases the deleted selection.
  - JSON projection may finish later; retain the prior managed UI value until
    the latest projection succeeds. Epoch/version checks discard stale results.
  - Alternative: let every binding independently reload old and new textures
    after commit and extend renderer claims across retired generations. This
    duplicates registry preparation and changes the existing retirement contract.
  - Rationale: use prepared payloads and the current renderer lifetime protocol;
    no polling filesystem, driver calls or asynchronous continuations in-frame.
  - Async binding shutdown stops acceptance, invalidates pending results, cancels
    and drains owned cold work, releases selections and clears JsonTypeInfo<T>
    before Simulation/plugin teardown. It precedes registry/device disposal and
    must not wait on renderer claims held by that same shutdown stage.
- D9 — Bounded work and failure visibility: **engineering selection**.
  - Allocate dense storage during cold initialization: default 256 bindings,
    configurable up to 4096 for this first implementation. Creation and release
    occur on the owner's cold lifecycle thread while presentation is quiescent;
    they are not Update/Render APIs. Reuse is generation checked; wrap is rejected.
  - Use bounded per-slot request/result state, at most one executing and one
    latest requested version per slot. Superseded work cannot overwrite a newer
    value. No unbounded Task.Run queue, per-frame delegate creation or storage
    growth. A persistent cold worker handles loading/projection; boundary
    exchange uses preallocated storage and never waits or invokes user code.
  - Full capacity rejects registration explicitly and preserves existing bindings.
    Incompatible types/unsupported payload loaders fail visibly, preserve the
    last good value, and require a compatible subsequent publication or explicit
    cold retry. Missing/re-added IDs recover automatically. Registry-rejected
    manifests keep existing bindings unchanged and retain transport diagnostics.
  - Initial missing/failed data uses fallback/empty and exposes its state.
    Projection/decode errors are available as cold diagnostics without repeated
    frame logging. Transient errors can be retried explicitly from a cold path.
  - Fallback JSON is immutable-by-convention caller data. Texture fallback is
    empty or a caller-owned non-retiring texture that outlives the owner;
    reject registry-generation tracked textures as constant fallbacks. This
    prevents an unrelated fallback generation becoming an unsafe dangling value.
  - Alternative: grow tables/queues automatically or retain every intermediate
    request. This avoids an explicit capacity error but makes memory and latency
    depend on source-edit bursts. Prefer predictable limits and latest-wins.
- D10 — Initial delivery scope: **engineering interpretation of the agreed goal**.
  - Shared typed bindings/JSON projection, existing Client texture loader,
    Static/Dynamic host lifecycle integration, CoinRush template and the existing
    local CoinRush game. Migrate its UI/player/coin/tile textures; move remaining
    presentation resource/job objects (including the existing legacy font handle)
    out of ClientDrawState into the scoped presentation owner. Font hot reload,
    shader reload, transactional presentation groups, gameplay config reload and
    source-file watching are excluded.
  - Alternative: add only SDK contracts and leave games to adopt them later.
    That would not demonstrate the requested visible update in CoinRush.
  - Preserve current fonts through their legacy loader in the scoped owner; do
    not claim a generic font decoder exists. Publish an SDK preserving the parent
    Content Reload feature, migrate the local game and verify the real visual path.

## Acceptance criteria

- AC1 — CoinRush title and selected texture updates become visible while the
  same client process continues running.
- AC2 — Existing fixed-version AssetRef<T> semantics and explicit update opt-in
  remain intact; gameplay state is not silently reloaded.
- AC3 — Hot-path binding reads/publication have measured zero allocations and no
  waits/I/O/decoding; capacities are bounded and observable on exhaustion.
- AC4 — Repeated publications, slow/failed loading, removal/re-add, owner disposal
  and shutdown do not apply stale work, use disposed GPU resources or leak owners.
- AC5 — Client presentation uses value handles in ECS and scoped DI ownership.
- AC6 — Failed replacements preserve the last good presentation; removal
  releases it and shows fallback/empty; re-add can populate the binding again.
- AC7 — A slow presentation binding does not delay another ready binding after
  registry publication. The existing `applied` result remains registry-level.
- AC8 — Typed JSON bindings use supplied JsonTypeInfo<T>, recover from projection
  failure, and release game-specific metadata/instances on owner disposal.
- AC9 — Missing/re-add, bounded-capacity errors, foreign/stale/released handles,
  explicit retry and latest-wins results follow D7/D9 without corrupting readers.
- AC10 — Static/Dynamic hosts, ordinary non-reload startup, SDK package delivery
  and local CoinRush integration preserve Client/Server/Shared boundaries.

## Open questions / blockers

None. Q1/Q2/Q4 were resolved interactively (D4/D5/D6); Q3/Q5/Q6 were
resolved through source investigation and the project constraints (D7–D10).

## Resume point

Design ready at revision 4. Planning is next, using
`plans/content-bindings-execplan.md`; request the workflow's one approval of
the complete plan before implementation. The dependent branch exists; no
implementation or plan approval has occurred.
