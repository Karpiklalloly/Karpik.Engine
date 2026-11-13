# Structural command acceptance evidence

Date: 2026-11-13. Branch: `codex/modding-0.7`, explicitly authorized by user. Task diff base: `86a5ffaecc1b0d030d59bb3dbe5092a8e3052ebd`. [Plan](lua-structural-commands-execplan.md), [design](lua-structural-commands-design.md), [API](../docs/modules/modding/structural-commands.md).

## Verification

| Check | Passed | Failed | Total | Artifact |
| --- | ---: | ---: | ---: | --- |
| Baseline targeted | 16 | 0 | 16 | `tests/Karpik.Engine.Core.Runner.Tests/TestResults/structural-baseline.trx` |
| Initial Lua Red | 2 | 3 | 5 | `tests/Karpik.Engine.Core.Runner.Tests/TestResults/structural-lua-red.trx` |
| Initial common Red | 0 | 3 | 3 | `tests/Karpik.Engine.Core.Runner.Tests/TestResults/structural-common-red.trx` |
| Integrated final feature | 57 | 0 | 57 | `tests/Karpik.Engine.Core.Runner.Tests/TestResults/structural-integrated.trx` |
| Full Runner | 640 | 0 | 640 | `tests/Karpik.Engine.Core.Runner.Tests/TestResults/structural-runner-full.trx` |
| Full Generator | 78 | 0 | 78 | `tests/Karpik.Engine.Core.Generator.Tests/TestResults/structural-generator.trx` |

Initial Reds were meaningful runtime failures after successful builds: Lua lacked ctx.commands (3 failed/2 passed), common stubs threw NotImplementedException (3 failed). Early new-fixture compilation errors CS9007/CS9174 and missing dynamic ScriptLogger registration were corrected before final acceptance. New tests are conventional TDD, not a claim of strict per-case Red for every later integration test.

Final fresh Release Runner build: 0 errors, 7 existing warnings (`/tmp/karpik-structural-final-build.log`). Full Runner: 640/640, no skips, exit0, 7m16s; Generator: 78/78, no skips, exit0,12s (`/tmp/karpik-structural-{runner-full,generator}.log`). Test commands use `-c Release -m:1 -nr:false`; final Runner run uses freshly built `--no-build --no-restore`. VSTest required authorized execution outside sandbox because loopback socket creation was blocked; no approval rejection occurred.

## Common C# allocations and latency

All scalar kinds, writable/default/readonly, native+dynamic creation/add/del and pointer payload common paths have warmed 0 B tests. Pointer revocation after enqueue rejects the entire deferred batch; 300 deterministic seeded FIFO sequences compare results with a reference model. Fatal listener/reentrancy and cancellation poisoning, technical-anchor protection, owner/generation/token checks and exact capacity boundaries are covered.

Standalone common-only probe uses the exact final production DLLs; probe/current Modding.Core, Modding.Lua and Runner.Tests SHA256 match. Source/reproduction: [probe](probes/lua-structural-commands/README.md). Raw results (microseconds, not production SLA; setup/factories/cleanup outside steady samples, cold includes JIT):

```text
Common N=0;C=0;V=0;cold_B=0;cold_us=789.625;steady_B/callback=0;p50_us=0.080;p95_us=0.090;max_us=0.110;world_capacity=16
Common N=1;C=2;V=2;cold_B=88;cold_us=9453.199;steady_B/callback=0;p50_us=1.854;p95_us=1.894;max_us=1.964;world_capacity=16
Common N=16;C=32;V=32;cold_B=0;cold_us=43.523;steady_B/callback=0;p50_us=24.988;p95_us=38.273;max_us=6169.449;world_capacity=32
Common N=256;C=512;V=512;cold_B=0;cold_us=1455.354;steady_B/callback=0;p50_us=617.417;p95_us=4809.978;max_us=7885.521;world_capacity=512
Common N=4096;C=8192;V=8192;cold_B=0;cold_us=11606.032;steady_B/callback=0;p50_us=1760.295;p95_us=5332.174;max_us=6628.104;world_capacity=8192
```

N=4096 common workload: 8192 commands,8192 copied leaves. All N have 0 B steady state; cold N=1 had88 B and JIT cost. OS/tiered-JIT outliers are retained, not silently excluded. No latency guarantee inferred.

## Experimental Lua allocations

Canonical workload: one create + native add + dynamic add per entity, two captured scalar leaves, no exact arithmetic. Prepared snapshots;16 warmup and32 measured callbacks. Cleanup is outside byte/time samples; Gen0 counter includes cleanup. Host test-specific limits exceed defaults for large N. Approved gate: `4096 + 1600*C + 1600*V + 544*O` B/callback. Previous typed/f32/composite gates unchanged.

