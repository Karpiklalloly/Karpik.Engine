using System.Composition;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.Graphics.Core;

public enum GraphicsLoadTestScenario
{
    SortedRects,
    UnsortedRects,
    TextureBatches,
    TextureThrash
}

[Export(typeof(GraphicsLoadTestSettings))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class GraphicsLoadTestSettings
{
    public const int MaxQuadCount = 8192 * 2;

    private int _quadCount;
    private int _scenario;

    public int QuadCount
    {
        get => Volatile.Read(ref _quadCount);
        set => Volatile.Write(ref _quadCount, Math.Clamp(value, 0, MaxQuadCount));
    }

    public GraphicsLoadTestScenario Scenario
    {
        get => (GraphicsLoadTestScenario)Volatile.Read(ref _scenario);
        set => Volatile.Write(ref _scenario, Math.Clamp((int)value, (int)GraphicsLoadTestScenario.SortedRects, (int)GraphicsLoadTestScenario.TextureThrash));
    }

    public static string GetScenarioName(GraphicsLoadTestScenario scenario) => scenario switch
    {
        GraphicsLoadTestScenario.SortedRects => "Sorted rects",
        GraphicsLoadTestScenario.UnsortedRects => "Unsorted rects",
        GraphicsLoadTestScenario.TextureBatches => "Texture batches",
        GraphicsLoadTestScenario.TextureThrash => "Texture thrash",
        _ => "Unknown"
    };
}
