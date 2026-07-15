namespace Karpik.Engine.Tooling;

public class AtomicDirectoryPublisher
{
    private const string OwnedMarkerSuffix = ".owned";
    private const string UnconfirmedProcessMarkerName = ".process-termination-unconfirmed";
    private readonly string _outputRoot;
    private readonly string _enginesRoot;
    private readonly string _stagingRoot;
    private readonly string _replacementRoot;

    public AtomicDirectoryPublisher(string outputRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputRoot);
        _outputRoot = Path.GetFullPath(outputRoot);
        _enginesRoot = Path.Combine(_outputRoot, "Engines");
        _stagingRoot = Path.Combine(_outputRoot, ".staging");
        _replacementRoot = Path.Combine(_outputRoot, ".replacement");
    }

    public string CreateStagingDirectory()
    {
        EnsureOwnedRoots();
        string transactionId = Guid.NewGuid().ToString("N");
        string stagingDirectory = Path.Combine(_stagingRoot, transactionId);
        Directory.CreateDirectory(stagingDirectory);
        File.WriteAllText(GetStagingMarker(transactionId), "karpik-staging-v1");
        return stagingDirectory;
    }

    public void Recover(Func<string, bool>? validateInstallation = null)
    {
        EnsureOwnedRoots();
        RecoverReplacements(validateInstallation);
        RecoverStaging();
    }

    public void Publish(
        string stagingDirectory,
        string destinationDirectory,
        Func<string, bool> validateInstallation)
    {
        ArgumentNullException.ThrowIfNull(validateInstallation);
        EnsureOwnedRoots();
        (string staging, string transactionId) = ValidateOwnedStaging(stagingDirectory);
        string destination = ValidateDestination(destinationDirectory);
        if (!validateInstallation(staging))
        {
            throw new InvalidDataException($"Staging installation did not pass validation: {staging}");
        }
        if (Directory.Exists(destination) && PathSafety.IsReparsePoint(destination))
        {
            throw new InvalidDataException($"Destination is a link or reparse point: {destination}");
        }

        Directory.CreateDirectory(_enginesRoot);
        if (!Directory.Exists(destination))
        {
            try
            {
                MoveDirectory(staging, destination);
                if (!validateInstallation(destination))
                {
                    MoveDirectory(destination, staging);
                    throw new InvalidDataException("Published installation failed post-move validation.");
                }
                File.Delete(GetStagingMarker(transactionId));
                return;
            }
            catch
            {
                if (Directory.Exists(destination) && !Directory.Exists(staging))
                {
                    MoveDirectory(destination, staging);
                }
                throw;
            }
        }

        if (validateInstallation(destination))
        {
            throw new InvalidOperationException(
                $"A valid immutable engine installation already exists at '{destination}'.");
        }

        string replacementId = Guid.NewGuid().ToString("N");
        string replacementDirectory = Path.Combine(_replacementRoot, replacementId);
        string previousPayload = Path.Combine(replacementDirectory, "payload");
        Directory.CreateDirectory(replacementDirectory);
        File.WriteAllText(Path.Combine(replacementDirectory, ".owned"), Path.GetFileName(destination));
        // Only an invalid/incomplete collision may be moved out of the way. A valid
        // installation is immutable and is rejected above without any visibility gap.
        MoveDirectory(destination, previousPayload);
        try
        {
            MoveDirectory(staging, destination);
            if (!validateInstallation(destination))
            {
                throw new InvalidDataException("Published replacement failed post-move validation.");
            }
        }
        catch
        {
            if (Directory.Exists(destination) && !Directory.Exists(staging))
            {
                MoveDirectory(destination, staging);
            }
            if (!Directory.Exists(destination) && Directory.Exists(previousPayload))
            {
                MoveDirectory(previousPayload, destination);
            }
            if (Directory.Exists(replacementDirectory) && !Directory.EnumerateFileSystemEntries(replacementDirectory).Any())
            {
                Directory.Delete(replacementDirectory);
            }
            throw;
        }

        File.Delete(GetStagingMarker(transactionId));
        try
        {
            DeleteOwnedTree(previousPayload);
            File.Delete(Path.Combine(replacementDirectory, ".owned"));
            Directory.Delete(replacementDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The new destination is already fully validated. Owned recovery metadata is
            // intentionally retained so the next run can finish backup cleanup safely.
            if (Directory.Exists(replacementDirectory) && !File.Exists(Path.Combine(replacementDirectory, ".owned")))
            {
                try { File.WriteAllText(Path.Combine(replacementDirectory, ".owned"), Path.GetFileName(destination)); }
                catch (Exception markerException) when (markerException is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    public void AbandonStaging(string stagingDirectory)
    {
        (string staging, string transactionId) = ValidateOwnedStaging(stagingDirectory);
        DeleteOwnedTree(staging);
        File.Delete(GetStagingMarker(transactionId));
    }

    public void PreserveStagingAfterUnconfirmedProcess(string stagingDirectory, int processId)
    {
        (string staging, _) = ValidateOwnedStaging(stagingDirectory);
        File.WriteAllText(
            Path.Combine(staging, UnconfirmedProcessMarkerName),
            $"dotnet process {processId} termination was not confirmed; inspect before removing this marker.{Environment.NewLine}");
    }

    protected virtual void MoveDirectory(string sourceDirectory, string destinationDirectory) =>
        Directory.Move(sourceDirectory, destinationDirectory);

    private void EnsureOwnedRoots()
    {
        Directory.CreateDirectory(_outputRoot);
        if (PathSafety.IsReparsePoint(_outputRoot))
        {
            throw new InvalidDataException($"Publication output root is a link or reparse point: {_outputRoot}");
        }
        foreach (string directory in new[] { _enginesRoot, _stagingRoot, _replacementRoot })
        {
            Directory.CreateDirectory(directory);
            if (PathSafety.IsReparsePoint(directory))
            {
                throw new InvalidDataException($"Owned publication directory is a link or reparse point: {directory}");
            }
        }
    }

    private (string Staging, string TransactionId) ValidateOwnedStaging(string stagingDirectory)
    {
        string staging = Path.GetFullPath(stagingDirectory);
        if (!PathSafety.IsContained(_stagingRoot, staging) ||
            !string.Equals(Path.GetDirectoryName(staging), _stagingRoot, PathSafety.PathComparison))
        {
            throw new ArgumentException("Staging directory must be a direct child of the owned .staging directory.", nameof(stagingDirectory));
        }
        string transactionId = Path.GetFileName(staging);
        if (!Guid.TryParseExact(transactionId, "N", out _) || !File.Exists(GetStagingMarker(transactionId)))
        {
            throw new ArgumentException("Staging directory is not owned by this publisher.", nameof(stagingDirectory));
        }
        if (!Directory.Exists(staging) || PathSafety.IsReparsePoint(staging))
        {
            throw new InvalidDataException($"Owned staging directory is missing or is a reparse point: {staging}");
        }
        return (staging, transactionId);
    }

    private string ValidateDestination(string destinationDirectory)
    {
        string destination = Path.GetFullPath(destinationDirectory);
        if (!PathSafety.IsContained(_enginesRoot, destination) ||
            !string.Equals(Path.GetDirectoryName(destination), _enginesRoot, PathSafety.PathComparison))
        {
            throw new ArgumentException("Destination must be a direct child of the owned Engines directory.", nameof(destinationDirectory));
        }
        string name = Path.GetFileName(destination);
        if (!IsSafeLeafName(name))
        {
            throw new ArgumentException("Destination name is invalid.", nameof(destinationDirectory));
        }
        return destination;
    }

    private void RecoverStaging()
    {
        foreach (string marker in Directory.EnumerateFiles(_stagingRoot, "*" + OwnedMarkerSuffix, SearchOption.TopDirectoryOnly))
        {
            string transactionId = Path.GetFileNameWithoutExtension(marker);
            if (!Guid.TryParseExact(transactionId, "N", out _))
            {
                continue;
            }
            string staging = Path.Combine(_stagingRoot, transactionId);
            if (File.Exists(Path.Combine(staging, UnconfirmedProcessMarkerName)))
            {
                continue;
            }
            if (Directory.Exists(staging))
            {
                DeleteOwnedTree(staging);
            }
            File.Delete(marker);
        }
    }

    private void RecoverReplacements(Func<string, bool>? validateInstallation)
    {
        foreach (string wrapper in Directory.EnumerateDirectories(_replacementRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string transactionId = Path.GetFileName(wrapper);
            string marker = Path.Combine(wrapper, ".owned");
            if (!Guid.TryParseExact(transactionId, "N", out _) || !File.Exists(marker) || PathSafety.IsReparsePoint(wrapper))
            {
                continue;
            }

            string destinationName = File.ReadAllText(marker).Trim();
            if (!IsSafeLeafName(destinationName))
            {
                throw new InvalidDataException($"Invalid owned replacement destination metadata: {marker}");
            }
            string destination = ValidateDestination(Path.Combine(_enginesRoot, destinationName));
            string payload = Path.Combine(wrapper, "payload");
            if (Directory.Exists(payload))
            {
                if (!Directory.Exists(destination))
                {
                    if (validateInstallation is not null && !TryValidateInstallation(validateInstallation, payload))
                    {
                        // Production recovery supplies a validator. Unknown, invalid, or
                        // exceptional legacy state remains owned evidence and is never exposed.
                        continue;
                    }
                    MoveDirectory(payload, destination);
                }
                else if (validateInstallation is null)
                {
                    // Legacy replacement state can contain either the last known-good
                    // payload or a newer valid candidate. Without validation, keep both.
                    continue;
                }
                else if (TryValidateInstallation(validateInstallation, destination))
                {
                    DeleteOwnedTree(payload);
                }
                else if (TryValidateInstallation(validateInstallation, payload))
                {
                    string invalidCandidate = Path.Combine(wrapper, "invalid-candidate");
                    if (Directory.Exists(invalidCandidate))
                    {
                        throw new InvalidDataException($"Legacy recovery already contains an invalid candidate: {invalidCandidate}");
                    }
                    MoveDirectory(destination, invalidCandidate);
                    try
                    {
                        MoveDirectory(payload, destination);
                    }
                    catch
                    {
                        if (!Directory.Exists(destination) && Directory.Exists(invalidCandidate))
                        {
                            MoveDirectory(invalidCandidate, destination);
                        }
                        throw;
                    }
                    DeleteOwnedTree(invalidCandidate);
                }
                else
                {
                    // Neither side is proven valid. Retain owned evidence for diagnosis.
                    continue;
                }
            }
            File.Delete(marker);
            if (!Directory.EnumerateFileSystemEntries(wrapper).Any())
            {
                Directory.Delete(wrapper);
            }
        }
    }

    private static bool TryValidateInstallation(Func<string, bool> validateInstallation, string path)
    {
        try
        {
            return validateInstallation(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private string GetStagingMarker(string transactionId) =>
        Path.Combine(_stagingRoot, transactionId + OwnedMarkerSuffix);

    private static bool IsSafeLeafName(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name is not "." and not ".." &&
        name.IndexOfAny(['/', '\\']) < 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void DeleteOwnedTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        if (PathSafety.IsReparsePoint(path))
        {
            Directory.Delete(path);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (Directory.Exists(entry))
            {
                DeleteOwnedTree(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }
        Directory.Delete(path);
    }
}
