using DCFApixels.DragonECS;
using Karpik.Engine.Client.Graphics.Core;
using Karpik.Engine.Core;

namespace Karpik.Engine.Client.UIToolkit;

[Module]
public class UIToolkitInstaller : IModule
{
    public string Name => "UIToolkit.Core";

    private UIManager _manager = new();
    
    public void OnRegisterServices(IServiceRegister services)
    {
        services.Register(_manager);
    }

    public void OnConfigure(IServiceContainer services, out IEcsModule? module)
    {
        var renderer = services.Get<IRenderer>();
        _manager.SetRoot(CreateDemoUI(), services.Get<Input>(), renderer, services.Get<IWindow>());
        _manager.Font = renderer.GetFontDefault();
        
        var codes = "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя"
                    + "0123456789"
                    + ".,!?-+()[]{}:;/\\\"'`~@#$%^&*=_|<> "
                    + "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
                    + "▼" + "▶";
        
        int count = 0;
        var chars = renderer.LoadCodepoints(codes, ref count);
        var font = renderer.LoadFont("Pressstart2p.ttf", 32, chars, count);
        Console.WriteLine(renderer.IsFontValid(font));

        module = new UIToolkitModule();
    }

    public void OnConfigureComplete(IServiceContainer services)
    {
        
    }

    
    private UIElement CreateDemoUI()
    {
        var root = new UIElement("root") { Classes = { "root-container" } };

        // --- 1. ХЕДЕР И ВЫПАДАЮЩЕЕ МЕНЮ ---
        var header = new UIElement { Classes = { "header" } };

        var homeLink = new UIElement { Classes = { "menu-item" }, Text = "Home" };

        // Создаем пункт меню, который будет открывать dropdown
        var fileMenuItem = new UIElement { Classes = { "menu-item" }, Text = "File" };

        // Создаем саму панель dropdown
        var dropdownPanel = new UIElement("file-dropdown") { Classes = { "dropdown-panel" } };
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "New" });
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "Open" });
        dropdownPanel.AddChild(new UIElement { Classes = { "dropdown-item" }, Text = "Save" });

        // Добавляем панель как дочерний элемент к пункту меню
        fileMenuItem.AddChild(dropdownPanel);

        var aboutLink = new UIElement { Classes = { "menu-item" }, Text = "About" };

        header.AddChild(homeLink);
        header.AddChild(fileMenuItem);
        header.AddChild(aboutLink);

        // --- 2. ОСНОВНОЙ КОНТЕНТ (старые тесты) ---
        var mainContent = new UIElement { Classes = { "main-content" } };

        // --- GROW TEST ---
        var growContainer = new UIElement { Classes = { "test-container", "grow-container" } };
        growContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Grow Test:" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "no-grow" }, Text = "Basis: 150px" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "grows-1" }, Text = "Grow: 1" });
        growContainer.AddChild(new UIElement { Classes = { "test-item", "grows-2" }, Text = "Grow: 2" });

        // --- SHRINK TEST ---
        var shrinkContainer = new UIElement { Classes = { "test-container", "shrink-container" } };
        shrinkContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Shrink Test:" });
        shrinkContainer.AddChild(new UIElement
            { Classes = { "test-item", "shrinks-1" }, Text = "Basis: 400, Shrink: 1" });
        shrinkContainer.AddChild(new UIElement
            { Classes = { "test-item", "shrinks-2" }, Text = "Basis: 200, Shrink: 2" });
        shrinkContainer.AddChild(new UIElement { Classes = { "test-item", "no-shrink" }, Text = "NO SHRINK" });

        // --- ALIGNMENT TEST И ТЕСТ ABSOLUTE ---
        var alignContainer = new UIElement { Classes = { "test-container", "align-container" } };
        alignContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Alignment & Absolute Test:" });
        alignContainer.AddChild(new UIElement { Classes = { "test-item", "align-center" }, Text = "Self: Center" });
        alignContainer.AddChild(new UIElement { Classes = { "test-item", "align-start" }, Text = "Self: Start" });

        // Создаем родительский элемент для значка
        var relativeParent = new UIElement { Classes = { "relative-parent" }, Text = "Relative Parent" };
        var notificationBadge = new UIElement { Classes = { "notification-badge" }, Text = "3" };
        relativeParent.AddChild(notificationBadge);

        // Добавляем этот элемент в сетку align-теста
        var stretchItem = new UIElement { Classes = { "test-item", "align-stretch" } }; // Без текста, чтобы не мешал
        stretchItem.AddChild(relativeParent); // Вкладываем relative-parent внутрь

        alignContainer.AddChild(stretchItem);
        
        var wrapContainer = new UIElement { Classes = { "test-container", "wrap-container" } };
        wrapContainer.AddChild(new UIElement { Classes = { "label", "test-item" }, Text = "Wrap Test:" });
        for (int i = 0; i < 10; i++)
        {
            wrapContainer.AddChild(new UIElement { Classes = { "test-item", "wrap-item" }, Text = $"Item {i + 1}" });
        }

        // Добавляем все тестовые контейнеры в mainContent
        mainContent.AddChild(growContainer);
        mainContent.AddChild(shrinkContainer);
        mainContent.AddChild(alignContainer);
        mainContent.AddChild(wrapContainer); // <-- Добавляем новый контейнер

        // --- 3. СБОРКА ИЕРАРХИИ ---
        root.AddChild(header);
        root.AddChild(mainContent);

        return root;
    }
}