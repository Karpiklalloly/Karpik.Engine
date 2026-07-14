using Avalonia.Controls;
using Dock.Model.ReactiveUI.Controls;
using Karpik.Editor;
using ReactiveUI.Builder;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class DockContextDataTemplateTests
{
    static DockContextDataTemplateTests()
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();
    }

    [Fact]
    public void Build_ForDockableWithContext_PresentsTheContext()
    {
        var context = new object();
        var tool = new Tool { Context = context };
        var template = new DockContextDataTemplate();

        Assert.True(template.Match(tool));
        var content = Assert.IsType<ContentControl>(template.Build(tool));
        Assert.Same(context, content.Content);
    }
}
