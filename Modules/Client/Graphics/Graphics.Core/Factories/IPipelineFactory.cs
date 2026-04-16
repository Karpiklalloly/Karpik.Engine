using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public interface IPipelineFactory
{
    public IPipeline Create(GraphicsPipelineDescription description);
    
    public void Destroy(IPipeline pipeline);
}