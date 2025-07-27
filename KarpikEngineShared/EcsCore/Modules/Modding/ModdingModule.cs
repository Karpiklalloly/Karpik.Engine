namespace Karpik.Engine.Shared.Modding;

public class ModdingModule : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new ModUpdateSystem());
    }
}