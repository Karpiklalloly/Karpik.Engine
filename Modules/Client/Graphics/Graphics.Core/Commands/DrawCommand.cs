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
    public readonly ulong SortKey;
    public readonly int Sequence;

    public DrawCommand(DrawCommandType type, int index, ulong sortKey, int sequence)
    {
        Type = type;
        Index = index;
        SortKey = sortKey;
        Sequence = sequence;
    }

    public static bool IsGreaterThan(in DrawCommand left, in DrawCommand right)
    {
        if (left.SortKey != right.SortKey)
        {
            return left.SortKey > right.SortKey;
        }

        return left.Sequence > right.Sequence;
    }
}

internal interface IOrderedCommandBuffer
{
    ReadOnlySpan<DrawCommand> GetCommands();
}
