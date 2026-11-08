---
status: executing
complexity: complex
complexity_source: auto
development_mode: tdd
source_design: ../docs/brainstorms/2026-11-08-content-bindings.md
design_revision: 4
plan_revision: 1
approved_plan_revision: 1
approved_design_revision: 4
---

# Add reusable live Content bindings and demonstrate them in CoinRush

This ExecPlan is maintained according to `plans/PLANS.md`. The source design
above is the authority for public behavior and architectural decisions.

## Purpose / Big Picture

An explicitly bound CoinRush UI title or player/coin/tile texture follows
published Content without process restart. Games obtain ready values through
value handles and do not implement their own reload subscriptions, loading
queues or JSON completion logic. A failed replacement preserves the last good
presentation; removal presents fallback/empty. Existing AssetRef<T> and registry
publication semantics remain unchanged.

## Progress

- [x] (2026-11-08) Investigated current registry, host boundary, GPU claims and
  CoinRush consumer code; saved ready design revision 4.
- [x] (2026-11-08) User authorized dependent branch `codex/content-bindings` from
  `codex/content-reload-notifications` at baseline `95b976f`.
- [x] (2026-11-08) User approved plan revision 1/design revision 4, including
  acceptance of PR #114, with “Делай”.
- [ ] Complete WP-00 through WP-05 with recorded verification per milestone.
- [ ] Complete independent read-only review, fixes and final acceptance.

## Surprises & Discoveries

- Registry preparation already loads replacements of loaded textures. Repeating
  that load in the game would duplicate work and create an ownership gap.
- New frame claims on retired textures fail even while an old consumer pin
  exists. Binding publication must precede drawing, not merely retain wrappers.
- Registry retirement is awaited before the next preparation. An abandoned
  presentation pin can stall future updates and shutdown.
- CoinRush's test-SDK fix is in still-open PR #114, head `6b50404`, targeting
  main. The local game already has that fix, but the parent template does not.
  Do not duplicate that independent fix inside this feature's diff.

## Decision Log

- Decision: follow design D1–D10; use TDD for observable runtime behavior.
  Rationale: this combines hot-path scheduling, native ownership, async projection
  and public handles; isolated tests plus real host/GPU checks are needed.
  Date/Author: 2026-11-08 / Codex.
- Decision: preserve a separate dependent PR targeting Content Reload; keep the
  parent source and fixed-version API intact.
  Rationale: explicit user authorization and repository branch rules.
  Date/Author: 2026-11-08 / user and Codex.

## Outcomes & Retrospective

Design and plan saved. No runtime code changed, implementation tests run,
independent review performed or implementation approval received yet.

## Context and Orientation

Shared contracts/loaders and registry are in `src/Karpik.Content.Runtime`.
`ContentRegistry.PrepareAsync` stages loaded payloads; `TryCommit` switches the
registry root. `ContentReloadCoordinator` supplies current-boundary changes.
An `AssetRef<T>` identifies one version. A proposed `ContentBinding<T>` instead
identifies an owner slot which follows selected authoring AssetIds.

`src/Karpik.Engine.Core.Runner/Runner.cs` creates Engine/ModSet/Simulation DI
scopes and registers the host boundary. Static composition uses generated typed
factories; Dynamic composition shares stable Content assemblies with modules.
`ContentReloadHost.cs` and `ContentReloadHostInstaller.cs` supply opt-in delivery.

`ContentResourceLifetime` and `ContentResourcePins` protect backend resources.
`Modules/Client/Graphics/Graphics.Core/Buffers/ThreadBuffer.cs` acquires claims for
draw commands. The OpenGL ContentTextureLoader already handles upload/cleanup.

CoinRush source lives under `templates/Karpik.CoinRush/Source/SSSuperGame.Client`.
ClientDrawSystem currently keeps resources/jobs and UiStrings in ClientDrawState;
the feature migrates those runtime owners out of ECS. The existing local game is
`/home/artem/Karpik-Games/coin-rush`; preserve user edits when applying equivalent
changes there. It is outside the workspace and requires tool write permission.

