using System.Composition;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;
using NeoVeldrid;
using StbImageSharp;

namespace Karpik.Engine.Client.Graphics.OpenGL;

/// <summary>Cold adapter for cooked PNG/JPEG textures. Driver work never runs in publication or command recording.</summary>
[Export(typeof(IContentPayloadLoader))]
[ServiceRegistration(ModuleScope.Engine, ServiceLifetime.Singleton)]
public sealed class ContentTextureLoader(GraphicsDevice device) : IContentPayloadLoader
{
    public bool CanLoad(Type payloadType, string declaredType) => payloadType == typeof(ITexture2D) && declaredType == "texture";
    public ValueTask<object> LoadAsync(Type payloadType, string declaredType, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (!CanLoad(payloadType, declaredType)) throw new NotSupportedException("Only texture/ITexture2D is supported.");
        return new ValueTask<object>(Task.Run<object>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            var factory = device.ResourceFactory;
            Texture? texture = null;
            TextureView? view = null;
            Sampler? sampler = null;
            ResourceLayout? layout = null;
            ResourceSet? set = null;
            // Queued after prior submissions; only this cold worker waits for the GL execution thread.
            void Destroy() => device.GetOpenGLInfo().ExecuteOnGLThread(() =>
            {
                GraphicsResourceShutdown.Dispose(() => { }, () => { }, set, view, sampler, layout, texture);
            });
            try
            {
                texture = factory.CreateTexture(TextureDescription.Texture2D((uint)image.Width, (uint)image.Height,
                    1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
                device.UpdateTexture(texture, image.Data, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);
                view = factory.CreateTextureView(texture);
                sampler = factory.CreateSampler(SamplerDescription.Linear);
                layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)));
                set = factory.CreateResourceSet(new ResourceSetDescription(layout, view, sampler));
                device.GetOpenGLInfo().ExecuteOnGLThread(() => { });
                cancellationToken.ThrowIfCancellationRequested();
                return new VeldridTexture2D(texture, set, new ContentResourceLifetime(Destroy));
            }
            catch { Destroy(); throw; }
        }, cancellationToken));
    }
}
