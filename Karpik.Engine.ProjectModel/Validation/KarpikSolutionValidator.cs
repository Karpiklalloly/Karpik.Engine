namespace Karpik.Engine.ProjectModel;

public sealed class KarpikSolutionValidator
{
    private const string RequiredSdkName = "Karpik.Engine.Sdk";

    public IReadOnlyList<KarpikDiagnostic> Validate(KarpikSolutionModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var diagnostics = new List<KarpikDiagnostic>();
        var solutionRoot = Path.GetDirectoryName(KarpikPathPolicy.Normalize(model.SolutionPath))!;
        var invalidProjects = ValidateSolutionProjects(model, solutionRoot, diagnostics);
        var projectsByPath = model.Projects
            .Where(project => !invalidProjects.Contains(project.ProjectPath))
            .GroupBy(project => project.ProjectPath, KarpikPathPolicy.Comparer)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), KarpikPathPolicy.Comparer);

        foreach (var project in projectsByPath.Values)
        {
            ValidateDeclarations(project, diagnostics);
        }
        ValidateReferences(projectsByPath, solutionRoot, diagnostics);
        ValidateCycles(projectsByPath, diagnostics, model.SolutionPath);
        return diagnostics;
    }

    private static HashSet<string> ValidateSolutionProjects(
        KarpikSolutionModel model,
        string solutionRoot,
        ICollection<KarpikDiagnostic> diagnostics)
    {
        var invalidProjects = new HashSet<string>(KarpikPathPolicy.Comparer);
        foreach (var project in model.Projects)
        {
            if (project.ReadStatus == KarpikProjectReadStatus.Success &&
                KarpikPathPolicy.IsWithinRoot(project.ProjectPath, solutionRoot) &&
                File.Exists(project.ProjectPath))
            {
                continue;
            }

            if (invalidProjects.Add(project.ProjectPath))
            {
                diagnostics.Add(new KarpikDiagnostic(
                    KarpikDiagnosticCodes.InvalidSolutionProject,
                    project.ProjectPath,
                    $"Solution project is missing, unreadable, or outside the solution root: {project.ProjectPath}"));
            }
        }

        foreach (var duplicate in model.Projects
                     .GroupBy(project => project.ProjectPath, KarpikPathPolicy.Comparer)
                     .Where(group => group.Count() > 1))
        {
            invalidProjects.Add(duplicate.Key);
            diagnostics.Add(new KarpikDiagnostic(
                KarpikDiagnosticCodes.InvalidSolutionProject,
                duplicate.Key,
                $"Solution project is duplicated: {duplicate.Key}"));
        }
        return invalidProjects;
    }

    private static void ValidateDeclarations(
        KarpikProjectDescriptor project,
        ICollection<KarpikDiagnostic> diagnostics)
    {
        if (!project.SdkNames.Contains(RequiredSdkName, StringComparer.OrdinalIgnoreCase))
        {
            diagnostics.Add(new KarpikDiagnostic(
                KarpikDiagnosticCodes.MissingSdk,
                project.ProjectPath,
                $"Project does not include {RequiredSdkName}."));
        }

        var kindIsValid = Enum.IsDefined(project.Kind);
        if (!kindIsValid)
        {
            diagnostics.Add(new KarpikDiagnostic(
                KarpikDiagnosticCodes.InvalidProjectKind,
                project.ProjectPath,
                "Project must declare a valid KarpikProjectKind."));
        }

        var sideIsValid = Enum.IsDefined(project.Side);
        if (!sideIsValid ||
            kindIsValid && project.Kind is KarpikProjectKind.Runtime or KarpikProjectKind.Test &&
            project.Side == KarpikProjectSide.None)
        {
            diagnostics.Add(new KarpikDiagnostic(
                KarpikDiagnosticCodes.InvalidProjectSide,
                project.ProjectPath,
                "Project must declare a valid KarpikSide for its project kind."));
        }
    }

    private static void ValidateReferences(
        IReadOnlyDictionary<string, KarpikProjectDescriptor> projectsByPath,
        string solutionRoot,
        ICollection<KarpikDiagnostic> diagnostics)
    {
        foreach (var project in projectsByPath.Values)
        {
            foreach (var referencePath in project.ProjectReferences)
            {
                if (!KarpikPathPolicy.IsWithinRoot(referencePath, solutionRoot) ||
                    !File.Exists(referencePath) ||
                    !projectsByPath.TryGetValue(referencePath, out var referencedProject))
                {
                    diagnostics.Add(new KarpikDiagnostic(
                        KarpikDiagnosticCodes.InvalidSolutionProject,
                        project.ProjectPath,
                        $"Project reference does not identify a readable project in the solution: {referencePath}"));
                    continue;
                }

                if (Enum.IsDefined(project.Side) &&
                    Enum.IsDefined(referencedProject.Side) &&
                    !IsSideAllowed(project.Side, referencedProject.Side))
                {
                    diagnostics.Add(new KarpikDiagnostic(
                        KarpikDiagnosticCodes.ForbiddenSideDependency,
                        project.ProjectPath,
                        $"{project.Side} project references forbidden {referencedProject.Side} project {referencePath}."));
                }
            }
        }
    }

    private static bool IsSideAllowed(KarpikProjectSide source, KarpikProjectSide target)
    {
        return source switch
        {
            KarpikProjectSide.Client => target is KarpikProjectSide.Client or KarpikProjectSide.Shared,
            KarpikProjectSide.Server => target is KarpikProjectSide.Server or KarpikProjectSide.Shared,
            KarpikProjectSide.Shared => target == KarpikProjectSide.Shared,
            KarpikProjectSide.None => target == KarpikProjectSide.None,
            _ => false
        };
    }

    private static void ValidateCycles(
        IReadOnlyDictionary<string, KarpikProjectDescriptor> projectsByPath,
        ICollection<KarpikDiagnostic> diagnostics,
        string solutionPath)
    {
        var incoming = projectsByPath.Keys.ToDictionary(path => path, _ => 0, KarpikPathPolicy.Comparer);
        var outgoing = projectsByPath.Keys.ToDictionary(path => path, _ => new List<string>(), KarpikPathPolicy.Comparer);
        foreach (var project in projectsByPath.Values)
        {
            foreach (var referencePath in project.ProjectReferences.Where(projectsByPath.ContainsKey))
            {
                outgoing[project.ProjectPath].Add(referencePath);
                incoming[referencePath]++;
            }
        }

        var ready = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var visited = 0;
        while (ready.Count > 0)
        {
            var path = ready.Dequeue();
            visited++;
            foreach (var referencedPath in outgoing[path])
            {
                incoming[referencedPath]--;
                if (incoming[referencedPath] == 0)
                {
                    ready.Enqueue(referencedPath);
                }
            }
        }

        if (visited != projectsByPath.Count)
        {
            var cycleProjects = incoming.Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .OrderBy(path => path, KarpikPathPolicy.Comparer);
            diagnostics.Add(new KarpikDiagnostic(
                KarpikDiagnosticCodes.ProjectReferenceCycle,
                solutionPath,
                $"Project-reference graph contains a cycle: {string.Join(", ", cycleProjects)}."));
        }
    }
}
