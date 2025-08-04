using System.Numerics;
using Game.Generated.Client;
using ImGuiNET;
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

public class MySystem : IEcsRun, IEcsInject<ModManager>, IEcsInit
{
    private bool[] _bools = new bool[1];
    
    private ModManager _modManager;
    private string _text = string.Empty;
    
    public void Init()
    {
        Input.CharPressed += c =>
        {
            _text += c;
        };
        
        Input.KeyPressed += key =>
        {
            if (key == KeyboardKey.Backspace && _text.Length > 0)
            {
                _text = _text.Substring(0, _text.Length - 1);
            }
        };
    }
    
    public void Run()
    {
        ImGui.Begin("DemoWindow");
        ShowButtons();
        ShowStats();
        var world = Worlds.Instance.World;
        var span = world.Where(EcsStaticMask.Inc<Position>().Build());
        if (span.Count > 0)
        {
            ImGui.Text($"{span[0]}");
        }
        else
        {
            ImGui.Text("No entities with Position component found.");
        }
        ImGui.Text(_text);
        ImGui.End();


        if (Input.IsPressed(KeyboardKey.Escape))
        {
            Time.IsPaused = !Time.IsPaused;
        }
    }

    private void ShowButtons()
    {
        ImGui.Columns(5);
        if (ImGui.Button("Spawn Player"))
        {
            var template = Loader.Instance.Load<ComponentsTemplate>("Player");
            var world = Worlds.Instance.World;
            var e = world.NewEntityLong();
            template.ApplyTo(e.ID, world);
        }

        ImGui.NextColumn();
        if (ImGui.Button("Spawn Enemy"))
        {
            var template = Loader.Instance.Load<ComponentsTemplate>("Enemy");
            var world = Worlds.Instance.World;
            var e = world.NewEntityLong();
            template.ApplyTo(e.ID, world);
        }
        
        ImGui.NextColumn();
        if (ImGui.Button("Reload Mods"))
        {
            _modManager.ReloadAllMods("Mods");
        }

        ImGui.NextColumn();
        ImGui.Columns(1);
    }

    private void ShowStats()
    {
        ImGui.Text($"Total time: {Time.TotalTime:F2}");
        ImGui.Text($"Delta time: {Time.DeltaTime}");
        ImGui.Text($"FPS: {Raylib.GetFPS()}");
        ImGui.Text($"Entities: {Worlds.Instance.World.Entities.Count}");
        ImGui.Text($"Event Entities: {Worlds.Instance.EventWorld.Entities.Count}");
        if (Worlds.Instance.World.Entities.Count > 0)
        {
            var span = Worlds.Instance.World.Where(EcsStaticMask
                .Inc<Player>()
                .Inc<Position>()
                .Inc<NetworkId>().Build());
            var pos = Worlds.Instance.World.GetPool<Position>().Get(span[0]);
            ImGui.Text($"Player Position: {pos.X:F2}, {pos.Y:F2}");
        }
        // if (!Worlds.Instance.MetaWorld.GetPlayer().Player.IsNull)
        // {
        //     var health = Worlds.Instance.MetaWorld.GetPlayer().Player.Get<Health>();
        //     var pos = Worlds.Instance.MetaWorld.GetPlayer().Player.Get<Transform>().Position;
        //     ImGui.Text($"Player Health: {health.Value} of {health.Max.ModifiedValue}");
        //     ImGui.Text($"Player Position: {pos.X:F2}, {pos.Y:F2}");
        // }

        ImGui.Checkbox("Auto move player", ref _bools[0]);
        if (_bools[0])
        {
            var world = Worlds.Instance.World;
            var span = world.Where(EcsStaticMask
                .Inc<LocalPlayer>()
                .Inc<Position>()
                .Inc<NetworkId>().Build());
            if (span.Count == 0) return;
            Rpc.Instance.Move(new MoveCommand()
            {
                Source = -1,
                Target = world.GetPool<NetworkId>().Get(span[0]).Id,
                Direction = new Vector2(1, 0) // Move right
            });
        }
    }

    public void Inject(ModManager obj)
    {
        _modManager = obj;
    }
}