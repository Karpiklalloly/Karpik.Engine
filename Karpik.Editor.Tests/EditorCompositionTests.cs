using Autofac;
using Karpik.Editor;
using ReactiveUI.Builder;
using System.ComponentModel.Composition;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorCompositionTests
{
    static EditorCompositionTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
    }

    [Fact]
    public void ContainerInjectsEditorViewModels()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}");
        var store = new WorkspaceStore(Path.Combine(directory, "workspace.json"));
        using IContainer container = App.CreateEditorContainer(
            new EditorStartupOptions(null, null), store, new EditorLogArchive(directory));
        var shell = container.Resolve<EditorShellViewModel>();

        Assert.Same(container.Resolve<ProjectViewModel>(), shell.Project);
        Assert.Same(container.Resolve<HierarchyViewModel>(), shell.Hierarchy);
        Assert.Same(container.Resolve<InspectorViewModel>(), shell.Inspector);
        Assert.Same(container.Resolve<ConsoleViewModel>(), shell.Console);
        Assert.Same(container.Resolve<PreviewViewModel>(), shell.Preview);
        Assert.Same(shell, container.Resolve<IActiveProjectPublisher>());
        Assert.IsType<ProjectOpenService>(container.Resolve<IProjectOpenService>());
    }

    [Fact]
    public void ExportedViewModelsAndManualConfigurationResolveThroughConstructors()
    {
        Assert.NotEmpty(typeof(EditorShellViewModel).GetCustomAttributes(typeof(ExportAttribute), false));
        Assert.NotEmpty(typeof(ProjectViewModel).GetCustomAttributes(typeof(ExportAttribute), false));

        string directory = Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}");
        var settings = new ProbeSettings("custom");
        var project = new ProjectViewModel();
        using IContainer container = App.CreateEditorContainer(
            new EditorStartupOptions(null, null),
            new WorkspaceStore(Path.Combine(directory, "workspace.json")),
            new EditorLogArchive(directory),
            configure: builder =>
            {
                builder.RegisterInstance(settings);
                builder.RegisterType<ConfiguredProbe>();
                builder.RegisterInstance(project);
            });

        Assert.Same(settings, container.Resolve<ConfiguredProbe>().Settings);
        Assert.Same(project, container.Resolve<EditorShellViewModel>().Project);
    }

    [Fact]
    public void CreationPolicyControlsExportLifetime()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"editor-{Guid.NewGuid():N}");
        using IContainer container = App.CreateEditorContainer(
            new EditorStartupOptions(null, null),
            new WorkspaceStore(Path.Combine(directory, "workspace.json")),
            new EditorLogArchive(directory),
            additionalAssemblies: [typeof(NonSharedProbe).Assembly]);

        Assert.NotSame(container.Resolve<INonSharedProbe>(), container.Resolve<INonSharedProbe>());
        Assert.Same(container.Resolve<ISharedProbe>(), container.Resolve<ISharedProbe>());
        Assert.Same(container.Resolve<IAnyProbe>(), container.Resolve<IAnyProbe>());
    }

    private sealed record ProbeSettings(string Value);
    private sealed class ConfiguredProbe(ProbeSettings settings)
    {
        public ProbeSettings Settings { get; } = settings;
    }

    public interface INonSharedProbe { }
    public interface ISharedProbe { }
    public interface IAnyProbe { }

    [Export(typeof(INonSharedProbe))]
    [PartCreationPolicy(CreationPolicy.NonShared)]
    public sealed class NonSharedProbe : INonSharedProbe { }

    [Export(typeof(ISharedProbe))]
    [PartCreationPolicy(CreationPolicy.Shared)]
    public sealed class SharedProbe : ISharedProbe { }

    [Export(typeof(IAnyProbe))]
    [PartCreationPolicy(CreationPolicy.Any)]
    public sealed class AnyProbe : IAnyProbe { }
}
