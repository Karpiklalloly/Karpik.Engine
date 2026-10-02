using Karpik.Engine.Core;
using Karpik.Engine.Core.Runner;
using Karpik.Engine.Generated;

return await StaticEngineHost.RunAsync(
    Side.Server,
    new GeneratedRuntimeComposition(),
    args);
