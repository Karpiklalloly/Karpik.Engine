# Version-bound Content snapshots and OpenGL texture ownership

Status: accepted for the 0.7 Content runtime API (2026-11-01).

## Failure mode

Replacing the registry dictionary during live use races reads and asynchronous loads. Disposing a replaced texture at frame end can also destroy a resource still referenced by a writing/ready/merging CPU buffer or an unsubmitted command list. A managed wrapper alone does not keep its native resource alive.

## Publication contract

`ContentRegistry` implements the existing `IContentRegistry` plus additive `IContentUpdateRegistry`. All registry exports resolve to the same Engine singleton. `RegisterManifest` is initialization-only; repeating it throws. Live callers migrate to `PrepareAsync` and `TryCommit`. Constructor injection adds an optional loader collection; two-argument source calls remain valid, but existing compiled consumers must rebuild against the new constructor signature.

An `AssetRef<T>` identifies one registry-local generation. Initial refs remain version 1, including generated fields. Resolve a current reference explicitly with `TryResolve<T>(id, out reference)` after a change; no reference silently follows the latest payload. Unloaded IDs can be resolved, then loaded. A bound incompatible CLR type cannot be resolved or loaded. Do not transfer version numbers between registry/process lifetimes: restored ECS data must re-resolve its authoring AssetIds.

Live presentation is an explicit additive consumer through `IContentBindings`.
Its unmanaged `ContentBinding<T>` identifies a Simulation-owned dense slot, not a
registry version. The scoped owner follows selected IDs, performs initial loading
and typed JSON projection cold, and exposes borrowed ready values at safe frame
boundaries. Failures preserve last-good presentation; removal clears to fallback.
Ready texture replacements use registry preparation and switch before new frame
claims; a managed old reference does not extend the retirement contract. Bindings
do not change fixed-version refs, manifest atomicity or transport `applied`
semantics. See [the binding contract](../content-bindings.md) for capacity,
projection metadata ownership, failure and teardown behavior.

Published lookup dictionaries and slot descriptors are immutable. Slots contain a fixed generation/store binding and one atomically published payload reference. Readers capture one root and do not lock, allocate or perform I/O. Loading captures its slot/store; a late result cannot overwrite another generation. Single-flight applies to compatible requests. Failed/cancelled loading may retry the same generation.

Preparation is control-plane work, outside Update/Render. It copies and normalizes descriptors/dependency order, rejects invalid schema/duplicate IDs/missing dependencies/cycles, then stages changed assets observed loaded under their slot lock. An asset observed loading gets a new unloaded slot; completion of old work does not change that choice. Unchanged slots preserve payload identity, in-flight work and generation. An identical manifest does not consume a revision. Changed/added slots use the next checked registry revision; removal followed by re-add cannot revive an old ref. Revision wrap is rejected.

Loaded built-in payloads (`string`, `byte[]`, `ReadOnlyMemory<byte>`, `RawJsonPayload`) can be staged. Updates validate artifact size, and RawJsonPayload updates validate JSON. Initial loading retains existing JSON behavior. Loaded custom CLR payload changes require a stable Engine-scoped `IContentPayloadLoader`; loaded declared-type migrations are rejected. Do not register decoders from reloadable gameplay assemblies or cache their Types/delegates statically. Payload values are read-only by caller convention; byte arrays and RawJsonPayload are not deep-cloned on reads.

There is one outstanding preparing/ready/retiring update per registry and at most 4096 manifest assets. A second preparation fails with a clear busy error. This deliberately throttles repeated reloads rather than growing a retirement queue. A prepared token is owner-bound and one-shot. Dispose aborts an unpublished candidate. Await its `Completion` on the control plane before another preparation; failures/cancellation leave the last good root intact. Completion also reports backend destruction failures. A retirement failure prevents subsequent updates.

`TryCommit` performs a root compare-exchange and queues the token's preallocated control work item. It performs no decoding, I/O, driver work, allocation or waits. **The host must call it at a chosen quiescent simulation/frame boundary.** The opt-in targeted reload host described below schedules that boundary. It does not watch source files or implicitly mutate ECS state.

## Captured stores and targeted reload

`PrepareAsync(manifest, store, ct)` binds changed/new slots to the candidate's
captured store; the original overload continues using the basis store. Unchanged
slots keep their old store, payload and version. Cold `IContentStoreLifetime`
claims protect snapshot defaults, individual slots and in-flight reads until
retirement completes. `FileContentStore` captures a physical immutable generation
and its matching manifest at startup. Writable legacy data is imported first;
read-only packages keep the ordinary non-owning filesystem fallback after metadata
validation. Such packages remain static while running; the fallback is not a live
reload store. Reload opt-in
requires captured metadata with known namespace/target and the correct side.

`ContentReloadCoordinator` owns one active preparation/publication/retirement and
one latest pending request. Pending input is copied and retained; replacement
settles the old pending request as superseded. Preparation and manifest comparison
run cold. The comparison uses the actual prepared basis/candidate, so skipped
pending manifests never become an invented application baseline. Failed
preparation leaves the current root intact and permits retry.

At each quiescent boundary `PublishBoundary` commits ready work and publishes
sorted added/updated/removed IDs through `IContentChangeSource`. Two preallocated
8192-element buffers cover replacing all 4096 old IDs with 4096 different IDs.
The boundary resets the current span on subsequent simulation frames, performs
no completion callbacks, I/O, locking or waits, and has measured zero caller
allocations. Consumers use the borrowed span during simulation `Begin`, then
explicitly resolve new refs through `IContentUpdateRegistry`. Fixed-tick consumers
capture the notification during `Begin`; client fixed ticks need not occur on
every frame. An ordinary non-opted host provides an empty source without starting
a coordinator or endpoint.

