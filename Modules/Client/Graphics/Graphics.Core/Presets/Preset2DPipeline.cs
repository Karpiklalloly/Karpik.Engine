using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Veldrid;
using Veldrid.SPIRV;

namespace Karpik.Engine.Client.Graphics.Core.Presets;

public class Preset2DPipeline : IOnInjectedDI
{
    public Pipeline RectPipeline { get; private set; } = null!;
    public Pipeline TexturePipeline { get; private set; } = null!;
    public Pipeline TextPipeline { get; private set; } = null!;

    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private GraphicsDevice _device = null;
    
    public void OnInjected()
    {
        JobHandle handle = Job.Run(async () =>
        {
            RectPipeline = await CreateRectPipeline();
            TexturePipeline = RectPipeline;
            TextPipeline = RectPipeline;
        });
        handle.Wait();
    }
    
    private async JobHandle<Pipeline> CreateRectPipeline()
    {
        ResourceFactory? factory = _device.ResourceFactory;
        using var vertexShaderHandle = await _assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert");
        using var fragmentShaderHandle = await _assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.frag");
        
        var shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, vertexShaderHandle.Asset.ShaderBytes, "main"),
            new ShaderDescription(ShaderStages.Fragment, fragmentShaderHandle.Asset.ShaderBytes, "main"));

        var resourceLayoutDesc = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)
        );
        ResourceLayout mainLayout = factory.CreateResourceLayout(ref resourceLayoutDesc);

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            PrimitiveTopology = PrimitiveTopology.TriangleStrip,
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [Vertex2D.Layout],
                shaders: shaders),
            ResourceLayouts = [mainLayout], 

            Outputs = _device.MainSwapchain.Framebuffer.OutputDescription
        };
    
        return factory.CreateGraphicsPipeline(ref pipelineDesc);
    }
}