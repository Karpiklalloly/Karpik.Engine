namespace Network.Codegen;

internal static class ProjectTypeDetector
{
    public enum ProjectType
    {
        Server,
        Client,
        Shared,
        Module,
        Unknown
    }

    public static ProjectType DetectProjectType(string assemblyName)
    {
        if (string.IsNullOrEmpty(assemblyName))
            return ProjectType.Unknown;

        if (assemblyName.EndsWith("MyGame.Server.Main"))
            return ProjectType.Server;

        if (assemblyName.EndsWith("MyGame.Client.Main"))
            return ProjectType.Client;
        
        if (assemblyName.EndsWith("MyGame.Shared.Main"))
            return ProjectType.Shared;
        
        return ProjectType.Module;
    }
    
    public static string GetGeneratedNamespace(string assemblyName)
    {
        return $"{assemblyName}.Generated";
    }
}