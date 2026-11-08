# Live presentation bindings

Use a live binding when a presentation value should follow published Content
versions. Continue using `AssetRef<T>` when a specific version is required.
Bindings do not change physics, gameplay or match configuration automatically.

Inject `IContentBindings` into a Simulation-scoped owner. Bind during cold
initialization or scene loading, while presentation is quiescent. A handle is an
unmanaged value suitable for ECS; it contains owner/slot/reuse identity, not the
payload or its service. Copying the handle does not acquire another owner. Recreate bindings from
AssetIds after a Simulation rebuild; a restored handle cannot address a new owner.

```csharp
// Cold initialization. The JsonTypeInfo belongs to this simulation.
var context = new UiJsonContext();
state.Ui = bindings.BindJson(uiAsset.Id, context.UiStrings, defaultUi);
state.Coin = bindings.Bind<ITexture2D>(coinAsset.Id);
```

The game supplies source-generated `JsonTypeInfo<T>` for typed JSON and can attach
cold validation to that metadata. Raw JSON loading remains shared by the registry;
projection to the game's UI type runs outside frame/fixed-update/render paths.
Do not use a global gameplay serializer/metadata cache. Font and shader decoding
are separate adapters; the texture adapter does not supply them.

In `Begin` or render preparation, read only ready values:

```csharp
if (bindings.TryGet(state.Ui, out UiStrings ui))
    DrawTitle(ui.Title);
if (bindings.TryGet(state.Coin, out ITexture2D texture))
    RecordCoin(texture);
```

The returned values are borrowed for that presentation frame. Do not cache a raw
texture across frame boundaries or dispose registry-owned textures. Draw commands
retain their own tracked resource claims through the renderer. Bindings adopt
already-prepared texture replacements at the host's safe boundary; no second
texture load or driver work runs in render preparation.

Static and Dynamic EngineRunner hosts automatically advance scoped binding
runtimes after Content publication and before gameplay consumers. Hosts with no
binding runtime registration keep an empty boundary. Ordinary startup without
reload opt-in still performs initial loading. A manually composed host must call
`IContentBindingRuntime.AdvanceBoundary` once at a quiescent boundary after its
registry/change source publication and before recording the next frame.

## Readiness, failure and removal

`TryGetStatus` returns a value snapshot with `State`, `HasValue`, `IsFallback`,
`RequestedVersion` and `PresentedVersion`. Availability is separate from state:
a pending or failed JSON replacement can still expose the last good value.

| Situation | Presentation |
| --- | --- |
| Initial load | Configured fallback, otherwise empty, until ready |
| Successful update | Ready replacement at a safe boundary |
| Failed JSON projection | Last good value, or fallback/empty when none exists |
| Registry rejects preparation | Existing registry and binding values remain unchanged |
| Asset removed | Fallback/empty; old selected resource is released |
| Asset re-added | Binding follows the new version automatically |
| Stale/foreign/released handle | Read/status returns false |

Bindings update independently after the registry publishes. Registry manifest
validation/publication remains atomic. A slow JSON projection does not delay a
ready texture or UI binding. Old or superseded worker results cannot overwrite a
newer request or a reused slot. `GetError` and `Retry` are cold diagnostic/control
operations, not calls to issue or log every frame.

Fallback objects are caller-owned and immutable by convention. A texture fallback
must be empty or a non-retiring caller-owned texture that outlives the owner.
Passing a registry-generation tracked texture as a constant fallback is rejected;
such a fallback could itself retire independently.

`delivery applied` still confirms the registry root change. It does not confirm
that a game-specific JSON projection is ready or that a frame was displayed.
Observe binding status or presentation separately. Publishing commands and
per-instance Content paths are described in [Content reload](content-reload.md).

## Capacity and lifetime

The default capacity is 256 bindings, configurable up to 4096 through
`ContentBindingOptions` before the owner starts. Register an override in the
Simulation installer through the normal DI registration mechanism. Tables and
queues never grow in a frame. Capacity includes live bindings and released slots
whose cold work is still draining; exhaustion rejects new registration explicitly
without disturbing existing handles. One executing and one latest requested
version per slot bound the work rather than buffering every source-edit event.

`Release` invalidates a handle; all copies become invalid. The owner also releases
all remaining handles during disposal. Registration/release/retry happen on the
owner's cold, quiescent lifecycle path. Reading and boundary advancement do not
allocate, wait, decode or perform I/O. Keep managed presentation owners, tasks,
fonts and serializer metadata out of ECS components.

Shutdown stops acceptance, invalidates late results, drains owned cold work and
releases selected resources/metadata before the Simulation scope and graphics
device disappear. Frame/merge ownership still follows the renderer's established
shutdown order. Do not wait for a retired texture while holding a pin that only
the same shutdown code can release.

The CoinRush template demonstrates typed UI strings and player/coin/tile texture
bindings, with its existing font kept in a scoped legacy-loader owner. Font hot
reload and automatic source watching are not included.
