namespace Karpik.Engine.Client.Graphics.Core;

public sealed class ThreadBuffer(int frameId) : ICommandBuffer
{
    private const int InitialCapacity = 256;
    
    public int FrameId { get; } = frameId;

    public int Count { get; private set; }

    private DrawCommand[] _commands = new DrawCommand[InitialCapacity];

    public void Add(in DrawCommand cmd)
    {
        AddCommand(cmd);
    }

    public void Clear()
    {
        Count = 0;
    }

    public ReadOnlySpan<DrawCommand> GetCommands()
    {
        return _commands;
    }
    
    private void AddCommand(DrawCommand cmd)
    {
        if (Count >= _commands.Length)
        {
            Array.Resize(ref _commands, _commands.Length * 2);
        }
        _commands[Count++] = cmd;
    }
}