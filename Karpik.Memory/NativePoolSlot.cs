namespace Karpik.Memory;

internal struct NativePoolSlot
{
    public int NextFree;
    public int Generation;
    public int Rented;
}
