namespace Karpik.Engine.Client.Graphics.Core;

public static class GraphicsContext
{
    private static int _currentFrameId;
    private static readonly Lock Lock = new();

    private static List<ICommandBuffer> _writeBuffers = new();
    private static List<ICommandBuffer> _pendingBuffers = new();
    [ThreadStatic] private static ThreadBuffer[]? _cachedBuffers;

    
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
