using Veldrid;

namespace Karpik.Engine.Client.Graphics.Core;

public sealed class VeldridShader(Shader shader) : IShader, IDisposable
{
    public readonly Shader Shader = shader;

    public void Dispose() => Shader.Dispose();
}