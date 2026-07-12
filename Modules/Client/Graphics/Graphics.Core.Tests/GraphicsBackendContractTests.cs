using Karpik.Engine.Client.Graphics.Core;
using Xunit;

public sealed class GraphicsBackendContractTests
{
    [Fact]
    public void GraphicsBackendContract_DoesNotExposeVeldridTypes()
    {
        Type backendType = typeof(IGraphicsBackend);

        foreach (var method in backendType.GetMethods())
        {
            AssertNotVeldrid(method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                AssertNotVeldrid(parameter.ParameterType);
            }
        }
    }

    private static void AssertNotVeldrid(Type type)
    {
        if (type.Namespace?.StartsWith("Veldrid", StringComparison.Ordinal) == true)
        {
            throw new InvalidOperationException(
                $"{nameof(IGraphicsBackend)} must not expose Veldrid type '{type.FullName}'.");
        }
    }
}
