using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;

namespace Karpik.Engine.Shared.Modding.Lua;

public class LuaModDefinitionLoader(IFileSystem fileSystem, IAssetsManager assetsManager) : ModDefinitionLoaderBase(fileSystem, assetsManager);