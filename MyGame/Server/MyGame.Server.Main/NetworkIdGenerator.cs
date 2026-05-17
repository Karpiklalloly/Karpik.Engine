namespace Karpik.Engine.MyGame.Server.Main;

public class NetworkIdGenerator
{
    private int _id = 0;
    
    public int Next()
    {
        return Interlocked.Increment(ref _id);
    }

    public int Current()
    {
        return _id;
    }

    public void EnsureAtLeast(int id)
    {
        var current = Volatile.Read(ref _id);
        while (current < id)
        {
            var previous = Interlocked.CompareExchange(ref _id, id, current);
            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}
