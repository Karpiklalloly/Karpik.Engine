namespace Karpik.Engine.Client.Graphics.Core;

public static class GraphicsContext
{
    private static int _currentFrameId;
    private static readonly Lock Lock = new();

    private static readonly List<ICommandBuffer> _allBuffers = new();
    [ThreadStatic] private static ICommandBuffer? _cachedBuffer;

    
    public static ICommandBuffer Buffer
    {
        get
        {
            if (_cachedBuffer != null && _cachedBuffer.FrameId == _currentFrameId)
            {
                return _cachedBuffer;
            }

            _cachedBuffer = new ThreadBuffer(_currentFrameId);
            lock (Lock)
            {
                _allBuffers.Add(_cachedBuffer);
            }
            return _cachedBuffer;
        }
    }

    internal static void BeginFrame()
    {
        lock (Lock)
        {
            _currentFrameId++;
            foreach (var buffer in _allBuffers) buffer.Clear();
            _allBuffers.Clear();
        }
    }

    internal static List<ICommandBuffer> CollectBuffers()
    {
        lock (Lock) return _allBuffers;
    }
}