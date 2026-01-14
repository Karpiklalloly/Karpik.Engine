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

    static void Main(string[] args)
    {
        var rootPath = FindSolutionRoot(Directory.GetCurrentDirectory());
        if (rootPath == null) { Console.WriteLine("Root not found (no .slnx)"); return; }

        var slnxPath = Directory.GetFiles(rootPath, "*.slnx").FirstOrDefault()!;
        var propsPath = Path.Combine(rootPath, "Directory.Build.props");

        var modules = ScanSlnx(slnxPath);
        LoadConfig(propsPath, modules);

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
                projectRefGroup.Add(CreateRef(ns, "ProjectReference", m.CorePath, false));
            }
            if (!string.IsNullOrEmpty(m.SelectedImplementation) && 
                m.ImplementationPaths.TryGetValue(m.SelectedImplementation, out var path)) {
                projectRefGroup.Add(CreateRef(ns, "PluginReference", path, true));
            }
        }

        var slnxDoc = XDocument.Load(slnxPath);
        var myGameProjs = slnxDoc.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));

        foreach (var path in myGameProjs) {
            projectRefGroup.Add(CreateRef(ns, "ProjectReference", path!, false));
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
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));
            
        foreach (var projPath in myGameProjs)
        {
            if (projPath.Contains("/Client/")) clientModules.Add(projPath);
            else if (projPath.Contains("/Server/")) serverModules.Add(projPath);
            else sharedModules.Add(projPath);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Runtime.Loader;");
        sb.AppendLine("using Karpik.Engine.Core.ModuleManagement;");
        sb.AppendLine();
        sb.AppendLine("public static class ModuleLoader");
        sb.AppendLine("{");

        // --- DEBUG part ---
        sb.AppendLine("#if DEBUG");
        sb.AppendLine("    public static readonly List<Assembly> LoadedAssemblies = new List<Assembly>();");
        sb.AppendLine("    private static AssemblyLoadContext? _pluginContext;");
        sb.AppendLine();
        GenerateDebugAssemblies(sb, "Shared", sharedModules);
        GenerateDebugAssemblies(sb, "ClientOnly", clientModules);
        GenerateDebugAssemblies(sb, "ServerOnly", serverModules);
        
        sb.AppendLine("    public static void LoadClientModules() => LoadPluginCollection(SharedAssemblies.Concat(ClientOnlyAssemblies));");
        sb.AppendLine("    public static void LoadServerModules() => LoadPluginCollection(SharedAssemblies.Concat(ServerOnlyAssemblies));");
        sb.AppendLine();
        sb.AppendLine("    private static void LoadPluginCollection(IEnumerable<string> assemblyNames)");
        sb.AppendLine("    {");
        sb.AppendLine("        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;");
        sb.AppendLine("        var assemblyPaths = assemblyNames.Distinct()");
        sb.AppendLine("            .Select(name => Path.Combine(baseDirectory, name + \".dll\"))");
        sb.AppendLine("            .Where(File.Exists)");
        sb.AppendLine("            .ToList();");
        sb.AppendLine();
        sb.AppendLine("        if (!assemblyPaths.Any()) return;");
        sb.AppendLine();
        sb.AppendLine("        var loadContext = new PluginLoadContext(assemblyPaths);");
        sb.AppendLine("        _pluginContext = loadContext;");
        sb.AppendLine();
        sb.AppendLine("        foreach (var path in assemblyPaths)");
        sb.AppendLine("        {");
        sb.AppendLine("            var assembly = loadContext.LoadFromAssemblyPath(path);");
        sb.AppendLine("            LoadedAssemblies.Add(assembly);");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("#endif");
        
        // --- RELEASE part ---
        sb.AppendLine();
        sb.AppendLine("#if !DEBUG");
        sb.AppendLine("    public static void RegisterClientModules(Karpik.Engine.Core.Bootstrap bootstrap)");
        sb.AppendLine("    {");
        sharedModules.Concat(clientModules).ToList().ForEach(p => GenerateRegistration(sb, rootPath, p));
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static void RegisterServerModules(Karpik.Engine.Core.Bootstrap bootstrap)");
        sb.AppendLine("    {");
        sharedModules.Concat(serverModules).ToList().ForEach(p => GenerateRegistration(sb, rootPath, p));
        sb.AppendLine("    }");
        sb.AppendLine("#endif");

        sb.AppendLine("}");

        var generatedDir = Path.Combine(rootPath, "Generated");
        Directory.CreateDirectory(generatedDir);
        File.WriteAllText(Path.Combine(generatedDir, "ModuleLoader.cs"), sb.ToString());
    }

    static void GenerateDebugAssemblies(StringBuilder sb, string name, List<string> modules)
    {
        sb.AppendLine($"    public static readonly string[] {name}Assemblies = {{");
        foreach (var projPath in modules.Distinct())
        {
            sb.AppendLine($"        \"{Path.GetFileNameWithoutExtension(projPath)}\",");
        }
        sb.AppendLine("    };");
    }

    static void GenerateRegistration(StringBuilder sb, string rootPath, string projPath)
    {
        var (namespaceName, className) = GetInstallerInfo(rootPath, projPath);
        if (namespaceName != null && className != null)
        {
            sb.AppendLine($"        bootstrap.RegisterModule(new {namespaceName}.{className}());");
        }
    }

    static (string? Namespace, string? ClassName) GetInstallerInfo(string rootPath, string projPath)
    {
        try
        {
            var fullProjPath = Path.GetFullPath(Path.Combine(rootPath, projPath));
            var projDir = Path.GetDirectoryName(fullProjPath)!;
            var doc = XDocument.Load(fullProjPath);
            var rootNs = doc.Descendants("RootNamespace").FirstOrDefault()?.Value ?? 
                         Path.GetFileNameWithoutExtension(fullProjPath);

            var installerFile = Directory.GetFiles(projDir, "*Installer.cs", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (installerFile == null) return (null, null);

            var content = File.ReadAllText(installerFile);
            var classNameMatch = System.Text.RegularExpressions.Regex.Match(content, @"class\s+([^\s]+Installer)");
            if (!classNameMatch.Success) return (null, null);
            
            var namespaceMatch = System.Text.RegularExpressions.Regex.Match(content, @"namespace\s+([^\s;]+)");
            var finalNs = namespaceMatch.Success ? namespaceMatch.Groups[1].Value : rootNs;

            return (finalNs, classNameMatch.Groups[1].Value);
        }
        catch { return (null, null); }
    }

    static XElement CreateRef(XNamespace ns, string type, string path, bool isPlugin)
    {
        string cond = "";
        string p = path.Replace('\\', '/');
        if (p.Contains("/Client/")) cond = "'$(IsClient)' == 'true'";
        if (p.Contains("/Server/")) cond = "'$(IsServer)' == 'true'";

        var el = new XElement(ns + type, new XAttribute("Include", $"$(MSBuildThisFileDirectory){path}"));
        if (!string.IsNullOrEmpty(cond)) el.Add(new XAttribute("Condition", cond));

        if (isPlugin)
        {
            el.Add(new XElement(ns + "ReferenceOutputAssembly", "false"));
            el.Add(new XElement(ns + "Private", "false"));
        }

        return el;
    }
    
    static void LoadConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static void SaveConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static string? FindSolutionRoot(string start) {
        var d = new DirectoryInfo(start);
        while(d!=null) { if(d.GetFiles("*.slnx").Any()) return d.FullName; d = d.Parent; }
        return null;
    }
}
