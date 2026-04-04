namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

public interface IGraphicsResource : IDisposable
{
    nint NativeHandle { get; }
    void SetDebugName(ReadOnlySpan<char> name); 
}