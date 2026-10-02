using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Generated;

return await StaticEngineHost.RunAsync(
    Side.Client,
    new GeneratedRuntimeComposition(),
    args);
