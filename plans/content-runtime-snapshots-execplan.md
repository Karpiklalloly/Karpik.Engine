# Publish version-safe Content runtime snapshots

This living ExecPlan follows `plans/PLANS.md`. The developer approved the semantic choice on 2026-11-01: `AssetRef<T>` identifies a particular runtime version, and callers explicitly resolve a new reference by `AssetId`. The detailed design below is proposed for review; implementation has not started.

## Purpose / Big Picture

A running registry must be able to prepare a replacement content manifest without making partially loaded data visible. On a host-controlled phase boundary, it publishes the prepared candidate atomically. Unchanged assets keep their references; changed or removed assets invalidate old references; a failed preparation leaves the current snapshot and loaded payloads usable. An old asynchronous load cannot publish a payload into a new generation.

This bounded library slice provides explicit prepare/commit APIs for managed data payloads. It does not connect CLI notifications, a filesystem watcher, IPC, the engine phase loop or Editor. It does not hot-reload graphics resources or automatically rewrite ECS components. Those integrations need the contracts established here and remain separate PRs.

## Progress

- [x] (2026-11-01) Developer selected explicit version-bound references instead of automatic latest-version resolution.
- [x] (2026-11-01) Inspected registry, slots, leases, refs, codegen, payload creation, runtime tests and accepted Content contracts.
- [x] (2026-11-01) Baseline Runtime suite: 25 passed, zero failed/skipped; existing engine/codegen/xUnit warnings remain.
- [x] (2026-11-01) Created independent task branch `codex/content-runtime-snapshots` from `origin/main` at `7224b18`; no changes inherited from open PRs #108/#109.
- [x] (2026-11-01) Drafted this design.
- [x] (2026-11-01) Independent builder check identified a missing load-state observation boundary; resolved with one per-slot observation under the control lock. No other material questions were reported.
- [ ] Developer approves the detailed design and proposed scope/compatibility decisions.
- [ ] Expand approved milestones into implementation tasks using writing-plans.
- [ ] Implement and validate snapshot reads, captured loads and explicit resolution.
- [ ] Implement and validate preparation, publication and generation invalidation.
- [ ] Complete concurrency/allocation/regression validation and independent review; document the accepted runtime contract and publish one PR.

## Surprises & Discoveries

- Observation: `RegisterManifest` replaces `_store`, clears a Dictionary and creates slots with version zero, even while read/load APIs can access the same fields.
  Evidence: `src/Karpik.Content.Runtime/ContentRegistry.cs`. Calling this API during live use is not a safe update protocol.
- Observation: asynchronous success updates payload, version and state separately; `TryGet` reads them separately. Repeated loading increments the version rather than assigning a manifest generation.
  Evidence: `LoadAsyncInner`, `TryGet` and `IsAlive` in the same file. Atomic reads of separate fields do not provide one coherent payload/version observation.
- Observation: generated typed refs explicitly use version one. The untyped `AssetRef` carries only `AssetId`; typed `AssetRef<T>` is an ECS component containing GUID and uint version.
  Evidence: `src/Karpik.Content.Runtime.Codegen/ContentCodegenGenerator.cs` and `src/Karpik.Content.Runtime/AssetRef.cs`.
- Observation: leases retain a managed payload and their Dispose is currently a no-op. RawJsonPayload is a mutable class; byte arrays are mutable too.
  Evidence: `src/Karpik.Content.Runtime/AssetLease.cs` and `RawJsonPayload.cs`. Atomic publication cannot make caller-owned mutation safe or provide GPU/native ownership.

## Decision Log

- Decision: preserve strict version-bound `AssetRef<T>` semantics, with explicit resolution by ID after change.
  Rationale: selected by developer; silently following the latest payload would change gameplay data without an explicit decision and undermine the existing Version field.
  Date/Author: 2026-11-01 / developer.
- Decision: preserve current reference layout and initial version-one behavior; use registry-local monotonic manifest revisions for changed/new assets, retaining unchanged asset versions.
  Rationale: keeps generated initial refs and existing ECS component layouts compatible, avoids a per-ID tombstone table, and prevents remove/re-add ABA within one registry lifetime. Wrapping must be rejected.
  Date/Author: 2026-11-01 / proposed by Codex.
