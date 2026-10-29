# Измерения typed Lua ECS bindings

Дата: 2026-10-29, Europe/Samara. Baseline проверки — рабочее дерево после 5699dc9 с typed Lua/tests. Первый полный Runner Release: **389/389**, exit 0. .NET 10.0.14. Это локальные измерения, не latency guarantee.

Команда из repository root: `dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -c Release --no-restore -m:1 -nr:false --logger "console;verbosity=detailed" --blame-hang --blame-hang-timeout 180s --blame-hang-dump-type none`; VSTEST_CONNECTION_TIMEOUT=180.

Каждый reserved probe: warmup 2000, затем 1000 callbacks; membership reset и formatting вне интервала. Common C# typed reserved query/access — 0 B (отдельный тест). Old f32 arithmetic workload сохраняет прежний gate; его нельзя сравнивать с copy workload как стоимость одной операции.

| Scenario | N | B/callback | p50 us | p95 us | p99 us | Gen0 / 1000 callbacks |
|---|---:|---:|---:|---:|---:|---:|
| bool | 0 | 1984 | 0.800 | 4.000 | 7.300 | 1 |
| bool | 1 | 3352 | 1.600 | 2.500 | 3.000 | 2 |
| bool | 16 | 23872 | 38.900 | 52.800 | 425.900 | 12 |
| bool | 256 | 352192 | 184.900 | 536.800 | 711.000 | 168 |
| bool | 4096 | 5605312 | 3969.000 | 4484.400 | 5133.800 | 2680 |
| f64 | 0 | 1984 | 0.900 | 3.800 | 5.800 | 0 |
| f64 | 1 | 3352 | 1.700 | 2.400 | 2.900 | 1 |
| f64 | 16 | 23872 | 13.400 | 29.400 | 352.700 | 11 |
| f64 | 256 | 352192 | 190.700 | 543.400 | 672.000 | 168 |
| f64 | 4096 | 5605312 | 4002.700 | 4579.800 | 5054.900 | 2681 |
| five-f32 | 0 | 1984 | 0.800 | 1.400 | 2.000 | 1 |
| five-f32 | 1 | 4120 | 2.800 | 4.900 | 5.500 | 2 |
| five-f32 | 16 | 36160 | 24.900 | 28.900 | 411.200 | 17 |
| five-f32 | 256 | 548800 | 390.500 | 796.200 | 984.900 | 263 |
| five-f32 | 4096 | 8751040 | 7564.700 | 8535.000 | 9434.300 | 4185 |
| i32 | 0 | 1984 | 0.800 | 3.500 | 4.400 | 1 |
| i32 | 1 | 3352 | 1.700 | 2.800 | 3.600 | 2 |
| i32 | 16 | 23872 | 12.800 | 14.500 | 348.600 | 12 |
| i32 | 256 | 352192 | 192.100 | 595.300 | 691.400 | 168 |
| i32 | 4096 | 5605312 | 4035.300 | 4842.500 | 5418.800 | 2680 |
| mixed-five | 0 | 1984 | 0.800 | 3.600 | 5.800 | 0 |
| mixed-five | 1 | 4120 | 2.500 | 3.200 | 3.400 | 2 |
| mixed-five | 16 | 36160 | 24.300 | 34.000 | 400.900 | 17 |
| mixed-five | 256 | 548800 | 378.300 | 803.000 | 973.100 | 262 |
| mixed-five | 4096 | 8751040 | 7495.500 | 8349.100 | 9192.700 | 4185 |
| u32 | 0 | 1984 | 0.800 | 1.500 | 1.900 | 0 |
| u32 | 1 | 3352 | 1.700 | 2.700 | 3.000 | 2 |
| u32 | 16 | 23872 | 12.800 | 14.000 | 340.900 | 12 |
| u32 | 256 | 352192 | 188.600 | 525.600 | 633.400 | 168 |
| u32 | 4096 | 5605312 | 4009.500 | 4629.300 | 5045.600 | 2681 |

Однородные copy scenarios: `1984 + 1368*N B/callback`, ниже согласованного `4096 + 1600*N`. Mixed-five и matched five-f32: одинаковые `1984 + 2136*N` при каждом N. Оба выполняют один native get, один dynamic add и пять read/write пар. На N=4096 это 8,751,040 B/callback; это не one-field workload и не no-GC Lua.

Forced growth / first invocation: initial column capacity 512, N=513, 1,124,552 B/callback, 4313.4 us. Включает query/VM cold costs, отдельно от reserved budgets.

В полном прогоне присутствовали warnings ProcessManager о невозможности удалить временные worker shadow directories. Failed tests не было; subsystem не изменялся. NativeAOT publish, ручная игра и editor hot reload/state restore не выполнялись.

Ссылки: [контракт](lua-ecs-type-catalog-spec.md), [ExecPlan](lua-ecs-type-catalog-execplan.md), [подробный план](../docs/superpowers/plans/2026-10-29-lua-ecs-type-catalog.md).

Финальная проверка после сохранения legacy F32 init/with и восстановления negative-control fixture: полный Runner Release **390/390**, полный Generator Release **76/76**, оба exit 0. В этой проверке вновь исполнялись все allocation gates; таблица выше сохраняет первый detailed run, а не смешивает измерения разных прогонов.
