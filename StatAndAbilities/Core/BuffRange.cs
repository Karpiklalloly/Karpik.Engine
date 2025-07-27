using System;

namespace Karpik.StatAndAbilities
{
    [Flags]
    public enum BuffRange
    {
        Min = 1,
        Max = 2,
        Value = 4,
        All = Min | Max | Value
    }

    public static class BuffRangeExtensions
    {
        public static bool Flagged(this BuffRange range, BuffRange flag) => (range & flag) == flag;
    }
    
    [Flags]
    public enum BuffEzRange
    {
        Min = 1,
        Max = 2,
        All = Min | Max
    }

    public static class BuffEzRangeExtensions
    {
        public static bool Flagged(this BuffEzRange range, BuffEzRange flag) => (range & flag) == flag;
    }
}