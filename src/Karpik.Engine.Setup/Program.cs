using System.Diagnostics;
using System.IO.Compression;
using Karpik.Engine.Tooling;

// Installs versioned KarpikEngine bundles built by New-KarpikDistribution.ps1:
//   setup sdk --payload <sdk.zip> [--engines-root <dir>]
//   setup editor --payload <editor.zip> [--engines-root <dir>]
//   setup launcher --source <dir> [--launcher-root <dir>]
//
// Sdk and editor parts compose one installation below the engines root:
// Engines/<version>/sdk,modules,native,shared,runners,manifest  (sdk bundle)
// Engines/<version>/editor                                      (editor bundle)
// The launcher is version-independent and lives in its own directory.

var validator = new EngineInstallationValidator();
return args.FirstOrDefault() switch
{
    "sdk" => InstallSdk(args.Skip(1).ToArray(), validator),
    "editor" => InstallEditor(args.Skip(1).ToArray(), validator),
    "launcher" => InstallLauncher(args.Skip(1).ToArray()),
    _ => Usage("Expected a mode: sdk, editor or launcher."),
};

static int Usage(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  setup sdk --payload <sdk.zip> [--engines-root <dir>]");
    Console.Error.WriteLine("  setup editor --payload <editor.zip> [--engines-root <dir>]");
    Console.Error.WriteLine("  setup launcher --source <dir> [--launcher-root <dir>]");
    return 1;
}

static int InstallSdk(string[] args, EngineInstallationValidator validator)
{
    string? payload = Option(args, "--payload");
    if (payload is null) return Usage("sdk mode requires --payload <sdk.zip>.");
    string enginesRoot = Option(args, "--engines-root") ?? DefaultEnginesRoot();

    string staged = ExtractPayload(payload, "sdk");
    try
    {
        string installDir = SingleTopDirectory(staged);
        EngineInstallationManifest manifest = ReadManifest(Path.Combine(staged, installDir));
        string target = ContainedPath(enginesRoot, installDir);
        if (Directory.Exists(target)) throw Fail($"Installation already exists: {target}");

        EngineInstallationValidationResult check;
        string editorPlaceholder = Path.Combine(staged, installDir, "editor");
        Directory.CreateDirectory(editorPlaceholder);
        try
        {
            check = validator.Validate(Path.Combine(staged, installDir));
        }
        finally
        {
            DeleteIfExists(editorPlaceholder);
        }
        if (!check.IsValid && check.Code != EngineInstallationValidationCode.MissingEditor)
            throw Fail($"SDK payload is invalid: [{check.Code}] {check.Message}");
        if (!check.IsValid) Console.WriteLine("Note: editor part is missing; install the Launcher+Editor bundle next.");

        Directory.CreateDirectory(enginesRoot);
        Directory.Move(Path.Combine(staged, installDir), target);
        RegisterNuGetSource(manifest.MsBuildSdkVersion, Path.Combine(target, "sdk"));
        Console.WriteLine($"SDK {manifest.MsBuildSdkVersion} installed: {target}");
        return 0;
    }
    finally
    {
        DeleteIfExists(staged);
    }
}

static int InstallEditor(string[] args, EngineInstallationValidator validator)
{
    string? payload = Option(args, "--payload");
    if (payload is null) return Usage("editor mode requires --payload <editor.zip>.");
    string enginesRoot = Option(args, "--engines-root") ?? DefaultEnginesRoot();

    string staged = ExtractPayload(payload, "editor");
    try
    {
        string expectedVersion = ReadVersionFile(Path.Combine(staged, "editor-version.txt"));
        string? installation = FindInstallationByEngineVersion(enginesRoot, expectedVersion);
        if (installation is null)
            throw Fail($"SDK bundle version '{expectedVersion}' is not installed. Install the SDK bundle first.");

        string editorTarget = Path.Combine(installation, "editor");
        if (Directory.Exists(editorTarget)) throw Fail($"Editor is already installed: {editorTarget}");
        string editorSource = Path.Combine(staged, "editor");
        if (!Directory.Exists(editorSource)) throw Fail("Editor payload does not contain an editor directory.");
        CopyDirectory(editorSource, editorTarget);

        EngineInstallationValidationResult check = validator.Validate(installation);
        if (!check.IsValid)
        {
            DeleteIfExists(editorTarget);
            throw Fail($"Installation is invalid after editor install: [{check.Code}] {check.Message}");
        }
        Console.WriteLine($"Editor {expectedVersion} installed: {editorTarget}");
        return 0;
    }
    finally
    {
        DeleteIfExists(staged);
    }
}

static int InstallLauncher(string[] args)
{
    string? source = Option(args, "--source");
    if (source is null) return Usage("launcher mode requires --source <dir>.");
    string launcherRoot = Option(args, "--launcher-root") ?? DefaultLauncherRoot();

    source = Path.GetFullPath(source);
    string exe = Path.Combine(source, OperatingSystem.IsWindows() ? "Karpik.Launcher.exe" : "Karpik.Launcher");
    if (!File.Exists(exe)) throw Fail($"Launcher payload does not contain the entry point: {exe}");

    DeleteIfExists(launcherRoot);
    CopyDirectory(source, launcherRoot);
    Console.WriteLine($"Launcher installed: {launcherRoot}");
    if (OperatingSystem.IsWindows()) CreateStartMenuShortcut(Path.Combine(launcherRoot, "Karpik.Launcher.exe"));
    return 0;
}

