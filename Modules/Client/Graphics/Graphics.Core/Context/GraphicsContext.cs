namespace Karpik.Engine.Client.Graphics.Core;

public static class GraphicsContext
{
    private static int _currentFrameId;
    private static readonly Lock Lock = new();

    private static List<ICommandBuffer> _writeBuffers = new();
    private static List<ICommandBuffer> _pendingBuffers = new();
    [ThreadStatic] private static ThreadBuffer[]? _cachedBuffers;
    [ThreadStatic] private static bool _threadBufferResizeDisabled;

    
    public static ICommandBuffer Buffer
    {
        get
        {
            var cachedBuffers = _cachedBuffers;
            if (cachedBuffers == null)
            {
                cachedBuffers = new ThreadBuffer[2];
                _cachedBuffers = cachedBuffers;
            }

            int frameId = _currentFrameId;
            int bufferIndex = frameId & 1;
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
                _writeBuffers.Add(buffer);
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
        var cachedBuffers = _cachedBuffers;
        if (cachedBuffers == null)
        {
            cachedBuffers = new ThreadBuffer[2];
            _cachedBuffers = cachedBuffers;
        }

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

    internal static void BeginFrame()
    {
        lock (Lock)
        {
            _currentFrameId++;
            (_pendingBuffers, _writeBuffers) = (_writeBuffers, _pendingBuffers);
            _writeBuffers.Clear();
        }
    }

    internal static List<ICommandBuffer> CollectBuffers()
    {
        lock (Lock) return _pendingBuffers;
    }
}
