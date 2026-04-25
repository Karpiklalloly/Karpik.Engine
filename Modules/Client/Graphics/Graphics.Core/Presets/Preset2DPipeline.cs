using Karpik.Engine.Client.Graphics.Core.AssetManagement;
using Karpik.Engine.Client.Graphics.Core.Sets;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Jobs;
using Veldrid;
using Veldrid.SPIRV;

namespace Karpik.Engine.Client.Graphics.Core.Presets;

public class Preset2DPipeline
{
    public Pipeline RectPipeline { get; private set; } = null!;
    public Pipeline TexturePipeline { get; private set; } = null!;
    public Pipeline TextPipeline { get; private set; } = null!;
    public ResourceSet WhiteRectResourceSet => _textureResources.WhiteRectResourceSet;

    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private GraphicsDevice _device = null;
    private TextureResources _textureResources = new();

    internal void Init()
    {
        RectPipeline = CreateRectPipeline();
        TexturePipeline = RectPipeline;
        TextPipeline = RectPipeline;
    }
    
    private  Pipeline CreateRectPipeline()
    {
        // лоадеры не успевают подгрузиться
        ResourceFactory? factory = _device.ResourceFactory;
        using var vertexShaderHandle = _assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert").GetAwaiter().GetResult();
        using var fragmentShaderHandle = _assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.frag").GetAwaiter().GetResult();
        
        var shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, vertexShaderHandle.Asset.ShaderBytes, "main"),
            new ShaderDescription(ShaderStages.Fragment, fragmentShaderHandle.Asset.ShaderBytes, "main"));

        var resourceLayoutDesc = new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)
        );
        ResourceLayout mainLayout = factory.CreateResourceLayout(ref resourceLayoutDesc);
        
        _textureResources.Init(_device, mainLayout);

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [Vertex2D.Layout],
                shaders: shaders),
            ResourceLayouts = [mainLayout], 

            Outputs = _device.MainSwapchain.Framebuffer.OutputDescription
        };
    
        return factory.CreateGraphicsPipeline(ref pipelineDesc);
    }
}