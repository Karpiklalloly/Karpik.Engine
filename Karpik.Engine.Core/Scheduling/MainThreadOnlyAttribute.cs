namespace Karpik.Engine.Shared.ECS.Scheduling;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class MainThreadOnlyAttribute : Attribute
{
}
