using System.ComponentModel.Composition;
using System.Reflection;
using Autofac;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Karpik.Editor;

public sealed partial class App : Application
{
    private IContainer? _container;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            EditorStartupOptions options = EditorStartupOptions.Parse(desktop.Args);
            _container = CreateEditorContainer(options);
            desktop.MainWindow = _container.Resolve<MainWindow>();
            desktop.Exit += (_, _) => _container.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static IContainer CreateEditorContainer(
        EditorStartupOptions options,
        WorkspaceStore? workspaceStore = null,
        EditorLogArchive? logArchive = null,
        Action<ContainerBuilder>? configure = null,
        IEnumerable<Assembly>? additionalAssemblies = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var builder = new ContainerBuilder();
        builder.RegisterInstance(options);
        builder.RegisterInstance(workspaceStore ?? WorkspaceStore.CreateDefault());
        builder.RegisterInstance(logArchive ?? new EditorLogArchive());
        foreach (Assembly assembly in (additionalAssemblies ?? []).Prepend(typeof(App).Assembly).Distinct())
        {
            foreach (Type type in assembly.GetTypes())
            {
                ExportAttribute[] exports = type.GetCustomAttributes<ExportAttribute>().ToArray();
                if (exports.Length == 0)
                {
                    continue;
                }

                if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
                {
                    throw new InvalidOperationException($"Editor export '{type.FullName}' must be a concrete, closed class.");
                }

                var registration = builder.RegisterType(type);
                foreach (ExportAttribute export in exports)
                {
                    registration.As(export.ContractType ?? type);
                }

                switch (type.GetCustomAttribute<PartCreationPolicyAttribute>()?.CreationPolicy ?? CreationPolicy.Any)
                {
                    case CreationPolicy.Any:
                    case CreationPolicy.Shared:
                        registration.SingleInstance();
                        break;
                    case CreationPolicy.NonShared:
                        registration.InstancePerDependency();
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported creation policy on '{type.FullName}'.");
                }
            }
        }

        builder.Register(context => EditorShellViewModel.CreateProjectOpenService(context.Resolve<WorkspaceStore>()))
            .As<IProjectOpenService>().SingleInstance();
        builder.Register(context => EditorShellViewModel.CreateProjectHandoffService(context.Resolve<EditorStartupOptions>()))
            .As<IProjectHandoffService>().SingleInstance();
        builder.RegisterType<MainWindow>().SingleInstance();
        configure?.Invoke(builder);
        return builder.Build();
    }
}