- Decision: first update support covers built-in managed payloads only: string, byte[], ReadOnlyMemory<byte> and RawJsonPayload.
  Rationale: reloadable custom decoders and IDisposable/GPU resources need explicit scope/retirement contracts. Existing arbitrary JSON startup loading stays available; updating an already loaded custom payload fails preparation clearly instead of guessing how to recreate or dispose it.
  Date/Author: 2026-11-01 / proposed by Codex, requires scope approval.
- Decision: initialization becomes single-use; live updates use an additive control-plane interface and prepared token.
  Rationale: repeating RegisterManifest is an unsafe reset. Preserve the existing read interface and add `IContentUpdateRegistry` rather than requiring external `IContentRegistry` implementations to implement new members.
  Date/Author: 2026-11-01 / proposed by Codex, requires compatibility approval.
- Decision: classify each changed slot once under its control lock during preparation; commit does not reclassify it or wait for old I/O.
  Rationale: a load finishing later belongs to the old generation. Rechecking mutable slot state during commit would require synchronization in the publication path and invalidate the bounded preparation contract.
  Date/Author: 2026-11-01 / Codex, following independent builder check.

## Outcomes & Retrospective

Design only. Runtime baseline is 25/25 on .NET SDK 10.0.114 using `dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore`. This is not validation of a new implementation. Product code, tests, codegen, source metadata and runtime schemas are unchanged.

## Context and Orientation

`ContentRegistry` is an Engine-scoped singleton exported as both concrete type and `IContentRegistry`. `Start` reads the manifest without loading artifacts. `LoadAsync<T>` currently chooses a CLR payload type per caller, performs single-flight I/O per slot and publishes a boxed/object payload. Current codegen emits version-one typed refs plus untyped ID-only refs. Existing tests cover initialization, ordinary single-flight/retry, typed retrieval and basic allocations.

An asset generation is the uint version in a typed reference. A registry revision identifies one committed manifest snapshot; it is also the generation assigned to assets added/changed by that commit. A slot groups descriptor, generation and loading state for one asset. A loaded record is one coherently published object containing the payload and its generation/type information. A prepared token holds a complete candidate root plus the exact root from which it was prepared.

The current branch starts at main independently of open PRs #108 (build cache) and #109 (manifest diff). The design does not require branching from either. If implementing against main before #109 merges, compare normalized descriptors inside Runtime; do not copy the public diff API or introduce an unapproved dependent branch. Prefer reusing the merged diff helper if it becomes available before implementation.

Relevant docs: `docs/02_ADR/content-pipeline-build-contract.md`, `content-target-profiles.md` and `content-pipeline-texture-source-format.md`. Relevant runtime files: `ContentRegistry.cs`, `ContentSlot.cs`, `AssetLease.cs`, `AssetRef.cs`, `IContentRegistry.cs`, `IContentStore.cs`, `FileContentStore.cs`. Tests are under `tests/Karpik.Content.Runtime.Tests`.

## Approaches

1. Mutate the current Dictionary/slots under a global lock. Simpler to write, but puts contention and potentially blocking waits into ECS reads; reject for the engine's real-time constraints.
2. Publish one root reference to an immutable lookup, with generation-bound slots and coherent loaded records. Selected proposal: prepare off-thread, commit without allocation/I/O/waiting, retain current zero-allocation lookup behavior and safe managed lifetimes.
3. Rebuild storage around native indexed handles/SoA. Could reduce indirection for measured large batches, but adds memory-safety and retirement machinery without a demonstrated lookup bottleneck. Defer; do not combine storage experimentation with correctness work.

Automatically treating every typed reference as latest was considered and rejected by the developer. No design below reinterprets an existing Version value as a wildcard.

## Proposed Runtime Contract

### Additive API and ownership

Keep `IContentRegistry` and the binary shape/default constructors of `AssetRef<T>` unchanged. Introduce an Engine-scoped `IContentUpdateRegistry` export implemented by the same ContentRegistry instance, using the existing Export/ServiceRegistration/constructor-injection conventions.

Proposed signatures are `bool TryResolve<T>(AssetId id, out AssetRef<T> current)`, `Task<PreparedContentUpdate> PrepareAsync(ContentManifest manifest, CancellationToken cancellationToken = default)` and `bool TryCommit(PreparedContentUpdate update)`. The prepared token is a process-local control object, not a class sent through IPC/RPC. Its concrete organization and internal helper names are builder choices.

