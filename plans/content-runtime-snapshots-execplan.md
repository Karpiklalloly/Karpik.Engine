# Publish version-safe Content runtime snapshots

This living ExecPlan follows plans/PLANS.md. The developer selected explicit version-bound refs, expanded scope to GPU textures, requested verification of existing backend capabilities, and authorized implementation with “Делай” on 2026-11-01. Authoritative resulting contract: docs/02_ADR/content-runtime-snapshots.md. Earlier provisional managed-only and mandatory-fork proposals are superseded.

## Purpose / Big Picture

Prepare a complete replacement off the frame path, publish one immutable root at a host-controlled quiescent boundary, and retire old texture bundles only after all recorded/unsubmitted CPU uses have submitted or been discarded. Preserve generated version-one refs and explicit resolution; do not auto-mutate ECS state. CLI watcher/IPC/Editor and host auto-scheduling remain separate.

## Progress

- [x] Baselines: Runtime 25 tests, Graphics 40 tests.
- [x] Independent branch codex/content-runtime-snapshots from main 7224b18; no dependency on unmerged PRs #108/#109. Draft PR #110.
- [x] Audit installed NeoVeldrid 1.2.1 source and IL; distinguish enqueue fences from existing ordered/deferred disposal. Native evidence: 30 ordering cases plus pending real GLsync case.
- [x] Implement immutable roots, captured loading, additive update API and explicit version resolution. RED missing APIs, then basic suite 29/29.
- [x] Implement graph/type/JSON/size validation, bounded one-shot prepare/commit/abort, no-op/re-add generations and shutdown. RED semantic checks, then Runtime 37/37 before interface mapping.
- [x] Implement neutral lifetime/pin contracts, texture/atlas recording, transfer to merge contexts, ready/claimed/superseded ownership and explicit submit/discard completion. Graphics initial ownership suite 42/42.
- [x] Implement full OpenGL texture bundle adapter and real old-green/new-red framebuffer readback during replacement, with orderly shutdown.
- [x] Allocation regression caught 32-byte Task continuation scheduling in commit. Replace with preallocated IThreadPoolWorkItem. Read/resolve/commit and recording/clear allocation checks pass.
- [x] Complete interface mapping and shutdown/drop regressions. Final Runtime 43/43, Graphics 44/44, production static factory tests 2/2; actual OpenGL/native probe builds pass. Native --content verifies actual MergeThread claimed-context exclusion, transfer, discard/supersede, old-green/new-red readback and shutdown; --di passes both registration/activation orders; six ordered/deferred/negative control cases pass.
- [x] Fresh independent whole-branch review: no Critical, six Important findings. Fixed load cleanup completion, all-slot cleanup aggregation/failure poisoning, deterministic merge-before-registry DI disposal, single public registry constructor, exception-safe merge cleanup and disposed-state recheck after acquiring the gate. Failure and constructor regressions observed RED then GREEN; native DI shutdown observed timeout RED then both orders GREEN. A final idempotent-drain regression found and fixed mutation of an already-disposed lifetime state. No deferred minors.
- [x] Pushed implementation commit da9a56d to codex/content-runtime-snapshots. PR #110 now describes the implemented feature, has enhancement/documentation labels, and is ready for review against main. No merge performed.

## Decisions and implementation rulings

- AssetRef layout stays Guid+uint, first generation 1. Monotonic checked registry revisions avoid remove/re-add ABA; identical updates do not advance revision. Versions are registry-local, not cross-process identities.
- Single-use RegisterManifest is a deliberate behavioral compatibility change; live reset callers migrate to PrepareAsync/TryCommit. Existing IContentRegistry implementers gain no new required members.
- Runtime owns no Client/NeoVeldrid references. Stable Client decoder targets ITexture2D, owns texture/view/sampler/layout/resource-set and orders deletion on the existing GL execution queue. No fork/package version change.
- One outstanding update remains busy through retirement and old inflight draining. At most 4096 manifest assets bound candidate/retirement bookkeeping. Cold pin polling allocates only during updates, never per frame. Cost: a stuck old load/CPU owner defers later updates until it settles.
- AssetLease remains borrowed managed data with no-op Dispose. Renderer/native escapes own explicit pins; copied ref structs cannot safely own disposable refcounts. Cost: external native callers must obey the pin protocol.
- Host supplies quiescent publication and shutdown ordering; no watcher/IPC/auto-commit loop is added. Cost: explicit API alone does not constitute end-to-end automatic hot reload.
- Loaded custom CLR changes require a stable loader; declared-type migration is rejected. Mutable managed payloads are read-only by contract, not copied on reads.
- Store binding is captured at initialization and preserved even through empty manifests. Descriptors/dependencies are cloned/normalized, graph validity checked before staging.

