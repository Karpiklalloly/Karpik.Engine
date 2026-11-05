# Targeted Content reload

Content reload delivers an explicit successful build to one development runtime.
Client and Server each use their own build target and output directory. The output
must be the runtime's actual `IFileSystem.ContentPath`.

Build Content before starting the opted-in runtime:

```sh
content build --source ./Assets --output ./bundle/Content --namespace game --target Client
```

Start the Dynamic runner or Static host with its usual arguments plus
`--content-reload-instance client-one`. Standalone hosts use the same option
without launcher IPC. Launcher callers set
`RuntimeLaunchOptions.ContentReloadInstanceId = "client-one"`; ProcessManager
preserves it across worker restarts. Names contain 1–64 ASCII letters, digits,
underscores or hyphens. Choose a different name for each instance sharing an
output directory.

Without that option the runtime opens no reload endpoint. Constructor-injected
`IContentChangeSource` remains available and returns an empty span and sequence 0.

Build and wait for the selected runtime's application result:

```sh
content build --source ./Assets --output ./bundle/Content --namespace game --target Client --notify client-one --delivery-timeout-ms 30000
```

The tool prints build success separately from delivery. Notification builds also
print a generation ID. Failed builds send no notification. A successful build is
still published if the endpoint is unavailable or rejects delivery.

Retry a published generation without rebuilding:

```sh
content notify --output ./bundle/Content --generation <generation-id> --namespace game --target Client --instance client-one --delivery-timeout-ms 30000
```

The retry ID is the canonical 32-character lower-case generation ID printed by
the tool. Retry fails clearly if that unused generation has already been
collected. Both notification commands retain the generation while waiting;
accepted runtime work acquires its own store ownership.

| Delivery result | Meaning | Exit code |
| --- | --- | --- |
| applied | The runtime published the requested manifest, including a no-op update. | 0 |
| superseded | A newer pending request replaced this pending request. | 0 |
| failed | A known validation, preparation or delivery failure occurred. | 4 |
| unknown | The wait ended without a trustworthy terminal result. | 5 |

Timeout or client disconnection does not cancel accepted runtime application.
The default wait is 30,000 ms; the valid range is 1–86,400,000 ms. Existing codes
remain 1 for usage, 2 for failed build validation, and 3 for unexpected errors.
The process exit code alone does not describe both build and delivery outcomes.

## Simulation consumers

Inject `IContentChangeSource` and consume `Changes` during each simulation
frame's `ISystemBegin.Begin`, after the host's publication boundary. The borrowed
span contains sorted `ContentAssetChange` values with `Added`, `Updated` or
`Removed` kinds. It is valid until the next simulation boundary. `Sequence`
advances for a nonempty change publication.

Resolve new references explicitly through constructor-injected
`IContentUpdateRegistry.TryResolve<T>(id, out reference)`. Store references in
consumer-owned state or ECS components; removal is handled by that consumer.
Existing references keep their old version and are not rewritten automatically.
Resolving a reference does not load it: queue loading/decoding outside frame and
tick hot paths.

Client fixed ticks may not run on every simulation frame. A consumer that needs
changes in `FixedUpdate` must capture or apply them in `Begin` first. Do not keep
the borrowed span across frames or poll it from an unrelated thread.

Preparation, file I/O and endpoint callbacks run on cold workers. The boundary
only publishes a ready update and copies a bounded preallocated change buffer.
There is one active update and one latest pending request. Intermediate pending
requests receive `superseded`; changes are computed against the actually applied
registry state.

## Files and lifetime

Builds publish immutable `.generations/<id>/` directories while preserving the
ordinary root manifest/artifact layout. Generation-captured stores retain the immutable
copies through current snapshots, pending updates, unchanged slots and in-flight
reads. Collection reclaims unused generations under the same permanent namespace
gate used for acquisition and publication. OS-held claims are released by process
death; uncertain ownership or metadata retains files.

Legacy writable output is imported before its first destructive publication or
physical runtime capture. An import has unknown namespace/target provenance.
Reload opt-in requires a validated build followed by runtime restart. Read-only
legacy and generation-marked packages retain ordinary non-development startup
behavior. Their non-owning fallback reads the root projection and cannot enable
reload; keep those packages static while the runtime is alive.

Cleanup is enabled only on verified local filesystem formats. Unsupported formats
retain generations and emit `KCO025`; the build result remains independent of
cleanup availability. These cooperative locks do not protect against manual
deletion, replacement of control files, or unrelated tools mutating generations.
Never remove or replace the permanent gate files while participants are alive.

Endpoint transport uses bounded same-user local named pipes and validates root,
namespace, target, generation and request correlation. It does not discover other
instances or open a network-facing service. Source watching and Editor integration
are outside this workflow.

See [the build contract](02_ADR/content-pipeline-build-contract.md) and
[snapshot/resource ownership](02_ADR/content-runtime-snapshots.md) for lifecycle
and API details.
