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

        // Auto-generate mode (non-interactive)
        if (args.Contains("--generate") || args.Contains("-g"))
        {
            GenerateFiles(rootPath, modules, slnxPath);
            Console.WriteLine("Files generated successfully.");
            return;
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
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));

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
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));
            
        foreach (var projPath in myGameProjs)
        {
            if (projPath.Contains("/Client/")) clientModules.Add(projPath);
            else if (projPath.Contains("/Server/")) serverModules.Add(projPath);
            else sharedModules.Add(projPath);
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("using Karpik.Engine.Core.ModuleManagement;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using System.Collections.Concurrent;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.IO;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Reflection;");
        sb.AppendLine("using System.Runtime.Loader;");
        sb.AppendLine();
        sb.AppendLine("public class ModuleLoader");
        sb.AppendLine("{");
        sb.AppendLine("    public Assembly[] LoadedAssemblies = [];");
        sb.AppendLine("    private AssemblyLoadContext? _currentContext;");
        sb.AppendLine("    private WeakReference<AssemblyLoadContext>? _previousContext;");
        sb.AppendLine("    private WeakReference a;");
        sb.AppendLine("    private string? _directoryToCleanup;");
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
        sb.AppendLine("        _directoryToCleanup = Path.Combine(AppContext.BaseDirectory, \"temp_bin\", Guid.NewGuid().ToString());");
        sb.AppendLine("        Directory.CreateDirectory(_directoryToCleanup);");
        sb.AppendLine();
        sb.AppendLine("        var allDlls = Directory.GetFiles(sourceDirectory, \"*.dll\", SearchOption.AllDirectories);");
        sb.AppendLine("        foreach (var dllPath in allDlls)");
        sb.AppendLine("        {");
        sb.AppendLine("            var fileName = Path.GetFileName(dllPath);");
        sb.AppendLine("            File.Copy(dllPath, Path.Combine(_directoryToCleanup, fileName), true);");
        sb.AppendLine();
        sb.AppendLine("            var pdbPath = Path.ChangeExtension(dllPath, \".pdb\");");
        sb.AppendLine("            if (File.Exists(pdbPath))");
        sb.AppendLine("                File.Copy(pdbPath, Path.Combine(_directoryToCleanup, Path.GetFileName(pdbPath)), true);");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        var newContext = new PluginLoadContext(_directoryToCleanup);");
        sb.AppendLine("        _currentContext = newContext;");
        sb.AppendLine();
        sb.AppendLine("        var loadedList = new List<Assembly>();");
        sb.AppendLine();
        sb.AppendLine("        foreach (var name in assemblyNames.Distinct())");
        sb.AppendLine("        {");
        sb.AppendLine("            var shadowPath = Path.Combine(_directoryToCleanup, name + \".dll\");");
        sb.AppendLine("            if (File.Exists(shadowPath))");
        sb.AppendLine("            {");
        sb.AppendLine("                var asm = newContext.LoadFromAssemblyPath(shadowPath);");
        sb.AppendLine("                loadedList.Add(asm);");
        sb.AppendLine("            }");
        sb.AppendLine("            else");
        sb.AppendLine("            {");
        sb.AppendLine("                Console.WriteLine($\"[ModuleLoader] Assembly not found: {shadowPath}\");");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        LoadedAssemblies = loadedList.ToArray();");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void Unload()");
        sb.AppendLine("    {");
        sb.AppendLine("        CleanupStaticFields(_currentContext.Assemblies);");
        sb.AppendLine("        if (_currentContext.Assemblies != null)");
        sb.AppendLine("        {");
        sb.AppendLine("            foreach (var asm in _currentContext.Assemblies)");
        sb.AppendLine("            {");
        sb.AppendLine("                foreach (var type in asm.GetTypes())");
        sb.AppendLine("                {");
        sb.AppendLine("                    System.ComponentModel.TypeDescriptor.Refresh(type);");
        sb.AppendLine("                }");
        sb.AppendLine("                System.ComponentModel.TypeDescriptor.Refresh(asm);");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        LoadedAssemblies = null;");
        sb.AppendLine();
        sb.AppendLine("        for (int i = 0; i < 100; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            GC.Collect(GC.MaxGeneration);");
        sb.AppendLine("            GC.WaitForPendingFinalizers();");
        sb.AppendLine("        }");
        sb.AppendLine("        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);");
        sb.AppendLine("        GC.WaitForFullGCApproach(-1);");
        sb.AppendLine();
        sb.AppendLine("        _currentContext.Unload();");
        sb.AppendLine();
        sb.AppendLine("        for (int i = 0; i < 100; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            GC.Collect(GC.MaxGeneration);");
        sb.AppendLine("            GC.WaitForPendingFinalizers();");
        sb.AppendLine("        }");
        sb.AppendLine("        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);");
        sb.AppendLine("        GC.WaitForFullGCApproach(-1);");
        sb.AppendLine("        ");
        sb.AppendLine("        _previousContext = new WeakReference<AssemblyLoadContext>(_currentContext);");
        sb.AppendLine("        a = new WeakReference(_currentContext);");
        sb.AppendLine("        _currentContext = null;");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public void CheckForPreviousContextUnload()");
        sb.AppendLine("    {");
        sb.AppendLine("        for (int i = 0; i < 100; i++)");
        sb.AppendLine("        {");
        sb.AppendLine("            GC.Collect(GC.MaxGeneration);");
        sb.AppendLine("            GC.WaitForPendingFinalizers();");
        sb.AppendLine("        }");
        sb.AppendLine("        ");
        sb.AppendLine("        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);");
        sb.AppendLine("        GC.WaitForFullGCApproach(-1);");
        sb.AppendLine();
        sb.AppendLine("        if (a.IsAlive)");
        sb.AppendLine("        {");
        sb.AppendLine("            _previousContext.TryGetTarget(out var context);");
        sb.AppendLine("            Console.ForegroundColor = ConsoleColor.Red;");
        sb.AppendLine("            Console.WriteLine($\"Unloaded assemblies: {context.Assemblies.Count()}\");");
        sb.AppendLine("            foreach (var assembly in context.Assemblies)");
        sb.AppendLine("            {");
        sb.AppendLine("                Console.WriteLine(assembly.FullName);");
        sb.AppendLine("            }");
        sb.AppendLine("            Console.WriteLine(\"[ModuleLoader] FATAL: Previous AssemblyLoadContext is still alive after GC. A strong reference is likely leaking.\");");
        sb.AppendLine("            Console.ResetColor();");
        sb.AppendLine("        }");
        sb.AppendLine("        else");
        sb.AppendLine("        {");
        sb.AppendLine("            Console.WriteLine(\"[ModuleLoader] Previous context successfully collected by GC.\");");
        sb.AppendLine();
        sb.AppendLine("            if (_directoryToCleanup != null)");
        sb.AppendLine("            {");
        sb.AppendLine("                try");
        sb.AppendLine("                {");
        sb.AppendLine("                    Directory.Delete(_directoryToCleanup, true);");
        sb.AppendLine("                    Console.WriteLine($\"[ModuleLoader] Successfully cleaned up old shadow directory.\");");
        sb.AppendLine("                }");
        sb.AppendLine("                catch (Exception e)");
        sb.AppendLine("                {");
        sb.AppendLine("                    Console.ForegroundColor = ConsoleColor.Red;");
        sb.AppendLine("                    Console.WriteLine($\"[ModuleLoader] ERROR: Failed to cleanup directory: {e.Message}\");");
        sb.AppendLine("                    Console.ResetColor();");
        sb.AppendLine("                }");
        sb.AppendLine("                _directoryToCleanup = null;");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    public static void CleanupStaticFields(IEnumerable<Assembly> assemblies)");
        sb.AppendLine("    {");
        sb.AppendLine("        var fields = assemblies");
        sb.AppendLine("            .SelectMany(x => x.GetTypes())");
        sb.AppendLine("            .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))");
        sb.AppendLine("            .Where(x => !x.FieldType.IsValueType");
        sb.AppendLine("                        // && !x.IsLiteral && !x.IsInitOnly");
        sb.AppendLine("                        );");
        sb.AppendLine("        ");
        sb.AppendLine("        foreach (var field in fields)");
        sb.AppendLine("        {");
        sb.AppendLine("            try");
        sb.AppendLine("            {");
        sb.AppendLine("                if (field.FieldType.IsArray)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var array = field.GetValue(null) as Array;");
        sb.AppendLine("                    if (array is not null)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        Array.Clear(array);");
        sb.AppendLine("                    }");
        sb.AppendLine("                }");
        sb.AppendLine("                else if (typeof(IDictionary).IsAssignableFrom(field.FieldType))");
        sb.AppendLine("                {");
        sb.AppendLine("                    var dict = (IDictionary)field.GetValue(null);");
        sb.AppendLine("                    dict.Clear();");
        sb.AppendLine("                }");
        sb.AppendLine("                else if (typeof(IList).IsAssignableFrom(field.FieldType))");
        sb.AppendLine("                {");
        sb.AppendLine("                    var list = (IList)field.GetValue(null);");
        sb.AppendLine("                    list.Clear();");
        sb.AppendLine("                }");
        sb.AppendLine("                else if (typeof(IEnumerable).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(string))");
        sb.AppendLine("                {");
        sb.AppendLine("                    var clearMethod = field.FieldType.GetMethod(\"Clear\");");
        sb.AppendLine("                    clearMethod?.Invoke(field.GetValue(null), null);");
        sb.AppendLine("                }");
        sb.AppendLine("                else");
        sb.AppendLine("                {");
        sb.AppendLine("                    // Зануляем ссылку");
        sb.AppendLine("                    field.SetValue(null, null);");
        sb.AppendLine("                }");
        sb.AppendLine("            }");
        sb.AppendLine("            catch (Exception ex)");
        sb.AppendLine("            {");
        sb.AppendLine("                // Некоторые поля (например, генерируемые компилятором или специфические readonly) ");
        sb.AppendLine("                // могут сопротивляться занулению. В контексте выгрузки это можно проигнорировать.");
        sb.AppendLine("                Console.WriteLine($\"Failed to null {field.Name} in {field.Name}: {ex.Message}\");");
        sb.AppendLine("            }");
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
    
    static void LoadConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static void SaveConfig(string path, List<ModuleInfo> modules) { /* ... */ }
    static string? FindSolutionRoot(string start) {
        var d = new DirectoryInfo(start);
        while(d!=null) { if(d.GetFiles("*.slnx").Any()) return d.FullName; d = d.Parent; }
        return null;
    }
}
