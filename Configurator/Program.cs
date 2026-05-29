using System.Text;
using System.Xml.Linq;

namespace ProjectConfigurator;

public class ModuleInfo
{
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string SelectedImplementation { get; set; } = "";
    public string? CorePath { get; set; }
    public List<string> Implementations { get; set; } = new();
    public Dictionary<string, string> ImplementationPaths { get; set; } = new();
}

class Program
{
    const string CONFIG_LABEL = "ProjectConfigurator";

    enum ProjectSide
    {
        Unknown,
        Shared,
        Client,
        Server
    }

    static int Main(string[] args)
    {
        var rootPath = FindSolutionRoot(Directory.GetCurrentDirectory());
        if (rootPath == null) { Console.WriteLine("Root not found (no .slnx)"); return 1; }

        var slnxPath = Directory.GetFiles(rootPath, "*.slnx").FirstOrDefault()!;
        var propsPath = Path.Combine(rootPath, "Directory.Build.props");

        if (args.Contains("--validate"))
        {
            return ValidateProjectReferences(rootPath, slnxPath);
        }

        var modules = ScanSlnx(slnxPath);
        LoadConfig(propsPath, modules);

        // Auto-generate mode (non-interactive)
        if (args.Contains("--generate") || args.Contains("-g"))
        {
            GenerateFiles(rootPath, modules, slnxPath);
            Console.WriteLine("Files generated successfully.");
            return 0;
        }

        // --- UI LOOP START ---
        bool running = true;
        int selectedIndex = 0;
        while (running)
        {
            Console.Clear();
            Console.WriteLine($"CONFIGURATOR | {Path.GetFileName(slnxPath)}");
            Console.WriteLine("[SPACE]: Toggle | [ENTER]: Switch Impl | [S]: Save | [Q]: Quit\n");

            for (int i = 0; i < modules.Count; i++)
            {
                var m = modules[i];
                Console.ForegroundColor = (i == selectedIndex) ? ConsoleColor.Yellow : ConsoleColor.Gray;
                Console.Write((i == selectedIndex) ? " > " : "   ");
                string check = m.IsEnabled ? "[x]" : "[ ]";
                Console.Write($"{check} {m.Name.PadRight(30)}");
                
                if (m.Implementations.Any())
                {
                    Console.ForegroundColor = m.IsEnabled ? ConsoleColor.Green : ConsoleColor.DarkGray;
                    Console.Write($" < {m.SelectedImplementation} >");
                }
                Console.WriteLine();
            }
            Console.ResetColor();

            var key = Console.ReadKey(true).Key;
            if (key == ConsoleKey.UpArrow) selectedIndex = Math.Max(0, selectedIndex - 1);
            else if (key == ConsoleKey.DownArrow) selectedIndex = Math.Min(modules.Count - 1, selectedIndex + 1);
            else if (key == ConsoleKey.Spacebar) modules[selectedIndex].IsEnabled = !modules[selectedIndex].IsEnabled;
            else if (key == ConsoleKey.Enter) {
                var m = modules[selectedIndex];
                if (m.Implementations.Any()) {
                    int idx = m.Implementations.IndexOf(m.SelectedImplementation);
                    m.SelectedImplementation = m.Implementations[(idx + 1) % m.Implementations.Count];
                }
            }
            else if (key == ConsoleKey.S) {
                SaveConfig(propsPath, modules);
                GenerateFiles(rootPath, modules, slnxPath);
                Console.WriteLine("\nSaved!");
                Thread.Sleep(500);
            }
            else if (key == ConsoleKey.Q) running = false;
        }
        // --- UI LOOP END ---
        return 0;
    }

