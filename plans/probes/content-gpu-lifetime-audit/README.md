# Content GPU lifetime audit

This is an opt-in Linux/OpenGL investigation probe, not production reload code or an automatic CI test. It uses hidden SDL windows and performs framebuffer readback on a real GL device. It does not fork/modify NeoVeldrid, change package versions, or install an engine hot-reload path.

## Result observed on 2026-11-01

Environment: NeoVeldrid 1.2.1 from central package management; AMD Radeon Graphics (radeonsi, renoir, ACO, DRM 3.64), OpenGL 4.6 Core / Mesa 26.0.8. Probe build succeeded; its referenced engine/tests projects emitted six existing obsolete/nullability warnings. Existing Graphics.Core tests passed 40/40.

| Check | Observed result |
| --- | --- |
| Fence after submission while GL execution is deliberately blocked | Signaled in every case; it does not prove GPU completion |
| Early Dispose with a prior queued deletion-flushing CPU barrier, followed by the queued draw | Black pixel, 10/10 repetitions: negative control reproduces the ordering hazard |
| Dispose requested inside a GL callback ordered after the submitted draw | Correct red pixel, 10/10 repetitions |
| Existing public DisposeWhenIdle followed by ordinary SwapBuffers | Correct red pixel, 10/10 repetitions; disposal is deferred until the public idle/swap boundary |
| Heavy submitted batch, native texture deletion and real GLsync observation | glIsTexture reports the old name deleted while ClientWaitSync(timeout=0) still returns GL_TIMEOUT_EXPIRED; readback is correct red |
| Source-linked ThreadBuffer/VeldridTexture2D | An unsubmitted command still retains the wrapper after Dispose is requested; the buffer does not establish resource ownership |

Raw final runs are in `ordered-early.txt` and `gpu-pending.txt`. `Texture.IsDisposed` alone reports a disposal request in this package; the pending-GPU case separately uses native glIsTexture to distinguish actual deletion of the GL name. The native GLsync is diagnostic only, not a proposed dependency on package fences.

The first unrecorded scratch attempt incorrectly placed SwapBuffers behind a deliberately blocked GL thread and hit its own timeout: SwapBuffers itself waits for the CPU GL queue. That attempt is not evidence about disposal correctness. The final probe instead confirms that the control producer has posted its CPU-queue barrier before enqueueing the draw, then releases the GL gate. No arbitrary sleep chooses the interleaving.

## What the experiment establishes

Ordinary GL texture disposal can use the existing backend's deferred deletion and ordered execution facilities without first implementing true GPU-completion fences. This is narrower than claiming a full renderer hot-reload implementation exists. The driver may retain the underlying storage after the texture name is deleted.

The public `GraphicsDevice.DisposeWhenIdle` is an existing alternative: public WaitForIdle drains that list, and this OpenGL backend calls public WaitForIdle inside SwapBuffersCore before enqueueing the swap. The ten deferred-mode cases verify this path without a custom ordered callback. It still requires that the last relevant CPU commands were submitted before the idle/swap boundary. This is not a promise of physical GPU completion by this package's WaitForIdle implementation.

The early negative control also shows why Dispose immediately after SubmitCommands returns is insufficient: the disposal queue is separate from the execution queue. A previously queued flush can delete the texture before the later queued draw reaches GL. Queue disposal through an ordered GL-thread callback only after all relevant CPU commands have been submitted or discarded. ExecuteOnGLThread blocks its caller, so the demonstrated path invokes it from a cold control task, never from Render/Update or the atomic version switch.

The baseline readback is green; the tested draw must change it to red. This prevents a false positive from merely observing a previous frame. In pending mode, the entire submitted batch's native sync is still pending after the native name has been deleted. The probe does not identify which particular GPU instruction within that batch is still executing.

At the time of the initial audit the engine still needed generation-aware ownership for writing/ready/merging buffers and unsubmitted command lists, all-or-nothing publication, resource-bundle disposal and bounded retirement. A retained managed wrapper is not an ownership claim preventing its Dispose method. The existing fake/signaled-at-enqueue fence must not be treated as GPU completion during that implementation.

## Reproduce

Run from the repository root with .NET 10, a working Linux DISPLAY/SDL/OpenGL context, libGL.so.1 and access to the local graphics device:

```sh
dotnet restore plans/probes/content-gpu-lifetime-audit/ContentGpuLifetimeAudit.csproj -m:1 -nr:false --ignore-failed-sources -p:NuGetAudit=false
dotnet build plans/probes/content-gpu-lifetime-audit/ContentGpuLifetimeAudit.csproj -m:1 -nr:false --no-restore
dotnet plans/probes/content-gpu-lifetime-audit/bin/Debug/net10.0/ContentGpuLifetimeAudit.dll 10
dotnet plans/probes/content-gpu-lifetime-audit/bin/Debug/net10.0/ContentGpuLifetimeAudit.dll --pending
```

