namespace Karpik.Engine.Tooling;

/// <summary>Содержит коды завершения процесса редактора, понятные launcher'у.</summary>
public static class EditorExitCodes
{
    /// <summary>Редактор завершился, запросив передачу управления другой установке.</summary>
    public const int HandoffRequested = 20;
}
