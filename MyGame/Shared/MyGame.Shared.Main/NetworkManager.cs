using DCFApixels.DragonECS;
using Karpik.Engine.Shared.Network.Core;

namespace Karpik.Engine.MyGame.Shared.Main;

public partial class NetworkManager
{
    public void Initialize()
    {
        RegisterSerializers();
    }
    
    private partial void RegisterSerializers();

    public partial void WriteSnapshot(EcsWorld world, IWriter writer, List<int> destroyed);
    public partial void ApplySnapshot(EcsWorld world, IReader reader);
    public partial void ClearClientCache();
}