using System.Reflection;
using Karpik.Editor;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorProcessExitContractTests
{
    [Fact]
    public void MainReturnsTheAvaloniaDesktopLifetimeExitCode()
    {
        Type program = typeof(App).Assembly.GetType("Karpik.Editor.Program", throwOnError: true)!;
        MethodInfo main = program.GetMethod(
            "Main",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        Assert.Equal(typeof(int), main.ReturnType);
    }
}
