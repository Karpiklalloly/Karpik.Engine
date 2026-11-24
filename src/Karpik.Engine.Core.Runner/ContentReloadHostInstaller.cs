using Autofac;
using Autofac.Core;
using Karpik.Content.Runtime;
using Karpik.Engine.Core.FileSystem;
using Karpik.Engine.Core.Runner;

namespace Karpik.Engine.Core;

public partial class EngineRunner
{
    // Host-owned infrastructure is explicitly configured, not a module discovered in game metadata.
    [Module(ModuleScope.Engine, int.MaxValue)]
    private sealed class ContentReloadHostInstaller(string instanceId, Action<Karpik.Content.Core.ContentDiagnostic> report) : IModuleInstaller
    {
        public string Name => "Content.Reload.Host";
        public void OnRegisterServices(ContainerBuilder builder)
        {
            // Minimal development hosts can opt in without installing the Content module.
            builder.Register(context => new FileContentStore(context.Resolve<IFileSystem>()))
                .AsSelf().As<IContentStore>().SingleInstance()
                .OnlyIf(registry => !registry.IsRegistered(new TypedService(typeof(IContentStore))));
            builder.Register(context => new ContentRegistry(context.Resolve<IFileSystem>(), context.Resolve<IContentStore>(),
                    context.Resolve<IEnumerable<IContentPayloadLoader>>(), context.Resolve<DeferredContentStartup>()))
                .AsSelf().As<IContentRegistry>().As<IContentUpdateRegistry>().As<IStartable>().SingleInstance()
                .OnlyIf(registry => !registry.IsRegistered(new TypedService(typeof(ContentRegistry))));
            builder.Register(context => new ContentReloadHostRuntime(instanceId, context.Resolve<Application>(),
                    context.Resolve<IFileSystem>(), context.Resolve<IContentStore>(),
                    context.Resolve<ContentReloadCoordinator>(), report))
                .AsSelf().SingleInstance();
        }
    }
}
