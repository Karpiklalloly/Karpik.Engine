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
    
    public const string flex_grow = "flex-grow";
    public const string flex_direction = "flex-direction";

    #endregion
    public List<StyleRule> Rules { get; } = new();
    
    public static StyleSheet Default => _default;
    private static StyleSheet _default;

    static StyleSheet()
    {
        _default = new StyleSheet();
        _default.Rules.Add(new StyleRule(new Selector("#root"))
        {
            Properties = {
                ["display"] = "flex",
                ["flex-direction"] = "column", // Располагаем top-bar, content и status-bar друг под другом
                ["width"] = "100%", // Занимаем все окно
                ["height"] = "100%"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".root-container"))
        {
            Properties = {
                ["display"] = "flex",
                ["flex-direction"] = "column", // Располагаем top-bar, content и status-bar друг под другом
                ["width"] = "100%", // Занимаем все окно
                ["height"] = "100%"
            }
        });

// Верхняя панель. Тоже flex-контейнер, но уже в виде строки.
        _default.Rules.Add(new StyleRule(new Selector(".top-bar"))
        {
            Properties = {
                ["display"] = "flex",
                ["flex-direction"] = "row",   // Элементы меню располагаются в ряд
                ["align-items"] = "center",   // Выравниваем элементы меню по центру по вертикали
                ["height"] = "40px",
                ["background-color"] = "lightgray",
                ["padding"] = "0 10px",       // 0 сверху/снизу, 10 слева/справа
            }
        });

// Элементы меню внутри верхней панели
        _default.Rules.Add(new StyleRule(new Selector(".nav-item"))
        {
            Properties = {
                // display: inline-block или block не нужен, так как они теперь flex-элементы
                ["padding"] = "5px 10px",
                ["margin-right"] = "5px",
                ["color"] = "black",
                ["font-size"] = "20",
                ["background-color"] = "raywhite",
            }
        });

// Основная область контента.
        _default.Rules.Add(new StyleRule(new Selector(".main-content"))
        {
            Properties = {
                // *** КЛЮЧЕВОЕ СВОЙСТВО FLEXBOX ***
                // Этот блок растянется и займет все доступное вертикальное пространство
                ["flex-grow"] = "1",
                ["background-color"] = "white",
                ["padding"] = "15px",
                ["color"] = "black",
                ["font-size"] = "20",
            }
        });

// Нижняя статусная строка.
        _default.Rules.Add(new StyleRule(new Selector(".status-bar"))
        {
            Properties = {
                ["height"] = "25px",
                ["background-color"] = "lightblue",
                ["padding"] = "5px 10px",
                ["color"] = "black",
                ["font-size"] = "18"
            }
        });
    }
}