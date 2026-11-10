namespace Karpik.Engine.Shared.ECS.Scheduling;

/// <summary>Marks a system whose instances and metadata are registered explicitly through AddInstance.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RuntimeSystemAttribute : Attribute;