That original source-linked setup is superseded by production project references so the probe now exercises the actual Graphics.Core and Graphics.OpenGL assemblies. Package versions come from Directory.Packages.props. The project is deliberately absent from KarpikEngine.slnx so an ordinary solution/test invocation does not open windows or require a graphics device.

Expect `AUDIT PASSED` for the 30 ordering cases and `PENDING GPU AUDIT PASSED` for the native-sync case. A very fast or differently scheduled device may finish pending-mode work before the observation; that case explicitly fails as inconclusive rather than asserting a driver bug. The three ordering scenarios do not depend on GPU speed. Barrier timeouts indicate an invalid/incomplete run and do not count as evidence. This is a correctness probe with deliberate waits and allocations, not a frame-time or no-GC benchmark.

## Source audit

The package's declared source commit is 3e00eb6ff9f927db036fcf47a1234a80b2b96d53. Relevant source and installed DLL IL were checked:

- OpenGLGraphicsDevice.SubmitCommandsCore queues ExecuteCommands, then calls OpenGLFence.Set immediately.
- OpenGLFence uses ManualResetEvent; its Signaled property is WaitOne(0).
- OpenGLTexture.DisposeCore queues EnqueueDisposal; FlushDisposables calls DestroyGLResources on the GL execution thread.
- ExecuteOnGLThread queues the callback and then waits for a CPU queue barrier; that barrier also flushes disposals.
- SwapBuffersCore first calls WaitForIdle, then queues a swap whose execution also flushes disposals.
- DisposeWhenIdle is drained by public GraphicsDevice.WaitForIdle, which this OpenGL backend calls from SwapBuffersCore on each present. The internal CPU-queue barriers used by ExecuteOnGLThread do not independently drain this public list.

Primary references: [device implementation](https://github.com/jhm-ciberman/neo-veldrid/blob/3e00eb6ff9f927db036fcf47a1234a80b2b96d53/src/NeoVeldrid/OpenGL/OpenGLGraphicsDevice.cs), [texture implementation](https://github.com/jhm-ciberman/neo-veldrid/blob/3e00eb6ff9f927db036fcf47a1234a80b2b96d53/src/NeoVeldrid/OpenGL/OpenGLTexture.cs), [Khronos synchronization](https://wikis.khronos.org/opengl/Synchronization), [OpenGL object deletion](https://wikis.khronos.org/opengl/OpenGL_Object#Deletion_orphaning).

## Limits and proposed learnings

Verified on one AMD/Mesa device and the pinned OpenGL package. Not an end-to-end ContentRegistry/renderer update test; not coverage of Vulkan, bindless/external memory, shader compilation, arbitrary native payload ownership or frame-time/allocation budgets. No claim that all earlier draft design details are ready to implement follows from this audit.

Potential knowledge notes, proposed only: distinguish CPU-recorded, GL-issued and GPU-completed work; audit execution/disposal queues separately; use negative controls and a changed baseline color for native readback evidence. No docs/knowledge files were created automatically.

Independent audit review found no critical/important issues and one minor probe-hardening issue: the early-mode assertion accepted any non-red pixel. It now requires exact opaque black; baseline and successful output require exact opaque green/red. The final archived 30 ordering cases and pending-GPU case passed after that change. No minor finding was left deferred.

## Production Content integration

`--content` loads real cooked green/red PNGs through ContentTextureLoader and ContentRegistry. It publishes while an old frame is ready, transfers ownership through the actual MergeThread, proves a claimed context is not reused while a newer frame completes, submits/discards command lists, verifies exact old-green/new-red pixels, supersedes ready lists and drains shutdown. Its small test pipeline bypasses the unrelated legacy shader-file loader; framebuffer readback uses a separate offscreen target.

`--di` creates production registry/merge services through Autofac in both registration and activation orders, leaves a tracked texture in a pending CPU frame and disposes the container. Before the constructor lifetime dependency this reproduced a timeout; with it both orders drain successfully. No shader or merge worker initialization is necessary for the writing-frame shutdown case.

```sh
dotnet plans/probes/content-gpu-lifetime-audit/bin/Debug/net10.0/ContentGpuLifetimeAudit.dll --content
dotnet plans/probes/content-gpu-lifetime-audit/bin/Debug/net10.0/ContentGpuLifetimeAudit.dll --di
```

Final integration and six refreshed native ordering/control results are archived in content-runtime.txt. The earlier unsubmitted-wrapper negative controls deliberately retain the legacy untracked constructor and still reproduce early disposal; they do not claim tracked Content textures behave that way.
