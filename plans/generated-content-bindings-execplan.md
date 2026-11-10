---
status: completed
complexity: medium
complexity_source: auto
development_mode: tdd
source_design: ../docs/brainstorms/2026-11-09-generated-content-bindings.md
design_revision: 5
plan_revision: 2
approved_plan_revision: 2
approved_design_revision: 5
---

# Generate live Content bindings alongside ContentRefs

This ExecPlan is a living document maintained according to `plans/PLANS.md`.

## Purpose / Big Picture

Games declare one Simulation owner. The Content generator emits the binding
setup and accessors for all bindable assets, with typed JSON overrides. A build property
disables automatic registration while preserving existing refs and manual APIs.

## Progress

- [x] (2026-11-09) Saved design revision 3 with user decisions on build-time
  disabling and one source declaration per project.
- [x] (2026-11-09) Revised design to revision 5 with default inclusion, .meta
  and declared-type exclusions, and eager Simulation startup registration.
- [x] (2026-11-09) User approved plan revision 2 and design revision 5.
- [x] (2026-11-09) T1 generator tests went Red on missing generated output
  (2 failures), then Green (15 codegen tests). The complete Runtime suite
  passed 94/94, including generated-owner idempotence, rollback and disabled
  manual registration. Logs: /tmp/generated-bindings-{red,green,runtime-final}.log.
- [x] (2026-11-09) T2 and T3 accepted. Runner 176/176 and packed-template
  materialization 1/1 passed. Fresh packaged Static CoinRush builds passed
  with 11 registrations enabled and zero disabled. CoinRush tests passed
  67/67. Static publish contains every artifact referenced by its manifest.
- [x] (2026-11-09) Dynamic client compiled with the same side-selected engine
  references supplied explicitly in the temporary project. Per-asset Tile
  exclusion and raw-json type exclusion both worked. The stock Dynamic
  template requires explicit module references.
- [x] (2026-11-09) Two live publications changed HUD title and platform texture
  in client PID 87684. Screenshots `live-frame-{1,2}.png` in the test root
  prove the rendered changes. Only temporary processes and assets were used.
- [x] (2026-11-09) Published and attached PR #117 against main.

## Surprises & Discoveries

- ContentRefs already consumes source .meta files as compiler inputs.
- Static composition must see the source declaration. An Export emitted by a
  separate generator cannot serve as its input in the same compiler run.
- Switching from the modding branch exposed its checked-out Karpik.Jobs
  revision against the main gitlink. Check the submodule worktree before
  adjusting it for tests, and preserve local changes and the recorded gitlink.

## Decision Log

- (2026-11-09) Use a per-project build property and one source-declared owner,
  per D2 and D3. Use medium/test-first because the feature reuses the existing
  Content runtime and the composition lifecycle.

## Outcomes & Retrospective

The generator and CoinRush integration passed local validation. A fresh
launcher build exposed an existing evaluation-time glob for cooked files.
Both CoinRush launchers now enumerate artifacts after the runtime project
build. The final packaged builds and publish verification passed.

## Context and Orientation

`src/Karpik.Content.Runtime.Codegen/ContentCodegenGenerator.cs` consumes the
manifest and .meta files and emits ContentRefs. The SDK exposes generator inputs
through `src/Karpik.Engine.Sdk/Sdk/Sdk.targets`. Existing live APIs and ownership
are documented in `docs/content-bindings.md`. CoinRush's handwritten bindings
are in `templates/Karpik.CoinRush/Source/SSSuperGame.Client/CoinRush/ClientPresentationAssets.cs`.

## Real-Time Assessment

Generated registration and cleanup run on the existing cold Simulation path.
Frame accessors directly read value handles through IContentBindings. No
reflection, dictionaries, JSON, I/O or new allocations are required per frame.
Bindings are recreated on Simulation rebuild, with the existing owner
capacity and release rules. Texture declarations belong to the client project.
Gameplay and fixed ticks are outside this feature.

## Plan of Work

### T1 — Generate binding accessors and registration

- Status: completed.
- Goal: Teach the Content generator and SDK compiler inputs to emit binding
  members for one source-declared partial owner, default inclusion of
  supported manifest entries, .meta and declared-type filters, and typed
  JSON providers. Keep the generated access surface compilable with automation
  off. Emit diagnostics for invalid enabled declarations.
