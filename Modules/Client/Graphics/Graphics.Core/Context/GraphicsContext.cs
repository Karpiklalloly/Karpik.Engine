namespace Karpik.Engine.Client.Graphics.Core;

public class GraphicsContext
{
    private static int _currentFrameId;
    private static readonly Lock Lock = new();
    private static readonly Lock FrameLock = new();
    
    private static readonly Dictionary<int, ICommandBuffer> _buffersByThread = new();
    [ThreadStatic] private static ICommandBuffer? _buffer;

    
    public static ICommandBuffer Buffer
    {
        get
        {
            int threadId = Environment.CurrentManagedThreadId;
            
            if (_buffer == null || _buffer.FrameId != _currentFrameId)
            {
                _buffer = new ThreadBuffer(_currentFrameId);

                lock (Lock)
                {
                    _buffersByThread[threadId] = _buffer;
                }
            }
            return _buffer;
        }
    }

    internal static void BeginFrame()
    {
        lock (FrameLock)
        {
            _currentFrameId++;
            
            lock (Lock)
            {
                foreach (var buffer in _buffersByThread)
                {
                    buffer.Value.Clear();
                }
                _buffersByThread.Clear();
            }
        }
    }

    internal static ICommandBuffer[] CollectBuffers()
    {
        lock (Lock)
        {
            return _buffersByThread.Values.ToArray();
        }
    }
}