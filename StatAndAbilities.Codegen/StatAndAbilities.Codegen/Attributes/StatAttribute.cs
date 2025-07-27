using System;

namespace Karpik.StatAndAbilities.Codegen.Attributes;

public class StatAttribute : Attribute
{
    public const string AttributeName = "StatAttribute";
}

public class RangeStatAttribute : Attribute
{
    public const string AttributeName = "RangeStatAttribute";
}

public class EzRangeStatAttribute : Attribute
{
    public const string AttributeName = "EzRangeStatAttribute";
}