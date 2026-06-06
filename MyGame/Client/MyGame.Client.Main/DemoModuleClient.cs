using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DCFApixels.DragonECS;
using DragonExtensions;
#if DEBUG
using DebugModule;
#endif
using ImGuiNET;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Client.InputModule;
using Karpik.Engine.Core;
using Karpik.Engine.MyGame.Client.Main.Systems;
using Karpik.Engine.MyGame.Shared.Main;
using Karpik.Engine.Shared.AssetManagement.Core;
using Karpik.Engine.Shared.DragonECS;
using Karpik.Engine.Shared.ECS;
using Karpik.Engine.Shared.ECS.Scheduling;
using Karpik.Engine.Shared.Extensions;
using Karpik.Engine.Shared.Log;
using Karpik.Engine.Shared.Modding;
using Karpik.Engine.Shared.Network.Core;
using Karpik.Engine.Shared.Physics.Core;
using Karpik.Jobs;
using Veldrid;

namespace Karpik.Engine.MyGame.Client.Main;

internal class DemoModuleClient : IModule
{
    public void Import(IBuilder b)
    {
        b.Add((object)new MySystem());
        b.Add(new SetLocalPlayerSystem());
        b.Add((object)new ApplySpriteSystem());
        b.Add((object)new DisplaySystem());
        b.Add(new DrawSpriteSystem());
        b.Add((object)new FlushDrawersSystem(), EcsConsts.POST_END_LAYER, 50);
        b.Add(new InputSystem());
        b.AddCaller<SetLocalPlayerTargetRpc>();

#if DEBUG
        // b.Add(new GameUISystem(), EcsConsts.POST_END_LAYER, 60);
#endif
    }
}

[SequentialSystem]
public class MySystem : ISystemUpdate, ISystemInit
{
    class Aspect : EcsAspect
    {
        public EcsReadonlyPool<LocalPlayer> local = Inc;
        public EcsReadonlyPool<Position> position = Inc;
        public EcsReadonlyPool<NetworkId> networkId = Inc;
    }
    
    class CameraAspect : EcsAspect
    {
        public EcsPool<CameraHolder> holder = Inc;
    }

    private const int AUTO_MOVE_PLAYER_INDEX = 0;
    private const int SHOW_WORLD_ENTITIES = 1;
    private bool[] _bools = new bool[2];

    [DI] private IModManager _modManager = null!;
    [DI] private DefaultWorld _world = null!;
    [DI] private EventWorld _eventWorld = null!;
    [DI] private IAssetsManager _assetsManager = null!;
    // [DI] private IRenderer2D _renderer = null!;
    [DI] private IRpc _rpc = null!;
    [DI] private Input _input = null!;
    // [DI] private UIManager _uiManager = null!;
    [DI] private Time _time = null!;
    [DI] private IPhysicsWorld2D _physicsWorld2D;
    [DI] private IServiceContainer _serviceContainer;
    [DI] private ImGuiOverlayState _overlay = null!;

    public void Init()
    {
    }

