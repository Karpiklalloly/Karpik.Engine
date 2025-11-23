namespace Karpik.Jobs;

internal static class ThreadLocalRandom
{
    [ThreadStatic] private static Random? _local;

    public static int Next(int min, int max)
    {
        if (_local == null) _local = new Random(UnsafeAdditiveHash(Environment.CurrentManagedThreadId));
        return _local.Next(min, max);
    }

    private static int UnsafeAdditiveHash(int input)
    {
        unchecked
        {
            int hash = 17;
            foreach (byte b in BitConverter.GetBytes(input)) hash = hash * 23 + b;
            return hash;
        }
    }
}