using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Karpik.Engine.ProjectModel;
using Microsoft.Win32.SafeHandles;

namespace Karpik.Editor;

public interface IProjectInputLeaseHook
{
    void AfterLeaseAcquired(string solutionPath);
}

public sealed class ProjectInputLease : IKarpikProjectInputProvider, IDisposable
{
    private static readonly string[] ConventionalEvaluationFiles =
    [
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Packages.props",
        "Directory.Solution.props",
        "Directory.Solution.targets",
        "Directory.Build.rsp",
        "MSBuild.rsp",
        "NuGet.Config",
        "nuget.config"
    ];

    private readonly Dictionary<string, LeasedFile> _files = new(PathComparer);
    private readonly Dictionary<string, SafeFileHandle?> _directories = new(PathComparer);
    private readonly object _lifetimeGate = new();
    private string? _mirrorOwnerRoot;
    private int _retentionCount;
    private bool _ownerReleased;
    private bool _disposed;

    private ProjectInputLease(string solutionPath)
    {
        SolutionPath = solutionPath;
        SolutionRoot = Path.GetDirectoryName(solutionPath)
                       ?? throw new InvalidDataException("Solution path has no parent directory.");
    }

    public string SolutionPath { get; }
    public string SolutionRoot { get; }
    public string? EvaluationRoot { get; private set; }

    public static ProjectInputLease Acquire(string solutionPath) =>
        AcquireCore(solutionPath, portableMirror: !OperatingSystem.IsWindows());

    public static ProjectInputLease AcquirePortableMirror(string solutionPath) =>
        AcquireCore(solutionPath, portableMirror: true);

