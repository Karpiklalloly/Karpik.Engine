using System.Collections.Concurrent;
using System.Composition;
using Karpik.Engine.Core;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Microsoft.Extensions.Logging;

namespace Karpik.Engine.Shared.Modding.Lua;

[Export(typeof(IModsRegistry))]
[ServiceRegistration(ModuleScope.ModSet, ServiceLifetime.Singleton)]
public class ModsRegistry : IModsRegistry
{
    private readonly IFileSystem _fileSystem;
    private readonly LuaModDefinitionLoader _definitionLoader;
    private readonly ConcurrentDictionary<string, ModDefinition> _mods = [];
    private readonly ILogger<IModsRegistry> _logger;
    private ModDefinition[] _definitions = [];
    private bool _scanning;
    private bool _prepared;

    public ModsRegistry(IAssetsManager assetsManager,
        ILogger<IModsRegistry> logger)
    {
        _logger = logger;
        _fileSystem = new PhysicalFileSystem();
        _definitionLoader = new LuaModDefinitionLoader(_fileSystem, assetsManager);
    }

    public async JobHandle Scan(ExecutionSide side, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_scanning || _prepared)
            throw new InvalidOperationException("ModSet catalog is already scanning or prepared.");

        _scanning = true;
        try
        {
            _mods.Clear();
            string directory = _fileSystem.Combine(_fileSystem.RootPath, IModsRegistry.ModsDirectory);
            if (_fileSystem.ExistsDirectory(directory))
            {
                string[] directories = _fileSystem.GetDirectories(directory).ToArray();
                foreach (string dir in directories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await LoadModDefinition(dir);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            _definitions = _mods.Values.OrderBy(mod => mod.MetaData.Id, StringComparer.Ordinal).ToArray();
            _prepared = true;
        }
        finally
        {
            _scanning = false;
        }
    }
    
    public IEnumerable<ModDefinition> GetMods()
    {
        if (!_prepared)
            throw new InvalidOperationException("ModSet catalog must be prepared before use.");
        return _definitions;
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
