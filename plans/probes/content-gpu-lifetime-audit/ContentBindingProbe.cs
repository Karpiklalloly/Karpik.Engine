using System.Diagnostics;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.OpenGL;
using NeoVeldrid;
using NeoVeldrid.StartupUtilities;

internal static partial class ContentReloadProbe
{
    public static void RunBindings()
    {
        NeoVeldridStartup.CreateWindowAndGraphicsDevice(
            new WindowCreateInfo(0, 0, 32, 32, WindowState.Hidden, "Content binding readback"),
            new GraphicsDeviceOptions(false, null, false), GraphicsBackend.OpenGL, out var window, out var device);
        var store = new Store();
        var registry = new ContentRegistry(null!, store, [new ContentTextureLoader(device)]);
        var id = new AssetId(Guid.NewGuid());
        ContentManifest Manifest(string hash, bool present = true) => new(1, present
            ? [new(id, "texture", "tex", "settings", hash, hash, store.Get(hash).Length, [])] : []);
        registry.RegisterManifest(Manifest("old"), store);
        var coordinator = new ContentReloadCoordinator(registry);
        var bindings = new ContentBindings(registry, registry, coordinator);
        var cpu = new ThreadBuffer();
        try
        {
            var binding = bindings.Bind<ITexture2D>(id);
            void PumpUntil(Func<bool> ready)
            {
                var deadline = Stopwatch.StartNew();
                while (true)
                {
                    coordinator.PublishBoundary();
                    bindings.AdvanceBoundary();
                    if (ready()) return;
                    if (deadline.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Binding did not reach expected readiness.");
                    Thread.Sleep(1); // Probe control plane only.
                }
            }
            PumpUntil(() => bindings.TryGet(binding, out _));
            if (!bindings.TryGet(binding, out var initial)) throw new Exception("Initial binding empty.");
            var old = (VeldridTexture2D)initial;

            var factory = device.ResourceFactory;
            using var layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)));
            using var target = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            using var readback = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, target));
            using var vs = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex,
                System.Text.Encoding.UTF8.GetBytes("#version 330 core\nconst vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3)); void main(){gl_Position=vec4(p[gl_VertexID],0,1);}"), "main"));
            using var fs = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment,
                System.Text.Encoding.UTF8.GetBytes("#version 330 core\nuniform sampler2D Tex; out vec4 color; void main(){color=texture(Tex,vec2(0.5));}"), "main"));
            using var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList,
                new ShaderSetDescription([], [vs, fs]), [layout], framebuffer.OutputDescription));
            using var commands = factory.CreateCommandList();
            void ExpectCurrent(byte r, byte g)
            {
                if (!bindings.TryGet(binding, out var texture)) throw new Exception("Selected binding unexpectedly empty.");
                commands.Begin(); commands.SetFramebuffer(framebuffer); commands.SetFullViewports(); commands.SetPipeline(pipeline);
                commands.SetGraphicsResourceSet(0, ((VeldridTexture2D)texture).ResourceSet);
                commands.Draw(3); commands.CopyTexture(target, readback); commands.End();
                device.SubmitCommands(commands);
                device.GetOpenGLInfo().ExecuteOnGLThread(() => { });
                var pixels = device.Map<Pixel>(readback, MapMode.Read);
                var pixel = pixels[0, 0]; device.Unmap(readback);
                if (pixel.R != r || pixel.G != g || pixel.B != 0 || pixel.A != 255)
                    throw new Exception($"Binding returned wrong rendered pixel: {pixel}");
                Console.WriteLine($"binding-pixel:{pixel}");
            }
            ExpectCurrent(0, 255);
            cpu.Add(new DrawTextureCmd { Texture = old });
            Task<ContentReloadResult> update = coordinator.EnqueueAsync(Manifest("new"), store);
            PumpUntil(() => update.IsCompleted);
            if (update.GetAwaiter().GetResult().Outcome != ContentReloadOutcome.Applied) throw new Exception("Texture update was not applied.");
            ExpectCurrent(255, 0);
            if (old.Texture.IsDisposed) throw new Exception("Old writing frame lost its native resource.");
            cpu.Clear();
            old.Lifetime!.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();

            Task<ContentReloadResult> removal = coordinator.EnqueueAsync(Manifest("new", false), store);
            PumpUntil(() => removal.IsCompleted);
            if (removal.GetAwaiter().GetResult().Outcome != ContentReloadOutcome.Applied || bindings.TryGet(binding, out _))
                throw new Exception("Removal did not clear the binding.");
            Task<ContentReloadResult> readd = coordinator.EnqueueAsync(Manifest("old"), store);
            PumpUntil(() => readd.IsCompleted && bindings.TryGet(binding, out _));
            if (readd.GetAwaiter().GetResult().Outcome != ContentReloadOutcome.Applied) throw new Exception("Re-add was not applied.");
            ExpectCurrent(0, 255);
            Console.WriteLine("CONTENT BINDINGS GPU PASSED: old green, new red, pinned old frame, removal, re-add green");
        }
        finally
        {
            cpu.Clear();
            bindings.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            registry.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            device.Dispose(); window.Close();
        }
    }
}