| N | B/callback | Ceiling | p50 us | p95 us |
| ---: | ---: | ---: | ---: | ---: |
| 0 | 1496 | 4096 | 3.095 | 9.879 |
| 1 | 3336 | 12096 | 10.830 | 31.079 |
| 16 | 30936 | 132096 | 37.341 | 53.632 |
| 256 | 472536 | 2052096 | 1532.600 | 4012.408 |
| 4096 | 7538136 | 32772096 | 19249.576 | 30910.365 |

Lua N=4096 allocates about7.54 MB/callback even though common C# is0B. The backend remains experimental and is unsuitable for claiming zero-GC production gameplay or a latency SLA. Peak managed/process measurements and raw stdout are in the TRX.

## Independent review

Separate read-only reviewer inspected actual code/tests/docs after complete Runner/Generator verification: no P1/P2 runtime findings. P3 stale recovery statements were corrected and rechecked. Reviewer requested explicit common-only nested/fixed0B evidence; the same-DLL probe extension below closes that measurement gap. Reviewer confirmed the follow-up closes the gap: final verdict no open actionable findings. No production changes or regression rerun required.

## Compatibility and limits

Immediate pool APIs and historical allocation gates preserved. Deferred commands reject final-component deletion, empty creation, readonly/foreign handles, incompatible snapshots and technical-anchor writes. Existing zero-component targets are rejected if an earlier pending materialization can collect them; initialize such a target before materializing other targets. Numeric IDs already stale before enqueue retain the previous raw-int limitation; captured generations detect replacement after enqueue.

Native listeners remain synchronous/non-reentrant; arbitrary host allocations are outside no-GC guarantees. No rollback for unexpected playback failure; stop Simulation. Save/network/reload, NativeAOT,32-bit and manual game/editor smoke not run and not claimed.

## Publication

No push or PR mutation. Existing remote draft PR101 still targets main from head4767dca; previous local type-catalog commit86a5ffa is unpublished. Reused branch publication would broaden that PR and publish the preceding stage, excluded by the approved scope. Prepared [PR title/body](lua-structural-commands-pr.md).

## Final source fingerprints

These hashes identify production/test source used by the accepted builds. Documentation-only updates do not invalidate that relation.

```text
009a2fb5bfeac44d0f94571d2253fe0f9c5c3ce73269558733cd8ba8995c46a3  Modules/Shared/Modding/Modding.Core/Bindings/ClrComponentPool.cs
0a6974a6ac04282b412cd2e454bf27289fc7febb2affaab9afdb169698cee9e9  Modules/Shared/Modding/Modding.Core/Bindings/ComponentQueryPreparer.cs
ae33a3bbf434a7434477840e2a33c86191be06ea6861b8a1fa66b64051304d05  Modules/Shared/Modding/Modding.Core/Bindings/ComponentSnapshot.cs
ffc36d611ce9db958a5140b43dbde7b954dd80138fc3ea8e4ca53bd945220f9e  Modules/Shared/Modding/Modding.Core/Bindings/DynamicComponentPool.cs
242f59887a439fb5633b67c0d201e697e515de62edae8ebc28673cef564081c1  Modules/Shared/Modding/Modding.Core/Bindings/DynamicComponentStorage.cs
3488351d107cdd4bdf3090dd09740d6466b295db8e43b723ff3319d70dbb12cd  Modules/Shared/Modding/Modding.Core/Bindings/StructuralCommandBuffer.cs
51e7c26c10ef48e0e8897ff2bdaefb4bdd172ea9c1d746e403e575add51b55f7  Modules/Shared/Modding/Modding.Core/Bindings/StructuralCommandPayload.cs
0c6309c26e5783ca565f7cd96cab2d404295a9cd87e1765db05c1fe3d1b360b5  Modules/Shared/Modding/Modding.Core/ScriptSystemOptions.cs
3fff98e55834776aff532907aae743bc9cc0346e01960d533925f9286ea008eb  Modules/Shared/Modding/Modding.Lua/LuaEcsContext.cs
7767fbd9925be8065601e95bb46cd98dec758f43f086f06d006a4e50a2c0c5ac  Modules/Shared/Modding/Modding.Lua/LuaScriptRuntime.cs
9775a9b0634bab28b160d26fe7c362c039f58764f70420e8c670baa685435767  tests/Karpik.Engine.Core.Runner.Tests/LuaStructuralCommandAllocationTests.cs
09dc8e40a887ead268355cea1661b8a66c4469800b2a8141e850f8b8d14cef20  tests/Karpik.Engine.Core.Runner.Tests/LuaStructuralCommandRunnerTests.cs
d94656b30f45f97d24c325d793c7b07e1e9c6c99852324d7c02441fbdd8c378d  tests/Karpik.Engine.Core.Runner.Tests/LuaStructuralCommandTests.cs
67c5faea887636372a1b3e8829ccdb47e569a9f7a400295ac1e918fdb4cd2f47  tests/Karpik.Engine.Core.Runner.Tests/StructuralCommandBufferTests.cs
850b8973f49b2b8f7290466235decb6a9eb4f1ad32c04128ab054520982b4be7  tests/Karpik.Engine.Core.Runner.Tests/StructuralCommandPayloadTests.cs
```

