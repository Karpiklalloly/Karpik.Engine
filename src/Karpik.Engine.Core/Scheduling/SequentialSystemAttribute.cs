namespace Karpik.Engine.Shared.ECS.Scheduling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class SequentialSystemAttribute : Attribute
{
}