static string? Option(string[] args, string name)
{
    for (int i = 0; i + 1 < args.Length; i++)
        if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
    return null;
}

static string DefaultEnginesRoot() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karpik", "Engines");

static string DefaultLauncherRoot() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karpik", "Launcher");

static string ExtractPayload(string payload, string kind)
{
    string path = Path.GetFullPath(payload);
    if (!File.Exists(path)) throw Fail($"Payload file does not exist: {path}");
    using ZipArchive archive = ZipFile.OpenRead(path);
    foreach (ZipArchiveEntry entry in archive.Entries)
    {
        string full = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
        if (full.Split(Path.DirectorySeparatorChar).Contains("..")) throw Fail($"Payload entry escapes the directory: {entry.FullName}");
    }
    string staged = Path.Combine(Path.GetTempPath(), $"karpik-setup-{kind}-{Guid.NewGuid():N}");
    ZipFile.ExtractToDirectory(path, staged);
    return staged;
}

static string SingleTopDirectory(string staged)
{
    string[] dirs = Directory.GetDirectories(staged);
    if (dirs.Length != 1) throw Fail($"Payload must contain exactly one top-level directory, found {dirs.Length}.");
    return Path.GetFileName(dirs[0]);
}

static EngineInstallationManifest ReadManifest(string installDir)
{
    string manifestPath = Path.Combine(installDir, "engine-installation.json");
    if (!File.Exists(manifestPath)) throw Fail($"Payload has no engine-installation.json: {manifestPath}");
    try
    {
        return EngineInstallationManifest.Parse(File.ReadAllText(manifestPath));
    }
    catch (Exception exception)
    {
        throw Fail($"Cannot read payload manifest: {exception.Message}");
    }
}

static string ReadVersionFile(string path)
{
    if (!File.Exists(path)) throw Fail($"Payload has no editor-version.txt: {path}");
    string version = File.ReadAllText(path).Trim();
    if (!IsSafeVersion(version)) throw Fail($"Payload has an invalid editor version: '{version}'.");
    return version;
}

static string? FindInstallationByEngineVersion(string enginesRoot, string engineVersion)
{
    if (!Directory.Exists(enginesRoot)) return null;
    foreach (string dir in Directory.EnumerateDirectories(enginesRoot))
    {
        string manifestPath = Path.Combine(dir, "engine-installation.json");
        if (!File.Exists(manifestPath)) continue;
        try
        {
            if (string.Equals(EngineInstallationManifest.Parse(File.ReadAllText(manifestPath)).EngineVersion, engineVersion, StringComparison.Ordinal))
                return Path.GetFullPath(dir);
        }
        catch (Exception)
        {
            // Unreadable manifests are skipped; only version-matching installs count.
        }
    }
    return null;
}

static string ContainedPath(string root, string name)
{
    if (!IsSafeVersion(name)) throw Fail($"Unsafe installation name: '{name}'.");
    string fullRoot = Path.GetFullPath(root);
    string full = Path.GetFullPath(Path.Combine(fullRoot, name));
    if (!full.StartsWith(fullRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        throw Fail($"Installation path escapes the engines root: {full}");
    return full;
}

static bool IsSafeVersion(string value) =>
    !string.IsNullOrWhiteSpace(value) && value is not ("." or "..") &&
    value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
    !value.Contains(Path.DirectorySeparatorChar) && !value.Contains(Path.AltDirectorySeparatorChar);

static void RegisterNuGetSource(string sdkVersion, string sdkDir)
{
    string name = $"KarpikEngine-{sdkVersion}";
    Run("dotnet", ["nuget", "remove", "source", name], ignoreExitCode: true);
    if (Run("dotnet", ["nuget", "add", "source", sdkDir, "--name", name], ignoreExitCode: false) != 0)
        throw Fail($"Failed to register NuGet source '{name}'.");
    string packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } custom
        ? Path.GetFullPath(custom)
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    string cache = Path.Combine(packagesRoot, "karpik.engine.sdk", sdkVersion.ToLowerInvariant());
    if (Path.GetFullPath(cache).StartsWith(Path.GetFullPath(packagesRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        DeleteIfExists(cache);
}

static void CopyDirectory(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (string dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
}

static void CreateStartMenuShortcut(string exe)
{
    try
    {
        string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Karpik");
        Directory.CreateDirectory(programs);
        Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;
        object shell = Activator.CreateInstance(shellType)!;
        object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, [Path.Combine(programs, "Karpik Launcher.lnk")])!;
        shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, shortcut, [exe]);
        shortcut.GetType().InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(exe)!]);
        shortcut.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, shortcut, []);
        Console.WriteLine("Start Menu shortcut created.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Shortcut creation skipped: {exception.Message}");
    }
}

static int Run(string file, string[] arguments, bool ignoreExitCode)
{
    var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {file}.");
    process.WaitForExit();
    if (process.ExitCode != 0 && !ignoreExitCode) return process.ExitCode;
    return 0;
}

static void DeleteIfExists(string path)
{
    try
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }
    catch (Exception) { }
}

static Exception Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(2);
    return new InvalidOperationException(message);
}
