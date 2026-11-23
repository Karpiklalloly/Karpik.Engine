namespace Karpik.Engine.Core;

public enum ModContentRefreshStatus { Unavailable, Applied, Busy, Failed }
public readonly record struct ModContentRefreshResult(ModContentRefreshStatus Status, string? Error = null);
