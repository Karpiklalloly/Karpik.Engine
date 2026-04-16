namespace Karpik.Engine.Client.Graphics.Core;

public interface ICommandBuffer
{
    public int FrameId { get; }
    public int Count { get; }
    public void Add(in DrawCommand cmd);
    internal void Clear();
    public ReadOnlySpan<DrawCommand> GetCommands();
}