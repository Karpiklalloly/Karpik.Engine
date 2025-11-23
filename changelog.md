# v0.2

## Key Changes
* **UI:** Added an HTML+CSS inspired UI system. See Wiki for details.
* **Dependency Injection:** Most static classes were removed and replaced with Dependency Injection. Implemented `AutoInject` for systems.
* **Karpik.Jobs:** Imported and integrated the job system.
* **Tween:** Added tweens (GTweens).

## Architecture and ECS
* Added `EcsCommandBuffer`, `BaseSystem`, and `IEcsRunParallel`.
* Renamed projects and added the `Game.targets` file.

## Network and Modding
* **Network:**
    * Fixed RPC behavior.
    * Added `LocalPlayer` component to identify the local player.
    * Implemented automatic port selection for the client.
* **Modding:**
    * Added separation into client and server subfolders for mods.
    * Added links to `Mods` and `Content` folders in the Debug build.
    * Fixed delta time (dt) calculation on the server.