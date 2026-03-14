using System.Drawing;
using System.Numerics;
using DCFApixels.DragonECS;
#if DEBUG
using DebugModule;
#endif
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Client.UIToolkit;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.Extensions;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Jobs;

namespace Karpik.Engine.MyGame.Client.Main;

internal class DemoModuleClient : IEcsModule
{
    public void Import(EcsPipeline.Builder b)
    {
        b.Add(new MySystem())
            .Add(new SetLocalPlayerSystem())
            .Add(new DisplaySystem())
            .Add(new DrawSpriteSystem())
            .Add(new FlushDrawersSystem(), EcsConsts.POST_END_LAYER, 50)
            .Add(new InputSystem())
            .AddCaller<SetLocalPlayerTargetRpc>();
    }
}

public class MySystem : IEcsRun, IEcsInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> local = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }

    private const int AUTO_MOVE_PLAYER_INDEX = 0;
    private const int SHOW_WORLD_ENTITIES = 1;
    private bool[] _bools = new bool[2];

    [DI] private IModManager _modManager = null!;
    [DI] private EcsDefaultWorld _world = null!;
    [DI] private EcsEventWorld _eventWorld = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    [DI] private IRenderer _renderer = null!;
    [DI] private IRpc _rpc = null!;
    [DI] private Input _input = null!;
    [DI] private UIManager _uiManager = null!;
    [DI] private Time _time = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D;
    [DI] private IServiceContainer _serviceContainer;

    public void Init()
    {
    }

    public void Run()
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
            _time.IsPaused = !_time.IsPaused;
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
        if (ImGui.Button("Spawn Scene"))
        {
            SpawnPhysicsScene();
        }
        
        // Note: Platformer level should be created on server, not client
        // Client only displays what server sends
        
#if DEBUG
        ImGui.NextColumn();
        if (ImGui.Button("Hot Reload"))
        {
            DebugThings.HotReload();
        }

        ImGui.NextColumn();
#endif
        ImGui.Columns(1);
    }

    private async JobHandle SpawnPhysicsScene()
    {
        int entity1 = _world.NewEntity();

        // 1. Обязательный компонент: Трансформ
        ref var transform1 = ref _world.GetPool<Transform2D>().Add(entity1);
        transform1.Position = new Vector2(0, -5f); // Пол внизу экрана
        transform1.Rotation = 0f;

        // 2. Запрос на создание физики (Статика)
        ref var request1 = ref _world.GetPool<CreateBodyRequest>().Add(entity1);
    
        request1.BodyConfig = new BodyConfig 
        {
            Type = BodyType.Static, // Не двигается
            Friction = 0.5f,
            Restitution = 0.0f,     // Не пружинит
            CategoryBits = 0x0001,  // Слой по умолчанию
            MaskBits = 0xFFFF       // Сталкивается со всем
        };

        request1.ShapeConfig = ShapeConfig.Box(new Vector2(10f, 1f)); // Широкий прямоугольник
        
        var renderer1 = new SpriteRenderer
        {
            Color = Color.White,
            Layer = 0,
            TexturePath = "Sprites/default.jpg",
            Width = 10f,  // Match physics shape width
            Height = 1f   // Match physics shape height
        };
        var r1 = await renderer1.OnLoad(renderer1, _serviceContainer);
        
        var renderer2 = new SpriteRenderer
        {
            Color = Color.White,
            Layer = 0,
            TexturePath = "Sprites/Player.png",
            Width = 1f,   // Match physics shape width
            Height = 1f   // Match physics shape height
        };
        var r2 = await renderer2.OnLoad(renderer2, _serviceContainer);
        
        _world.GetPool<SpriteRenderer>().TryAddOrGet(entity1) = r1;
        
        
        int entity2 = _world.NewEntity();

        // 1. Трансформ (Позиция спавна)
        ref var transform2 = ref _world.GetPool<Transform2D>().Add(entity2);
        transform2.Position = new Vector2(0, 5f); // Ящик высоко в воздухе
        transform2.Rotation = 0.5f; // Слегка повернут для красивого падения

        // 2. Добавляем Velocity, так как мы хотим читать его скорость в будущем
        ref var velocity2 = ref _world.GetPool<Velocity2D>().Add(entity2);
        velocity2.Linear = Vector2.Zero;
        velocity2.Angular = 0f;

        // 3. Запрос на создание физики (Динамика)
        ref var request2 = ref _world.GetPool<CreateBodyRequest>().Add(entity2);
    
        request2.BodyConfig = new BodyConfig 
        {
            Type = BodyType.Dynamic, // Подвержен гравитации
            Mass = 10f,              // Весит 10 кг
            Friction = 0.3f,
            Restitution = 0.4f,      // Слегка отскакивает (bounciness)
            CategoryBits = 0x0001,
            MaskBits = 0xFFFF
        };

        request2.ShapeConfig = ShapeConfig.Box(new Vector2(1f, 1f)); // Квадрат 1x1 метр
        
        _world.GetPool<SpriteRenderer>().TryAddOrGet(entity2) = r2;

        _world.GetPool<Player>().Add(entity2);
    }

    private void ShowStats()
    {
        ImGui.Text($"Total time: {_time.TotalTime:F2}");
        ImGui.Text($"Delta time: {_time.DeltaTime}");
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

        ImGui.Checkbox("Auto move player", ref _bools[AUTO_MOVE_PLAYER_INDEX]);
        if (_bools[AUTO_MOVE_PLAYER_INDEX])
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

        ImGui.Checkbox("Show world entities", ref _bools[SHOW_WORLD_ENTITIES]);
        if (_bools[SHOW_WORLD_ENTITIES])
        {
            ImGui.Begin("World Entities", ref _bools[SHOW_WORLD_ENTITIES]);
            List<IEcsPool> pools = [];
            var entities = _world.Entities;
            foreach (var e in entities)
            {
                if (ImGui.CollapsingHeader($"Entity {e}"))
                {
                    ImGui.Indent();
                    ImGui.PushID(e);
                    _world.GetComponentPoolsFor(e, pools);
                    foreach (var pool in pools)
                    {
                        if (ImGui.CollapsingHeader(pool.ComponentType.Name))
                        {
                            var component = pool.GetRaw(e);
                            ImGui.Text(component.AutoToString());
                        }
                    }
                    ImGui.PopID();
                    ImGui.Unindent();
                }
                
            }
            ImGui.End();
        }

        if (_world.GetPool<PhysicsBodyRef>().Count > 1)
        {
            ImGui.Text($"Position: {_world.GetPool<Transform2D>().Get(2).Position}");
        }
        
        ImGui.Text($"GC: {GC.GetTotalMemory(false) / 1024 / 1024}Mb");
        if (_input.IsPressing(KeyboardKeys.LeftAlt))
        {
            ImGui.Text($"GC: {GC.GetTotalMemory(false) / 1024}Kb");
        }
        
        ImGui.Text($"Camera pos: {_renderer.MainCamera2D.Position}");
        ImGui.Text($"Camera zoom: {_renderer.MainCamera2D.Zoom}");
        var zoom = _renderer.MainCamera2D.Zoom;
        ImGui.SliderFloat($"Camera Zoom", ref zoom, 1, 100);
        _renderer.MainCamera2D.Zoom = zoom;
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

    protected entlong CreateEntity(EcsWorld world)
    {
        lock (world)
        {
            return world.NewEntityLong();
        }
    }
}
