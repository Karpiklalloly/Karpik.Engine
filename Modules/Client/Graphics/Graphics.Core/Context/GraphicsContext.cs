namespace Karpik.Engine.Client.Graphics.Core;

public static class GraphicsContext
{
    private const int CommandSetCount = 3;

    private static int _currentFrameId;
    private static readonly Lock Lock = new();

    private static readonly CommandSet[] CommandSets =
    [
        new CommandSet(),
        new CommandSet(),
        new CommandSet()
    ];

    private static int _writeSetIndex;
    private static int _readySetIndex = -1;
    [ThreadStatic] private static ThreadBuffer[]? _cachedBuffers;
    [ThreadStatic] private static bool _threadBufferResizeDisabled;

    static GraphicsContext()
    {
        CommandSets[_writeSetIndex].BeginWrite(_currentFrameId);
    }

    
    public static ICommandBuffer Buffer
    {
        get
        {
            ThreadBuffer[] cachedBuffers = GetOrCreateCachedBuffers();

            int frameId = Volatile.Read(ref _currentFrameId);
            int bufferIndex = Volatile.Read(ref _writeSetIndex);
            if (bufferIndex < 0)
            {
                throw new InvalidOperationException("No writable graphics command set is available for this frame.");
            }
            var buffer = cachedBuffers[bufferIndex];
            if (buffer != null && buffer.FrameId == frameId)
            {
                return buffer;
            }

            if (buffer == null)
            {
                buffer = new ThreadBuffer();
                buffer.AllowResize = !_threadBufferResizeDisabled;
                cachedBuffers[bufferIndex] = buffer;
            }

            buffer.BeginFrame(frameId);
            lock (Lock)
            {
                CommandSets[_writeSetIndex].Buffers.Add(buffer);
            }
            return buffer;
        }
    }

    public static void EnsureThreadBufferCapacity(int rects, int textures, int texts)
    {
        EnsureThreadBufferCapacity(rects, textures, texts, textChars: 0);
    }

    public static void EnsureThreadBufferCapacity(int rects, int textures, int texts, int textChars)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rects);
        ArgumentOutOfRangeException.ThrowIfNegative(textures);
        ArgumentOutOfRangeException.ThrowIfNegative(texts);
        ArgumentOutOfRangeException.ThrowIfNegative(textChars);

        int commands = checked(rects + textures + texts);
        ThreadBuffer[] cachedBuffers = GetOrCreateCachedBuffers();

        for (int i = 0; i < cachedBuffers.Length; i++)
        {
            ThreadBuffer? buffer = cachedBuffers[i];
            if (buffer == null)
            {
                buffer = new ThreadBuffer();
                buffer.AllowResize = !_threadBufferResizeDisabled;
                cachedBuffers[i] = buffer;
            }

            buffer.EnsureCapacity(rects, textures, texts, commands, textChars);
        }
    }

    public static void SetThreadBufferAutoResize(bool allowResize)
    {
        _threadBufferResizeDisabled = !allowResize;
        var cachedBuffers = _cachedBuffers;
        if (cachedBuffers == null)
        {
            return;
        }

        for (int i = 0; i < cachedBuffers.Length; i++)
        {
            ThreadBuffer? buffer = cachedBuffers[i];
            if (buffer != null)
            {
                buffer.AllowResize = allowResize;
            }
        }
    }

    public static void BeginFrame()
    {
        lock (Lock)
        {
            if (_writeSetIndex >= 0)
            {
                if (_readySetIndex >= 0)
                {
                    CommandSets[_readySetIndex].Release();
                }

                CommandSets[_writeSetIndex].Publish();
                _readySetIndex = _writeSetIndex;
            }

            int nextWriteSetIndex = FindFreeSetIndex();
            if (nextWriteSetIndex < 0)
            {
                _writeSetIndex = -1;
                return;
            }

            _currentFrameId++;
            _writeSetIndex = nextWriteSetIndex;
            CommandSets[_writeSetIndex].BeginWrite(_currentFrameId);
        }
    }

    internal static bool TryAcquireMergeBuffers(out int commandSetIndex, out List<ICommandBuffer>? buffers)
    {
        lock (Lock)
        {
            if (_readySetIndex < 0)
            {
                commandSetIndex = -1;
                buffers = null;
                return false;
            }

            commandSetIndex = _readySetIndex;
            _readySetIndex = -1;
            CommandSets[commandSetIndex].AcquireForMerge();
            buffers = CommandSets[commandSetIndex].Buffers;
            return true;
        }
    }

    internal static void ReleaseMergeBuffers(int commandSetIndex)
    {
        lock (Lock)
        {
            CommandSets[commandSetIndex].Release();
        }
    }

    private static int FindFreeSetIndex()
    {
        for (int i = 0; i < CommandSetCount; i++)
        {
            if (CommandSets[i].State == CommandSetState.Free)
            {
                return i;
            }
        }

        return -1;
    }

    private static ThreadBuffer[] GetOrCreateCachedBuffers()
    {
        var cachedBuffers = _cachedBuffers;
        if (cachedBuffers == null || cachedBuffers.Length != CommandSetCount)
        {
            cachedBuffers = new ThreadBuffer[CommandSetCount];
            _cachedBuffers = cachedBuffers;
        }

        return cachedBuffers;
    }

    private sealed class CommandSet
    {
        public readonly List<ICommandBuffer> Buffers = new();
        public int FrameId { get; private set; } = -1;
        public CommandSetState State { get; private set; } = CommandSetState.Free;

        public void BeginWrite(int frameId)
        {
            FrameId = frameId;
            Buffers.Clear();
            State = CommandSetState.Writing;
        }

        public void Publish()
        {
            State = CommandSetState.Ready;
        }

        public void AcquireForMerge()
        {
            State = CommandSetState.Merging;
        }

        public void Release()
        {
            State = CommandSetState.Free;
        }
    }

    private enum CommandSetState : byte
    {
        Free,
        Writing,
        Ready,
        Merging
    }
}
