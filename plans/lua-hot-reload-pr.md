# Prepared feature pull request — not published

## Title

feat: Reload Lua code while preserving Simulation state

## Body

Lua code changes currently require restarting runtime scopes, which loses the running Simulation. Add an explicit local-only host request that pauses at a frame barrier, prepares all Lua mods and a separate execution scheduler, validates unchanged schemas and the new graph, then switches revisions while preserving the ECS world, entity generations, component data and CLR-system instances.

Each Lua revision owns VM/callback capabilities, query listeners and bounded command scratch separately from persistent Simulation storage. `on_load` and optional `on_reload` prepare replacements; `on_start` is not repeated. Changes to systems, queries, access and order are allowed. Invalid candidates/cancellation leave the old version active after successful cleanup. Runtime cleanup attempts all owners; Lua-hook failures are logged, host disposal/logging failures stop the Simulation. Commit has no user callbacks and never rolls back into retired state.

Client worker reservations and frame-entry guards exclude overlapping simulation/render accesses. Main-thread control pumping continues; snapshot/state capture is rejected during maintenance. Fixed and variable host clocks exclude the pause without losing the pre-pause fixed remainder. Optional-null constructor dependencies in Static generated factories now match Dynamic DI, preserving existing composition without a registered scheduler while using it when provided.

Verification:

- New feature selection: 62/62, no skips; real Lua files, Static/Dynamic × Deterministic/Parallel, SingleThread smoke, real client boundary and shutdown/cancel/error cases.
- Complete Runner: 702/702, no skips, exit0 using existing repository CI collection serialization and180s watchdog for heavy allocation probes. An earlier default-parallel trial hit an existing10s replacement-worker readiness timeout; that test passes isolated and in the final CI-configured full suite. No test deadline/assertion/allocation ceiling was weakened.
- Generator: 80/80; ECS.Core: 67/67, no skips, exit0.
- Warmed common ticks before/after reload: 0 B; existing Lua allocation ceilings remain green. Lua is still allocating/experimental.
- 100 seeded sequences ×3 real host requests,200 successful replacements;600 retired owner/VM/context weak references collected. Separate N=0/1/16,16-entity,20-reload phase/allocation/sampled-memory/thread probes retained with fingerprints and limitations.
- Independent read-only review against design r10/AC1–AC8: no actionable findings, no reviewer edits or rerun claims.
- Whitespace/local documentation links checked; API docs and lifetime ADR added.

Compatibility: opt-in is disabled by default; network hosts must not enable local-only mode. Mod set/metadata/schemas, native component layouts and network/save formats do not change. Optional `on_reload` must be a Lua function if present; non-function entries with that newly reserved callback name are rejected. Custom asynchronous manual factories must supply the main-thread scheduler; generated production factories use the registered host scheduler. No network/save/schema migration, editor UI/watcher, CLR assembly reload or NativeAOT acceptance is claimed.

## Publication state

Task base fe018fd; user explicitly chose implementation on existing codex/modding-0.7 as an isolation exception. The approved D10 does not authorize automatically publishing earlier unpublished task commits or broadening an unrelated existing PR. No push/PR mutation was performed. This file is a concrete feature review description, not a claim that a PR exists. An isolated authorized publication scope must be resolved before pushing. Apply actual repository feature-category labels at publication; do not invent label names. The optional callback validation impact is explicitly described above; no wire/schema migration is introduced.