Initialization still accepts manifest/store through RegisterManifest and supports existing Start/tests. The first call creates revision one and generation-one slots. A second RegisterManifest call throws InvalidOperationException with a migration instruction to prepare/commit. A registry binds one store for its lifetime; PrepareAsync uses that store rather than swapping it while loads are running.

The host owns the safe phase boundary. TryCommit performs no callbacks, ECS edits, disk I/O, decoder work or waiting. This slice does not register a new per-frame polling system or claim that an arbitrary caller is automatically on a safe boundary. External phase integration remains a prerequisite for production hot reload.

### Snapshot reads and single-flight loading

Replace the mutable shared map with a root object published through one acquire/release reference operation. The root owns a read-only-after-publication Dictionary of AssetId to slot, its canonical normalized descriptors and a revision. Read methods capture this root exactly once. Keep the current direct ID-to-slot shape; avoid adding a second dictionary or an unnecessary index indirection without measurement.

Each slot's ID, generation, descriptor and store binding are fixed. Its asynchronous control state may use the existing per-slot lock outside the hot read path. A loaded payload is published as one immutable record reference; TryGet never combines a version from one record with a payload from another. Loaded records/slots are built outside TryCommit. Payload objects are read-only by caller contract, not deep-immutable: this work does not clone byte arrays or RawJsonPayload on each read.

LoadAsync captures root, slot, descriptor, store and requested generation before starting I/O. A request with a stale version fails clearly (KCR204) without mutating the current slot. A first-load type binds that slot's payload type; a concurrent incompatible T must fail rather than share another type's successful task. Same-version/same-type calls retain single-flight behavior; the loaded fast path remains Task.CompletedTask.

Retries do not increment asset generation: they retry that descriptor/version. A loader finishing after an update may complete its original task against its captured slot, but cannot write through a new registry lookup into the replacement slot. Completion means the captured load finished, not that its generation is still current; callers must use TryGet/IsAlive. Failure and cancellation update only the captured slot and allow retry.

TryResolve returns a reference for a currently registered slot's generation, including unloaded slots so callers can request loading. It does not perform I/O or refresh an ECS component implicitly. A missing ID returns false. For an already type-bound slot, incompatible T returns false; an unbound slot can be resolved and is bound by the first valid load. Generic type compatibility uses existing CLR payload behavior, not an invented declaredType-to-CLR mapping.

### Prepare and commit

Preparation captures one expected root, clones/normalizes the proposed manifest and validates schema, unique IDs, required descriptor data, dependency existence and cycles before staging work. Namespace/target correctness remains the build/host contract because the public manifest does not contain target metadata. Do not claim runtime can reconstruct target selection from folder names.

Compare normalized canonical entry fields, ignoring entry/dependency ordering. An identical candidate is a no-op and does not advance revision. For a real change, compute checked `expected.Revision + 1`; reject exhaustion before preparation can publish anything. Unchanged assets retain their existing slots and versions. Every new or changed asset uses the new revision as its generation; removed IDs are omitted. Re-adding an ID in a later revision cannot match its old reference even though no tombstone table is kept.

Classify each changed slot exactly once while briefly holding that slot's control lock during preparation; capture its loaded record or its not-loaded classification, then release the lock before I/O. This is a per-slot observation, not a claim that mutable load states form a global snapshot. A load that completes after this observation stays work for the old slot, even if it completes before commit. Do not reclassify the candidate or newly reject a custom decoder at commit time. A candidate prepared from a loading slot is allowed to be unloaded after publication; the host explicitly loads the newly resolved reference.

For a changed asset observed loaded, recreate a built-in payload from the new artifact off-thread and put the completed result into a new slot. Check the returned byte length against the descriptor size; RawJsonPayload updates additionally require valid JSON rather than merely wrapping arbitrary text. String/byte-buffer payloads retain their existing decoding semantics. Any I/O/decoding/validation error or cancellation rejects the entire candidate and keeps the expected root visible. Changed slots observed unloaded/failed/loading become new unloaded slots, without waiting for old I/O. Removed slots may finish old work in isolation. Unchanged loading slots may be shared because their descriptor/generation did not change.

