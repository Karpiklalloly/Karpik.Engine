using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridPipeline(Pipeline pipeline) : IPipeline, IDisposable
{
    public readonly Pipeline Pipeline = pipeline;

    public void Dispose() => Pipeline.Dispose();
}