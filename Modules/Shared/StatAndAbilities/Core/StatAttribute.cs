using System;

namespace Karpik.Engine.Shared.StatAndAbilities
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class StatAttribute : Attribute
    {
        public string Name = string.Empty;
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public class RangeStatAttribute : Attribute
    {
        public string Name = string.Empty;
    }
    
    [AttributeUsage(AttributeTargets.Struct)]
    public class EzRangeStatAttribute : Attribute
    {
        public string Name = string.Empty;
    }
}