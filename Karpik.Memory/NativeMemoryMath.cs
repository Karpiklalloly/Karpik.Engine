namespace Karpik.Memory;

internal static class NativeMemoryMath
{
    public static nuint AlignUp(nuint value, nuint alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    public static nuint PowerOfTwoAtLeast(nuint value)
    {
        if (value <= 1)
        {
            return 1;
        }

        value--;
        for (int shift = 1; shift < nuint.Size * 8; shift <<= 1)
        {
            value |= value >> shift;
        }

        return value + 1;
    }
}
