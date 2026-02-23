using DCFApixels.DragonECS;
using Karpik.Engine.MyGame.Server.Main.Systems;

namespace Karpik.Engine.MyGame.Server.Main;

internal class MyGameServerModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b
            .Add(new NetworkSystem());
    }
}