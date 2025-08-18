namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
{
    #region Constants

    public const string width = "width";
    public const string min_width = "min-width";
    public const string max_width = "max-width";
    
    public const string height = "height";
    public const string min_height = "min-height";
    public const string max_height = "max-height";

    public const string top = "top";
    public const string bottom = "bottom";
    public const string left = "left";
    public const string right = "right";

    public const string position = "position";
    public const string relative = "relative";
    public const string position_absolute = "absolute";
    public const string position_static = "static";
    public const string position_fixed = "fixed";

    public const string padding = "padding";
    public const string padding_top = "padding_top";
    public const string padding_bottom = "padding-bottom";
    public const string padding_left = "padding-left";
    public const string padding_right = "padding-right";

    public const string margin = "margin";
    public const string margin_top = "margin-top";
    public const string margin_bottom = "margin-bottom";
    public const string margin_left = "margin-left";
    public const string margin_right = "margin-right";

    public const string background_color = "background-color";
    public const string color = "color";

    public const string font_size = "font-size";
    public const string line_height = "line-height";
    
    public const string box_sizing = "box-sizing";
    public const string box_sizing_border_box = "border-fox";

    public const string display = "display";
    public const string display_none = "none";
    public const string display_block = "block";
    public const string display_inline_block = "inline-block";

    public const string z_index = "z-index";

    #endregion
    public List<StyleRule> Rules { get; } = new();
    
    public static StyleSheet Default => _default;
    private static StyleSheet _default;

    static StyleSheet()
    {
        _default = new StyleSheet();
        // _default.Rules.Add(new StyleRule(new Selector("#main"))
        // {
        //     Properties = {
        //         [width] = "500px",
        //         [height] = "auto",
        //         [padding] = "20px",
        //         [background_color] = "lightgray"
        //     }
        // });
        _default.Rules.Add(new StyleRule(new Selector(".card"))
        {
            Properties = {
                [width] = "100%",
                [height] = "50px",
                [padding] = "10px",
                [margin_bottom] = "20px",
                [background_color] = "lightblue",
                [box_sizing] = "border-box" 
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".content-panel"))
        {
            Properties = {
                [width] = "auto",
                [height] = "auto",
                [background_color] = "lightyellow",
                [padding] = "15px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".paragraph"))
        {
            Properties = { 
                [height] = "25px",
                [margin_bottom] = "10px",
                [background_color] = "white" 
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".paragraph--last"))
        {
            // Модификатор для последнего параграфа
            Properties =
            {
                [margin_bottom] = "0"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".popup-container")) {
            Properties =
            {
                [position] = "relative",
                [background_color] = "lightyellow",
                [min_height] = "150px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".popup")) {
            Properties = { 
                [position] = "absolute",
                [top] = "10px",
                [left] = "100px", 
                [width] = "200px",
                [height] = "100px",
                [padding] = "10px",
                [background_color] = "red",
                [z_index] = "10",
                [color] = "white"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".hidden-element")) {
            Properties =
            {
                [display] = "none"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".top-bar"))
        {
            Properties = {
                [position] = "fixed", // Всегда на экране
                [top] = "0",
                [left] = "0",
                [right] = "0", // Растягиваем на всю ширину с помощью left/right
                [height] = "40px",
                [background_color] = "lightgray",
                [padding] = "5px 10px", // 5px сверху/снизу, 10px слева/справа
                [z_index] = "100" // Поверх всего остального
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".status-bar"))
        {
            Properties = {
                [position] = "fixed", // Всегда на экране
                [bottom] = "0",
                [left] = "0",
                [right] = "0", // Растягиваем на всю ширину
                [height] = "25px",
                [background_color] = "lightblue",
                [padding] = "5px 10px",
                [color] = "black",
                [z_index] = "100"
            }
        });

        _default.Rules.Add(new StyleRule(new Selector(".main-content"))
        {
            Properties = {
                // Отступы, чтобы контент не залезал под fixed-панели
                [margin_top] = "50px", 
                [margin_bottom] = "35px",
                [margin_left] = "20px",
                [margin_right] = "20px",
                [padding] = "15px",
                [height] = "auto",
                [background_color] = "white"
            }
        });

        // --- Стили для элементов меню ---
        _default.Rules.Add(new StyleRule(new Selector(".nav-item"))
        {
            Properties = {
                [display] = "inline-block",
                [padding] = "5px 10px",
                [margin_right] = "5px",
                [color] = "black",
                [font_size] = "20",
                [background_color] = "raywhite",
                
                [min_width] = "50px", 
                [min_height] = "20px"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".content-text"))
        {
            Properties = {
                [height] = "30px",
                [font_size] = "20",
                [color] = "black"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".wrapping-text-box"))
        {
            Properties = {
                [width] = "400px", // Задаем фиксированную ширину для демонстрации переноса
                [height] = "auto",
                [background_color] = "red",
                [padding] = "10px",
                [margin_top] = "10px",
                [color] = "black",
                [font_size] = "20",
                [line_height] = "24px" // Межстрочный интервал
            }
        });
    }
}