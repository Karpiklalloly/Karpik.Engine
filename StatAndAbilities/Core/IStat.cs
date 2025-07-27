using DCFApixels.DragonECS;

namespace Karpik.StatAndAbilities
{
    public interface IStat : IEcsComponent
    {
        public void Init();
        public void DeInit();
    }

    public interface IRangeStat : IStat { }
    
    public interface IEzRangeStat : IStat { }
}