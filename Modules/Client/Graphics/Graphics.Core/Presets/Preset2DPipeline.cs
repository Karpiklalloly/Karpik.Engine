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
    [DI] private GraphicsDevice _device = null!;
    private TextureResources _textureResources = new();
    private ResourceLayout _textureLayout = null!;

    internal void Init()
    {
        RectPipeline = CreateRectPipeline();
        TexturePipeline = CreateTexturePipeline("Shaders/2D.frag");
        TextPipeline = CreateTexturePipeline("Shaders/TextSdf.frag");
    }
    
    private Pipeline CreateRectPipeline()
    {
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
        _textureLayout = factory.CreateResourceLayout(ref resourceLayoutDesc);
        
        _textureResources.Init(_device, _textureLayout);

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [Vertex2D.Layout],
                shaders: shaders),
            ResourceLayouts = [_textureLayout], 

            Outputs = _device.MainSwapchain.Framebuffer.OutputDescription
        };
    
        return factory.CreateGraphicsPipeline(ref pipelineDesc);
    }
    
    private Pipeline CreateTexturePipeline(string fragmentShaderPath)
    {
        ResourceFactory? factory = _device.ResourceFactory;
        using var vertexShaderHandle = _assetsManager.LoadAssetAsync<ShaderAsset>("Shaders/2D.vert").GetAwaiter().GetResult();
        using var fragmentShaderHandle = _assetsManager.LoadAssetAsync<ShaderAsset>(fragmentShaderPath).GetAwaiter().GetResult();
        
        var shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, vertexShaderHandle.Asset.ShaderBytes, "main"),
            new ShaderDescription(ShaderStages.Fragment, fragmentShaderHandle.Asset.ShaderBytes, "main"));

        var pipelineDesc = new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = RasterizerStateDescription.CullNone,
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ShaderSet = new ShaderSetDescription(
                vertexLayouts: [Vertex2D.Layout],
                shaders: shaders),
            ResourceLayouts = [_textureLayout], 

            Outputs = _device.MainSwapchain.Framebuffer.OutputDescription
        };
    
        return factory.CreateGraphicsPipeline(ref pipelineDesc);
    }
}