    public void Update()
    {
        if (!_overlay.Enabled)
        {
            return;
        }
        
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
        // PrintUI(_uiManager.Root);
        ImGui.End();
        
        
        if (_input.IsPressed(Key.Escape))
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
                _world.Del(entity);
            }
        }
        
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

    private void ShowStats()
    {
        ImGui.Text($"Total time: {_time.TotalTime:F2}");
        ImGui.Text($"Delta time: {_time.DeltaTime}");
        ImGui.Text($"FPS: {1 / _time.DeltaTime}");
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
                            ImGui.PushID(component.GetType().Name);

                            foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                            {
                                ImGui.PushID(field.Name);
                                if (field.IsInitOnly)
                                {
                                    ImGui.Text($"{field.Name} (ReadOnly): {field.GetValue(component)}");
                                }
                                else if (field.FieldType == typeof(int))
                                {
                                    var value = (int)field.GetValue(component)!;
                                    if (ImGui.InputInt(field.Name, ref value))
                                    {
                                        field.SetValue(component, value);
                                    }
                                }
                                else if (field.FieldType == typeof(double))
                                {
                                    var value = (double)field.GetValue(component)!;
                                    if (ImGui.InputDouble(field.Name, ref value))
                                    {
                                        field.SetValue(component, value);
                                    }
                                }
                                else if (field.FieldType == typeof(float))
                                {
                                    var value = (float)field.GetValue(component)!;
                                    if (ImGui.InputFloat(field.Name, ref value))
                                    {
                                        field.SetValue(component, value);
                                    }
                                }
                                else if (field.FieldType == typeof(Vector2))
                                {
                                    var value = (Vector2)field.GetValue(component)!;
                                    if (ImGui.InputFloat2(field.Name, ref value))
                                    {
                                        Console.WriteLine("Changed!");
                                        field.SetValue(component, value);
                                    }
                                }
                                else if (field.FieldType == typeof(Color))
                                {
                                    var value = (Color)field.GetValue(component)!;
                                    int[] colors = [value.R, value.G, value.B, value.A];
                                    if (ImGui.InputInt4(field.Name, ref colors[0]))
                                    {
                                        var nextColor = Color.FromArgb(
                                            Math.Clamp(colors[3], 0, 255), // A
                                            Math.Clamp(colors[0], 0, 255), // R
                                            Math.Clamp(colors[1], 0, 255), // G
                                            Math.Clamp(colors[2], 0, 255)  // B
                                        );
                                        field.SetValue(component, nextColor);
                                    }
                                }
                                else
                                {
                                    ImGui.Text($"{field.Name}: {field.GetValue(component)}");
                                }
                                ImGui.PopID();
                            }
                            
                            ImGui.PopID();
                            pool.SetRaw(e, component);
                        }
                    }
                    ImGui.PopID();
                    ImGui.Unindent();
                }
                
            }
            ImGui.End();
        }
        
        if (_world.Base.GetPool<PhysicsBodyRef>().Count > 1)
        {
            ImGui.Text($"Position: {_world.Base.GetPool<Transform2D>().Get(2).Position}");
        }
        
        ImGui.Text($"GC: {GC.GetTotalMemory(false) / 1024 / 1024}Mb");
        if (_input.IsPressing(Key.AltLeft))
        {
            ImGui.Text($"GC: {GC.GetTotalMemory(false) / 1024}Kb");
        }

        foreach (var e in _world.Where(out CameraAspect a))
        {
            ref var holder = ref a.holder.Get(e);
            ImGui.Text($"Camera pos: {holder.Camera.Position}");
            ImGui.Text($"Camera zoom: {holder.Camera.Zoom}");
            ImGui.SliderFloat($"Camera Zoom", ref holder.Camera.Zoom, 1, 100);
            ImGui.SliderFloat($"Camera PixelsPerUnit", ref holder.Camera.PixelsPerUnit, 1, 100);
        }
    }

    // private void PrintUI(UIElement element, int indent = 0)
    // {
    //     bool print = ImGui.Button("Print");
    //     Print(element, indent, print);
    // }

    // private void Print(UIElement element, int indent = 0, bool print = false)
    // {
    //     var box = element.LayoutBox;
    //     var text =
    //         $"<Element id='{element.Id}' " +
    //         $"class='{string.Join(" ", element.Classes)}'> " +
    //         $"Content: X={box.ContentRect.X:F0}, Y={box.ContentRect.Y:F0}, W={box.ContentRect.Width:F0}, H={box.ContentRect.Height:F0}";
    //     var margin =
    //         $"Margin: X={box.MarginRect.X:F0}, Y={box.MarginRect.Y:F0}, W={box.MarginRect.Width:F0}, H={box.MarginRect.Height:F0}";
    //     var padding =
    //         $"Padding: X={box.PaddingRect.X:F0}, Y={box.PaddingRect.Y:F0}, W={box.PaddingRect.Width:F0}, H={box.PaddingRect.Height:F0}";
    //     var border =
    //         $"Border: X={box.BorderRect.X:F0}, Y={box.BorderRect.Y:F0}, W={box.BorderRect.Width:F0}, H={box.BorderRect.Height:F0}";
    //     var content =
    //         $"Content: X={box.ContentRect.X:F0}, Y={box.ContentRect.Y:F0}, W={box.ContentRect.Width:F0}, H={box.ContentRect.Height:F0}";
    //
    //     if (print)
    //     {
    //         Console.WriteLine(text);
    //         Console.WriteLine(margin);
    //         Console.WriteLine(padding);
    //         Console.WriteLine(border);
    //         Console.WriteLine(content);
    //     }
    //
    //
    //     if (ImGui.CollapsingHeader(text))
    //     {
    //         ImGui.Indent(indent * 2);
    //         ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
    //         if (ImGui.CollapsingHeader("style " + text))
    //         {
    //             ImGui.Text(margin);
    //             ImGui.Text(padding);
    //             ImGui.Text(border);
    //             ImGui.Text(content);
    //
    //             foreach (var (key, value) in element.ComputedStyle)
    //             {
    //                 ImGui.Text($"{key}: {value}");
    //             }
    //         }
    //
    //         ImGui.PopStyleColor(1);
    //
    //         foreach (var child in element.Children)
    //         {
    //             Print(child, indent + 5, print);
    //         }
    //
    //         ImGui.Unindent(indent);
    //     }
    // }

    private async JobHandle Spawn(string path)
    {
        AssetHandle<ComponentsTemplateAsset> handle = new();
        try
        {
            handle = await _assetsManager.LoadAssetAsync<ComponentsTemplateAsset>(path);
            var entity = CreateEntity(_world);
            await handle.Asset.Template.ApplyTo(entity.ID, _world.Base);
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

    protected entlong CreateEntity(World world)
    {
        lock (world)
        {
            return world.New();
        }
    }
}
