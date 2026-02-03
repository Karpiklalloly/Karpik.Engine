using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Karpik.Engine.Core.Runner")]
namespace Karpik.Engine.Core.ModuleManagement;

internal interface IRunner
{
    public void RegisterTypes(Type[] types);
    public void Setup(ServiceProvider serviceProvider, bool hotReload, MainThreadScheduler scheduler, Action releaseOld, Type[]? newTypes = null);
    public void Run(double dt);
    public void Destroy();
}