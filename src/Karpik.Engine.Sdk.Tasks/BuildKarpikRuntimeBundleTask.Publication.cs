namespace Karpik.Engine.Sdk.Tasks;

public sealed partial class BuildKarpikRuntimeBundleTask
{
    /// <summary>Проверяет входные пути, восстанавливает незавершённую публикацию и публикует staging.</summary>
    private void Publish()
    {
        if (Side is not ("Client" or "Server"))
        {
            throw new ArgumentException("KarpikSide must be Client or Server for a runtime bundle.");
        }
        if (!Path.IsPathFullyQualified(BundlePath))
        {
            throw new ArgumentException("KarpikRuntimeBundlePath must be absolute.");
        }

        string destination = TrimRoot(Path.GetFullPath(BundlePath));
        string? parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrEmpty(parent))
        {
            throw new ArgumentException("The runtime bundle destination must have a parent directory.");
        }
        EnsureExistingAncestorsNotReparse(parent);
        Directory.CreateDirectory(parent);
        EnsureNotReparse(parent);

        string backup = destination + ".previous";
        RecoverBackup(destination, backup);
        RecoverOwnedStaging(parent, Path.GetFileName(destination));
        if (Directory.Exists(destination) && !IsProvenBundleOutput(destination))
        {
            throw new InvalidDataException($"Refusing to replace unproven directory '{destination}'.");
        }

        string? inputFingerprint = TryComputeInputFingerprint();
        if (inputFingerprint is not null && IsCurrentPublishedBundle(destination, inputFingerprint))
        {
            return;
        }

        string staging = destination + ".staging." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ".karpik-owned-staging"), OwnedStagingMarker);
        try
        {
            if (IsStaticMode)
            {
                MaterializeStatic(staging);
                if (!IsCompleteStaticBundle(staging, Side, allowOwnershipMarker: true))
                {
                    throw new InvalidDataException("The staged static runtime bundle did not pass completion validation.");
                }
            }
            else
            {
                MaterializeDynamic(staging);
                if (!IsCompleteBundle(staging, Side, allowOwnershipMarker: true, requiredPrimaryAssembly: PrimaryAssembly))
                {
                    throw new InvalidDataException("The staged runtime bundle did not pass completion validation.");
                }
            }

            bool hadDestination = Directory.Exists(destination);
            if (hadDestination)
            {
                _fileSystem.MoveDirectory(destination, backup);
            }
            try
            {
                _fileSystem.MoveDirectory(staging, destination);
                _fileSystem.DeleteFile(Path.Combine(destination, ".karpik-owned-staging"));
            }
            catch
            {
                DeleteOwnedPublishedBundle(destination);
                if (!Directory.Exists(destination) && Directory.Exists(backup) && IsProvenBundleOutput(backup))
                {
                    _fileSystem.MoveDirectory(backup, destination);
                }
                throw;
            }

            if (Directory.Exists(backup) && IsProvenBundleOutput(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            if (inputFingerprint is not null)
            {
                File.WriteAllText(GetInputFingerprintPath(destination), inputFingerprint + "\n");
            }
        }
        finally
        {
            DeleteOwnedStaging(staging);
        }
    }

    /// <summary>Проверяет, является ли каталог завершённым bundle, которым безопасно управлять.</summary>
    private bool IsProvenBundleOutput(string path)
    {
        return Directory.Exists(path) && (
            IsCompleteBundle(path, Side, allowOwnershipMarker: false, requiredPrimaryAssembly: null)
            || IsCompleteStaticBundle(path, Side, allowOwnershipMarker: false));
    }

    private bool IsCurrentPublishedBundle(string destination, string inputFingerprint)
    {
        bool complete = IsStaticMode
            ? IsCompleteStaticBundle(destination, Side, allowOwnershipMarker: false)
            : IsCompleteBundle(destination, Side, allowOwnershipMarker: false, requiredPrimaryAssembly: PrimaryAssembly);
        return complete && HasExactUtf8File(GetInputFingerprintPath(destination), inputFingerprint + "\n");
    }

    /// <summary>Восстанавливает или удаляет подтверждённый backup прерванной публикации.</summary>
    private void RecoverBackup(string destination, string backup)
    {
        if (!Directory.Exists(backup))
        {
            return;
        }
        if (IsReparsePoint(backup) || !IsProvenBundleOutput(backup))
        {
            throw new InvalidDataException($"Refusing to move or delete unproven interrupted backup '{backup}'.");
        }
        if (!Directory.Exists(destination))
        {
            _fileSystem.MoveDirectory(backup, destination);
            return;
        }
        if (!IsProvenBundleOutput(destination))
        {
            throw new InvalidDataException($"Interrupted bundle state contains an invalid destination '{destination}'.");
        }
        Directory.Delete(backup, recursive: true);
    }

    /// <summary>Удаляет незавершённые staging-каталоги, подтверждённо созданные этой задачей.</summary>
    private static void RecoverOwnedStaging(string parent, string destinationName)
    {
        int count = 0;
        foreach (string candidate in Directory.EnumerateDirectories(parent, destinationName + ".staging.*", SearchOption.TopDirectoryOnly))
        {
            if (++count > MaxTreeEntries)
            {
                throw new InvalidDataException($"Runtime bundle staging recovery exceeds the {MaxTreeEntries} entry limit.");
            }
            DeleteOwnedStaging(candidate);
        }
    }

    /// <summary>Удаляет безопасный staging-каталог с маркером владения.</summary>
    private static void DeleteOwnedStaging(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        if (IsBoundedTreeWithoutLinks(path) && HasExactUtf8File(marker, OwnedStagingMarker))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>Удаляет опубликованный bundle только после проверки его структуры и маркера владения.</summary>
    private void DeleteOwnedPublishedBundle(string path)
    {
        if (!Directory.Exists(path) || IsReparsePoint(path))
        {
            return;
        }
        string marker = Path.Combine(path, ".karpik-owned-staging");
        bool complete = IsStaticMode
            ? IsCompleteStaticBundle(path, Side, allowOwnershipMarker: true)
            : IsCompleteBundle(path, Side, allowOwnershipMarker: true, requiredPrimaryAssembly: PrimaryAssembly);
        if (complete && HasExactUtf8File(marker, OwnedStagingMarker))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
