using Microsoft.CodeAnalysis;

namespace StaticAnalyzer.ECS;

public static class SchedulerDiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor OpaqueUpdateAccess = new(
        id: DiagnosticIds.EcsOpaqueUpdateAccess,
        title: "Opaque ECS access in update system",
        messageFormat: "Update system '{0}' contains ECS access that cannot be proven or summarized",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AccessSummaryContradiction = new(
        id: DiagnosticIds.EcsAccessSummaryContradiction,
        title: "ECS access summary contradicts inferred access",
        messageFormat: "Update system or helper '{0}' declares ECS access that contradicts inferred access",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidOrderCycle = new(
        id: DiagnosticIds.EcsInvalidOrderCycle,
        title: "Invalid ECS update order cycle",
        messageFormat: "Update system order edge '{0}' creates a cycle",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MainThreadOnlyUpdateAccess = new(
        id: DiagnosticIds.EcsMainThreadOnlyUpdateAccess,
        title: "Main-thread-only API used from update system",
        messageFormat: "Update system '{0}' uses main-thread-only API from scheduled update code",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnsupportedManagedComponentSummary = new(
        id: DiagnosticIds.EcsUnsupportedManagedComponentSummary,
        title: "Unsupported managed ECS component in access summary",
        messageFormat: "Access summary '{0}' targets a component type that is not supported by the scheduler",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GeneratedRegistryMissingSystem = new(
        id: DiagnosticIds.EcsGeneratedRegistryMissingSystem,
        title: "Generated ECS scheduler registry missing system",
        messageFormat: "Generated scheduler registry is missing registered update system '{0}'",
        category: "ECS",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
