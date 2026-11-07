using Autofac;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;

namespace Karpik.Engine.Core;

public partial class EngineRunner
{
    // Host-owned infrastructure is explicitly configured, not a module discovered in game metadata.
    [Module(ModuleScope.Engine, int.MaxValue)]
    private sealed class ContentReloadHostInstaller(string instanceId) : IModuleInstaller
    {
        public string Name => "Content.Reload.Host";
        public void OnRegisterServices(ContainerBuilder builder)
        {
            // Typed factories also work when Static composition suppresses reflected system registrations.
            builder.Register(context => new FileContentStore(context.Resolve<IFileSystem>()))
                .AsSelf().As<IContentStore>().SingleInstance();
            builder.Register(context => new ContentRegistry(context.Resolve<IFileSystem>(), context.Resolve<FileContentStore>(),
                    context.Resolve<IEnumerable<IContentPayloadLoader>>()))
                .AsSelf().As<IContentRegistry>().As<IContentUpdateRegistry>().As<IStartable>().SingleInstance();
            builder.Register(context => new ContentReloadCoordinator(context.Resolve<ContentRegistry>()))
                .AsSelf().As<IContentChangeSource>().As<IContentReloadLifetime>().SingleInstance();
            builder.Register(context => new ContentReloadHostRuntime(instanceId, context.Resolve<Application>(),
                    context.Resolve<IFileSystem>(), context.Resolve<FileContentStore>(), context.Resolve<ContentRegistry>(),
                    context.Resolve<ContentReloadCoordinator>()))
                .AsSelf().As<IStartable>().SingleInstance();
            builder.Register(context => new ContentReloadBoundarySystem(context.Resolve<ContentReloadCoordinator>(),
                context.Resolve<ContentReloadHostRuntime>())).AsSelf().SingleInstance();
        }
    }
}