For a loaded custom payload whose descriptor changes, or a loaded asset whose declared type changes in place, fail preparation with a clear unsupported-decoder/type-migration diagnostic; do not reinterpret the old CLR payload as a new declared type. Do not retain custom Type/delegate caches in Engine static fields. Removing a custom managed payload is allowed under existing managed ownership, but this API never disposes GPU/native/IDisposable resources. Such resource lifetimes require a separate backend contract. First-load CLR type bookkeeping is per inflight slot and must be released on failed/cancelled loading rather than retaining a reloadable type indefinitely after failure.

Bound preparation to one active or ready candidate per registry. A second preparation is rejected rather than queued without limit. The gate is acquired/released on the control plane, not by TryGet. Abandoning/disposing a prepared token releases its managed candidate references and the gate without affecting active content.

TryCommit is a one-shot operation: verify owner/token state/cancellation and compare-exchange the expected root with the fully prepared root. Success publishes all candidate entries at once and invalidates the token. A stale, foreign, disposed, cancelled or already consumed token returns false and never rewrites current content. Release the preparation gate without callbacks or allocation. There is no automatic retry; the host resolves the current revision and prepares again. An identical candidate succeeds without changing the root/revision or reloading payloads.

The store must serve immutable artifact bytes for captured locators during preparation; this library API does not make an in-place build output generation-safe. Coordination with the publisher (which can delete old files), notifications, overlapping builds and retention of artifact generations must be designed before wiring live filesystem updates. Last-good guarantees in this slice cover already loaded managed payloads and the published in-memory root, not restoration of externally deleted files.

### Leases and worker restart

After commit, old changed/removed references and lease IsAlive checks fail; unchanged references stay alive. A lease already holding an old managed payload keeps that object memory-safe until it leaves scope, even though it is no longer current. Dispose remains a no-op for these managed payloads. No GPU fence, native ownership or pool-return claim follows from this behavior.

AssetRef versions are local to their owning registry lifetime. The layout contains no registry/session ID, so a value copied into a different registry cannot intrinsically detect that mistake. Persist AssetId as authoring identity; on worker restoration, the host must re-resolve references before gameplay uses restored data. Do not claim old version numbers are valid after a process restart or that this PR automatically repairs ECS fields.

This slice leaves codegen's initial version-one fields unchanged. After a runtime update, callers use the field's ID with TryResolve to obtain a current version. Wiring restore-time reference rebinding/invalidation into ECS serialization is a separate integration task, required before declaring end-to-end content/code hot reload complete. There is no change to physics/gameplay fixed dt or automatic mutation of authoritative ECS state.

## Real-Time Assessment

TryGet, IsAlive, TryResolve and TryCommit must allocate zero bytes after warm-up and must not lock, wait, deserialize, copy payloads or touch the filesystem. Readers capture one root and one coherent loaded record. Preparation allocates dictionaries, slot/record objects and decoder buffers off the simulation hot path; publication is O(1). No worker-driven changes to a published lookup are permitted. Background preparation can still create process-wide GC work; this is a control-plane operation to throttle, not a per-frame task.

The existing per-asset object layout is retained to limit scope. Dictionary lookup/pointer chasing remains a known cost; dense/native redesign requires profiling. No Client/Server import leak, static gameplay state, service locator or new package is introduced. Payloads are content resources, not authoritative gameplay components. ECS only stores existing unmanaged refs; application of new game data remains explicit at fixed-tick boundaries.

## Plan of Work

After design approval, expand these milestones into TDD implementation tasks. Expected changes: ContentRegistry/ContentSlot coherent root/record storage and captured loading; a new IContentUpdateRegistry plus prepared-token/snapshot helper files in Runtime; lease checks using captured generation; focused runtime update/concurrency/allocation tests; and a durable runtime-content ADR. Preserve codegen/AssetRef layout and existing read interface. No Core build cache/diff changes are required by this task.

## Milestones

### 1. Coherent reads and captured loads

Write failing tests for mixed-type single-flight requests, stale versions and out-of-order load completion against a replaced snapshot; establish the immutable root/loaded-record boundary. Validate ordinary init/retry/single-flight behavior and add explicit TryResolve coverage. End with focused runtime tests and record results.

### 2. Prepared updates and version-safe publication

