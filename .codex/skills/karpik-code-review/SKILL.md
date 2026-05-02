---
name: karpik-code-review
description: Review policy for KarpikEngine code changes. Use when Codex reviews pull requests, diffs, commits, or local changes for real-time suitability, GC allocations, Dragon ECS anti-patterns, networking mistakes, Client/Server/Shared boundary leaks, hot reload risks, and missing tests or benchmarks.
---

# Karpik Code Review

## Scope
Use this skill for review mode. Prioritize concrete bugs, regressions, and risks over style. Findings must cite exact files and lines when possible.

This skill is not a substitute for a Roslyn analyzer or benchmark suite. It defines what an agent must inspect during review. Mechanical enforcement should be implemented separately when a pattern is frequent and detectable.

## Review Order
Review in this order:

1. Correctness and behavioral regressions.
2. Real-time safety: allocations, latency spikes, cache locality, locks, blocking work.
3. Dragon ECS rules and data layout.
4. Client / Server / Shared boundaries.
5. Networking protocol, serialization, delivery, throttling, authority.
6. Hot reload state safety.
7. Tests, benchmarks, and validation gaps.

Do not spend review budget on formatting unless it hides a bug or violates an established local convention.

## Real-Time Red Flags
Flag code in hot paths when it uses:

- LINQ, closures, iterator blocks, async state machines, boxing, reflection, or `params` allocations;
- `new` managed objects in `Update`, `FixedUpdate`, ECS `Run`, render loops, network pumps, or serialization loops;
- `Dictionary`, `List` growth, string formatting, interpolation, or logging per entity/per packet/per frame;
- locks, waits, blocking I/O, sleeps, or sync-over-async;
- object graphs where dense arrays, ECS pools, or SoA would be more appropriate.

If the code is not on a hot path, judge the risk by call frequency and lifetime. Do not overstate cold-path allocations.

## Dragon ECS Checks
Flag:

- class components or components missing `IEcsComponent`;
- managed references inside components;
- gameplay state stored in static fields;
- repeated `GetPool<T>()` in per-entity loops;
- broad world scans where an `EcsAspect` or cached pools would be clearer and faster;
- mutable access where `EcsReadonlyPool<T>` is enough;
- entity references stored as objects instead of `entlong`.

## Side Boundary Checks
Flag:

- Client referencing Server projects or Server referencing Client projects;
- graphics/input types leaking into Shared or Server;
- duplicated logic that should be in Shared;
- `#if CLIENT` / `#if SERVER` used to hide an architectural dependency that should be split.

## Networking Checks
Flag:

- classes or non-versionable payloads sent through RPC/serialization;
- RPC calls from `Update` without throttle, batching, or queueing;
- reliable delivery used for high-frequency lossy state where it can cause backlog;
- missing server authority or validation for client-originated state;
- allocations in serialization/deserialization loops;
- protocol changes without round-trip tests or compatibility notes.

## Hot Reload Checks
Flag:

- gameplay state in static fields;
- persistent references to objects owned by unloadable assemblies;
- event subscriptions without clear unsubscribe/reload lifecycle;
- heavy module constructors;
- reload changes without repeated-reload validation.

## Validation Expectations
Expect focused tests for behavior and edge cases. For hot paths, ask for allocation checks or BenchmarkDotNet when the change may affect frame time, serialization, ECS iteration, or jobs.

Missing tests are a finding when the change is behavioral, cross-module, or runtime-sensitive. For trivial documentation or cold-path cleanup, mention residual risk without blocking.

## Output Rules
Lead with findings ordered by severity. Each finding should explain:

- what breaks or may regress;
- why it matters for KarpikEngine;
- where it occurs;
- what safer alternative or validation is expected.

If no issues are found, state that clearly and mention any remaining test or benchmark gaps.
