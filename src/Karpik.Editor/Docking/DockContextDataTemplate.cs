using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Karpik.Editor;

public sealed class DockContextDataTemplate : IDataTemplate
{
    public bool Match(object? data) =>
        data is IDockable { Context: not null }
        && (data is ITool or IDocument);

    public Control? Build(object? data) => data is IDockable { Context: { } context }
        ? new ContentControl { Content = context }
        : null;
}