Implement control-plane preparation, checked revision assignment, all-or-nothing built-in payload preload and one-shot allocation-free commit. Cover no-op, change/add/remove/re-add, abandoned/cancelled/foreign tokens, rejected overlapping preparation and loaded custom-payload rejection. Validate after the milestone before expanding integration.

### 3. Concurrency, allocations and compatibility

Use deterministic barriers/TaskCompletionSource stores, not sleeps, to drive old-load/new-publication orderings and repeated commits. Validate monotonic generations, correct payload/version pairing and last-good content on preparation failures. Measure hot read/resolve/commit allocations with all test setup outside the measurement. Run full Runtime and Content suites and smallest Runtime build. Existing codegen tests must continue to pass without snapshot rewrites.

### 4. Review and delivery

Update the runtime ADR with the approved reference/lease/ownership/initialization contract and explicit integration prerequisites. Independent whole-branch review, final checks, push and one PR to main; identify the second-RegisterManifest behavioral restriction. Do not close roadmap content-notification or end-to-end hot-reload items for this library-only slice.

## Concrete Steps

Run from `/home/artem/git/Karpik.Engine`:

```sh
dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore
dotnet test tests/Karpik.Content.Tests/Karpik.Content.Tests.csproj -m:1 -nr:false --no-restore
dotnet build src/Karpik.Content.Runtime/Karpik.Content.Runtime.csproj -m:1 -nr:false --no-restore
git diff --check
```

Use a focused FullyQualifiedName filter for milestone tests; then run the full affected suites. Runtime baseline is 25 passed/zero skipped; Content main baseline is 111. Counts may increase if independent PRs merge; inspect the actual integration revision instead of rewriting expected results from memory. VSTest requires a local testhost socket and may require approved sandbox escalation. Existing warnings are recorded separately from failures.

## Validation and Acceptance

Verify initial generated version-one refs; same-generation retry and no-extra-I/O loaded fast path; duplicate/mixed-type request semantics; explicit resolution; no-op preserving generations/payload identity; changed/added/removed IDs; remove/re-add not reviving old refs; loaded built-in updates staged before commit; failed/cancelled/custom-type preparation preserving the current root; late old success/failure unable to poison new slots; unchanged inflight loads retaining single-flight; all readers seeing coherent payload/version pairs; zero hot-path allocation/locks/waits; one-shot tokens and bounded preparation; checked exhaustion rather than uint wrap; single-use initialization; input-manifest mutation after prepare unable to alter candidate descriptors.

Exercise classification races in both directions: an asset observed loaded must be staged (or reject its custom decoder), while an asset observed loading may finish old work before commit and still publish a new unloaded slot. A custom load completing after the observation must not turn an already prepared candidate into a commit-time failure. Also verify descriptor-size mismatch, invalid RawJsonPayload JSON and loaded declared-type migration fail preparation without damaging active content.

Tests must not assert that a snapshot-version value can distinguish a foreign registry/process; the existing layout cannot supply that guarantee. Tests should instead demonstrate explicit rebinding by ID against a fresh registry before use, and documentation must make the host prerequisite visible. Likewise do not assert safety for concurrent mutation of shared payload objects, arbitrary IDisposable resources or uncoordinated build-output deletion.

## Idempotence and Recovery

Discarding or failing preparation leaves active content unchanged. No-op candidates may be repeated. Commit tokens cannot be replayed. Correcting a bad input/store artifact allows a new preparation; caller-controlled retries must not run in the game loop. Keep source assets and public manifests unmodified. Return to previous code by reverting the task branch if necessary; no source-data migration is required, but code calling RegisterManifest repeatedly must adopt the new update API.

Do not merge #108/#109, rebase onto their unmerged branches or change ECS serialization as an incidental fix. Integration against a newly merged main may use their public APIs after verifying the diff. Broader file-generation retention and restart-time rebinding need their own accepted plans.

## Artifacts and Notes

Design/implementation record: this file. Branch: `codex/content-runtime-snapshots`, parent integration commit `7224b18`. Source graphs/QMD were unavailable in this session; targeted source and original ADRs supplied context. Baseline Runtime verification: 25/25, existing compilation warnings, no new product changes.

Potential knowledge notes after implementation (proposals only): publish payload/version as one record; bind async work to its original generation and store; treat versioned handles as registry-local and rebind durable IDs at restore.
