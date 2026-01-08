namespace Karpik.Engine.Core;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ModuleAttribute : Attribute
{
    public int Priority { get; }
    
    public ModuleAttribute(int priority = 0)
    {
        Priority = priority;
    }
}