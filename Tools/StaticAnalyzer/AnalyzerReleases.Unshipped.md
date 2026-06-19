; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
K001 | ECS | Error | UseWorldAnalyzer
K002 | ECS | Error | DoNotUseDragonLifecycleAnalyzer
K003 | ECS | Error | EcsOpaqueUpdateAccess
K004 | ECS | Error | EcsAccessSummaryContradiction
K005 | ECS | Error | EcsInvalidOrderCycle
K006 | ECS | Error | EcsMainThreadOnlyUpdateAccess
K007 | ECS | Error | EcsUnsupportedManagedComponentSummary
K008 | ECS | Error | EcsGeneratedRegistryMissingSystem
