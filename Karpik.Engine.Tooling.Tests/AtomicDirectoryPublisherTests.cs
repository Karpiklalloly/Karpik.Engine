using Karpik.Engine.Tooling;
using Xunit;

namespace Karpik.Engine.Tooling.Tests;

public sealed class AtomicDirectoryPublisherTests
{
    [Fact]
    public void InvalidCandidateNeverBecomesVisible()
    {
        using var temporary = new TemporaryDirectory();
        var publisher = new AtomicDirectoryPublisher(temporary.RootPath);
        publisher.Recover();
        string staging = publisher.CreateStagingDirectory();
        File.WriteAllText(Path.Combine(staging, "partial"), "partial");
        string destination = Path.Combine(temporary.RootPath, "Engines", "0.6.0");

        Assert.Throws<InvalidDataException>(() => publisher.Publish(staging, destination, _ => false));
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void ValidDestinationConflictIsRejectedWithoutMovingKnownGoodInstallation()
    {
        using var temporary = new TemporaryDirectory();
        var publisher = new TrackingMovePublisher(temporary.RootPath);
        publisher.Recover();
        string staging = publisher.CreateStagingDirectory();
        File.WriteAllText(Path.Combine(staging, "new"), "new");
        string destination = Path.Combine(temporary.RootPath, "Engines", "0.6.0");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "good"), "good");

        Assert.Throws<InvalidOperationException>(() => publisher.Publish(
            staging,
            destination,
            path => File.Exists(Path.Combine(path, path == staging ? "new" : "good"))));
        Assert.True(File.Exists(Path.Combine(destination, "good")));
        Assert.False(File.Exists(Path.Combine(destination, "new")));
        Assert.Equal(0, publisher.MoveCount);
    }

    [Fact]
    public void RecoverRestoresInterruptedReplacementAndCleansOnlyOwnedStaging()
    {
        using var temporary = new TemporaryDirectory();
        string engines = Path.Combine(temporary.RootPath, "Engines");
        Directory.CreateDirectory(engines);
        string transaction = Guid.NewGuid().ToString("N");
        string replacement = Path.Combine(temporary.RootPath, ".replacement", transaction);
        Directory.CreateDirectory(Path.Combine(replacement, "payload"));
        File.WriteAllText(Path.Combine(replacement, ".owned"), "0.6.0");
        File.WriteAllText(Path.Combine(replacement, "payload", "good"), "good");
        var publisher = new AtomicDirectoryPublisher(temporary.RootPath);
        string ownedStaging = publisher.CreateStagingDirectory();
        string unrelated = Path.Combine(temporary.RootPath, ".staging", "unrelated");
        Directory.CreateDirectory(unrelated);

        publisher.Recover();

        Assert.True(File.Exists(Path.Combine(engines, "0.6.0", "good")));
        Assert.False(Directory.Exists(ownedStaging));
        Assert.True(Directory.Exists(unrelated));
    }

    [Fact]
    public void LegacyRecoveryRestoresValidBackupOverInvalidCandidate()
    {
        using var temporary = new TemporaryDirectory();
        string engines = Path.Combine(temporary.RootPath, "Engines");
        string destination = Path.Combine(engines, "0.6.0");
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, "partial"), "partial");
        string replacement = Path.Combine(temporary.RootPath, ".replacement", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(replacement, "payload"));
        File.WriteAllText(Path.Combine(replacement, ".owned"), "0.6.0");
        File.WriteAllText(Path.Combine(replacement, "payload", "good"), "good");
        var publisher = new AtomicDirectoryPublisher(temporary.RootPath);

        publisher.Recover(path => File.Exists(Path.Combine(path, "good")));

        Assert.True(File.Exists(Path.Combine(destination, "good")));
        Assert.False(File.Exists(Path.Combine(destination, "partial")));
        Assert.False(Directory.Exists(replacement));
    }

    [Fact]
    public void LegacyRecoveryDoesNotExposeUnprovenBackupWhenDestinationIsAbsent()
    {
        using var temporary = new TemporaryDirectory();
        string engines = Path.Combine(temporary.RootPath, "Engines");
        string destination = Path.Combine(engines, "0.6.0");
        string replacement = Path.Combine(temporary.RootPath, ".replacement", Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(replacement, "payload");
        Directory.CreateDirectory(payload);
        File.WriteAllText(Path.Combine(replacement, ".owned"), "0.6.0");
        File.WriteAllText(Path.Combine(payload, "unproven"), "unproven");
        var publisher = new AtomicDirectoryPublisher(temporary.RootPath);

        publisher.Recover(_ => false);

        Assert.False(Directory.Exists(destination));
        Assert.True(File.Exists(Path.Combine(payload, "unproven")));
        Assert.True(File.Exists(Path.Combine(replacement, ".owned")));
    }

    [Fact]
    public void PublisherRejectsDestinationTraversalOutsideEnginesRoot()
    {
        using var temporary = new TemporaryDirectory();
        var publisher = new AtomicDirectoryPublisher(temporary.RootPath);
        string staging = publisher.CreateStagingDirectory();
        File.WriteAllText(Path.Combine(staging, "valid"), "valid");
        string escaped = Path.Combine(temporary.RootPath, "Engines", "..", "escaped");

        Assert.Throws<ArgumentException>(() => publisher.Publish(staging, escaped, _ => true));
        Assert.False(Directory.Exists(Path.Combine(temporary.RootPath, "escaped")));
    }

    private sealed class TrackingMovePublisher(string outputRoot) : AtomicDirectoryPublisher(outputRoot)
    {
        public int MoveCount { get; private set; }

        protected override void MoveDirectory(string sourceDirectory, string destinationDirectory)
        {
            MoveCount++;
            base.MoveDirectory(sourceDirectory, destinationDirectory);
        }
    }
}
