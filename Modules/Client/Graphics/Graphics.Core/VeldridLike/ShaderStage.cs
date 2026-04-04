namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

[Flags]
public enum ShaderStage : byte
{
    Vertex = 1 << 0,
    Fragment = 1 << 1,
    Compute = 1 << 2
}