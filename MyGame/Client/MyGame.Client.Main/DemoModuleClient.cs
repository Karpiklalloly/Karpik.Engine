using System.Numerics;
using DCFApixels.DragonECS;
using ImGuiNET;
using Karpik.Engine.Client;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Base;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Extensions;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Jobs;

namespace Karpik.Engine.MyGame.Client.Main;

public class DemoModuleClient : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new MySystem())
            .Add(new SetLocalPlayerSystem())
            .Add(new DisplaySystem())
            .Add(new DrawSpriteSystem())
            .Add(new InputSystem())
            .AddCaller<SetLocalPlayerTargetRpc>();
    }
}

public class MySystem : BaseSystem, IEcsRunParallel, IEcsInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> local = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }
    
    private bool[] _bools = new bool[1];
    
    [DI] private IModManager _modManager = null!;
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private EcsEventWorld _eventWorld = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private IRenderer _renderer = null!;
    [DI] private IRpc _rpc = null!;
    [DI] private Input _input = null!;
    [DI] private UIManager _uiManager = null!;
    
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


        if (_input.IsPressed(KeyboardKeys.Escape))
        {
            Time.IsPaused = !Time.IsPaused;
        }
    }

    private void ShowButtons()
    {
        ImGui.Columns(5);
        if (ImGui.Button("Spawn Player"))
        {
            Spawn("Player.json");
        }

        ImGui.NextColumn();
        if (ImGui.Button("Spawn Enemy"))
        {
            Spawn("Enemy.json");
        }
        
        ImGui.NextColumn();
        if (ImGui.Button("Reload Mods"))
        {
            _modManager.ReloadAllMods(_assetsManager.ModsPath);
            _rpc.ReloadMods(new ReloadModsCommand());
        }
        
        ImGui.NextColumn();
        if (ImGui.Button("Generate Demo Entity"))
        {
            _ = CreateTemplate();
        }
        
        ImGui.NextColumn();
        if (ImGui.Button("Clear World"))
        {
            var entities = _world.Entities;
            foreach (var entity in entities)
            {
                _world.DelEntity(entity);
            }
        }

        ImGui.NextColumn();
        ImGui.Columns(1);
    }

    private void ShowStats()
    {
        ImGui.Text($"Total time: {Time.TotalTime:F2}");
        ImGui.Text($"Delta time: {Time.DeltaTime}");
        ImGui.Text($"FPS: {_renderer.GetFPS().ToString()}");
        ImGui.Text($"Entities: {_world.Entities.Count}");
        ImGui.Text($"Event Entities: {_eventWorld.Entities.Count}");
        if (_world.Entities.Count > 0)
        {
            var span = _world.Where(out Aspect a);
            if (span.Count > 0)
            {
                var pos = a.position.Get(span[0]);
                ImGui.Text($"Player Position: {pos.X:F2}, {pos.Y:F2}");
            }
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
            $"<Element id='{element.Id}' " +
            $"class='{string.Join(" ", element.Classes)}'> " +
            $"Content: X={box.ContentRect.X:F0}, Y={box.ContentRect.Y:F0}, W={box.ContentRect.Width:F0}, H={box.ContentRect.Height:F0}";
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

    private async JobHandle Spawn(string path)
    {
        AssetHandle<ComponentsTemplateAsset> handle = new();
        try
        {
            handle = await _assetsManager.LoadAssetAsync<ComponentsTemplateAsset>(path);
            var entity = CreateEntity(_world);
            await handle.Asset.Template.ApplyTo(entity.ID, _world);
        }
        catch (Exception e)
        {
            await Logger.Instance.Log(e.ToString(), LogLevel.Error);
        }
        finally
        {
            handle.Dispose();
        }
    }
    
    private async JobHandle CreateTemplate()
    {
        await Job.Run(() =>
        {
            var entity = CreateEntity(_world);
            entity.Add<Health>().Value = 255;
            entity.Add<Player>();
            ref var pos = ref entity.Add<Position>();
            pos.X = 10;
            pos.Y = 20;
            pos.Z = -15;
            try
            {
                var components = _world.GetComponentsFor(entity.ID);
                var template = new ComponentsTemplate(components.ToArray().Cast<IEcsComponentMember>().ToArray());
                var asset = new ComponentsTemplateAsset()
                {
                    RawValue = template
                };
                var handle = _assetsManager.SaveAssetAsync(
                    asset,
                    "Player.json");
                handle.GetAwaiter().GetResult().Dispose();
            }
            catch (Exception e)
            {
                Logger.Instance.Log(e.ToString(), LogLevel.Error).GetAwaiter().GetResult();
            }
        });
    }
}