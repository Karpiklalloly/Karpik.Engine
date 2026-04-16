using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public class VeldridPipelineFactory(GraphicsDevice device) : IPipelineFactory
{
    private readonly ResourceFactory _factory = device.ResourceFactory;
    
    public IPipeline Create(GraphicsPipelineDescription description)
    {
        Pipeline? pipeline = _factory.CreateGraphicsPipeline(description);
        if (pipeline is null)
        {
            throw new NullReferenceException($"Pipeline is null!");
        }

        return new VeldridPipeline(pipeline);
    }

    public void Destroy(IPipeline pipeline)
    {
        pipeline.Dispose();
    }
}