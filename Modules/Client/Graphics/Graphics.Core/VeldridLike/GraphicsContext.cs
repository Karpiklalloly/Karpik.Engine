using System.Buffers;
using System.Collections.Concurrent;

namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

// Контекст отрисовки. Существует в единственном экземпляре на кадр (Singleton/Injected)
public sealed class GraphicsContext : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly CommandListPool _pool;
    
    // Список списков, которые готовы к отправке на GPU в этом кадре
    private readonly ConcurrentQueue<ICommandList> _pendingSubmissions = new();

    public GraphicsContext(IGraphicsDevice device)
    {
        _device = device;
        _pool = new CommandListPool(device);
    }

    // Рабочий поток вызывает это, чтобы получить свой личный буфер
    public ICommandList BeginCommandList()
    {
        return _pool.Rent();
    }

    // Рабочий поток вызывает это, когда закончил запись
    public void SubmitCommandList(ICommandList list)
    {
        list.End();
        _pendingSubmissions.Enqueue(list);
    }

    // Главный поток вызывает это В КОНЦЕ КАДРА
    public void ExecuteFrame()
    {
        // 1. Собираем все комманд-листы из очереди (Zero-allocation через аренду массива)
        int count = _pendingSubmissions.Count;
        var listsToSubmit = ArrayPool<ICommandList>.Shared.Rent(count);
        
        int idx = 0;
        while (_pendingSubmissions.TryDequeue(out var list))
        {
            listsToSubmit[idx++] = list;
        }

        // 2. Отправляем на видеокарту батчом (Span)
        _device.Submit(new ReadOnlySpan<ICommandList>(listsToSubmit, 0, count));

        // 3. Возвращаем использованные листы обратно в пул для следующего кадра
        for (int i = 0; i < count; i++)
        {
            _pool.Return(listsToSubmit[i]);
        }

        ArrayPool<ICommandList>.Shared.Return(listsToSubmit);
    }

    public void Dispose()
    {
        _pool.Dispose();
    }
}