## Context and work sequence

Root/slot code: src/Karpik.Content.Runtime. Graphics recording: Graphics.Core/Buffers/ThreadBuffer.cs and Context/GraphicsContext.cs. GL merge/submission: Graphics.OpenGL/Merge and OpenGLGraphicsBackend.cs. Backend adapter: Graphics.OpenGL/AssetManagement/ContentTextureLoader.cs. Runtime tests: tests/Karpik.Content.Runtime.Tests. Graphics tests use source links under Graphics.Core.Tests. Opt-in native probe is outside the solution.

Execution uses executing-plans inline and TDD, with this ExecPlan as the repository-required ledger. Existing dedicated task checkout is reused. Pre-flight: snapshot API produces version-bound payloads; renderer consumes optional neutral lifetime owners; backend destruction callbacks remain cold and ordered after CPU claim exhaustion. No direct Runtime-to-Client dependency is allowed.

## Validation and acceptance

Run smallest projects with -m:1 -nr:false. Verify preparation invisibility, same-generation retry/single-flight, mismatched types, changed/unchanged/no-op/removal/re-add, late load isolation, abort/foreign token/cancellation, size/JSON/type/graph rejection, registry shutdown and zero-allocation publication/reads. Verify texture and font-atlas claims, dropped frames, merge transfer, claimed list exclusion, superseded/discarded contexts and shutdown clearing. Run native --content plus prior ordering probe; build actual OpenGL module and Client host. Fresh reviewer focuses on missed CPU owners, exception cleanup, saturated reloads, shutdown/DI ordering, and misleading fence assumptions.

## Outcomes / remaining work

Implementation and review fixes are verified. Native evidence is archived in plans/probes/content-gpu-lifetime-audit/content-runtime.txt. Read/resolve/commit and recording/clear allocation checks pass. OpenGL module and the production probe build without errors.

Standard Client.Publish build compiles its assembly but fails the existing Configurator graph-validation gate on Linux because old Windows-style ProjectReference paths are treated as literal backslashes. Reproduced the same errors against a pristine archive of main 7224b18, including its exact submodule commits, with unchanged Configurator. Diagnostic Client.Publish build with -p:KarpikSkipValidation=true passes (0 warnings/errors). This does not turn the standard validation gate green and is recorded as a pre-existing verification limitation, not fixed in this feature.

Review limits: no external ownership-contract violations are supported, and actual GL deletion does not need the misleading enqueue fence. Existing sequential disposed-registry regression plus the new post-gate disposed check cover shutdown rejection; the exact instruction-level pre-check/gate race was verified by code reasoning rather than a deterministic scheduler hook.

Verification commands: dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false --no-restore; corresponding Graphics.Core.Tests command; Engine.Core.Generator.Tests filtered to Run_Static_RegistersAndConstructsContentRuntimeServices and Run_Static_MergeFactoryResolvesContentBeforeActivation; dotnet build the native probe with -m:1 -nr:false --no-restore; probe --content, --di and 2. Standard and diagnostic Client.Publish builds as described above.

Worth recording in docs/knowledge after developer confirmation: OpenGL enqueue fences versus ordered deferred deletion; distinct CPU-buffer/command-list resource ownership; DI activation dependencies as shutdown-order constraints.

## Audit Findings: Fences and Deferred OpenGL Deletion

