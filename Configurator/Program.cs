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
                GenerateTargetsFile(rootPath, modules, slnxPath); // ГЕНЕРАЦИЯ
                Console.WriteLine("\nSaved!");
                Thread.Sleep(500);
            }
            else if (key == ConsoleKey.Q) running = false;
        }
        // --- UI LOOP END ---
    }

    // 1. SCANNER
    static List<ModuleInfo> ScanSlnx(string slnxPath)
    {
        var doc = XDocument.Load(slnxPath);
        var dict = new Dictionary<string, ModuleInfo>();
        var projects = doc.Descendants("Project").Select(p => p.Attribute("Path")?.Value).Where(p => !string.IsNullOrEmpty(p));

        foreach (var rawPath in projects)
        {
            string path = rawPath!.Replace('\\', '/');
            // Сканируем только Modules для UI. MyGame добавим автоматически при генерации.
            if (!path.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase)) continue;

            var fileName = Path.GetFileNameWithoutExtension(path);
            var parts = fileName.Split('.');
            if (parts.Length < 2) continue;

            string impl = parts.Last();
            string name = string.Join(".", parts.Take(parts.Length - 1));

            if (!dict.ContainsKey(name)) dict[name] = new ModuleInfo { Name = name };
            if (impl.Equals("Core", StringComparison.OrdinalIgnoreCase)) dict[name].CorePath = rawPath;
            else {
                dict[name].Implementations.Add(impl);
                dict[name].ImplementationPaths[impl] = rawPath;
            }
        }
        
        foreach(var m in dict.Values) 
            if (m.Implementations.Any() && string.IsNullOrEmpty(m.SelectedImplementation)) 
                m.SelectedImplementation = m.Implementations.First();

        return dict.Values.OrderBy(m => m.Name).ToList();
    }

    // 2. GENERATOR (ГЛАВНАЯ ЧАСТЬ)
    static void GenerateTargetsFile(string rootPath, List<ModuleInfo> modules, string slnxPath)
    {
        XNamespace ns = "http://schemas.microsoft.com/developer/msbuild/2003";
        var doc = new XDocument(new XElement(ns + "Project"));
        var root = doc.Root;
        var itemGroup = new XElement(ns + "ItemGroup");

        // 1. Модули из UI
        foreach (var m in modules)
        {
            if (!m.IsEnabled) continue;

            // Core -> ProjectReference
            if (m.CorePath != null) {
                itemGroup.Add(CreateRef(ns, "ProjectReference", m.CorePath));
            }

            // Impl -> PluginReference (!!!)
            if (!string.IsNullOrEmpty(m.SelectedImplementation) && 
                m.ImplementationPaths.TryGetValue(m.SelectedImplementation, out var path)) {
                itemGroup.Add(CreateRef(ns, "PluginReference", path));
            }
        }

        // 2. MyGame -> ProjectReference
        var slnxDoc = XDocument.Load(slnxPath);
        var myGameProjs = slnxDoc.Descendants("Project")
            .Select(p => p.Attribute("Path")?.Value)
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith("MyGame/", StringComparison.OrdinalIgnoreCase));

        foreach (var path in myGameProjs) {
            itemGroup.Add(CreateRef(ns, "ProjectReference", path!));
        }

        root!.Add(itemGroup);
        doc.Save(Path.Combine(rootPath, "AutoGenerated.targets"));
    }

    static XElement CreateRef(XNamespace ns, string type, string path)
    {
        string cond = "";
        string p = path.Replace('\\', '/');
        if (p.Contains("/Client/")) cond = "'$(IsClient)' == 'true'";
        if (p.Contains("/Server/")) cond = "'$(IsServer)' == 'true'";

        var el = new XElement(ns + type, new XAttribute("Include", $"$(MSBuildThisFileDirectory){path}"));
        if (!string.IsNullOrEmpty(cond)) el.Add(new XAttribute("Condition", cond));
        return el;
    }
    
    // --- Boilerplate (Load/Save/FindRoot) ---
    static void LoadConfig(string path, List<ModuleInfo> modules) { /* ваш старый код */ }
    static void SaveConfig(string path, List<ModuleInfo> modules) { /* ваш старый код */ }
    static string? FindSolutionRoot(string start) {
        var d = new DirectoryInfo(start);
        while(d!=null) { if(d.GetFiles("*.slnx").Any()) return d.FullName; d = d.Parent; }
        return null;
    }
}