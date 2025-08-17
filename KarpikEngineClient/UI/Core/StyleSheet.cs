namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
{
    public List<StyleRule> Rules { get; } = new();
    
    public static StyleSheet Default => _default;
    private static StyleSheet _default;

    static StyleSheet()
    {
        _default = new StyleSheet();
        _default.Rules.Add(new StyleRule(new Selector("#main"))
        {
            Properties = {
                ["width"] = "500px",
                ["height"] = "auto",
                ["padding"] = "20px",
                ["background-color"] = "lightgray"
            }
        });
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
    }
}