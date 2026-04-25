using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core.Presets;
using Karpik.Engine.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsCoreInitSystem : IEcsInit
{
    [DI] private Preset2DPipeline _pipelines = null!;

    public void Init()
    {
        _pipelines.Init();
    }
}

public class GraphicsCoreBeginSystem : IEcsRun
{
    public void Run()
    {
        GraphicsContext.BeginFrame();
    }
}

public class GraphicsCoreMergeSystem : IEcsRun
{
    [DI] private IMergeThread _mergeThread = null!;

    public void Run()
    {
        _mergeThread.BeginMerge();
    }
}

public class GraphicsCoreSubmitSystem : IEcsRun
{
    [DI] private IMergeThread _mergeThread = null!;
    [DI] private GraphicsDevice _device = null!;

    public void Run()
    {
        // Ждем, если мердж еще не успел закончиться (обычно он уже готов)
        _mergeThread.WaitForCompletion();

        // Отправляем готовые команды в GPU
        _device.SubmitCommands(_mergeThread.GetCommandList());
        _device.SwapBuffers();
    }
}