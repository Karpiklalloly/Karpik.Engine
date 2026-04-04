using System.Collections.Concurrent;

namespace Karpik.Engine.Client.Graphics.Core.VeldridLike;

// Этот класс управляет пулом для КАЖДОГО потока отдельно, чтобы избежать lock-ов (Lock-free подход)
public sealed class CommandListPool : IDisposable
{
    private readonly IGraphicsDevice _device;
    private readonly ConcurrentBag<ICommandList> _availableLists = new();

    public CommandListPool(IGraphicsDevice device)
    {
        _device = device;
    }

    public ICommandList Rent()
    {
        if (_availableLists.TryTake(out var list))
        {
            list.Begin(); // Автоматически начинаем запись
            return list;
        }

        // Если пул пуст, только тогда аллоцируем новый
        var newList = _device.CreateCommandList();
        newList.Begin();
        return newList;
    }

    public void Return(ICommandList list)
    {
        // Не вызываем End() здесь, он вызывается перед Submit
        _availableLists.Add(list);
    }

    public void Dispose()
    {
        foreach (var list in _availableLists) list.Dispose();
    }
}