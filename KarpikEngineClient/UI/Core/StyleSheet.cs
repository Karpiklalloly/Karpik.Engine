namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
{
    public List<StyleRule> Rules { get; } = new();
    
    public static StyleSheet Default => _default;
    private static StyleSheet _default;

    static StyleSheet()
    {
        _default = new StyleSheet();
        // _default.Rules.Add(new StyleRule(new Selector("#main"))
        // {
        //     Properties = {
        //         ["width"] = "500px",
        //         ["height"] = "auto",
        //         ["padding"] = "20px",
        //         ["background-color"] = "lightgray"
        //     }
        // });
        _default.Rules.Add(new StyleRule(new Selector(".card"))
        {
            Properties = {
                ["width"] = "100%",
                ["height"] = "50px",
                ["padding"] = "10px",
                ["margin-bottom"] = "20px",
                ["background-color"] = "lightblue",
                ["box-sizing"] = "border-box" 
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".content-panel"))
        {
            Properties = {
                ["width"] = "auto",
                ["height"] = "auto",
                ["background-color"] = "lightyellow",
                ["padding"] = "15px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".paragraph"))
        {
            Properties = { 
                ["height"] = "25px",
                ["margin-bottom"] = "10px",
                ["background-color"] = "white" 
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".paragraph--last"))
        {
            // Модификатор для последнего параграфа
            Properties =
            {
                ["margin-bottom"] = "0"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".popup-container")) {
            Properties =
            {
                ["position"] = "relative",
                ["background-color"] = "lightyellow",
                ["min-height"] = "150px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".popup")) {
            Properties = { 
                ["position"] = "absolute",
                ["top"] = "10px",
                ["left"] = "100px", 
                ["width"] = "200px",
                ["height"] = "100px",
                ["padding"] = "10px",
                ["background-color"] = "red",
                ["z-index"] = "10",
                ["color"] = "white"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".hidden-element")) {
            Properties =
            {
                ["display"] = "none"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".top-bar"))
        {
            Properties = {
                ["position"] = "fixed", // Всегда на экране
                ["top"] = "0",
                ["left"] = "0",
                ["right"] = "0", // Растягиваем на всю ширину с помощью left/right
                ["height"] = "40px",
                ["background-color"] = "lightgray",
                ["padding"] = "5px 10px", // 5px сверху/снизу, 10px слева/справа
                ["z-index"] = "100" // Поверх всего остального
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".status-bar"))
        {
            Properties = {
                ["position"] = "fixed", // Всегда на экране
                ["bottom"] = "0",
                ["left"] = "0",
                ["right"] = "0", // Растягиваем на всю ширину
                ["height"] = "25px",
                ["background-color"] = "lightblue",
                ["padding"] = "5px 10px",
                ["color"] = "black",
                ["z-index"] = "100"
            }
        });

        _default.Rules.Add(new StyleRule(new Selector(".main-content"))
        {
            Properties = {
                // Отступы, чтобы контент не залезал под fixed-панели
                ["margin-top"] = "50px", 
                ["margin-bottom"] = "35px",
                ["margin-left"] = "20px",
                ["margin-right"] = "20px",
                ["padding"] = "15px",
                ["height"] = "auto",
                ["background-color"] = "white"
            }
        });

        // --- Стили для элементов меню ---
        _default.Rules.Add(new StyleRule(new Selector(".nav-item"))
        {
            Properties = {
                ["display"] = "inline-block",
                ["padding"] = "5px 10px",
                ["margin-right"] = "5px",
                ["color"] = "black",
                ["font-size"] = "20",
                ["background-color"] = "raywhite",
                
                ["min-width"] = "50px", 
                ["min-height"] = "20px"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".content-text"))
        {
            Properties = {
                ["height"] = "30px",
                ["font-size"] = "20",
                ["color"] = "black"
            }
        });
    }
}