namespace Karpik.Engine.Client.Graphics.Core;

internal enum DrawCommandType : byte
{
    Rect,
    Texture,
    Text
}

internal readonly struct DrawCommand
{
    public readonly DrawCommandType Type;
    public readonly int Index;

    public DrawCommand(DrawCommandType type, int index)
    {
        Type = type;
        Index = index;
    }
}

internal interface IOrderedCommandBuffer
{
    ReadOnlySpan<DrawCommand> GetCommands();
}
