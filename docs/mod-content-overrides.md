# Package a mod that replaces an asset

A mod can replace the data behind a game or another mod's stable `AssetId`. The target ID and its declared type stay the same. Generated Content refs and live bindings see the replacement; legacy `AssetsManager` path loads and direct file reads still see the original game files.

## Build and ship

This example is a texture-only mod with ID `winter-skins`. Put `icon.png` in `ModSources/`, then use the existing Content CLI:

```sh
content create --source ./ModSources --file icon.png --namespace winter-skins
content build --source ./ModSources --output ./Release/Mods/winter-skins/Content/Client --namespace winter-skins --target Client
```

Keep the `assetId` generated in `icon.png.meta` across releases. It identifies the **replacement**, not the target. Copy the target ID from the game or the target mod's published manifest. Do not include the target asset or its manifest in the build input or in the ordinary dependency list.

Save `mod_info.json` at `Release/Mods/winter-skins/mod_info.json`:

```json
{"id":"winter-skins","name":"Winter Skins","contentOnly":true}
```

`contentOnly: true` means this package must not have `main.lua` under `Shared`, `Client`, or `Server`. An existing Lua mod can ship cooked Content without adding this flag.

In `Content/Client/overrides.json` declare the replacement. Substitute the current ID from `.karpik-current-generation`, the original target ID, and your replacement's generated ID:

```json
{
  "schemaVersion": 1,
  "generationId": "0123456789abcdef0123456789abcdef",
  "overrides": [
    {
      "targetModId": null,
      "targetAssetId": "70a6a163-b4e7-42ef-8be6-715cf1acaeab",
      "replacementAssetId": "99d2bcae-d1b2-48b4-94de-832eb4ddefa3"
    }
  ]
}
```

`targetModId: null` means the base game. To patch another mod, set it to that mod's `mod_info.json` ID. The replacement must be an original asset owned by this package and match the target's `declaredType` exactly. An uninstalled or [disabled](modules/modding/mod-activation.md) owner produces a warning and skips that rule. An enabled, installed owner with a missing target, or a mismatched type, produces an error and skips the rule, leaving the original or a previous valid replacement intact. If multiple mods replace one asset, the last valid rule in the mod loader's [dependency order](modules/modding/mod-dependencies.md) wins. Rules use the replacement's original bytes; they do not follow other override rules.

Ship the complete cooked output including the hidden current-generation marker and matching `.generations/<id>/` data, plus the declaration. The player puts `winter-skins/` under the game's `Mods/` directory and starts the game. Source assets, a CLI invocation in the game, and config edits are not needed. For a Server build, create its own `Content/Server` output with `--target Server` and a declaration matching that generation. Each side only reads its own directory. Mod Content package capture is supported on Linux and Windows.

## Update while the game is running

When updating an installed package, build and assemble it before putting it in `Mods`; write the declaration together with its matching generation. The runtime may reject a partially copied package or mismatched generation. Refresh again after the install is complete. Do not modify artifacts inside a published generation in place.

A game menu or console command can take `Karpik.Engine.Core.IModContentRefresh` by constructor injection and `await refresh.RequestModContentRefreshAsync()` while the game continues to run frames. There is no watcher, and the call does not require the CLI reload endpoint. `Applied` means the change (or a no-op) was published; `Busy` means a refresh is already waiting or preparing; `Failed` leaves the last good catalog in place; `Unavailable` means this host cannot accept the request. Do not synchronously wait on a thread that must drive the publication boundary.

Refresh can add, update, and remove content-only packages. It can also update cooked Content for a loaded Lua mod, but it does not restart Lua or the simulation. A candidate with invalid dependencies, or one that would change active Lua mod order or any `has_dependency` result, is rejected. Adding or removing Lua mods still requires a restart. If a loaded Lua mod's folder disappears, refresh keeps its last active Content and logs that a restart is needed. Turning an installed package into a Lua mod also leaves its last active Content in place until restart. A [base Content reload](content-reload.md) preserves the active mod layer and does not rescan `Mods`.

Existing mod metadata and Content manifests need no migration. Rebuild binaries that directly construct `ModMetaData`, `ModsRegistry`, or `ContentRegistry` against the updated engine assemblies; their source-level calls still compile.