    static List<ModuleInfo> ScanSlnx(string slnxPath)
    {
        var doc = XDocument.Load(slnxPath);
        var dict = new Dictionary<string, ModuleInfo>();
        var projects = doc.Descendants("Project").Select(p => p.Attribute("Path")?.Value).Where(p => !string.IsNullOrEmpty(p));

        foreach (var rawPath in projects)
        {
            string path = rawPath!.Replace('\\', '/');
            if (!path.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase)) continue;

            var fileName = Path.GetFileNameWithoutExtension(path);
            var parts = fileName.Split('.');
            
            if (parts.Length < 2)
            {
                string name = fileName;
                if (!dict.ContainsKey(name))
                {
                    dict[name] = new ModuleInfo { Name = name, CorePath = rawPath };
                }
                continue;
            }

            string impl = parts.Last();
            string moduleName = string.Join(".", parts.Take(parts.Length - 1));

            if (!dict.ContainsKey(moduleName)) dict[moduleName] = new ModuleInfo { Name = moduleName };
            
            if (impl.Equals("Core", StringComparison.OrdinalIgnoreCase))
            {
                dict[moduleName].CorePath = rawPath;
            }
            else
            {
                dict[moduleName].Implementations.Add(impl);
                dict[moduleName].ImplementationPaths[impl] = rawPath;
            }
        }
        
        foreach(var m in dict.Values) 
            if (m.Implementations.Any() && string.IsNullOrEmpty(m.SelectedImplementation)) 
                m.SelectedImplementation = m.Implementations.First();

