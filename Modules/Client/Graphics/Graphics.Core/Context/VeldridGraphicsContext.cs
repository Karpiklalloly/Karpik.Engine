using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridGraphicsContext : IGraphicsContext
{
    private readonly GraphicsDevice _device;
    
    public ITextureFactory Textures { get; }
    public IShaderFactory Shaders { get; }
    public IPipelineFactory Pipelines { get; }
    
    public VeldridGraphicsContext(GraphicsDevice device)
    {
        _device = device;
        Textures = new VeldridTextureFactory(device);
        Shaders = new VeldridShaderFactory(device);
        Pipelines = new VeldridPipelineFactory(device);
    }
    
    public void Init()
    {
        
    }

    public void BeginFrame()
    {
        GraphicsContext.BeginFrame();
    }

    public void Submit()
    {
        // Собрать все буферы
        var buffers = GraphicsContext.CollectBuffers();
    
        // Слить в единый CommandList
        var commandList = _device.ResourceFactory.CreateCommandList();
        commandList.Begin();
    
        foreach (ICommandBuffer buffer in buffers)
        {
            foreach (DrawCommand cmd in buffer.GetCommands())
            {
                // TODO: Сделать
                // Execute cmd on commandList
            }
        }
    
        commandList.End();
        _device.SubmitCommands(commandList);
    }
}