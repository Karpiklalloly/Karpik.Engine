using Microsoft.Build.Framework;
using System.Text;

namespace Karpik.Engine.Sdk.Tasks;

/// <summary>
/// Минимальная файловая абстракция для управляемого тестирования публикации runtime bundle.
/// </summary>
public class RuntimeBundleFileSystem
{
    /// <summary>Перемещает каталог в новое расположение.</summary>
    public virtual void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
    /// <summary>Удаляет файл.</summary>
    public virtual void DeleteFile(string path) => File.Delete(path);
}

/// <summary>
/// MSBuild-задача, создающая и атомарно публикующая client- или server-runtime bundle игры.
/// </summary>
public sealed partial class BuildKarpikRuntimeBundleTask : Microsoft.Build.Utilities.Task
{
    /// <summary>Максимальное число записей в дереве bundle.</summary>
    public const int MaxTreeEntries = 32_768;
    /// <summary>Максимальная глубина дерева bundle.</summary>
    public const int MaxTreeDepth = 64;
    /// <summary>Максимальный размер manifest-файла модулей в байтах.</summary>
    public const int MaxManifestBytes = 1024 * 1024;
    /// <summary>Максимальное число записей в manifest-файле модулей.</summary>
    public const int MaxManifestEntries = 4_096;
    /// <summary>Максимальный размер одного файла bundle в байтах.</summary>
    public const long MaxIndividualFileBytes = 4L * 1024 * 1024 * 1024;
    /// <summary>Максимальный суммарный размер bundle в байтах.</summary>
    public const long MaxBundleBytes = 32L * 1024 * 1024 * 1024;
    /// <summary>Точное содержимое маркера завершённой публикации bundle.</summary>
    public const string BundleCompletionMarker = "karpik-runtime-bundle-v1\n";
    /// <summary>Точное содержимое маркера завершённого staging модулей.</summary>
    public const string ModuleCompletionMarker = "karpik-module-staging-v1\n";
    /// <summary>Префикс маркера стороны runtime bundle.</summary>
    public const string SideMarkerPrefix = "karpik-runtime-side-v1:";
    /// <summary>Маркер staging-каталога, которым владеет эта задача.</summary>
    private const string OwnedStagingMarker = "karpik-runtime-owned-staging-v1\n";

    private readonly RuntimeBundleFileSystem _fileSystem;

    /// <summary>Создаёт задачу с файловой системой по умолчанию.</summary>
    public BuildKarpikRuntimeBundleTask() : this(new RuntimeBundleFileSystem()) { }

    /// <summary>Создаёт задачу с заданной файловой системой.</summary>
    public BuildKarpikRuntimeBundleTask(RuntimeBundleFileSystem fileSystem) =>
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    [Required]
    /// <summary>Получает сторону публикуемого bundle: <c>Client</c> или <c>Server</c>.</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>
    /// "Dynamic" (default) keeps the versioned managed module staging layout;
    /// "Static" stages executable payload inputs only: content, mods and native
    /// files — never a managed module manifest or module DLLs.
    /// </summary>
    public string CompositionMode { get; set; } = "Dynamic";

    [Required]
    /// <summary>Получает путь к главной DLL игрового runtime-проекта.</summary>
    public string PrimaryAssembly { get; set; } = string.Empty;

    [Required]
    /// <summary>Получает абсолютный путь назначения runtime bundle.</summary>
    public string BundlePath { get; set; } = string.Empty;

    /// <summary>Получает управляемые DLL для dynamic bundle.</summary>
    public ITaskItem[] Assemblies { get; set; } = [];

    /// <summary>Получает контент, который следует скопировать в bundle.</summary>
    public ITaskItem[] Content { get; set; } = [];

    /// <summary>Получает файлы модов, которые следует скопировать в bundle.</summary>
    public ITaskItem[] Mods { get; set; } = [];

    /// <summary>Получает native-файлы для static bundle.</summary>
    public ITaskItem[] NativeFiles { get; set; } = [];

    /// <summary>Определяет, выбран ли режим статической композиции.</summary>
    private bool IsStaticMode => IsStaticCompositionMode(CompositionMode);

    /// <summary>Проверяет строковое значение режима статической композиции.</summary>
    private static bool IsStaticCompositionMode(string? compositionMode) =>
        string.Equals(compositionMode ?? "Dynamic", "Static", StringComparison.Ordinal);

    /// <summary>Публикует bundle и переводит ожидаемые ошибки в диагностику MSBuild.</summary>
    /// <returns><see langword="true"/> при успешной публикации; иначе <see langword="false"/>.</returns>
    public override bool Execute()
    {
        try
        {
            if (!IsStaticCompositionMode(CompositionMode)
                && !string.Equals(CompositionMode, "Dynamic", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(CompositionMode))
            {
                throw new ArgumentException(
                    $"KarpikCompositionMode must be exactly 'Dynamic' or 'Static'; actual value: '{CompositionMode}'.");
            }

            Publish();
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.LogError($"KARPIK006: Runtime bundle publication failed: {exception.Message}");
            return false;
        }
    }

}
