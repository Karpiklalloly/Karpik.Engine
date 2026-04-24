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

public class GraphicsCoreEndSystem : IEcsRun
{
    [DI] private IMergeThread _mergeThread = null!;
    [DI] private GraphicsDevice _device = null!;
    
    public void Run()
    {
        _mergeThread.BeginMerge();
        _mergeThread.WaitForCompletion();
        CommandList commandList = _mergeThread.GetCommandList();
        _device.SubmitCommands(commandList);
        _device.SwapBuffers();
    }
}