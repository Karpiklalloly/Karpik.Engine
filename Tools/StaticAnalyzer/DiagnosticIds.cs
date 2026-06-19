namespace StaticAnalyzer;

public class DiagnosticIds
{
    public const string UseWorldInsteadOfEcsWorld = "K001";
    public const string DoNotUseDragonLifecycleAPI = "K002";
    public const string EcsOpaqueUpdateAccess = "K003";
    public const string EcsAccessSummaryContradiction = "K004";
    public const string EcsInvalidOrderCycle = "K005";
    public const string EcsMainThreadOnlyUpdateAccess = "K006";
    public const string EcsUnsupportedManagedComponentSummary = "K007";
    public const string EcsGeneratedRegistryMissingSystem = "K008";
}