The installed `NeoVeldrid` 1.2.1 public Fence is not evidence of GPU completion. Its NuGet metadata identifies source commit `3e00eb6ff9f927db036fcf47a1234a80b2b96d53` of `jhm-ciberman/neo-veldrid`. [SubmitCommandsCore](https://github.com/jhm-ciberman/neo-veldrid/blob/3e00eb6ff9f927db036fcf47a1234a80b2b96d53/src/NeoVeldrid/OpenGL/OpenGLGraphicsDevice.cs#L455) queues ExecuteCommands then immediately calls OpenGLFence.Set; OpenGLFence wraps a ManualResetEvent without GLsync. This was independently confirmed against the installed DLL's IL, not inferred only from repository metadata. The latest checked release v1.3.1 retains this behavior.

However, absence of an actual completion fence does not imply that ordinary OpenGL object deletion requires a fork. The earlier hard-prerequisite claim was too broad and is retracted. A separate existing mechanism was missed in that first conclusion: [OpenGLTexture.DisposeCore](https://github.com/jhm-ciberman/neo-veldrid/blob/3e00eb6ff9f927db036fcf47a1234a80b2b96d53/src/NeoVeldrid/OpenGL/OpenGLTexture.cs#L684) enqueues deferred deletion; OpenGLGraphicsDevice.FlushDisposables invokes native destruction on the GL execution thread, including at SwapBuffers. Installed-DLL IL confirms this path too. ResourceSet disposal is managed bookkeeping, while texture/view native destruction goes through the GL queue.

OpenGL orders the effects of commands within a context as if preceding commands completed. Deleting a GL object is not the same as immediately reclaiming all underlying storage: the driver retains storage still needed by earlier issued work. See [Khronos synchronization](https://wikis.khronos.org/opengl/Synchronization) and [OpenGL 4.4 section 5.1.3](https://registry.khronos.org/OpenGL/specs/gl/glspec44.core.pdf). Ordinary ordered texture deletion should not be designed as though it were Vulkan-style externally managed physical memory reuse. Explicit GPU-completion primitives remain necessary for operations that genuinely require observing completion, but that is a different requirement from requesting deletion of an ordinary GL texture.

Engine CPU ownership and GL submission ordering still matter. A draw retained in a writing/ready/merging set or an unsubmitted command list has not yet been issued to GL and is not protected by driver lifetime rules. Also, the resource-disposal queue is separate from the execution work queue: calling Dispose just after SubmitCommands returns is not by itself proof of ordering. An earlier queued SwapBuffers could flush newly queued deletions before a later queued command list is executed.

Candidate existing-API path: retain the resource until all CPU owners have submitted/discarded their commands; enqueue its disposal through BackendInfoOpenGL.ExecuteOnGLThread after all corresponding SubmitCommands calls have entered the ordered execution queue. That callback then requests native deletion after the last relevant GL calls have been issued; GL manages physical storage lifetime. ExecuteOnGLThread blocks its calling thread for a CPU execution-queue barrier, so use it only from a bounded retirement/control worker, never from Render, Update or the publication boundary. The callback may allocate only as cold reload work, not per frame. This avoids claiming a physical GPU-completion signal is needed merely to dispose a texture.

The primitive is now supported by the hardware audit below, not just source inspection. Full engine update integration, delayed writer/merge ownership, cancellation/upload work and exactly-once bundle disposal still require their own tests. Do not substitute the misleading Fence for this ordering or call FlushAndFinish on the main render path. Public DisposeWhenIdle is drained by public GraphicsDevice.WaitForIdle; this OpenGL backend calls it from SwapBuffersCore on each present. The deferred-mode smoke verifies that existing path once the relevant draws have entered the queue.

No fork, upstream issue/PR or package change has been authorized or created. Prefer validating the existing deferred-delete/ordered-execution facilities first; propose a dependency patch only if that audit demonstrates a concrete remaining gap. The fence implementation itself remains a separate confirmed limitation and must not be described as actual GPU completion elsewhere in the design.


## Reproducible audit evidence

See plans/probes/content-gpu-lifetime-audit/README.md, ordered-early.txt and gpu-pending.txt for commands, exact hardware evidence and experiment limits. The historical unsubmitted-wrapper audit intentionally uses the legacy untracked constructor; the new --content mode exercises tracked production Content lifetimes.
