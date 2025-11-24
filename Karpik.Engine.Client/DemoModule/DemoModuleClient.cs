using System.Numerics;
using Game.Generated.Client;
using ImGuiNET;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Shared;
using Karpik.Engine.Shared.DEMO;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.Modding;
using Network;
using Raylib_cs;

namespace Karpik.Engine.Client;

public class DemoModuleClient : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new MySystem())
            .Add(new SetLocalPlayerSystem())
            .Add(new DisplaySystem())
            .Add(new InputSystem())
            .AddCaller<SetLocalPlayerTargetRpc>();
    }
}

public class MySystem : IEcsRunParallel, IEcsInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> local = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }
    
    private bool[] _bools = new bool[1];
    
    [DI] private ModManager _modManager;
    [DI] private EcsDefaultWorld _world;
    [DI] private EcsEventWorld _eventWorld;
    [DI] private AssetsManager _assetsManager;
    [DI] private Rpc _rpc;
    [DI] private Input _input;
    [DI] private UIManager _uiManager;
    
    public void Init()
    {
        
    }
    
    public void RunParallel()
    {
        ImGui.Begin("DemoWindow");
        ShowButtons();
        ShowStats();
        var span = _world.Where(out Aspect a);
        if (span.Count > 0)
        {
            
            ImGui.Text($"Local Player (net id): {a.networkId.Get(span[0]).Id}");
            ImGui.Text($"Local Player (local id): {span[0]}");
        }
        else
        {
            ImGui.Text("No entities with Position component found.");
        }
        ImGui.End();

        ImGui.Begin("UI");
        PrintUI(_uiManager.Root);
        ImGui.End();


        if (_input.IsPressed(KeyboardKey.Escape))
        {
            Time.IsPaused = !Time.IsPaused;
        }
    }

    private void ShowButtons()
    {
        ImGui.Columns(5);
        if (ImGui.Button("Spawn Player"))
        {
            _ = Spawn("Player.json");
        }

        ImGui.NextColumn();
        if (ImGui.Button("Spawn Enemy"))
        {
            _ = Spawn("Enemy.json");
        }
        
        ImGui.NextColumn();
        if (ImGui.Button("Reload Mods"))
        {
            _modManager.ReloadAllMods(_assetsManager.ModsPath);
            _rpc.ReloadMods(new ReloadModsCommand());
        }

        ImGui.NextColumn();
        ImGui.Columns(1);
    }

    private void ShowStats()
    {
        ImGui.Text($"Total time: {Time.TotalTime:F2}");
        ImGui.Text($"Delta time: {Time.DeltaTime}");
        ImGui.Text($"FPS: {Raylib.GetFPS()}");
        ImGui.Text($"Entities: {_world.Entities.Count}");
        ImGui.Text($"Event Entities: {_eventWorld.Entities.Count}");
        if (_world.Entities.Count > 0)
        {
            var span = _world.Where(out Aspect a);
            var pos = a.position.Get(span[0]);
            ImGui.Text($"Player Position: {pos.X:F2}, {pos.Y:F2}");
        }

        ImGui.Checkbox("Auto move player", ref _bools[0]);
        if (_bools[0])
        {
            var span = _world.Where(out Aspect a);
            if (span.Count == 0) return;
            _rpc.Move(new MoveCommand()
            {
                Source = -1,
                Target = a.networkId.Get(span[0]).Id,
                Direction = new Vector3(1, 0, 0) // Move right
            });
        }
    }

    private void PrintUI(UIElement element, int indent = 0)
    {
        bool print = ImGui.Button("Print");
        Print(element, indent, print);
    }

    private void Print(UIElement element, int indent = 0, bool print = false)
    {
        var box = element.LayoutBox;
        var text =
            $"<Element id='{element.Id}' class='{string.Join(" ", element.Classes)}'> Content: X={box.ContentRect.X:F0}, Y={box.ContentRect.Y:F0}, W={box.ContentRect.Width:F0}, H={box.ContentRect.Height:F0}";
        var margin =
            $"Margin: X={box.MarginRect.X:F0}, Y={box.MarginRect.Y:F0}, W={box.MarginRect.Width:F0}, H={box.MarginRect.Height:F0}";
        var padding =
            $"Padding: X={box.PaddingRect.X:F0}, Y={box.PaddingRect.Y:F0}, W={box.PaddingRect.Width:F0}, H={box.PaddingRect.Height:F0}";
        var border =
            $"Border: X={box.BorderRect.X:F0}, Y={box.BorderRect.Y:F0}, W={box.BorderRect.Width:F0}, H={box.BorderRect.Height:F0}";
        var content =
            $"Content: X={box.ContentRect.X:F0}, Y={box.ContentRect.Y:F0}, W={box.ContentRect.Width:F0}, H={box.ContentRect.Height:F0}";

        if (print)
        {
            Console.WriteLine(text);
            Console.WriteLine(margin);
            Console.WriteLine(padding);
            Console.WriteLine(border);
            Console.WriteLine(content);
        }

        
        if (ImGui.CollapsingHeader(text))
        {
            ImGui.Indent(indent * 2);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            if (ImGui.CollapsingHeader("style " + text))
            {
                ImGui.Text(margin);
                ImGui.Text(padding);
                ImGui.Text(border);
                ImGui.Text(content);

                foreach (var (key, value) in element.ComputedStyle)
                {
                    ImGui.Text($"{key}: {value}");
                }
            }
            ImGui.PopStyleColor(1);

            foreach (var child in element.Children)
            {
                Print(child, indent + 5, print);
            }

            ImGui.Unindent(indent);
        }
    }

    private async Task Spawn(string path)
    {
        using var handle = await _assetsManager.LoadAssetAsync<ComponentsTemplate>(path);
        var e = _world.NewEntityLong();
        handle.Asset.ApplyTo(e.ID, _world);
    }
}