# Content reload notifications

- Status: ready
- Started: 2026-11-01
- Updated: 2026-11-02
- Revision: 11
- Complexity: complex

## Goal and scope

Design delivery of explicitly initiated successful Content builds to a running runtime, building on manifest diff and version-bound snapshots. Automatic source watching and Editor integration are excluded.

## Requirements and behavior

Failed builds must not replace the last good runtime state. A successful build remains successful if delivery fails; delivery reports a separate error and can be retried. Existing references remain version-bound.

## Architecture and integration

The existing runtime requires preparation outside frame paths and host-controlled commit at a quiescent boundary. Manifest diff is a control-plane primitive, not notification delivery.

## Constraints

Avoid allocations, I/O and waits on frame hot paths. Captured artifact locators require immutable bytes and generation retention. The existing ContentAtomicPublisher overwrites or deletes artifacts, and FileContentStore reads paths lazily; notification alone cannot safely preserve previous generations.

## Decisions and alternatives

- D1 — Trigger: **explicit build**, followed by notification and runtime update.
  - Alternative: automatic filesystem watch and rebuild; more convenient but adds debounce and build coordination.
  - Rationale: developer selected a smaller first stage.
- D2 — Recipient: **one explicitly selected runtime instance**.
  - Alternative: discover and update all instances of a project, requiring discovery and project identity.
  - Rationale: developer selected explicit targeting.
- D3 — Generation retention: **automatic cleanup with usage tracking**.
  - Alternative: retain immutable generations until manual cleanup; simpler but consumes disk indefinitely.
  - Rationale: developer selected automatic reclamation. Exact crash-safe ownership mechanism remains open.
- D4 — Delivery failure: **report separately from successful build**; preserve the published result and allow retry.
  - Alternative: fail the entire command when delivery fails, conflating publication and application.
  - Rationale: developer selected separate outcomes.
- D5 — Gameplay notification: **deliver changed AssetIds inside runtime; consumers explicitly resolve new references**.
  - Alternative: automatically rewrite ECS references; changes gameplay state and violates the existing snapshot contract.
  - Rationale: developer accepted explicit resolution.
- D6 — Runtime sides: **Client and Server**, using a shared contract and each host’s quiescent boundary.
  - Alternative: Client-only initial integration; smaller but leaves server gameplay data unsupported.
  - Rationale: developer selected both sides.
- D7 — Busy runtime: **retain the latest delivered build and apply it after the current update settles**. Intermediate pending versions may be superseded; pending storage must be bounded and its files retained until no longer needed.
  - Alternative: reject while busy and require a retry; simpler acknowledgment semantics but less convenient.
  - Rationale: developer selected latest-pending behavior.
- D8 — Delivery acknowledgment: **wait for applied, superseded, or failed outcome, subject to timeout**. Timeout reports unknown outcome rather than asserting failure or cancellation.
  - Alternative: immediately acknowledge queueing and report later through runtime diagnostics.
  - Rationale: developer selected a terminal result.
- D9 — Launch modes: **support launcher-managed and standalone runtime processes** on both sides.
  - Alternative: use only the existing launcher/worker IPC; it does not cover standalone.
  - Rationale: developer selected both modes; a separate addressable Content endpoint is needed.
- D10 — Endpoint activation: **explicit development opt-in**, disabled by default.
  - Alternative: always open an endpoint; simpler setup but exposes an unnecessary service in ordinary sessions.
  - Rationale: developer selected opt-in.
- D11 — Storage and synchronization: **immutable published generations with crash-released cross-process ownership**. Retain generations for current and pending snapshots, unchanged slots and in-flight reads; coordinate acquisition, publication and collection to prevent check/delete races. A process crash must release ownership; if ownership cannot be proven, collection fails closed.
  - Alternative: mutable published tree with a notification or PID-only bookkeeping; neither proves old bytes survive reads or PID reuse.
  - Rationale: correctness takes precedence over reclaiming disk space. Cross-platform locking behavior must be demonstrated before integration.
- D12 — Delivery and runtime integration: **local opt-in targeted endpoint, cold preparation, quiescent commit and bounded preallocated change notification**. Address and project/target identity are validated; malformed or unrelated requests are rejected. Compare the applied manifest with the candidate, not a superseded intermediate build. Keep callbacks and I/O outside frame paths.
  - Alternatives: existing launcher IPC alone excludes standalone; automatic reference mutation changes gameplay state.
  - Rationale: supports both host modes and preserves registry semantics and no-GC frame execution.

## Acceptance criteria

- AC1 — Explicit successful build can target one opted-in Client or Server instance, both launcher-managed and standalone; ordinary runtime opens no Content endpoint.
- AC2 — Build and delivery outcomes are separate; delivery reports applied, superseded or failure, and timeout is an unknown outcome. Retry is possible.
- AC3 — Busy runtime holds at most one latest pending build; superseded requests receive their outcome. Changes are calculated against the actually applied manifest.
- AC4 — Immutable files remain readable for current, pending, unchanged and in-flight users across concurrent publication, crashes and cleanup; unused generations are reclaimed automatically, with failed-closed behavior when safety cannot be proven.
- AC5 — Preparation and I/O run off the frame path; commit occurs at a quiescent Client frame / Server tick boundary. Failed preparation retains the prior snapshot and references.
- AC6 — Consumers receive bounded added/updated/removed AssetIds after commit and explicitly re-resolve refs; ECS state is not implicitly modified. No allocation or blocking in steady-state frame/tick processing.
- AC7 — Target/namespace mismatch, malformed input, shutdown, repeated reload, concurrent builds and unavailable endpoint have defined observable failure handling; existing non-development workflow remains compatible.

## Open questions / blockers

None.

## Resume point

Design revision 11 and implementation plan revision 1 were approved on 2026-11-02.
Implementation was initially deferred and then explicitly requested on the same date.
Continue using `plans/content-reload-notifications-execplan.md` for execution state.
