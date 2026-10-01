using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.ECS.Scheduling;
using SSSuperGame.Shared.CoinRush;

namespace SSSuperGame.Client.CoinRush;

/// <summary>Positions the camera over the interpolated local player after client updates.</summary>
/// <param name="world">World containing the local player and display state.</param>
/// <param name="cameras">Active camera state to update.</param>
[SequentialSystem]
public sealed class ClientCameraSystem(EcsDefaultWorld world, GraphicsCameraState cameras)
    : ISystemLateUpdate
{
    /// <summary>Follows the assigned player, or player zero until a slot is assigned.</summary>
    public void LateUpdate()
    {
        int slot = 0;
        var locals = world.Where(out LocalAspect aspect);
        for (int i = 0; i < locals.Count; i++)
        {
            int s = aspect.Locals.Read(locals[i]).Slot;
            if (s >= 0)
            {
                slot = s;
            }
        }

        float x = 0f;
        float y = 2f;
        var players = world.Where(out PlayerAspect playerAspect);
        for (int i = 0; i < players.Count; i++)
        {
            int e = players[i];
            if (playerAspect.Players.Read(e).Index == slot)
            {
                DisplayState d = playerAspect.Displays.Read(e);
                x = d.X;
                y = d.Y + 1.5f;
            }
        }

        var cam = Camera2D.CreateDefault();
        cam.Position = new System.Numerics.Vector2(x, y);
        cam.Zoom = 1f;
        cam.PixelsPerUnit = 32f;
        cameras.SetActive(cam);
    }

    /// <summary>Selects entities identifying the local player slot.</summary>
    private sealed class LocalAspect : EcsAspect
    {
        public EcsPool<LocalPlayer> Locals = Inc;
    }

    /// <summary>Selects players with interpolated display positions.</summary>
    private sealed class PlayerAspect : EcsAspect
    {
        public EcsPool<Player> Players = Inc;
        public EcsPool<DisplayState> Displays = Inc;
    }
}
