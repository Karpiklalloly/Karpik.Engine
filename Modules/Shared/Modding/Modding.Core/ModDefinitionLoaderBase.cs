using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public abstract class ModDefinitionLoaderBase(IFileSystem fileSystem, IAssetsManager assetsManager) : IModDefinitionLoader
{
    public virtual async JobHandle<Optional<ModDefinition, string>> Load(string modDirectory)
    {
        if (!fileSystem.ExistsDirectory(modDirectory))
        {
            modDirectory = fileSystem.Combine(fileSystem.RootPath, "Mods", modDirectory);
            if (!fileSystem.ExistsDirectory(modDirectory))
            {
                return Error(modDirectory, "No such mod directory");
            }
        }

        string metadataPath = fileSystem.Combine(modDirectory, "mod_info.json");
        using AssetHandle<ModMetaDataAsset> handle = await assetsManager.LoadAssetAsync<ModMetaDataAsset>(metadataPath);
        if (!handle.IsValid)
        {
            return Error(modDirectory, "Could not find mod_info.json");
        }

        if (string.IsNullOrWhiteSpace(handle.Asset!.MetaData.Id))
        {
            return Error(modDirectory, "Invalid mod Id");
        }

        ModMetaData assetMetaData = handle.Asset.MetaData;
        return new(new(assetMetaData, modDirectory));
    }
    
    protected virtual Optional<ModDefinition, string> Error(string modDirectory, string why)
    {
        return new Optional<ModDefinition, string>(default, $"Could not load mod {modDirectory}: {why}.");
    }
}