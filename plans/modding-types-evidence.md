# Приёмка расширения Lua/ECS типов — 2026-11-12

Окончательный код в текущей codex/modding-0.7: Runner **583/583**, Generator **78/78**, failed0/skipped0, exit0. Fresh Release build с -m:1 -nr:false; dotnet10.0.115 / CLR10.0.15 / MoonSharp2.0.0. Полные команды/история исправлений — [ExecPlan](modding-types-execplan.md). TRX: `/tmp/karpik-types-runner-complete.trx`, `/tmp/karpik-types-generator-complete.trx`. No push/PR.

Fingerprint 35 changed production/test .cs/.csproj files (sorted path+NUL+bytes+NUL): `57d81f5b8e1abd5fe7d77fd88a504f38fdec7f8a3c415e4c210084982afea26a`. Документы и standalone probe в fingerprint не включены. Он привязывает результаты к uncommitted task sources, не является commit hash.

Independent read-only review завершено: R1 native InlineArray diagnostic и R2 constructor-injection invariant исправлены и повторно проверены reviewer; open findings0. Reviewer не запускал tests. Авторские проверки включают Static/Dynamic × Deterministic/Parallel, обе error policies, readonly/owner/disposal/schema failure paths, retained snapshot/weak references, strict exact-number conversions и current-process native-width bounds.

## Common C# и cold costs

Reserved scalar/nested/native/fixed/pointer access и repeated snapshot copy_to — **0 B** в исполняемых tests. Создание snapshots не входит в это обещание: оно создаёт собственное typed data/metadata. Common snapshots не удерживают source pool/world; lifetime ограничен Simulation gate.

Отдельный диагностический probe выполнен по окончательным DLL в свежем процессе. [Исходник](artifacts/modding-types-cold-probe.cs), временный net10.0 console project `/tmp/karpik-types-cold-probe/Cold.csproj` использует прямые ссылки на Release Modding.Core/Modding.Lua/DragonECS/Core/MoonSharp DLL, без пересборки repository. Команда `dotnet run --project /tmp/karpik-types-cold-probe/Cold.csproj -c Release -m:1 -nr:false -p:RestoreSources=/home/artem/.nuget/packages -p:NuGetAudit=false`; DOTNET_CLI_HOME=/tmp/karpik-dotnet-cli, NUGET_PACKAGES=/home/artem/.nuget/packages. Runtime reflection только для регистрации internal exact module при подготовке диагностического probe, не в измеряемом typed access.

```text
Common schema/world/query preparation: 169704 B (cold; includes world).
Common new_snapshot L=7: 1528 B/call.
Retained 256 snapshots managed-live delta: 398928 B.
Common copy L=7: 1528 B/call.
Lua empty: 256 B/call (includes Script.Call).
Lua i64 string constructor: 736 B/call (includes Script.Call).
Lua decimal string constructor: 2264 B/call (includes Script.Call).
Lua decimal tostring: 784 B/call (includes Script.Call).
Probe process observed peak working set: 47833088 B; includes CLR/code/preparation, not a VM-only peak.
```

Common factory samples:32 warmups/256 retained objects,7 scalar leaves. Managed-live delta — наблюдение до/после Full GC при удержании256snapshots, включает framework/cache differences. Working-set peak относится ко всему отдельному probe process, не изолированной VM. Exact constructors/tostring:128 warmups/256 measured calls, стоимость включает Script.Call, empty control256B/call. Нет отдельного allocation SLA для cold/explicit allocation APIs.

## Lua canonical workloads

Новый ceiling `4096+1600*N*L+544*N*O`: L copied scalar leaves/entity, O exact arithmetic operations/entity; N=0/1/16/256/4096. Formatting/parsing и snapshot creation вне measured canonical loops. Existing f32/mixed-five gates не повышались. В одном промежуточном полном прогоне existing mixed-five N0 дал8B суммарной разницы на1000calls; unchanged focused5/5 и два последующих full gate runs прошли. Причина единичного overhead не установлена; исключений/округлений в gate не добавлялось.

Время/Gen0 — диагностические измерения, не hard latency SLA. Lua остаётся экспериментальным и выделяет мегабайты на больших пачках, даже когда общий C# слой0B. Вот полные строки окончательного full Runner run:

