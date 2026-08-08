namespace Karpik.Engine.Core;

[AttributeUsage(
    AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = false)]
public sealed class ServiceRegistrationAttribute : Attribute
{
    public ModuleScope Scope { get; }
    public ServiceLifetime Lifetime { get; }

    public ServiceRegistrationAttribute(
        ModuleScope scope,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        Scope = scope;
        Lifetime = lifetime;
    }
}