- Acceptance: AC1, AC2, AC3.
- Verification: Add test-first generator compilation cases in
  `tests/Karpik.Content.Runtime.Tests` for default inclusion and both exclusion layers, typed JSON precedence,
  diagnostics, deterministic output and enabled/disabled builds. Preserve the
  existing ContentRefs snapshots and public manual API.

### T2 — Connect the generated owner to Simulation and migrate CoinRush

- Status: completed.
- Goal: Use source-visible owner registration and generated cold lifecycle
  members. Register all included entries once at cold startup. Drain partial
  registration on failure and report the asset and capacity context.
  Route CoinRush through generated UI and texture access and preserve
  its source-generated UI validator, fallback and scoped font handling.
- Depends on: T1.
- Acceptance: AC2, AC4, AC5.
- Verification: Add targeted lifecycle/disabled behavior cases before changing
  the owner. Exercise Static and Dynamic composition, restart/release, capacity
  errors with partial cleanup, latest ready access and zero allocation reads. Reuse CoinRush
  presentation tests for JSON last-good, fallback and texture replacement.

### T3 — Verify packaged SDK behavior and document usage

- Status: completed.
- Goal: Verify SDK/template packaging and a generated CoinRush build with
  automation on and off. Document .meta and type exclusions, typed JSON,
  capacity impact, the property
  and access behavior in `docs/content-bindings.md`.
- Depends on: T1, T2.
- Acceptance: AC1–AC5.
- Verification: Run relevant Content Runtime and host tests, build both project
  variants from the packaged template and verify repeated live UI/texture
  changes in CoinRush with automation on. In the off variant verify no
  automatic handles and that ContentRefs and explicit manual bindings work.

## Milestones

T1 ends with generated C# compiling in both modes. T2 ends with lifecycle and
presentation tests. T3 ends with packaged game builds and the end-to-end
evidence. Record each milestone's command and result before accepting it.

## Concrete Steps

From `/home/artem/git/Karpik.Engine`, run the focused filters while implementing,
then the relevant full suites and packaged SDK checks:

    dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false
    dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false -p:CI=true
    dotnet test tests/Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter FullyQualifiedName~ContentBinding_packed_templates

Packager output root: `/tmp/karpik-auto-bindings.X9Kukt/verified-install`.
SDK package version: `0.6.0-auto-bindings-test3`. Generated projects are
`verified-on`, `verified-off` and `verified-dynamic` under the test root.
Static commands used `dotnet build BindingProbe.slnx -m:1 -nr:false
-p:EmitCompilerGeneratedFiles=true` with `-p:KarpikAutoContentBindings=false`
for the disabled variant. Dynamic used `-p:KarpikCompositionMode=Dynamic
-p:KarpikAutoContentBindingExcludedTypes=raw-json` and
`"autoBind": false` on Tile.png.meta. Publish used
`dotnet publish Source/BindingProbe.Client.Launcher/BindingProbe.Client.Launcher.csproj
-c Debug -m:1 -nr:false --no-restore -o ../verified-publish`.
Logs: `/tmp/generated-bindings-verified-*.log`. Live check:
`/tmp/generated-bindings-live.log`.

## Validation and Acceptance

T1 covers AC1–AC3, T2 covers AC2/AC4/AC5 and T3 confirms all criteria through
the packaged workflow. Distinguish registry delivery from actual presentation.

## Idempotence and Recovery

Use the dedicated `codex/auto-content-bindings` branch based on main. Preserve
unrelated workspace and submodule changes. Use temporary generated game inputs
for verification and versioned SDK artifacts. The project switch permits
disabling registrations without editing game source.

## Risks / Blockers

Static composition visibility and disabled compilation are the main risks.
A source-visible owner and the two build variants are explicit verification gates.

## Adjustments

- Revision 2 replaces per-asset opt-in with default inclusion and layered
  exclusions. All included entries register at Simulation startup by explicit
  user choice. The user approved revision 2.
- (2026-11-10) The user removed the owner marker requirement while PR #117
  was open. Codegen emits a standalone GeneratedContentBindings class
  alongside ContentRefs. CoinRush uses the same cold Simulation lifecycle
  with Initialize/Dispose on that class. Typed JSON providers are accessible
  static methods. Runtime 94/94 and local CoinRush 67/67 passed with SDK
  0.6.0-local-20261110-183955, with 11 generated registrations.

## Artifacts and Notes

Design revision 5 in `docs/brainstorms/2026-11-09-generated-content-bindings.md`.
Implementation and verification are complete. PR: https://github.com/Karpiklalloly/Karpik.Engine/pull/117.
