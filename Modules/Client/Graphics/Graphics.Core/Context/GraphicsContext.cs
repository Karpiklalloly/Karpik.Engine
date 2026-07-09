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
    private static int _pendingSetIndex;
    [ThreadStatic] private static ThreadBuffer[]? _cachedBuffers;
    [ThreadStatic] private static bool _threadBufferResizeDisabled;

    
    public static ICommandBuffer Buffer
    {
        get
        {
            ThreadBuffer[] cachedBuffers = GetOrCreateCachedBuffers();

            int frameId = _currentFrameId;
            int bufferIndex = GetSetIndex(frameId);
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
            int previousWriteSetIndex = _writeSetIndex;
            _currentFrameId++;
            _pendingSetIndex = previousWriteSetIndex;
            _writeSetIndex = GetSetIndex(_currentFrameId);
            CommandSets[_writeSetIndex].Clear(_currentFrameId);
        }
    }

    internal static List<ICommandBuffer> CollectBuffers()
    {
        lock (Lock) return CommandSets[_pendingSetIndex].Buffers;
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

    private static int GetSetIndex(int frameId)
    {
        return frameId % CommandSetCount;
    }

    private sealed class CommandSet
    {
        public readonly List<ICommandBuffer> Buffers = new();
        public int FrameId { get; private set; } = -1;

        public void Clear(int frameId)
        {
            FrameId = frameId;
            Buffers.Clear();
        }
    }
}