## Real-Time Assessment

Hot paths are binding reads, boundary adoption and render preparation. They use
generation-checked value handles, dense preallocated slots and bounded request/
result state. No hot-path locks, waits, Task creation, JSON, filesystem calls,
driver calls, logging or arbitrary callbacks are allowed. Allocate storage at
cold initialization; reject capacity exhaustion explicitly. Bound work per frame
and measure both no-change and changed/ready paths after warm-up.

Shared runtime imports no Client/backend types. Existing fixed-dt physics and
gameplay remain untouched. Game-specific JsonTypeInfo<T> and prepared UI objects
belong to Simulation and must drain/release on teardown. Cold workers perform
initial loading, projection, cancellation and diagnostics. Version checks reject
late work. Registry-ready textures are adopted at the quiescent boundary;
already-recorded frames retain their own resource claims. Stop/drain binding work
and release presentation ownership before registry/device teardown, while keeping
the established renderer shutdown ordering intact.

## Plan of Work / Work Packages

### WP-00 — Integrate the existing CoinRush test prerequisite

- Status: pending.
- Goal: make the source template's test setup match the already-fixed local game.
- Scope: after approval of this plan, recheck PR #114 and accept its existing
  test-SDK fix if its head remains `6b50404` and the reviewed scope is unchanged.
  Merge updated main into the Content Reload parent, then merge that parent into
  this dependent branch. Publish the parent synchronization normally.
- Boundaries: no new feature changes in main/parent; do not silently merge a
  materially changed PR or absorb unrelated fixes. The plan approval explicitly
  authorizes acceptance of this specific prerequisite PR.
- Depends on: none.
- Acceptance: AC10 prerequisite.
- Verification: confirm merged SHA/ancestry and ensure the feature's diff against
  its parent contains neither inherited changes nor a duplicate test-SDK fix.

### WP-01 — Define and test dense handle/status/owner contracts

- Status: pending.
- Goal: implement D7/D9 value handles, typed ready reads, statuses, cold
  registration/release and bounded capacity in `src/Karpik.Content.Runtime`.
- Scope: public IContentBindings/ContentBinding<T> contracts, owner/reuse identity,
  configuration validation, fixed slot tables and initial fallback state.
- Boundaries: no client/backend dependencies or game-specific DTOs; no registry
  ref semantics change. Registration/release are cold/quiescent APIs.
- Depends on: none.
- Acceptance: AC2, AC3, AC5, AC9.
- Verification: write failing tests first in Karpik.Content.Runtime.Tests for
  default/foreign/released/reused handles, exact capacity boundaries and invalid
  configuration; verify no-allocation reads and explicit errors on exhaustion.
- Integration notes: lock down the shared contracts before downstream edits.

### WP-02 — Implement cold loading, typed JSON and latest-wins publication

- Status: pending.
- Goal: supply automatic initial/update loading and JSON projection per D4–D9.
- Scope: scoped implementation in Content.Runtime, bounded mailboxes and cold
  worker, BindJson<T> with JsonTypeInfo<T>, retry/status diagnostics, supersession,
  removal/re-add, ready swaps, cancellation and async owner shutdown.
- Boundaries: no game code or graphics backend dependency. Preserve registry
  manifest atomicity and transport acknowledgement. Do not change the global
  payload loader table to cache gameplay Types/serializers.
- Depends on: WP-01.
- Acceptance: AC2, AC3, AC4, AC6, AC7, AC8, AC9.
- Verification: deterministic gated-load tests for initial missing, successful
  update, projection error/last-good, removal during load, re-add, type mismatch,
  stale results, independent readiness, full queues and concurrent stop. Confirm
  caller-owned fallbacks are not disposed. Verify release of serialization
  metadata/owner references, including collectible-context lifetime where feasible.

