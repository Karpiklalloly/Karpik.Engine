using System.Security.Cryptography;
using System.Text;
using Karpik.Engine.Tooling;
using Microsoft.Build.Framework;

namespace Karpik.Engine.Sdk.Tasks;

public sealed partial class BuildKarpikRuntimeBundleTask
{
    private const string InputFingerprintVersion = "karpik-runtime-inputs-v1";

    private string? TryComputeInputFingerprint()
    {
        try
        {
            // ponytail: hashes every input on each build; add a persistent Merkle index only if read cost dominates profiling.
            var inputs = new List<string>();
            if (!IsStaticMode)
            {
                if (!TryAddFingerprintInput(inputs, "assembly", Path.GetFileName(PrimaryAssembly), PrimaryAssembly))
                {
                    return null;
                }
                foreach (ITaskItem assembly in Assemblies)
                {
                    if (!TryAddFingerprintInput(inputs, "assembly", Path.GetFileName(assembly.ItemSpec), assembly.ItemSpec))
                    {
                        return null;
                    }
                }
                if (!string.IsNullOrWhiteSpace(EngineRoot))
                {
                    string catalogPath = Path.Combine(Path.GetFullPath(EngineRoot), "modules", EngineModuleCatalog.FileName);
                    if (!TryAddFingerprintInput(inputs, "engine-catalog", EngineModuleCatalog.FileName, catalogPath))
                    {
                        return null;
                    }
                    foreach (ITaskItem selection in EngineModuleSelections.OrderBy(item => item.ItemSpec, StringComparer.Ordinal))
                    {
                        inputs.Add($"engine-selection\n{selection.ItemSpec}\n{selection.GetMetadata("Enabled")}\n{selection.GetMetadata("Implementation")}");
                    }
                }
            }

            foreach (ITaskItem content in Content)
            {
                if (!TryAddFingerprintInput(inputs, "content", GetTargetPath(content), content.ItemSpec))
                {
                    return null;
                }
            }
            foreach (ITaskItem mod in Mods)
            {
                if (!TryAddFingerprintInput(inputs, "mod", GetTargetPath(mod), mod.ItemSpec))
                {
                    return null;
                }
            }
            if (IsStaticMode)
            {
                foreach (ITaskItem native in NativeFiles)
                {
                    if (!TryAddFingerprintInput(inputs, "native", native.GetMetadata("TargetPath"), native.ItemSpec))
                    {
                        return null;
                    }
                }
            }

            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            AppendPart(hash, InputFingerprintVersion);
            AppendPart(hash, Side);
            AppendPart(hash, CompositionMode);
            foreach (string input in inputs.Order(StringComparer.Ordinal))
            {
                AppendPart(hash, input);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryAddFingerprintInput(ICollection<string> inputs, string kind, string targetPath, string itemPath)
    {
        string source = Path.GetFullPath(itemPath);
        if (!File.Exists(source) || IsReparsePoint(source))
        {
            return false;
        }
        EnsureNotReparse(Path.GetDirectoryName(source)!);
        using FileStream stream = File.OpenRead(source);
        string contentHash = Convert.ToHexString(SHA256.HashData(stream));
        inputs.Add($"{kind}\n{targetPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)}\n{contentHash}");
        return true;
    }

    private static string GetTargetPath(ITaskItem item)
    {
        string targetPath = item.GetMetadata("TargetPath");
        return string.IsNullOrWhiteSpace(targetPath) ? Path.GetFileName(item.ItemSpec) : targetPath;
    }

    private static void AppendPart(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private static string GetInputFingerprintPath(string bundlePath) => bundlePath + ".inputs.v1";
}