        return dict.Values.OrderBy(m => m.Name).ToList();
    }

    static void GenerateFiles(string rootPath, List<ModuleInfo> modules, string slnxPath)
    {
        var enabledModules = modules.Where(m => m.IsEnabled).ToList();
        
        GenerateCodeFiles(rootPath, enabledModules, slnxPath);
        GenerateTargetsFile(rootPath, enabledModules, slnxPath);
    }

    static void GenerateTargetsFile(string rootPath, List<ModuleInfo> modules, string slnxPath)
    {
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var doc = new XDocument(new XElement(ns + "Project"));
        var root = doc.Root;
        
        var projectRefGroup = new XElement(ns + "ItemGroup");
        root!.Add(projectRefGroup);

        foreach (var m in modules)
        {
            if (m.CorePath != null) {
                projectRefGroup.Add(CreateRef(ns, "PluginReference", m.CorePath, true));
            }
            if (!string.IsNullOrEmpty(m.SelectedImplementation) && 
                m.ImplementationPaths.TryGetValue(m.SelectedImplementation, out var path)) {
                projectRefGroup.Add(CreateRef(ns, "PluginReference", path, true));
            }
        }

        var slnxDoc = XDocument.Load(slnxPath);
        var myGameProjs = slnxDoc.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .OfType<string>()
            .Where(p => p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));

        foreach (var path in myGameProjs) {
            projectRefGroup.Add(CreateRef(ns, "PluginReference", path!, true));
        }
        
        var compileGroup = new XElement(ns + "ItemGroup");
        var compileElement = new XElement(ns + "Compile", new XAttribute("Include", "$(MSBuildThisFileDirectory)Generated/ModuleLoader.cs"));
        compileGroup.Add(compileElement);
        root.Add(compileGroup);

        doc.Save(Path.Combine(rootPath, "AutoGenerated.targets"));
    }
    
    static void GenerateCodeFiles(string rootPath, List<ModuleInfo> modules, string slnxPath)
    {
        var sharedModules = new List<string>();
        var clientModules = new List<string>();
        var serverModules = new List<string>();

        // 1. Add enabled engine modules
        foreach (var m in modules)
        {
            var modulePaths = new List<string>();
            if (m.CorePath != null) modulePaths.Add(m.CorePath);
            if (!string.IsNullOrEmpty(m.SelectedImplementation) &&
                m.ImplementationPaths.TryGetValue(m.SelectedImplementation, out var path))
            {
                modulePaths.Add(path);
            }

            foreach (var projPath in modulePaths)
            {
                if (projPath.Contains("/Client/")) clientModules.Add(projPath);
                else if (projPath.Contains("/Server/")) serverModules.Add(projPath);
                else sharedModules.Add(projPath);
            }
        }
        
        // 2. Add MyGame modules
        var slnxDoc = XDocument.Load(slnxPath);
        var myGameProjs = slnxDoc.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .OfType<string>()
            .Where(p => p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));
            
        foreach (var projPath in myGameProjs)
        {
            if (projPath.Contains("/Client/")) clientModules.Add(projPath);
            else if (projPath.Contains("/Server/")) serverModules.Add(projPath);
            else sharedModules.Add(projPath);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Karpik.Engine.Core.ModuleManagement;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine();
        sb.AppendLine("public class ModuleLoader");
        sb.AppendLine("{");
        sb.AppendLine("    public Assembly[] LoadedAssemblies = [];");
        sb.AppendLine("    private PluginLoadContext? _loadContext;");
        sb.AppendLine("    private string? _shadowCopyDirectory;");
        sb.AppendLine();
        GenerateAssemblyArray(sb, "Shared", sharedModules);
        GenerateAssemblyArray(sb, "ClientOnly", clientModules);
        GenerateAssemblyArray(sb, "ServerOnly", serverModules);
        sb.AppendLine();
        sb.AppendLine("    public void LoadClientModules() => LoadPluginCollection(SharedAssemblies.Concat(ClientOnlyAssemblies));");
        sb.AppendLine("    public void LoadServerModules() => LoadPluginCollection(SharedAssemblies.Concat(ServerOnlyAssemblies));");
        sb.AppendLine();
        sb.AppendLine("    public void LoadPluginCollection(IEnumerable<string> assemblyNames)");
        sb.AppendLine("    {");
        sb.AppendLine("        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, \"modules\");");
        sb.AppendLine("        if (!Directory.Exists(sourceDirectory))");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new DirectoryNotFoundException(");
        sb.AppendLine("                $\"Module staging directory was not found: {sourceDirectory}. Build the launcher project before starting the worker.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        var shadowRoot = Path.Combine(AppContext.BaseDirectory, \"reload\", \"shadow\");");
        sb.AppendLine("        _shadowCopyDirectory = Path.Combine(shadowRoot, $\"{Environment.ProcessId}_{Guid.NewGuid():N}\");");
        sb.AppendLine("        Directory.CreateDirectory(_shadowCopyDirectory);");
        sb.AppendLine();
        sb.AppendLine("        CopyDirectory(sourceDirectory, _shadowCopyDirectory);");
        sb.AppendLine();
        sb.AppendLine("        var requiredAssemblies = assemblyNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();");
        sb.AppendLine("        var missing = requiredAssemblies");
        sb.AppendLine("            .Where(name => !File.Exists(Path.Combine(_shadowCopyDirectory, name + \".dll\")))");
        sb.AppendLine("            .ToArray();");
        sb.AppendLine();
        sb.AppendLine("        if (missing.Length > 0)");
        sb.AppendLine("        {");
        sb.AppendLine("            throw new FileNotFoundException(");
        sb.AppendLine("                $\"Required module assemblies are missing from {sourceDirectory}: {string.Join(\", \", missing)}. Build the matching ClientLauncher or ServerLauncher project.\");");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        _loadContext = new PluginLoadContext(_shadowCopyDirectory);");
        sb.AppendLine("        var loadedList = new List<Assembly>(requiredAssemblies.Length);");
        sb.AppendLine();
        sb.AppendLine("        foreach (var name in requiredAssemblies)");
        sb.AppendLine("        {");
        sb.AppendLine("            var shadowPath = Path.Combine(_shadowCopyDirectory, name + \".dll\");");
        sb.AppendLine("            loadedList.Add(_loadContext.LoadFromAssemblyPath(shadowPath));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        LoadedAssemblies = loadedList.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)");
        sb.AppendLine("    {");
        sb.AppendLine("        foreach (var directory in Directory.GetDirectories(sourceDirectory, \"*\", SearchOption.AllDirectories))");
        sb.AppendLine("        {");
        sb.AppendLine("            var relativePath = Path.GetRelativePath(sourceDirectory, directory);");
        sb.AppendLine("            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        foreach (var file in Directory.GetFiles(sourceDirectory, \"*\", SearchOption.AllDirectories))");
        sb.AppendLine("        {");
        sb.AppendLine("            var relativePath = Path.GetRelativePath(sourceDirectory, file);");
        sb.AppendLine("            var destinationPath = Path.Combine(destinationDirectory, relativePath);");
        sb.AppendLine("            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);");
        sb.AppendLine("            File.Copy(file, destinationPath, overwrite: true);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        var generatedDir = Path.Combine(rootPath, "Generated");
        Directory.CreateDirectory(generatedDir);
        File.WriteAllText(Path.Combine(generatedDir, "ModuleLoader.cs"), sb.ToString());
    }

    static void GenerateAssemblyArray(StringBuilder sb, string name, List<string> modules)
    {
        sb.AppendLine($"    public readonly string[] {name}Assemblies = {{");
        foreach (var projPath in modules.Distinct())
        {
            sb.AppendLine($"        \"{Path.GetFileNameWithoutExtension(projPath)}\",");
        }
        sb.AppendLine("    };");
    }

    static XElement CreateRef(XNamespace ns, string type, string path, bool isPlugin)
    {
        string cond = "";
        string p = path.Replace('\\', '/');
        if (p.Contains("/Client/")) cond = "'$(IsClient)' == 'true'";
        if (p.Contains("/Server/")) cond = "'$(IsServer)' == 'true'";

        var el = new XElement(ns + type, new XAttribute("Include", $"$(MSBuildThisFileDirectory){path}"));
        if (!string.IsNullOrEmpty(cond)) el.Add(new XAttribute("Condition", cond));

        // if (isPlugin)
        // {
        //     el.Add(new XElement(ns + "ReferenceOutputAssembly", "false"));
        //     el.Add(new XElement(ns + "Private", "false"));
        // }

        return el;
    }

    static int ValidateProjectReferences(string rootPath, string slnxPath)
    {
        var projects = LoadSolutionProjects(rootPath, slnxPath);
        var errors = new List<string>();

        foreach (var project in projects)
        {
            var fromSide = GetProjectSide(project);
            if (fromSide == ProjectSide.Unknown)
            {
                continue;
            }

            foreach (var reference in ReadProjectReferences(project))
            {
                var toSide = GetProjectSide(reference);
                if (toSide == ProjectSide.Unknown)
                {
                    continue;
                }

                if (!IsProjectReferenceAllowed(fromSide, toSide))
                {
                    errors.Add(
                        $"{Path.GetRelativePath(rootPath, project)} ({fromSide}) references forbidden project " +
                        $"{Path.GetRelativePath(rootPath, reference)} ({toSide})");
                }
            }
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("Project side validation passed.");
            return 0;
        }

        Console.Error.WriteLine("Project side validation failed:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 1;
    }

    static List<string> LoadSolutionProjects(string rootPath, string slnxPath)
    {
        var doc = XDocument.Load(slnxPath);
        return doc.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(Path.Combine(rootPath, p!)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static List<string> ReadProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var doc = XDocument.Load(projectPath);

        return doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(Path.Combine(projectDirectory, p!)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static ProjectSide GetProjectSide(string projectPath)
    {
        var normalized = projectPath.Replace('\\', '/');

        if (normalized.Contains("/Modules/Client/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/MyGame/Client/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Client;
        }

        if (normalized.Contains("/Modules/Server/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/MyGame/Server/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Server;
        }

        if (normalized.Contains("/Modules/Shared/", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("/MyGame/Shared/", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSide.Shared;
        }

        return ProjectSide.Unknown;
    }

    static bool IsProjectReferenceAllowed(ProjectSide fromSide, ProjectSide toSide)
    {
        return fromSide == toSide ||
               (fromSide is ProjectSide.Client or ProjectSide.Server && toSide == ProjectSide.Shared);
    }
    
    static void LoadConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static void SaveConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static string? FindSolutionRoot(string start) {
        var d = new DirectoryInfo(start);
        while(d!=null) { if(d.GetFiles("*.slnx").Any()) return d.FullName; d = d.Parent; }
        return null;
    }
}
