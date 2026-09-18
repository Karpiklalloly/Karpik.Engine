using Autofac;
using Karpik.Engine.Core;
using Karpik.Engine.Shared.Log;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karpik.Editor.Tests;

public sealed class EditorConsoleLoggerTests
{
    private static readonly object ConsoleGate = new();

    [Fact]
    public void LoggerModule_EmitsOneMarkedLineWhenEditorCaptureIsEnabled()
    {
        string output = CaptureOutput(editorCapture: true, logger =>
            logger.LogInformation("first{NewLine}second", Environment.NewLine));

        string line = Assert.Single(
            output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
            item => item.StartsWith(EditorConsoleLogProtocol.Prefix, StringComparison.Ordinal));

        Assert.True(EditorConsoleLogProtocol.TryParse(line, out EditorConsoleLogEvent? entry));
        Assert.Equal((int)LogLevel.Information, entry!.Level);
        Assert.Equal("first\r\nsecond", entry.Message);
    }

    [Fact]
    public void LoggerModule_DoesNotEmitMarkedLineWhenEditorCaptureIsDisabled()
    {
        string output = CaptureOutput(editorCapture: false, logger => logger.LogInformation("message"));

        Assert.DoesNotContain(EditorConsoleLogProtocol.Prefix, output, StringComparison.Ordinal);
    }

    private static string CaptureOutput(bool editorCapture, Action<ILogger> write)
    {
        lock (ConsoleGate)
        {
            string? previousCapture = Environment.GetEnvironmentVariable("KARPIK_EDITOR_LOG_CAPTURE");
            TextWriter previousOutput = Console.Out;
            using var output = new StringWriter();
            try
            {
                Environment.SetEnvironmentVariable("KARPIK_EDITOR_LOG_CAPTURE", editorCapture ? "1" : null);
                Console.SetOut(output);
                var builder = new ContainerBuilder();
                new LoggerModuleInstaller().OnRegisterServices(builder);
                using IContainer container = builder.Build();
                ILoggerFactory factory = container.Resolve<ILoggerFactory>();

                write(factory.CreateLogger("test"));
                return output.ToString();
            }
            finally
            {
                Console.SetOut(previousOutput);
                Environment.SetEnvironmentVariable("KARPIK_EDITOR_LOG_CAPTURE", previousCapture);
            }
        }
    }
}
