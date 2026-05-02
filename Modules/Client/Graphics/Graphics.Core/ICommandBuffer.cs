namespace Karpik.Engine.Client.Graphics.Core;

public interface ICommandBuffer
{
    public int FrameId { get; }
    public void Add(in DrawRectCmd cmd);
    public void Add(in DrawTextureCmd cmd);
    public void Add(in DrawTextCmd cmd);
    public ReadOnlySpan<DrawRectCmd> GetRectCommands();
    public ReadOnlySpan<DrawTextureCmd> GetTextureCommands();
    public ReadOnlySpan<DrawTextCmd> GetTextCommands();
    internal void Clear();
}
