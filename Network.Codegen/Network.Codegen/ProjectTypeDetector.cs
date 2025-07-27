using System;

namespace Network.Codegen;

internal static class ProjectTypeDetector
{
    public enum ProjectType
    {
        Server,
        Client,
        Shared,
        Unknown
    }

    public static ProjectType DetectProjectType(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return ProjectType.Unknown;

        // Проверяем на серверный проект
        if (assemblyName.Contains("Server"))
            return ProjectType.Server;

        // Проверяем на клиентский проект
        if (assemblyName.Contains("Client"))
            return ProjectType.Client;

        // Проверяем на общий проект
        if (assemblyName.Contains("Shared"))
            return ProjectType.Shared;

        // Если не удалось определить тип
        return ProjectType.Unknown;
    }

    public static string GetGeneratedNamespace(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.Server => "Karpik.Engine.Server",
            ProjectType.Client => "Karpik.Engine.Client",
            _ => throw new ArgumentException($"Cannot generate namespace for project type: {projectType}")
        };
    }
    
    public static string GetGeneratedFileName(ProjectType projectType)
    {
        return projectType switch
        {
            ProjectType.Server => "TargetClientRpcSender.g.cs",
            ProjectType.Client => "TargetClientRpcDispatcher.g.cs",
            _ => throw new ArgumentException($"Cannot generate file name for project type: {projectType}")
        };
    }

    public static bool ShouldGenerateCode(ProjectType projectType)
    {
        return projectType == ProjectType.Server || projectType == ProjectType.Client;
    }
}