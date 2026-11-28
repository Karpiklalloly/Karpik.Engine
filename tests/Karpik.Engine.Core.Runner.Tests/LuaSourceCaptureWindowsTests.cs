using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Modding.Lua;
using MoonSharp.Interpreter;
using Xunit;

public sealed class LuaSourceCaptureWindowsTests
{
    [Fact]
    public void NativeEntryPointAndFrozenImportsAgreeWithoutFoldingIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;

        string root = Directory.CreateTempSubdirectory("karpik-lua-case-").FullName;
        try
        {
            const ExecutionSide side = ExecutionSide.Server;
            const string helperText = "return 'pinned'";
            string canonical = WritePackage("canonical", "Shared", "main.lua");
            string aliasedSlice = WritePackage("aliased-slice", "shared", "main.lua");
            string mixed = WritePackage("mixed", "shared", "MAIN.LUA");
            var accepted = LuaSourceCapture.Capture(canonical, side);

            Assert.Equal(accepted.SharedIdentity, LuaSourceCapture.Capture(aliasedSlice, side).SharedIdentity);
            Assert.Equal("Shared/main.lua", Assert.Single(LuaSourceCapture.ObserveEntryPoints(mixed, side)));
            var captured = LuaSourceCapture.Capture(mixed, side, required: accepted);

            Assert.True(LuaSourceCapture.HasEntryPoint(captured));
            Assert.True(captured.TryGetFile("Shared/main.lua", out var entry));
            Assert.Equal("return {}", entry);
            Assert.Equal(accepted.FileCount, captured.FileCount);
            Assert.NotEqual(accepted.SharedIdentity, captured.SharedIdentity);
            Assert.Equal(accepted.SideIdentity, captured.SideIdentity);

            File.Delete(Path.Combine(mixed, "shared", "Tools", "Helper.LUA"));
            var loader = new ModScriptLoader(captured);
            Assert.True(loader.ScriptFileExists("Shared/tools/helper.lua"));
            Assert.Equal(helperText, Assert.IsType<string>(loader.LoadFile("Shared/tools/helper.lua", new Script().Globals)));

            string WritePackage(string name, string slice, string entryName)
            {
                string package = Path.Combine(root, name);
                string shared = Path.Combine(package, slice);
                Directory.CreateDirectory(Path.Combine(shared, "Tools"));
                File.WriteAllText(Path.Combine(shared, entryName), "return {}");
                File.WriteAllText(Path.Combine(shared, "Tools", "Helper.LUA"), helperText);
                return package;
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