### WP-03 — Integrate host boundaries, DI and texture ownership

- Status: pending.
- Goal: advance scoped bindings automatically in Static/Dynamic hosts before
  gameplay/render consumers and preserve GPU retirement/shutdown safety.
- Scope: Runner lifecycle integration, stable typed registrations/factories and
  graphics/resource ownership integration/tests. Use the existing prepared
  texture payload; release deleted/replaced selections before retirement join.
- Boundaries: no raw value cached across frame boundaries; no extended
  RetainExisting bypass for arbitrary retired resources; no new texture decoder.
- Depends on: WP-01, WP-02.
- Acceptance: AC2, AC3, AC4, AC6, AC10.
- Verification: host tests with and without reload opt-in, actual static
  generated composition and Dynamic stable identities; changed texture on the
  first new frame, dropped/merging old frames, removal, and shutdown while old
  frames are pinned or a load is in progress. Extend the hardware Content probe
  to read back old/new colors using binding selection. Measure allocation and
  bounded frame work under maximum configured active bindings.

### WP-04 — Migrate CoinRush UI and texture consumption

- Status: pending.
- Goal: demonstrate visible title/player/coin/tile updates using the common API.
- Scope: CoinRush client template systems, Simulation-scoped presentation owner,
  source-generated JSON context, value-only ECS presentation state and sample
  tests. Apply equivalent changes to the existing local game after reviewing its
  current files. Keep the existing legacy font loader in the scoped owner.
- Boundaries: preserve gameplay, match rules and fixed dt. Do not add font/shader
  hot reload or erase local asset edits. Keep Server/Shared independent of Client.
- Depends on: WP-00, WP-02, WP-03.
- Acceptance: AC1, AC4, AC5, AC6, AC7, AC8, AC10.
- Verification: template tests prove changing title updates recorded HUD content
  and changed texture binds the current ready resource without restart; exercise
  failure/fallback/re-add and repeated updates. Build and run all local CoinRush
  tests; retain only numeric handles/value data in ECS components.

### WP-05 — Package, document and verify the full workflow

- Status: pending.
- Goal: ship the reusable feature and make the real CoinRush scenario reproducible.
- Scope: SDK/codegen dependency closure if needed, content usage guide and durable
  ADR updates, generated-host/package integration tests, local SDK publication,
  local game version pin and real running-client verification.
- Boundaries: no gameplay auto-reload or change to `delivery applied` semantics.
  Retain immutable old SDK installations and reversible backups of user assets.
- Depends on: WP-00, WP-01, WP-02, WP-03, WP-04.
- Acceptance: AC1–AC10, with AC1 specifically verified end-to-end here.
- Verification: packaged template and real local game build/publish; use reversible
  UI/texture edits, publish with --notify and verify actual presented title/pixel
  change in the same process. Exercise a second update, bad projection, deletion
  and restoration. Record registry delivery and binding readiness separately.

## Milestones

WP-00 establishes the source prerequisite. WP-01 establishes public contracts.
WP-02 establishes deterministic CPU behavior. WP-03 establishes safe host/GPU
integration. WP-04 establishes the game consumer. WP-05 proves packaged delivery.
Record each package's actual command/result and relevant Red/Green evidence in
Progress before accepting the milestone. Execution selects delegation and edit/
build coordination dynamically; only the main agent updates this plan.

## Concrete Steps

From `/home/artem/git/Karpik.Engine`, run targeted filters first, then the relevant
complete suites as their packages integrate:

```sh
dotnet test tests/Karpik.Content.Runtime.Tests/Karpik.Content.Runtime.Tests.csproj -m:1 -nr:false
dotnet test tests/Karpik.Engine.Core.Runner.Tests/Karpik.Engine.Core.Runner.Tests.csproj -m:1 -nr:false
dotnet test tests/Karpik.Content.Tool.Reload.Tests/Karpik.Content.Tool.Reload.Tests.csproj -m:1 -nr:false
dotnet test tests/Karpik.Engine.Sdk.IntegrationTests/Karpik.Engine.Sdk.IntegrationTests.csproj -m:1 -nr:false --filter FullyQualifiedName~ContentBinding
```