    private static ProjectInputLease AcquireCore(
        string solutionPath,
        bool portableMirror)
    {
        string normalizedSolution = Path.GetFullPath(solutionPath);
        var lease = new ProjectInputLease(normalizedSolution);
        try
        {
            lease.AcquireDirectoryAncestors(lease.SolutionRoot);
            lease.AcquireDirectoryTree(lease.SolutionRoot, lease.SolutionRoot);
            lease.AcquireRequiredFile(normalizedSolution, "solution");
            lease.AcquireRequiredFile(
                Path.Combine(lease.SolutionRoot, "global.json"),
                "global.json");
            lease.AcquireConventionalInputs(lease.SolutionRoot);

            XDocument solution = lease.LoadXml(normalizedSolution);
            var pendingProjects = new Queue<string>();
            var visitedProjects = new HashSet<string>(PathComparer);
            foreach (string declaredPath in solution.Descendants()
                         .Where(element => element.Name.LocalName == "Project")
                         .Select(element => element.Attributes()
                             .FirstOrDefault(attribute => attribute.Name.LocalName == "Path")?.Value)
                         .OfType<string>()
                         .Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string projectPath = Path.GetFullPath(declaredPath, lease.SolutionRoot);
                lease.EnsureWithinRoot(projectPath, "solution project");
                lease.AcquireDirectoryTree(
                    lease.SolutionRoot,
                    Path.GetDirectoryName(projectPath)!);
                lease.AcquireRequiredFile(projectPath, "solution project");
                pendingProjects.Enqueue(projectPath);
            }

            while (pendingProjects.Count > 0)
            {
                string projectPath = pendingProjects.Dequeue();
                if (!visitedProjects.Add(projectPath))
                {
                    continue;
                }
                XDocument project = lease.LoadXml(projectPath);
                lease.RejectUnleasedImports(project, projectPath);
                string projectDirectory = Path.GetDirectoryName(projectPath)!;
                lease.RejectNestedGlobalJson(projectDirectory);
                lease.AcquireConventionalInputs(projectDirectory);
                foreach (XElement reference in project.Root?.Elements()
                             .Where(element => element.Name.LocalName == "ItemGroup")
                             .SelectMany(group => group.Elements()
                                 .Where(element => element.Name.LocalName == "ProjectReference"))
                         ?? [])
                {
                    string? include = reference.Attributes()
                        .FirstOrDefault(attribute => attribute.Name.LocalName == "Include")?.Value;
                    if (string.IsNullOrWhiteSpace(include))
                    {
                        continue;
                    }
                    if (!IsStaticReference(reference, include))
                    {
                        throw new InvalidDataException(
                            $"ProjectReference must use a literal, unconditional path: {projectPath}");
                    }
                    string referencePath = Path.GetFullPath(include, projectDirectory);
                    lease.EnsureWithinRoot(referencePath, "raw ProjectReference");
                    lease.AcquireDirectoryTree(
                        lease.SolutionRoot,
                        Path.GetDirectoryName(referencePath)!);
                    lease.AcquireRequiredFile(referencePath, "referenced project");
                    pendingProjects.Enqueue(referencePath);
                }
            }

            if (portableMirror)
            {
                lease.BuildPortableMirror();
            }
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    public KarpikSolutionModel CreateEvaluationSolution(KarpikSolutionModel original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (EvaluationRoot is null)
        {
            return original;
        }
        return original with
        {
            SolutionPath = MapOriginalToEvaluation(original.SolutionPath),
            Projects = original.Projects.Select(project => project with
            {
                ProjectPath = MapOriginalToEvaluation(project.ProjectPath),
                ProjectReferences = project.ProjectReferences
                    .Select(MapOriginalToEvaluation)
                    .ToArray()
            }).ToArray()
        };
    }

    public IReadOnlyList<MsBuildProjectEvaluation> RemapEvaluations(
        IReadOnlyList<MsBuildProjectEvaluation> evaluations)
    {
        ArgumentNullException.ThrowIfNull(evaluations);
        if (EvaluationRoot is null)
        {
            return evaluations;
        }
        return evaluations.Select(evaluation => evaluation with
        {
            ProjectPath = MapEvaluationPathToOriginal(evaluation.ProjectPath),
            RuntimeBundlePath = RemapPathValuedResult(evaluation.RuntimeBundlePath),
            TargetPath = RemapPathValuedResult(evaluation.TargetPath),
            ProjectReferences = evaluation.ProjectReferences
                .Select(MapEvaluationPathToOriginal)
                .ToArray()
        }).ToArray();
    }

    public Stream OpenRead(string absolutePath)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        string normalized = Path.GetFullPath(absolutePath);
        if (!_files.TryGetValue(normalized, out LeasedFile? file))
        {
            throw new FileNotFoundException(
                "The requested project input is not part of the retained lease.",
                normalized);
        }
        return file.OpenView();
    }

    public bool Exists(string absolutePath)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed), this);
        return _files.ContainsKey(Path.GetFullPath(absolutePath));
    }

    public IDisposable Retain()
    {
        lock (_lifetimeGate)
        {
            ObjectDisposedException.ThrowIf(_ownerReleased || _disposed, this);
            _retentionCount++;
            return new Retention(this);
        }
    }

    private void AcquireDirectoryAncestors(string directoryPath)
    {
        var ancestors = new Stack<string>();
        for (DirectoryInfo? current = new(Path.GetFullPath(directoryPath));
             current is not null;
             current = current.Parent)
        {
            ancestors.Push(current.FullName);
        }

        while (ancestors.Count > 0)
        {
            string path = ancestors.Pop();
            bool isSolutionRoot = PathComparer.Equals(path, SolutionRoot);
            AcquireDirectory(path, blockWrites: isSolutionRoot);
        }
    }

    private void AcquireDirectoryTree(string root, string directoryPath)
    {
        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedDirectory = Path.GetFullPath(directoryPath);
        EnsureWithinRoot(normalizedDirectory, "project directory");
        AcquireDirectory(normalizedRoot, blockWrites: true);
        string relative = Path.GetRelativePath(normalizedRoot, normalizedDirectory);
        if (relative == ".")
        {
            return;
        }

        string current = normalizedRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                break;
            }
            AcquireDirectory(current, blockWrites: true);
        }
    }

    private void AcquireConventionalInputs(string startDirectory)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(startDirectory));
             current is not null;
             current = current.Parent)
        {
            if (!IsWithinRoot(current.FullName, SolutionRoot))
            {
                break;
            }
            AcquireDirectory(
                current.FullName,
                blockWrites: IsWithinRoot(current.FullName, SolutionRoot));
            foreach (string fileName in ConventionalEvaluationFiles)
            {
                string path = Path.Combine(current.FullName, fileName);
                bool acquired = TryAcquireFile(path);
                if (acquired
                    && (fileName.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                        || fileName.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)))
                {
                    RejectUnleasedImports(LoadXml(path), path);
                }
            }
        }
    }

    private void RejectNestedGlobalJson(string projectDirectory)
    {
        for (DirectoryInfo? current = new(Path.GetFullPath(projectDirectory));
             current is not null && IsWithinRoot(current.FullName, SolutionRoot);
             current = current.Parent)
        {
            string path = Path.Combine(current.FullName, "global.json");
            if (PathComparer.Equals(path, Path.Combine(SolutionRoot, "global.json")))
            {
                continue;
            }
            if (File.Exists(path))
            {
                AcquireRequiredFile(path, "nested global.json");
                throw new InvalidDataException(
                    $"Nested global.json would change SDK resolution and is not allowed: {path}");
            }
        }
    }

    private void RejectUnleasedImports(XDocument document, string inputPath)
    {
        foreach (XElement import in document.Descendants()
                     .Where(element => element.Name.LocalName == "Import"))
        {
            string? project = import.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Project")?.Value;
            string? sdk = import.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Sdk")?.Value;
            if (!string.IsNullOrWhiteSpace(project) && string.IsNullOrWhiteSpace(sdk))
            {
                throw new InvalidDataException(
                    $"Explicit MSBuild Project imports are not supported by the transactional input lease: {inputPath}");
            }
        }
    }

    private static bool IsStaticReference(XElement reference, string include) =>
        reference.Attributes().All(attribute =>
            attribute.Name.LocalName != "Condition") &&
        reference.Parent?.Attributes().All(attribute =>
            attribute.Name.LocalName != "Condition") == true &&
        !include.Contains("$(", StringComparison.Ordinal) &&
        !include.Contains("@(", StringComparison.Ordinal) &&
        !include.Contains("%(", StringComparison.Ordinal) &&
        include.IndexOfAny(['*', '?', ';']) < 0;

    private XDocument LoadXml(string path)
    {
        using Stream stream = OpenRead(path);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private void AcquireRequiredFile(string path, string description)
    {
        if (!TryAcquireFile(path))
        {
            throw new FileNotFoundException(
                $"Required {description} was not found.",
                path);
        }
    }

    private bool TryAcquireFile(string path)
    {
        string normalized = Path.GetFullPath(path);
        if (_files.ContainsKey(normalized))
        {
            return true;
        }
        if (!File.Exists(normalized))
        {
            return false;
        }

        SafeFileHandle handle = OperatingSystem.IsWindows()
            ? NativeLease.OpenFile(normalized)
            : NativeLease.OpenUnixFile(normalized);
        try
        {
            NativeLease.EnsureNotReparsePoint(handle, normalized);
            var stream = new FileStream(
                handle,
                FileAccess.Read,
                bufferSize: 16 * 1024,
                isAsync: false);
            _files.Add(normalized, new LeasedFile(stream));
            return true;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private void AcquireDirectory(string path, bool blockWrites)
    {
        string normalized = NormalizeDirectoryPath(path);
        if (_directories.ContainsKey(normalized))
        {
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            if (!Directory.Exists(normalized)
                || new DirectoryInfo(normalized).LinkTarget is not null
                || (File.GetAttributes(normalized) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Project input directory is missing or linked: {normalized}");
            }
            _directories.Add(normalized, null);
            return;
        }

        SafeFileHandle handle = NativeLease.OpenDirectory(normalized, blockWrites);
        try
        {
            NativeLease.EnsureNotReparsePoint(handle, normalized);
            _directories.Add(normalized, handle);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private void BuildPortableMirror()
    {
        const int maximumFiles = 4_096;
        const long maximumIndividualBytes = 16L * 1024 * 1024;
        const long maximumAggregateBytes = 64L * 1024 * 1024;
        if (_files.Count > maximumFiles)
        {
            throw new InvalidDataException(
                $"Portable project metadata exceeds {maximumFiles} files.");
        }

        _mirrorOwnerRoot = Path.Combine(
            Path.GetTempPath(),
            $"karpik-project-input-{Guid.NewGuid():N}");
        EvaluationRoot = Path.Combine(_mirrorOwnerRoot, "root");
        Directory.CreateDirectory(EvaluationRoot);
        long aggregate = 0;
        foreach ((string originalPath, LeasedFile file) in _files)
        {
            EnsureWithinRoot(originalPath, "portable metadata input");
            long length = file.Length;
            if (length > maximumIndividualBytes
                || aggregate > maximumAggregateBytes - length)
            {
                throw new InvalidDataException(
                    "Portable project metadata exceeds its file or aggregate byte bound.");
            }
            aggregate += length;

            string destination = MapOriginalToEvaluation(originalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using Stream source = file.OpenView();
            using var output = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            source.CopyTo(output);
        }
    }

    private string MapOriginalToEvaluation(string originalPath)
    {
        if (EvaluationRoot is null)
        {
            return Path.GetFullPath(originalPath);
        }
        EnsureWithinRoot(originalPath, "portable evaluation input");
        return Path.GetFullPath(
            Path.Combine(
                EvaluationRoot,
                Path.GetRelativePath(SolutionRoot, Path.GetFullPath(originalPath))));
    }

    private string MapEvaluationPathToOriginal(string evaluationPath)
    {
        if (EvaluationRoot is null)
        {
            return Path.GetFullPath(evaluationPath);
        }
        string normalized = Path.GetFullPath(evaluationPath);
        if (!IsWithinRoot(normalized, EvaluationRoot))
        {
            throw new InvalidDataException(
                $"MSBuild returned a project graph path outside the owned metadata mirror: {normalized}");
        }
        return Path.GetFullPath(
            Path.Combine(
                SolutionRoot,
                Path.GetRelativePath(EvaluationRoot, normalized)));
    }

    private string RemapPathValuedResult(string path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !Path.IsPathFullyQualified(path)
            || EvaluationRoot is null)
        {
            return path;
        }
        string normalized = Path.GetFullPath(path);
        if (IsWithinRoot(normalized, EvaluationRoot))
        {
            return MapEvaluationPathToOriginal(normalized);
        }
        if (_mirrorOwnerRoot is not null && IsWithinRoot(normalized, _mirrorOwnerRoot))
        {
            throw new InvalidDataException(
                $"MSBuild returned an unmappable path inside the owned metadata staging area: {normalized}");
        }
        return normalized;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length
            ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : full;
    }

    private void EnsureWithinRoot(string path, string description)
    {
        if (!IsWithinRoot(path, SolutionRoot))
        {
            throw new InvalidDataException(
                $"{description} escapes the solution root: {path}");
        }
    }

    private static bool IsWithinRoot(string path, string root)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        bool disposeResources;
        lock (_lifetimeGate)
        {
            if (_ownerReleased)
            {
                return;
            }
            _ownerReleased = true;
            disposeResources = _retentionCount == 0;
        }
        if (disposeResources)
        {
            DisposeResources();
        }
    }

    private void ReleaseRetention()
    {
        bool disposeResources;
        lock (_lifetimeGate)
        {
            if (_retentionCount <= 0)
            {
                return;
            }
            _retentionCount--;
            disposeResources = _ownerReleased && _retentionCount == 0;
        }
        if (disposeResources)
        {
            DisposeResources();
        }
    }

    private void DisposeResources()
    {
        lock (_lifetimeGate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        foreach (LeasedFile file in _files.Values)
        {
            file.Dispose();
        }
        foreach (SafeFileHandle? directory in _directories.Values)
        {
            directory?.Dispose();
        }
        _files.Clear();
        _directories.Clear();
        TryDeleteOwnedMirror(_mirrorOwnerRoot);
        _mirrorOwnerRoot = null;
        EvaluationRoot = null;
    }

    private sealed class Retention(ProjectInputLease owner) : IDisposable
    {
        private ProjectInputLease? _owner = owner;

        public void Dispose() =>
            Interlocked.Exchange(ref _owner, null)?.ReleaseRetention();
    }

    private static void TryDeleteOwnedMirror(string? ownerRoot)
    {
        if (string.IsNullOrWhiteSpace(ownerRoot))
        {
            return;
        }
        try
        {
            string normalized = Path.GetFullPath(ownerRoot);
            string temporaryRoot = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!IsWithinRoot(normalized, temporaryRoot)
                || !Path.GetFileName(normalized).StartsWith(
                    "karpik-project-input-",
                    StringComparison.Ordinal))
            {
                return;
            }

            const int maximumEntries = 4_096;
            int entries = 0;
            var directories = new Stack<string>();
            var pending = new Stack<string>();
            pending.Push(normalized);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                directories.Push(directory);
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (++entries > maximumEntries)
                    {
                        return;
                    }
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            Directory.Delete(entry);
                        }
                        else
                        {
                            File.Delete(entry);
                        }
                    }
                    else if ((attributes & FileAttributes.Directory) != 0)
                    {
                        pending.Push(entry);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }
            foreach (string directory in directories)
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception exception) when (exception is IOException
                                         or UnauthorizedAccessException
                                         or NotSupportedException)
        {
            // A suspicious or concurrently changed mirror is retained for inspection.
        }
    }

    private sealed class LeasedFile(FileStream stream) : IDisposable
    {
        private readonly object _gate = new();
        public long Length => stream.Length;

        public Stream OpenView()
        {
            Monitor.Enter(_gate);
            try
            {
                stream.Position = 0;
                return new NonClosingStream(stream, _gate);
            }
            catch
            {
                Monitor.Exit(_gate);
                throw;
            }
        }

        public void Dispose() => stream.Dispose();
    }

    private sealed class NonClosingStream(Stream inner, object gate) : Stream
    {
        private bool _disposed;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                Monitor.Exit(gate);
            }
            base.Dispose(disposing);
        }
    }

    private static class NativeLease
    {
        private const uint GenericRead = 0x80000000;
        private const uint FileListDirectory = 0x00000001;
        private const uint FileReadAttributes = 0x00000080;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileAttributeNormal = 0x00000080;
        private const uint FileFlagSequentialScan = 0x08000000;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOpenReparsePoint = 0x00200000;
        private const int FileAttributeTagInfo = 9;
        private const int UnixReadOnly = 0;
        private const int LinuxNoFollow = 0x20000;
        private const int LinuxCloseOnExec = 0x80000;
        private const int MacNoFollow = 0x100;
        private const int MacCloseOnExec = 0x1000000;

        public static SafeFileHandle OpenFile(string path) =>
            Open(
                path,
                GenericRead,
                FileShareRead,
                FileAttributeNormal | FileFlagSequentialScan | FileFlagOpenReparsePoint);

        public static SafeFileHandle OpenDirectory(string path, bool blockWrites) =>
            Open(
                path,
                FileListDirectory | FileReadAttributes,
                blockWrites ? FileShareRead : FileShareRead | FileShareWrite,
                FileFlagBackupSemantics | FileFlagOpenReparsePoint);

        public static SafeFileHandle OpenUnixFile(string path)
        {
            int flags = UnixReadOnly | (OperatingSystem.IsMacOS()
                ? MacNoFollow | MacCloseOnExec
                : LinuxNoFollow | LinuxCloseOnExec);
            int descriptor = OpenUnix(path, flags);
            if (descriptor < 0)
            {
                throw new IOException(
                    $"Unable to open project metadata '{path}' without following symbolic links.",
                    new Win32Exception(Marshal.GetLastPInvokeError()));
            }
            return new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        }

        public static void EnsureNotReparsePoint(SafeFileHandle handle, string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }
            if (!GetFileInformationByHandleEx(
                    handle,
                    FileAttributeTagInfo,
                    out FileAttributeTagInformation information,
                    (uint)Marshal.SizeOf<FileAttributeTagInformation>()))
            {
                throw new IOException(
                    $"Unable to inspect leased project input '{path}'.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
            if ((information.FileAttributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Project input contains a link or reparse point: {path}");
            }
        }

        private static SafeFileHandle Open(
            string path,
            uint desiredAccess,
            uint shareMode,
            uint flags)
        {
            SafeFileHandle handle = CreateFileW(
                path,
                desiredAccess,
                shareMode,
                IntPtr.Zero,
                OpenExisting,
                flags,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                if (error is 2 or 3)
                {
                    throw new FileNotFoundException("Project input was not found.", path);
                }
                throw new IOException(
                    $"Unable to acquire project input lease for '{path}'.",
                    new Win32Exception(error));
            }
            return handle;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle file,
            int fileInformationClass,
            out FileAttributeTagInformation fileInformation,
            uint bufferSize);

        [DllImport("libc", EntryPoint = "open", SetLastError = true)]
        private static extern int OpenUnix(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            int flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct FileAttributeTagInformation
        {
            public uint FileAttributes;
            public uint ReparseTag;
        }
    }

    private static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
