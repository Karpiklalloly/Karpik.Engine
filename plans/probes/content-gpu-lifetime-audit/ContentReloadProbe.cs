using Autofac;
using Karpik.Content.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.Graphics.OpenGL;
using NeoVeldrid;
using NeoVeldrid.StartupUtilities;

internal static class ContentReloadProbe
{
    private sealed class Store : IContentStore
    {
        public readonly byte[] Bytes = Png(0, 255, 0);
        public readonly byte[] NewBytes = Png(255, 0, 0);
        public ReadOnlyMemory<byte> Get(string locator) => locator == "old" ? Bytes : NewBytes;
        public Task<ReadOnlyMemory<byte>> GetAsync(string locator, CancellationToken ct = default) => Task.FromResult(Get(locator));
    }
    private static byte[] Png(byte r, byte g, byte b)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string name, byte[] data)
        {
            Span<byte> number = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length); output.Write(number);
            byte[] type = System.Text.Encoding.ASCII.GetBytes(name);
            output.Write(type); output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte value in type.Concat(data))
            {
                crc ^= value;
                for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); output.Write(number);
        }
        Chunk("IHDR", [0,0,0,1, 0,0,0,1, 8,6,0,0,0]);
        using var compressed = new MemoryStream();
        using (var zip = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true)) zip.Write(new byte[] { 0, r, g, b, 255 });
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", []);
        return output.ToArray();
    }
    public static void RunDi()
    {
        NeoVeldridStartup.CreateWindowAndGraphicsDevice(new WindowCreateInfo(0, 0, 32, 32, WindowState.Hidden, "Content shutdown"),
            new GraphicsDeviceOptions(false, null, false), GraphicsBackend.OpenGL, out var window, out var device);
        try
        {
            for (int order = 0; order < 2; order++)
            {
                var store = new Store();
                var builder = new ContainerBuilder();
                builder.RegisterInstance(device).ExternallyOwned();
                var preset = (Karpik.Engine.Client.Graphics.Core.Presets.Preset2DPipeline)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Karpik.Engine.Client.Graphics.Core.Presets.Preset2DPipeline));
                builder.RegisterInstance(preset).ExternallyOwned();
                builder.RegisterInstance(new Karpik.Engine.Core.ClientFrameMetrics());
                void RegisterRegistry() => builder.Register(_ => new ContentRegistry(null!, store, [new ContentTextureLoader(device)]))
                    .AsSelf().As<IContentRegistry>().SingleInstance();
                void RegisterMerge() => builder.RegisterType<MergeThread>().As<IMergeThread>().SingleInstance();
                void RegisterCoordinator() => builder.Register(context => new ContentReloadCoordinator(context.Resolve<ContentRegistry>()))
                    .AsSelf().As<IContentReloadLifetime>().SingleInstance();
                if (order == 0) { RegisterMerge(); RegisterCoordinator(); RegisterRegistry(); }
                else { RegisterRegistry(); RegisterCoordinator(); RegisterMerge(); }
                var container = builder.Build();
                ContentRegistry registry;
                IMergeThread merge;
                if (order == 0) { merge = container.Resolve<IMergeThread>(); registry = container.Resolve<ContentRegistry>(); }
                else { registry = container.Resolve<ContentRegistry>(); merge = container.Resolve<IMergeThread>(); }
                var id = new AssetId(Guid.NewGuid());
                registry.RegisterManifest(new(1, [new(id, "texture", "tex", "s", "s", "old", store.Bytes.Length, [])]), store);
                var reference = new AssetRef<ITexture2D>(id); registry.LoadAsync(reference).GetAwaiter().GetResult();
                if (!registry.TryGet(reference, out var lease)) throw new Exception("DI texture missing");
                var old = (VeldridTexture2D)lease.Payload!;
                GraphicsContext.BeginFrame(); GraphicsContext.Buffer.Add(new DrawTextureCmd { Texture = lease.Payload! });
                var coordinator = container.Resolve<ContentReloadCoordinator>();
                Task<ContentReloadResult> application = coordinator.EnqueueAsync(
                    new(1, [new(id, "texture", "tex", "s", "new", "new", store.NewBytes.Length, [])]), store);
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (coordinator.Sequence == 0)
                {
                    if (wait.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("DI coordinator never prepared a texture update.");
                    coordinator.PublishBoundary();
                    Thread.Sleep(1);
                }
                if (application.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult().Outcome != ContentReloadOutcome.Applied)
                    throw new Exception("DI coordinator did not acknowledge texture publication.");
                if (old.Texture.IsDisposed) throw new Exception("DI coordinator released a CPU-owned old texture before renderer shutdown.");
                coordinator.RequestStop();
                Task shutdown = container.DisposeAsync().AsTask();
                try { shutdown.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); }
                catch { merge.Dispose(); shutdown.GetAwaiter().GetResult(); throw; }
                if (!old.Texture.IsDisposed) throw new Exception("DI shutdown did not finish old texture retirement.");
            }
            Console.WriteLine("DI SHUTDOWN PASSED: both registration and activation orders release CPU owners before coordinator retirement and registry drainage");
        }
        finally { device.Dispose(); window.Close(); }
    }
    public static void Run()
    {
        NeoVeldridStartup.CreateWindowAndGraphicsDevice(new WindowCreateInfo(0, 0, 32, 32, WindowState.Hidden, "Content reload"),
            new GraphicsDeviceOptions(false, null, false), GraphicsBackend.OpenGL, out var window, out var device);
        try
        {
            var store = new Store();
            var registry = new ContentRegistry(null!, store, [new ContentTextureLoader(device)]);
            var id = new AssetId(Guid.NewGuid());
            ContentManifest Manifest(string hash) => new(1, [new(id, "texture", "tex", "settings", hash, hash, store.Get(hash).Length, [])]);
            registry.RegisterManifest(Manifest("old"), store);
            var reference = new AssetRef<ITexture2D>(id);
            registry.LoadAsync(reference).GetAwaiter().GetResult();
            if (!registry.TryGet(reference, out var lease)) throw new Exception("Initial texture missing");
            var old = (VeldridTexture2D)lease.Payload!;
            var cpu = new ThreadBuffer(); cpu.Add(new DrawTextureCmd { Texture = old });
            GraphicsContext.BeginFrame();
            GraphicsContext.Buffer.Add(new DrawTextureCmd { Texture = old, Color = System.Drawing.Color.White, Size = System.Numerics.Vector2.One });
            GraphicsContext.BeginFrame();
            using var prepared = registry.PrepareAsync(Manifest("new")).GetAwaiter().GetResult();
            if (!registry.TryCommit(prepared)) throw new Exception("Commit failed");
            if (old.Texture.IsDisposed) throw new Exception("Old CPU-owned texture disposed during publication");
            var factory = device.ResourceFactory;
            using var layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)));
            using var output = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
            using var staging = factory.CreateTexture(TextureDescription.Texture2D(4, 4, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
            using var framebuffer = factory.CreateFramebuffer(new FramebufferDescription(null, output));
            using var vs = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes("#version 330 core\nconst vec2 p[3]=vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3)); void main(){gl_Position=vec4(p[gl_VertexID],0,1);}"), "main"));
            using var fs = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes("#version 330 core\nuniform sampler2D Tex; out vec4 color; void main(){color=texture(Tex,vec2(0.5));}"), "main"));
            using var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList,
                new ShaderSetDescription([], [vs,fs]), [layout], framebuffer.OutputDescription));
            using var commands = factory.CreateCommandList();
            void Draw(VeldridTexture2D texture)
            {
                commands.Begin(); commands.SetFramebuffer(framebuffer); commands.SetFullViewports(); commands.SetPipeline(pipeline);
                commands.SetGraphicsResourceSet(0, texture.ResourceSet); commands.Draw(3); commands.CopyTexture(output, staging); commands.End();
                device.SubmitCommands(commands);
            }
            void Expect(byte r, byte g)
            {
                device.GetOpenGLInfo().ExecuteOnGLThread(() => { });
                var mapped = device.Map<Pixel>(staging, MapMode.Read); var value = mapped[0,0]; device.Unmap(staging);
                if (value.R != r || value.G != g || value.B != 0 || value.A != 255) throw new Exception($"Unexpected content pixel {value}");
            }
            using var mergePipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone, PrimitiveTopology.TriangleList,
                new ShaderSetDescription([], [vs,fs]), [layout], device.MainSwapchain.Framebuffer.OutputDescription));
            // Exercise production merge ownership without the legacy file-based shader loader.
            var preset = (Karpik.Engine.Client.Graphics.Core.Presets.Preset2DPipeline)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Karpik.Engine.Client.Graphics.Core.Presets.Preset2DPipeline));
            typeof(Karpik.Engine.Client.Graphics.Core.Presets.Preset2DPipeline).GetProperty("TexturePipeline")!.SetValue(preset, mergePipeline);
            using var merge = new MergeThread(device, preset, new Karpik.Engine.Core.ClientFrameMetrics());
            merge.Init();
            var renderView = new RenderView(default, 4, 4);
            if (!merge.TryBeginMerge(renderView)) throw new Exception("Merge did not acquire old ready frame");
            merge.WaitForCompletion();
            if (!merge.TryTakeCompletedCommandList(out var mergedCommands, out var mergedFence)) throw new Exception("Merge output unavailable");
            cpu.Clear(); // The claimed production merge list is now the only old-generation CPU owner.
            old.Lifetime!.Retire();
            if (old.Lifetime.TryDispose()) throw new Exception("Claimed merge list lost its texture ownership");
            if (!registry.TryResolve<ITexture2D>(id, out var next) || !registry.TryGet(next, out var nextLease)) throw new Exception("New texture missing");
            ITexture2D newTexture = nextLease.Payload!;
            void QueueNewFrame()
            {
                GraphicsContext.BeginFrame();
                GraphicsContext.Buffer.Add(new DrawTextureCmd { Texture = newTexture, Color = System.Drawing.Color.White, Size = System.Numerics.Vector2.One });
                GraphicsContext.BeginFrame();
                if (!merge.TryBeginMerge(renderView)) throw new Exception("Merge cannot acquire another free context");
                merge.WaitForCompletion();
            }
            QueueNewFrame(); // A claimed context must not be rebuilt while another frame completes.
            if (merge.TryTakeCompletedCommandList(out _, out _)) throw new Exception("A second claim was allowed");
            device.ResetFence(mergedFence); device.SubmitCommands(mergedCommands, mergedFence);
            Draw(old); // Explicit use remains covered by the still-held merge claim until this submission returns.
            merge.CompleteSubmission(mergedCommands!);
            if (!merge.TryTakeCompletedCommandList(out var discarded, out _) || ReferenceEquals(discarded, mergedCommands)) throw new Exception("Claimed context was reused");
            merge.CompleteSubmission(discarded!); // Deliberately discard an unsubmitted list.
            prepared.Completion.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Expect(0, 255);
            if (!old.Texture.IsDisposed || registry.TryGet(reference, out _)) throw new Exception("Old generation was not retired");
            if (!registry.TryResolve<ITexture2D>(id, out var current) || !registry.TryGet(current, out var latest)) throw new Exception("New generation unavailable");
            if (latest.Payload!.Width != 1 || latest.Payload.Height != 1) throw new Exception("Decoded dimensions incorrect");
            Draw((VeldridTexture2D)latest.Payload);
            Expect(255, 0);
            QueueNewFrame(); QueueNewFrame(); // Supersede a ready list; Dispose must release the remaining ready context.
            merge.Dispose();
            registry.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            Console.WriteLine("CONTENT TEXTURE RELOAD PASSED: prepare, publish, transfer, submit old green, retire, submit new red, shutdown");
        }
        finally { device.Dispose(); window.Close(); }
    }
}
