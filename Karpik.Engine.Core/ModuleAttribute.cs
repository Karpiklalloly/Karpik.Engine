namespace Karpik.Engine.Core;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ModuleAttribute : Attribute
{
    public int Priority { get; }
    public ModuleScope Scope { get; }
    
    public ModuleAttribute(ModuleScope scope, int priority = 0)
    {
        Scope = scope;
        Priority = priority;
    }
}