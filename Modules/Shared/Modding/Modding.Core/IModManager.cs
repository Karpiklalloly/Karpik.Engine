using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModManager
{
    public void Init(ExecutionSide side);

    public JobHandle LoadMods(string modsRootDirectory);
    public JobHandle ReloadAllMods(string modsRootDirectory);
    public ModMetaData GetModMetadata(string modId);

    public void StartMods();
    public void UpdateMods();
}