## Review follow-up: direct common nested/fixed measurements

Durable probe source was extended with dynamic struct+fixed and generated native NativeShape nested/fixed/readonly data. Fresh probe-only build0warnings0errors; execution exit0. No production/test source or DLL was rebuilt by this probe (references existing DLLs only). All15 workloads pass with0B warmed allocation, including copied nested/fixed values and readonly/default checks outside samples. Raw data retain cold allocations (including native4096 cold6288B), JIT/OS outliers and full p50/p95/max:

```text
Common flat N=0;C=0;V=0;cold_B=0;cold_us=3161.369;steady_B/callback=0;p50_us=0.081;p95_us=0.091;max_us=0.180;world_capacity=16
Common flat N=1;C=2;V=2;cold_B=88;cold_us=17189.149;steady_B/callback=0;p50_us=1.773;p95_us=1.814;max_us=1.874;world_capacity=16
Common flat N=16;C=32;V=32;cold_B=0;cold_us=42.110;steady_B/callback=0;p50_us=24.317;p95_us=25.759;max_us=43.513;world_capacity=32
Common flat N=256;C=512;V=512;cold_B=0;cold_us=400.294;steady_B/callback=0;p50_us=438.817;p95_us=1314.506;max_us=2309.034;world_capacity=512
Common flat N=4096;C=8192;V=8192;cold_B=0;cold_us=7073.118;steady_B/callback=0;p50_us=1312.603;p95_us=4972.833;max_us=13572.001;world_capacity=8192
Common dynamic-nested-fixed N=0;C=0;V=0;cold_B=0;cold_us=1.062;steady_B/callback=0;p50_us=0.050;p95_us=0.051;max_us=0.070;world_capacity=16
Common dynamic-nested-fixed N=1;C=2;V=5;cold_B=0;cold_us=117.775;steady_B/callback=0;p50_us=1.092;p95_us=1.153;max_us=1.252;world_capacity=16
Common dynamic-nested-fixed N=16;C=32;V=80;cold_B=0;cold_us=18.254;steady_B/callback=0;p50_us=14.648;p95_us=19.066;max_us=5569.541;world_capacity=32
Common dynamic-nested-fixed N=256;C=512;V=1280;cold_B=0;cold_us=197.396;steady_B/callback=0;p50_us=175.515;p95_us=391.606;max_us=4044.423;world_capacity=512
Common dynamic-nested-fixed N=4096;C=8192;V=20480;cold_B=0;cold_us=3911.339;steady_B/callback=0;p50_us=1958.896;p95_us=3678.575;max_us=5652.700;world_capacity=8192
Common native-nested-fixed N=0;C=0;V=0;cold_B=0;cold_us=0.881;steady_B/callback=0;p50_us=0.040;p95_us=0.050;max_us=0.060;world_capacity=16
Common native-nested-fixed N=1;C=2;V=5;cold_B=0;cold_us=2620.487;steady_B/callback=0;p50_us=0.742;p95_us=0.772;max_us=1.453;world_capacity=16
Common native-nested-fixed N=16;C=32;V=80;cold_B=0;cold_us=19.347;steady_B/callback=0;p50_us=10.050;p95_us=10.239;max_us=18.715;world_capacity=32
Common native-nested-fixed N=256;C=512;V=1280;cold_B=0;cold_us=161.127;steady_B/callback=0;p50_us=161.298;p95_us=335.199;max_us=2283.204;world_capacity=512
Common native-nested-fixed N=4096;C=8192;V=20480;cold_B=6288;cold_us=4499.410;steady_B/callback=0;p50_us=2578.918;p95_us=5599.157;max_us=6572.824;world_capacity=8192
```
