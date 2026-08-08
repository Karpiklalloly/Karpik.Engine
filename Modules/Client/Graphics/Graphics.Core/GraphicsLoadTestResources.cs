using System.Composition;
using Karpik.Engine.Core;
using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

[Export(typeof(GraphicsLoadTestResources))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class GraphicsLoadTestResources : IDisposable
{
    private static readonly RgbaByte[] Colors =
    [
        new(255, 96, 96, 255),
        new(96, 196, 255, 255),
        new(128, 255, 128, 255),
        new(255, 224, 96, 255)
    ];

    private readonly Texture[] _textures = new Texture[Colors.Length];
    private readonly TextureView[] _views = new TextureView[Colors.Length];
    private readonly ResourceSet[] _resourceSets = new ResourceSet[Colors.Length];
    private readonly ITexture2D[] _texture2Ds = new ITexture2D[Colors.Length];
    private bool _initialized;

    public void Initialize(GraphicsDevice device, ResourceLayout textureLayout)
    {
        if (_initialized)
        {
            return;
        }

        ResourceFactory factory = device.ResourceFactory;
        for (int i = 0; i < Colors.Length; i++)
        {
            Texture texture = factory.CreateTexture(TextureDescription.Texture2D(
                1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
            device.UpdateTexture(texture, [Colors[i]], 0, 0, 0, 1, 1, 1, 0, 0);
            TextureView view = factory.CreateTextureView(texture);
            ResourceSet resourceSet = factory.CreateResourceSet(new ResourceSetDescription(
                textureLayout,
                view,
                device.PointSampler));

            _textures[i] = texture;
            _views[i] = view;
            _resourceSets[i] = resourceSet;
            _texture2Ds[i] = new VeldridTexture2D(texture, resourceSet);
        }

        _initialized = true;
    }

    public ITexture2D GetTexture(int index)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Graphics load test resources were used before initialization.");
        }

        return _texture2Ds[index % _texture2Ds.Length];
    }

    public void Dispose()
    {
        for (int i = 0; i < _textures.Length; i++)
        {
            _resourceSets[i]?.Dispose();
            _views[i]?.Dispose();
            _textures[i]?.Dispose();
        }

        _initialized = false;
    }
}
