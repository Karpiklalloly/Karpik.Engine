namespace Karpik.Engine.Core;

public class Time
{
    private long _deltaTimeBits;
    private long _fixedDeltaTimeBits = BitConverter.DoubleToInt64Bits(Application.TICK_DT);
    private long _totalTimeBits;

    public double DeltaTime => BitConverter.Int64BitsToDouble(Volatile.Read(ref _deltaTimeBits));
    public double FixedDeltaTime
    {
        get => BitConverter.Int64BitsToDouble(Volatile.Read(ref _fixedDeltaTimeBits));
        internal set => Volatile.Write(ref _fixedDeltaTimeBits, BitConverter.DoubleToInt64Bits(value));
    }
    public double TotalTime => BitConverter.Int64BitsToDouble(Volatile.Read(ref _totalTimeBits));

    public bool IsPaused { get; set; }

    internal void Update(double deltaTime)
    {
        Volatile.Write(ref _deltaTimeBits, BitConverter.DoubleToInt64Bits(deltaTime));
        if (!IsPaused)
        {
            double totalTime = BitConverter.Int64BitsToDouble(Volatile.Read(ref _totalTimeBits));
            Volatile.Write(ref _totalTimeBits, BitConverter.DoubleToInt64Bits(totalTime + deltaTime));
        }
    }
}
