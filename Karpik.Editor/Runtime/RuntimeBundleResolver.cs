using System.Text.Json;
using System.Text.Json.Serialization;
using Karpik.Engine.Core;

namespace Karpik.Editor;

public sealed record RuntimeBundleManifest(Side Side);

public sealed record EditorRuntimeBundle(
    Side Side,
    string DirectoryPath,
    string WorkerExecutablePath);

public sealed class RuntimeBundleResolver
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _editorDirectory;

    public RuntimeBundleResolver(string editorDirectory)
    {
        _editorDirectory = Path.GetFullPath(editorDirectory);
    }

    public EditorRuntimeBundle Resolve(Side side)
    {
        string sideName = side.ToString().ToLowerInvariant();
        string bundleDirectory = Path.Combine(_editorDirectory, "runtimes", sideName);
        string manifestPath = Path.Combine(bundleDirectory, "runtime-bundle.json");
        using (FileStream stream = File.OpenRead(manifestPath))
        {
            RuntimeBundleManifest manifest = JsonSerializer.Deserialize<RuntimeBundleManifest>(stream, JsonOptions)
                                             ?? throw new InvalidDataException(
                                                 $"Runtime bundle manifest '{manifestPath}' is empty.");
            if (manifest.Side != side)
            {
                throw new InvalidDataException(
                    $"Runtime bundle '{bundleDirectory}' is for {manifest.Side}, not {side}.");
            }
        }

        string workerName = OperatingSystem.IsWindows()
            ? "Karpik.Engine.Core.Runner.exe"
            : "Karpik.Engine.Core.Runner";
        string workerPath = Path.Combine(bundleDirectory, workerName);
        if (!File.Exists(workerPath))
        {
            throw new FileNotFoundException(
                $"Runtime worker was not found in the {side} bundle.",
                workerPath);
        }

        bool hasCompletedModules = Directory.EnumerateDirectories(
                bundleDirectory,
                "modules.version.*",
                SearchOption.TopDirectoryOnly)
            .Any(directory => File.Exists(Path.Combine(directory, ".complete")));
        if (!hasCompletedModules)
        {
            throw new InvalidDataException(
                $"Runtime bundle '{bundleDirectory}' has no completed module staging directory.");
        }

        return new EditorRuntimeBundle(side, bundleDirectory, workerPath);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
