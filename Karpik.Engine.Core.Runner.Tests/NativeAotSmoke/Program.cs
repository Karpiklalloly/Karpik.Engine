using Autofac;

var builder = new ContainerBuilder();
builder.Register(_ => new SmokeMarker()).As<ISmokeMarker>().SingleInstance();

using var container = builder.Build();
Console.WriteLine(container.Resolve<ISmokeMarker>().Value);

internal interface ISmokeMarker
{
    string Value { get; }
}

internal sealed class SmokeMarker : ISmokeMarker
{
    public string Value => "STATIC_AOT_OK";
}
