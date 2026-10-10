using System.Collections.Concurrent;
using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsRegistry))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public class ModsRegistry : IModsRegistry
{
    private readonly IFileSystem _fileSystem;
    private readonly ModDefinitionLoader _definitionLoader;
    private readonly ConcurrentDictionary<string, ModDefinition> _mods = [];
    private readonly ILogger<IModsRegistry> _logger;

    public ModsRegistry(IAssetsManager assetsManager,
        ILogger<IModsRegistry> logger)
    {
        _logger = logger;
        _fileSystem = new PhysicalFileSystem();
        _definitionLoader = new ModDefinitionLoader(_fileSystem, assetsManager);
    }

    public async JobHandle Scan(ExecutionSide side)
    {
        string directory = _fileSystem.Combine(_fileSystem.RootPath, IModsRegistry.ModsDirectory);
        if (!_fileSystem.ExistsDirectory(directory))
        {
            return;
        }
        _mods.Clear();

        Span<string> directories = _fileSystem.GetDirectories(directory);
        
        // TODO: Сделать их параллельными?
        await foreach (var dir in directories.ToArray().ToAsyncEnumerable())
        {
            await LoadModDefinition(dir);
        }
    }
    
    public IEnumerable<ModDefinition> GetMods()
    {
        return _mods.Values;
    }
    
    private async JobHandle LoadModDefinition(string modDirectory)
    {
        try
        {
            Optional<ModDefinition, string> modDefinition = await _definitionLoader.Load(modDirectory);
            if (modDefinition.Error is not null)
            {
                _logger.LogError("{Error}", modDefinition.Error);
                return;
            }

            ModDefinition definition = modDefinition.Value;
            ModMetaData metaData = definition.MetaData;
            if (!_mods.TryAdd(metaData.Id, definition))
            {
                _logger.LogError("Duplicate mod id: {Id}", metaData.Id);
                return;
            }
            
            _logger.LogDebug("Mod loaded: {Name} v{Version} by {Author}", metaData.Name, metaData.Version, metaData.Author);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading mod {ModDirectory}", modDirectory);
        }
    }
}