An applied result acknowledges the root switch before retirement completes. A
later backend destruction failure remains a sticky coordinator diagnostic and
prevents further updates; it does not retroactively make the committed root
unapplied. Delivery deadlines and disconnects report unknown and do not cancel
accepted work. The transport validates version, request correlation, root,
namespace, target and generation with bounded messages/connections.

Both Dynamic and Static WorkerHost paths opt in with
`--content-reload-instance`; launcher starts/restarts preserve that option.
Client publication runs only after reserving an idle simulation frame; Server
publication precedes its fixed tick. Endpoint callbacks and preparation stay on
cold workers. Core/Runtime/Transport assemblies share stable default-context
identity with Dynamic modules so registry, payload-loader and AssetId contracts
cannot split across plugin load contexts.

The last-added boundary system requests stop before gameplay destruction. This
settles pending/preparing delivery requests without waiting for renderer pins.
`MergeThread` additionally depends on optional `IContentReloadLifetime`, enforcing
reverse Autofac disposal: renderer releases claims, coordinator fully joins
retirement, registry drains, then the graphics device is disposed. Endpoint
disposal joins accepted handlers without prematurely awaiting GPU retirement.
Direct manual composition must preserve the same order.

The new store-binding interface overload requires external
`IContentUpdateRegistry` implementers to rebuild/adapt. The additional optional
MergeThread constructor parameter preserves ordinary source calls, but previously
compiled direct callers must rebuild. No source-meta or public manifest migration
is required. See [the usage guide](../content-reload.md).

## GPU contract

Shared Runtime knows only `IContentPayloadLoader`, `IContentTrackedResource`, `ContentResourceLifetime` and preallocated `ContentResourcePins`; it imports no Client/backend types. The OpenGL `ContentTextureLoader` exports a stable loader for declared type `texture` and payload `ITexture2D`. The interface has `[ContentType("texture")]`, so generated Client refs can use it. Cooked PNG/JPEG bytes retain the existing format. Shader/pipeline and font-content decoding are separate adapters, not implemented by this texture adapter.

Decoding, public NeoVeldrid creation/upload calls and an ordered GL execution barrier run on cold workers. Publication occurs only after upload readiness. The adapter owns the complete texture, view, sampler, layout and resource-set bundle, including failure cleanup. Destruction is ordered resource-set/view/sampler/layout/texture. The legacy two-argument `VeldridTexture2D` constructor retains its old texture-only ownership contract; it does not become a Content generation.

ThreadBuffer acquires one claim per unique tracked texture/atlas. Claims live in preallocated open-addressed tables, without per-draw allocations or duplicate atomic retains. A retired generation rejects a first new claim; an already-owned generation may still be recorded within its owning buffer and transferred into a merge context. Warm up command/resource capacity before real-time use. Claim saturation throws a capacity error; it never frees an owner early or grows retirement storage in Render.

Writing/ready/merging buffers retain claims. Dropping a ready frame clears claims. Merge contexts acquire their own unique claims before CPU buffers are released. A ready or claimed command list cannot be selected for rebuilding. Superseded/failed/unsubmitted lists release claims. After `TryTakeCompletedCommandList`, the renderer calls `CompleteSubmission` only after SubmitCommands returns, or when explicitly discarding the claimed list. Direct callers must follow the same protocol; `GetCommandList` is deprecated. Shutdown joins the merge worker and discards all pending CPU frames and contexts. MergeThread constructor injection resolves IContentRegistry before merge activation, guaranteeing reverse Autofac disposal releases merge ownership first. Standalone manual callers may omit that dependency and must enforce the documented shutdown order themselves. Native cleanup attempts every resource and releases CPU claims even if the device or an individual Dispose fails; errors are aggregated.

Once retirement is requested and the last CPU claim is released, cold collection queues bundle destruction through public `BackendInfoOpenGL.ExecuteOnGLThread`. This callback is ordered after the last relevant submitted GL commands. The existing deferred native deletion path and GL storage lifetime rules preserve previously issued GPU work. The callback can block its cold caller; it never runs on Update/Render/publication. NeoVeldrid 1.2.1 OpenGL Fence signals at enqueue and is **not** evidence of GPU completion. This implementation does not use that fence to authorize resource destruction and does not require a NeoVeldrid fork.

`AssetLease<T>` remains a borrowed managed value; Dispose is a no-op. It is not a native ownership claim, and copying a ref struct must not duplicate a disposable refcount. Record tracked resources through the renderer, or explicitly `TryRetain`/`Release` when escaping to native use. Do not directly dispose registry-owned payloads during normal use. Width/height remain cached metadata after texture retirement.

Cold shutdown order: stop simulation recording, join/dispose the merge worker (discard pending frames), abort or commit any prepared update and await Completion, await `ContentRegistry.DisposeAsync`, then dispose the GraphicsDevice. Do not wait for retirement while holding a claim that the same caller still needs to release. An outstanding prepared update must be settled before registry shutdown; the API rejects shutdown otherwise.

## Limits and evidence

The store must preserve immutable bytes for captured locators. The cooperative
generation protocol now provides that retention for the filesystem build/runtime
workflow; it cannot recover files manually deleted or overwritten outside the
protocol. Verified local filesystem locking and atomic rename semantics are
required; unknown formats fail closed for collection.

Unit tests cover versioning, staging failure/cancellation, late loads, bounded preparation, abort/foreign tokens, declared-type migration, graph validation, dropped frames, transfer and allocation budgets. The opt-in hardware probe under `plans/probes/content-gpu-lifetime-audit` verifies actual green-old/red-new readback during replacement using the production loader and ownership code. It also preserves the earlier ordered/deferred-delete negative-control experiment and native pending-GLsync audit. These probes require a real graphics device and are not CI unit tests.
