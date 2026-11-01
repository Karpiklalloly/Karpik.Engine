using System.Runtime.InteropServices;
using System.Text;
using Karpik.Engine.Client.Graphics.Core;
using NeoVeldrid;
using NeoVeldrid.Sdl2;
using NeoVeldrid.StartupUtilities;

if (args.Contains("--pending"))
{
    Run("pending", 0);
    Console.WriteLine("PENDING GPU AUDIT PASSED");
    return;
}
if (!int.TryParse(args.Length == 0 ? "1" : args[0], out int repeats) || repeats is < 1 or > 100)
    throw new ArgumentException("Repetition count must be between 1 and 100, or use --pending.");
for (int i = 0; i < repeats; i++)
{
    Run("ordered", i);
    Run("early", i);
    Run("deferred", i);
}
Console.WriteLine("AUDIT PASSED");

static void Run(string mode, int iteration)
{
    var windowCI = new WindowCreateInfo(0, 0, 32, 32, WindowState.Hidden, "Content GPU lifetime audit");
    NeoVeldridStartup.CreateWindowAndGraphicsDevice(windowCI, new GraphicsDeviceOptions(false, null, false), GraphicsBackend.OpenGL,
        out Sdl2Window window, out GraphicsDevice gd);
    try
    {
        var info = gd.GetOpenGLInfo();
        if (iteration == 0)
        {
            nint glLibrary = NativeLibrary.Load("libGL.so.1");
            var getString = Marshal.GetDelegateForFunctionPointer<GlGetString>(NativeLibrary.GetExport(glLibrary, "glGetString"));
            string? renderer = null;
            info.ExecuteOnGLThread(() => renderer = Marshal.PtrToStringAnsi(getString(0x1F01)));
            Console.WriteLine($"{mode}: GL {info.Version}; renderer={renderer}");
            NativeLibrary.Free(glLibrary);
        }
        var factory = gd.ResourceFactory;
        using Texture source = factory.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        gd.UpdateTexture(source, new byte[] {0, 255, 0, 255}, 0, 0, 0, 1, 1, 1, 0, 0);
        using TextureView view = factory.CreateTextureView(source);
        using ResourceLayout layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
            new ResourceLayoutElementDescription("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new ResourceLayoutElementDescription("Samp", ResourceKind.Sampler, ShaderStages.Fragment)));
        using ResourceSet set = factory.CreateResourceSet(new ResourceSetDescription(layout, view, gd.PointSampler));
        uint targetSize = mode == "pending" ? 1024u : 4u;
        using Texture output = factory.CreateTexture(TextureDescription.Texture2D(targetSize, targetSize, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.RenderTarget));
        using Texture readback = factory.CreateTexture(TextureDescription.Texture2D(targetSize, targetSize, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging));
        using Framebuffer fb = factory.CreateFramebuffer(new FramebufferDescription(null, output));
        using Shader vs = factory.CreateShader(new ShaderDescription(ShaderStages.Vertex, Encoding.UTF8.GetBytes("""
            #version 330 core
            const vec2 positions[3] = vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
            void main() { gl_Position = vec4(positions[gl_VertexID],0,1); }
            """), "main"));
        using Shader fs = factory.CreateShader(new ShaderDescription(ShaderStages.Fragment, Encoding.UTF8.GetBytes("""
            #version 330 core
            uniform sampler2D Tex;
            out vec4 color;
            void main() { color = texture(Tex, vec2(0.5,0.5)); }
            """), "main"));
        using Pipeline pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
            BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
            PrimitiveTopology.TriangleList, new ShaderSetDescription([], [vs,fs]), [layout], fb.OutputDescription));
        using CommandList cl = factory.CreateCommandList();
        void Record()
        {
            cl.Begin(); cl.SetFramebuffer(fb); cl.SetFullViewports();
            cl.ClearColorTarget(0, RgbaFloat.Blue); cl.SetPipeline(pipeline); cl.SetGraphicsResourceSet(0, set);
            for (int draw = 0; draw < (mode == "pending" ? 128 : 1); draw++) cl.Draw(3);
            cl.CopyTexture(output, readback); cl.End();
        }
        Pixel Read()
        {
            var mapped = gd.Map<Pixel>(readback, MapMode.Read);
            Pixel value = mapped[0,0]; gd.Unmap(readback); return value;
        }
        Record(); gd.SubmitCommands(cl); info.ExecuteOnGLThread(() => {});
        Pixel baseline = Read();
        if (baseline.R != 0 || baseline.G != 255 || baseline.B != 0 || baseline.A != 255) throw new Exception($"Baseline draw failed: {baseline}");

        gd.UpdateTexture(source, new byte[] {255, 0, 0, 255}, 0, 0, 0, 1, 1, 1, 0, 0);
        Record();
        var wrapper = new VeldridTexture2D(source, set);
        var cpuBuffer = new ThreadBuffer();
        cpuBuffer.Add(new DrawTextureCmd { Texture = wrapper });
        using Fence fence = factory.CreateFence(false);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int disposedAtCallback = -1;
        uint syncBefore = 0, syncAfter = 0;
        bool disposedWhileSyncPending = false;
        bool nativeTextureDeleted = false;
        uint sourceName = 0;
        nint nativeSync = 0;
        nint syncLibrary = 0;
        GlFenceSync? fenceSync = null;
        GlClientWaitSync? pollSync = null;
        GlDeleteSync? deleteSync = null;
        GlFlush? flush = null;
        GlIsTexture? isTexture = null;
        if (mode == "pending")
        {
            syncLibrary = NativeLibrary.Load("libGL.so.1");
            fenceSync = Marshal.GetDelegateForFunctionPointer<GlFenceSync>(NativeLibrary.GetExport(syncLibrary, "glFenceSync"));
            pollSync = Marshal.GetDelegateForFunctionPointer<GlClientWaitSync>(NativeLibrary.GetExport(syncLibrary, "glClientWaitSync"));
            deleteSync = Marshal.GetDelegateForFunctionPointer<GlDeleteSync>(NativeLibrary.GetExport(syncLibrary, "glDeleteSync"));
            flush = Marshal.GetDelegateForFunctionPointer<GlFlush>(NativeLibrary.GetExport(syncLibrary, "glFlush"));
            isTexture = Marshal.GetDelegateForFunctionPointer<GlIsTexture>(NativeLibrary.GetExport(syncLibrary, "glIsTexture"));
        }
        Thread? producer = null;
        Task blocked = Task.Run(() =>
        {
          producer = Thread.CurrentThread;
          info.ExecuteOnGLThread(() =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Audit GL gate timed out");
        });
        });
        if (!entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("GL thread did not enter gate");
        if (!SpinWait.SpinUntil(() => producer is not null && (producer.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)))
        { release.Set(); throw new TimeoutException("CPU queue barrier was not posted before the draw"); }
        Task? ordered = null;
        bool fenceAlreadySignaled;
        try
        {
            // The blocked producer has already posted a CPU-queue barrier before this draw.
            // That barrier flushes the separate disposal queue when the GL gate opens.
            gd.SubmitCommands(cl, fence);
            fenceAlreadySignaled = fence.Signaled;
            if (!fenceAlreadySignaled) throw new Exception("Expected package fence to signal at enqueue");
            if (mode == "early") source.Dispose();
            else if (mode == "deferred") gd.DisposeWhenIdle(source);
            else
            {
                ordered = Task.Run(() => info.ExecuteOnGLThread(() =>
                {
                    // This callback is enqueued after the draw. It has reached GL execution, not just submission.
                    disposedAtCallback = source.IsDisposed ? 1 : 0;
                    if (mode == "pending")
                    {
                        sourceName = info.GetTextureName(source);
                        nativeSync = fenceSync!(0x9117, 0); // GL_SYNC_GPU_COMMANDS_COMPLETE
                        flush!();
                        syncBefore = pollSync!(nativeSync, 0, 0);
                    }
                    source.Dispose();
                }));
            }
        }
        finally { release.Set(); }
        blocked.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        if (mode == "deferred")
        {
            if (source.IsDisposed) throw new Exception("DisposeWhenIdle ran before the public idle/swap boundary");
            gd.SwapBuffers(); // Public WaitForIdle drains deferred disposals after the queued draw reaches GL.
        }
        if (ordered is not null) ordered.WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
        info.ExecuteOnGLThread(() =>
        {
            if (mode == "pending")
            {
                syncAfter = pollSync!(nativeSync, 0, 0);
                nativeTextureDeleted = isTexture!(sourceName) == 0;
                disposedWhileSyncPending = nativeTextureDeleted && source.IsDisposed && syncAfter == 0x911B; // GL_TIMEOUT_EXPIRED
            }
        });
        Pixel result = Read();
        if (mode == "pending")
        {
            info.ExecuteOnGLThread(() => deleteSync!(nativeSync));
            NativeLibrary.Free(syncLibrary);
            Console.WriteLine($"native_sync_before_delete=0x{syncBefore:x}, after_delete=0x{syncAfter:x}, native_texture_deleted={nativeTextureDeleted}, texture_disposed_while_GPU_pending={disposedWhileSyncPending}");
            if (!disposedWhileSyncPending) throw new Exception("Inconclusive pending-GPU test: work completed before observation");
        }
        bool red = result.R == 255 && result.G == 0 && result.B == 0 && result.A == 255;
        bool black = result.R == 0 && result.G == 0 && result.B == 0 && result.A == 255;
        bool staleCpuCommand = ReferenceEquals(cpuBuffer.GetTextureCommands()[0].Texture, wrapper) && wrapper.Texture.IsDisposed;
        if (!staleCpuCommand) throw new Exception("Expected unsubmitted engine buffer to retain the disposed texture reference");
        Console.WriteLine($"{mode}[{iteration}] fence_signaled_while_GL_blocked={fenceAlreadySignaled}, pixel={result}, disposed={source.IsDisposed}, disposed_at_callback={disposedAtCallback}, unsubmitted_buffer_keeps_disposed_texture={staleCpuCommand}");
        if (mode != "early" && (!red || (mode != "deferred" && disposedAtCallback != 0) || !source.IsDisposed)) throw new Exception("Ordered disposal failed");
        if (mode == "early" && !black) throw new Exception($"Negative control must reproduce opaque black, got {result}");
    }
    finally
    {
        gd.Dispose();
        window.Close();
    }
}

[StructLayout(LayoutKind.Sequential, Pack=1)]
struct Pixel
{
    public byte R, G, B, A;
    public override readonly string ToString() => $"{R},{G},{B},{A}";
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate nint GlGetString(uint name);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate nint GlFenceSync(uint condition, uint flags);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate uint GlClientWaitSync(nint sync, uint flags, ulong timeout);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void GlDeleteSync(nint sync);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate void GlFlush();

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
delegate byte GlIsTexture(uint name);