```text
Composite nested; N=0; L=7; O=1; samples=20; B/callback=1984; ceiling=4096; p50_us=0.952; p95_us=1.884; p99_us=1.964; Gen0=0; CLR=10.0.15
Composite nested; N=16; L=7; O=1; samples=20; B/callback=64960; ceiling=192000; p50_us=58.372; p95_us=89.721; p99_us=676.029; Gen0=0; CLR=10.0.15
Composite nested; N=1; L=7; O=1; samples=20; B/callback=5920; ceiling=15840; p50_us=13.846; p95_us=14.989; p99_us=15.630; Gen0=0; CLR=10.0.15
Composite nested; N=256; L=7; O=1; samples=20; B/callback=1009600; ceiling=3010560; p50_us=1316.099; p95_us=3015.329; p99_us=3135.990; Gen0=9; CLR=10.0.15
Composite nested; N=4096; L=7; O=1; samples=20; B/callback=16123840; ceiling=48107520; p50_us=17962.790; p95_us=20973.260; p99_us=22170.132; Gen0=154; CLR=10.0.15
Composite pointer; N=0; L=1; O=0; samples=20; B/callback=1968; ceiling=4096; p50_us=0.962; p95_us=1.833; p99_us=1.883; Gen0=0; CLR=10.0.15
Composite pointer; N=16; L=1; O=0; samples=20; B/callback=22576; ceiling=29696; p50_us=13.406; p95_us=13.636; p99_us=15.810; Gen0=0; CLR=10.0.15
Composite pointer; N=1; L=1; O=0; samples=20; B/callback=3256; ceiling=5696; p50_us=1.853; p95_us=2.585; p99_us=2.585; Gen0=0; CLR=10.0.15
Composite pointer; N=256; L=1; O=0; samples=20; B/callback=331696; ceiling=413696; p50_us=262.030; p95_us=819.122; p99_us=909.754; Gen0=3; CLR=10.0.15
Composite pointer; N=4096; L=1; O=0; samples=20; B/callback=5277616; ceiling=6557696; p50_us=5801.232; p95_us=5998.728; p99_us=6346.611; Gen0=50; CLR=10.0.15
Composite snapshot-copy-to; N=0; L=7; O=0; samples=20; B/callback=1912; ceiling=4096; p50_us=1.052; p95_us=1.783; p99_us=2.174; Gen0=0; CLR=10.0.15
Composite snapshot-copy-to; N=16; L=7; O=0; samples=20; B/callback=14968; ceiling=183296; p50_us=11.763; p95_us=12.664; p99_us=12.684; Gen0=0; CLR=10.0.15
Composite snapshot-copy-to; N=1; L=7; O=0; samples=20; B/callback=2728; ceiling=15296; p50_us=1.643; p95_us=2.404; p99_us=2.475; Gen0=0; CLR=10.0.15
Composite snapshot-copy-to; N=256; L=7; O=0; samples=20; B/callback=210808; ceiling=2871296; p50_us=745.301; p95_us=1110.758; p99_us=1511.893; Gen0=1; CLR=10.0.15
Composite snapshot-copy-to; N=4096; L=7; O=0; samples=20; B/callback=3344248; ceiling=45879296; p50_us=3318.928; p95_us=4948.306; p99_us=5196.488; Gen0=32; CLR=10.0.15
Exact-add decimal, N=0: 736 B/callback, ceiling=4096.
Exact-add decimal, N=16: 8544 B/callback, ceiling=12800.
Exact-add decimal, N=1: 1224 B/callback, ceiling=4640.
Exact-add decimal, N=256: 125664 B/callback, ceiling=143360.
Exact-add decimal, N=4096: 1999584 B/callback, ceiling=2232320.
Exact-add i64, N=0: 736 B/callback, ceiling=4096.
Exact-add i64, N=16: 8416 B/callback, ceiling=12800.
Exact-add i64, N=1: 1216 B/callback, ceiling=4640.
Exact-add i64, N=256: 123616 B/callback, ceiling=143360.
Exact-add i64, N=4096: 1966816 B/callback, ceiling=2232320.
Exact-add u64, N=0: 736 B/callback, ceiling=4096.
Exact-add u64, N=16: 8416 B/callback, ceiling=12800.
Exact-add u64, N=1: 1216 B/callback, ceiling=4640.
Exact-add u64, N=256: 123616 B/callback, ceiling=143360.
Exact-add u64, N=4096: 1966816 B/callback, ceiling=2232320.
Forced growth first invocation N=513; B/callback=1124544; us=1068.237; includes query/VM cold costs
Typed ECS bool N=0; B/callback=1984; p50_us=0.952; p95_us=5.470; p99_us=10.540; Gen0=1; CLR=10.0.15
Typed ECS bool N=16; B/callback=23872; p50_us=51.819; p95_us=58.792; p99_us=461.018; Gen0=12; CLR=10.0.15
Typed ECS bool N=1; B/callback=3352; p50_us=1.854; p95_us=2.716; p99_us=3.166; Gen0=2; CLR=10.0.15
Typed ECS bool N=256; B/callback=352192; p50_us=219.348; p95_us=610.674; p99_us=776.741; Gen0=168; CLR=10.0.15
Typed ECS bool N=4096; B/callback=5605312; p50_us=4729.591; p95_us=6121.170; p99_us=8429.823; Gen0=2680; CLR=10.0.15
Typed ECS f64 N=0; B/callback=1984; p50_us=0.962; p95_us=5.290; p99_us=10.369; Gen0=1; CLR=10.0.15
Typed ECS f64 N=16; B/callback=23872; p50_us=17.353; p95_us=20.669; p99_us=531.944; Gen0=11; CLR=10.0.15
Typed ECS f64 N=1; B/callback=3352; p50_us=1.884; p95_us=3.156; p99_us=3.867; Gen0=1; CLR=10.0.15
Typed ECS f64 N=256; B/callback=352192; p50_us=220.942; p95_us=604.372; p99_us=830.563; Gen0=168; CLR=10.0.15
Typed ECS f64 N=4096; B/callback=5605312; p50_us=4509.659; p95_us=5023.238; p99_us=5759.642; Gen0=2681; CLR=10.0.15
Typed ECS five-f32 N=0; B/callback=1984; p50_us=0.962; p95_us=1.393; p99_us=1.693; Gen0=1; CLR=10.0.15
Typed ECS five-f32 N=16; B/callback=36160; p50_us=30.328; p95_us=38.624; p99_us=462.101; Gen0=17; CLR=10.0.15
Typed ECS five-f32 N=1; B/callback=4120; p50_us=3.096; p95_us=3.667; p99_us=4.068; Gen0=2; CLR=10.0.15
Typed ECS five-f32 N=256; B/callback=548800; p50_us=527.265; p95_us=2119.070; p99_us=3684.235; Gen0=263; CLR=10.0.15
Typed ECS five-f32 N=4096; B/callback=8751040; p50_us=9554.627; p95_us=12034.517; p99_us=14192.160; Gen0=4185; CLR=10.0.15
Typed ECS i32 N=0; B/callback=1984; p50_us=0.952; p95_us=5.200; p99_us=9.588; Gen0=1; CLR=10.0.15
Typed ECS i32 N=16; B/callback=23872; p50_us=15.449; p95_us=18.516; p99_us=436.884; Gen0=11; CLR=10.0.15
Typed ECS i32 N=1; B/callback=3352; p50_us=1.953; p95_us=3.477; p99_us=11.242; Gen0=2; CLR=10.0.15
Typed ECS i32 N=256; B/callback=352192; p50_us=228.826; p95_us=786.259; p99_us=1492.035; Gen0=168; CLR=10.0.15
Typed ECS i32 N=4096; B/callback=5605312; p50_us=4880.356; p95_us=6690.868; p99_us=8303.142; Gen0=2681; CLR=10.0.15
Typed ECS mixed-five N=0; B/callback=1984; p50_us=0.952; p95_us=1.863; p99_us=2.394; Gen0=0; CLR=10.0.15
Typed ECS mixed-five N=16; B/callback=36160; p50_us=30.679; p95_us=36.790; p99_us=445.148; Gen0=17; CLR=10.0.15
Typed ECS mixed-five N=1; B/callback=4120; p50_us=3.086; p95_us=3.777; p99_us=4.328; Gen0=2; CLR=10.0.15
Typed ECS mixed-five N=256; B/callback=548800; p50_us=472.732; p95_us=817.248; p99_us=869.298; Gen0=262; CLR=10.0.15
Typed ECS mixed-five N=4096; B/callback=8751040; p50_us=9426.824; p95_us=12150.928; p99_us=29305.297; Gen0=4185; CLR=10.0.15
Typed ECS u32 N=0; B/callback=1984; p50_us=0.962; p95_us=4.910; p99_us=9.518; Gen0=1; CLR=10.0.15
Typed ECS u32 N=16; B/callback=23872; p50_us=15.599; p95_us=20.750; p99_us=472.982; Gen0=12; CLR=10.0.15
Typed ECS u32 N=1; B/callback=3352; p50_us=1.864; p95_us=3.066; p99_us=3.737; Gen0=2; CLR=10.0.15
Typed ECS u32 N=256; B/callback=352192; p50_us=228.826; p95_us=720.364; p99_us=1488.137; Gen0=168; CLR=10.0.15
Typed ECS u32 N=4096; B/callback=5605312; p50_us=4693.874; p95_us=6068.491; p99_us=7345.410; Gen0=2681; CLR=10.0.15
```
