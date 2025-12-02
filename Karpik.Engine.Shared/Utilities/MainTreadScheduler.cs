using System.Collections.Concurrent;

namespace Karpik.Engine.Shared;

public class MainTreadScheduler : SynchronizationContext
{
    private readonly ConcurrentQueue<Action> _actions = new ConcurrentQueue<Action>();

    public override void Post(SendOrPostCallback d, object state)
    {
        _actions.Enqueue(() => d(state));
    }

    public override void Send(SendOrPostCallback d, object state)
    {
        _actions.Enqueue(() => d(state));
    }

    public void Execute()
    {
        while (_actions.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in MainThread: {ex}");
            }
        }
    }

    internal static void RunOnMainThread(Action action)
    {
        if (Current is MainTreadScheduler context)
        {
            context._actions.Enqueue(action);
        }
        else
        {
            action();
        }
    }
}