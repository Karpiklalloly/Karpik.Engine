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
}