Name new package/host tests so the final filter exercises the new feature. Run
the existing Content GPU probe using its README/build instructions and the new
binding mode; it requires a real OpenGL device. Do not report a skipped hardware
probe or gated test as passing.

After source verification, publish with
`pwsh -NoProfile -File ./_scripts/Update-KarpikSdk.ps1`. Record the generated exact
SDK version and immutable installation, update only the local game pin, and from
`/home/artem/Karpik-Games/coin-rush` run:

```sh
dotnet build CoinRush.slnx -m:1 -nr:false
dotnet test Tests/CoinRush.Tests/CoinRush.Tests.csproj -m:1 -nr:false --no-build
```

Start the client from its launcher output with --content-reload-instance, using
the newly published content tool and that launcher's actual Content directory
for notifications. Capture the same-process presentation evidence, restore edited
assets and republish. Stop only processes owned by these checks unless the user
requests leaving a particular client running.

## Validation and Acceptance

AC1 is covered by WP-04 command-level tests and WP-05 real client/GPU evidence.
AC2 by WP-01/02/03 compatibility tests. AC3 by WP-01/02/03 allocation/latency
checks. AC4/AC6 by CPU/host/graphics stress and teardown cases. AC5 by unmanaged
handle/component checks and scoped ownership in WP-01/04. AC7/AC8 by gated JSON
and independent-readiness tests. AC9 by handle/capacity/failure tests. AC10 by
WP-00 prerequisite, Static/Dynamic host tests, packaged generation and local game.

Do not substitute `delivery applied` or successful compilation for visible
presentation evidence. Report existing/unrelated failures separately with
baseline evidence; no automatic weakening of tests or requirements.

## Independent Review

After integration and primary verification, a separate read-only reviewer must
inspect the full feature against design revision 4, this plan and AC1–AC10.
Prioritize stale-result races, owner/collectible lifetime, GPU retirement,
bounded hot-path behavior, Static/Dynamic registration, AOT metadata and package
closure. The main agent triages findings, fixes confirmed issues and reruns
affected checks. Record reviewer evidence; do not claim review before it occurs.

## Idempotence and Recovery

Use the approved dependent branch; preserve unrelated `.idea` state and user
asset edits. Test fixtures own their temporary directories and processes. Save
original asset bytes and restore them in cleanup, then publish the restoration.
Use unique SDK versions, never mutate an installed immutable payload, and keep
the previous game SDK pin available for rollback. Dispose failed/unpublished
binding results cold, drain owner work and release pins before backend teardown.
Do not remove generation gates/claims by hand. Do not force-push or reset user
branches. Retarget the feature PR only after the parent merges and its diff has
been checked for inherited changes.

## Risks / Blockers

No unresolved design blockers. WP-00 is an explicit plan-approval prerequisite.
Capacity defaults and worker tuning need measured confirmation without changing
the approved bounds or hot-path contract. JSON source-generated metadata may
retain gameplay assemblies unless shutdown truly drains. Backend cleanup failures
must remain observable. GPU verification requires the local graphics session.

## Adjustments

None yet. Public behavior/API or architectural changes reopen the affected
design decisions and require scoped reapproval; private task refinements do not.

## Artifacts and Notes

Design: `docs/brainstorms/2026-11-08-content-bindings.md`, revision 4.
Existing contracts: `docs/content-reload.md` and
`docs/02_ADR/content-runtime-snapshots.md`. GPU investigation/probe:
`plans/probes/content-gpu-lifetime-audit/README.md`.
Prerequisite: https://github.com/Karpiklalloly/Karpik.Engine/pull/114.
The final feature PR targets `codex/content-reload-notifications` initially.
