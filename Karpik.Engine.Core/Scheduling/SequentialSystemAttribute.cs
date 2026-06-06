namespace Karpik.Engine.Shared.ECS.Scheduling;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SequentialSystemAttribute : Attribute
{
}
