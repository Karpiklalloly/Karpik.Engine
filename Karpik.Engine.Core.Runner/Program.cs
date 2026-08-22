using Karpik.Engine.Core;

namespace Karpik.Engine.Core.Runner;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("[Worker] Starting...");
        try
        {
            new WorkerHost().Run(args, LoadDynamicModules);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Worker] Engine crashed: {ex}");
            throw;
        }
        finally
        {
            ModuleLoader.CleanupDisposedShadows();
        }
    }

    private static WorkerRuntimeConfiguration LoadDynamicModules(
        Side side,
        string bundleRoot,
        string engineRoot,
        Bootstrap bootstrap)
    {
        var loader = new ModuleLoader(
            bundleRoot,
            RuntimeModuleComposition.Resolve(engineRoot, side),
            Path.Combine(engineRoot, "native"));
        switch (side)
        {
            case Side.Client:
                loader.LoadClientModules();
                break;
            case Side.Server:
                loader.LoadServerModules();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, null);
        }
        Type[] types = loader.LoadedAssemblies
            .SelectMany(assembly => assembly.GetTypes())
            .ToArray();
        RuntimeModuleComposition.ValidateRequiredInstallers(types);
        bootstrap.RegisterTypes(types);
        return new WorkerRuntimeConfiguration(loader.ModuleDirectory, loader);
    }
}
