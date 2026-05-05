using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.MyGame.Client.Main.Systems;

/// <summary>
/// Flushes all accumulated draw actions after 3D rendering is complete.
/// This system must run AFTER PreEndContextSystem (EndMode3D) but BEFORE EndContextSystem.
/// </summary>
public class FlushDrawersSystem : IEcsInit, IEcsRun
{
    [DI] private Drawer _drawer;
    [DI] private IAssetsManager _assetsManager = null!;
    private AssetHandle<FontAsset> _fontAsset;
    
    public void Run()
    {
        _drawer.Draw();
    }

    public void Init()
    {
        _fontAsset = _fontAsset = _assetsManager.LoadAssetAsync<FontAsset>("PressStart.font-json").GetAwaiter().GetResult();
        _drawer.SetFont(_fontAsset.Asset.Font);
    }
}
