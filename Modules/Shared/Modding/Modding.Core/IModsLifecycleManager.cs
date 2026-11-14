using Karpik.Engine.Core;
using Microsoft.Extensions.Logging;
﻿using Karpik.Jobs;

namespace Karpik.Engine.Shared.Modding;

public interface IModsLifecycleManager
{
    public IReadOnlyList<ModContainer> Containers { get; }
    
    /// <summary>Creates and loads an inactive replacement without publishing any containers.</summary>
    public JobHandle<ModContainer[]> PrepareReload(IReadOnlyList<ModDefinition> definitions, ExecutionSide side,
        MainThreadScheduler? scheduler, ILogger logger, CancellationToken cancellationToken = default) =>
        JobHandle<ModContainer[]>.FromException(new NotSupportedException("This mod lifecycle does not support code reload."));

    /// <summary>Publishes already prepared containers. No callbacks run in this operation.</summary>
    public ModContainer[] ReplaceForReload(ModContainer[] containers) =>
        throw new NotSupportedException("This mod lifecycle does not support code reload.");

    public JobHandle Load(IReadOnlyList<ModDefinition> definitions, ExecutionSide side,
        CancellationToken cancellationToken = default);
}
