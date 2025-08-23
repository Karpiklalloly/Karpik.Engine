
namespace Karpik.Engine.Client.UIToolkit;

public class StyleSheet
{
    // ReSharper disable InconsistentNaming
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

    public const string auto = "auto";

    public const string position = "position";
    public const string position_relative = "relative";
    public const string position_absolute = "absolute";
    public const string position_static = "static";
    public const string position_fixed = "fixed";

    public const string padding = "padding";
    public const string padding_top = "padding-top";
    public const string padding_bottom = "padding-bottom";
    public const string padding_left = "padding-left";
    public const string padding_right = "padding-right";

    public const string margin = "margin";
    public const string margin_top = "margin-top";
    public const string margin_bottom = "margin-bottom";
    public const string margin_left = "margin-left";
    public const string margin_right = "margin-right";
    
    public const string border_width = "border-width";
    public const string border_top_width = "border-top-width";
    public const string border_bottom_width = "border-bottom-width";
    public const string border_left_width = "border-left-width";
    public const string border_right_width = "border-right-width";
    
    public const string border_color = "border-color";
    public const string border_top_color = "border-top-color";
    public const string border_bottom_color = "border-bottom-color";
    public const string border_left_color = "border-left-color";
    public const string border_right_color = "border-right-color";

    public const string background_color = "background-color";
    public const string color = "color";

    public const string font_size = "font-size";
    public const string line_height = "line-height";
    
    public const string box_sizing = "box-sizing";
    public const string box_sizing_border_box = "border-box";

    public const string display = "display";
    public const string display_none = "none";
    public const string display_block = "block";
    public const string display_flex = "flex";
    public const string display_inline_block = "inline-block";

    public const string z_index = "z-index";
    
    public const string flex_grow = "flex-grow";
    public const string flex_basis = "flex-basis";
    public const string flex_shrink = "flex-shrink";
    
    public const string align_self = "align-self";
    public const string align_start = "flex-start";
    public const string align_flex_end = "flex-end";
    public const string align_center = "center";
    public const string align_stretch = "stretch";
    
    public const string align_items = "align-items";
    
    public const string flex_direction = "flex-direction";
    public const string flex_direction_row = "row";
    public const string flex_direction_row_reverse = "row-reverse";
    public const string flex_direction_column = "column";
    public const string flex_direction_column_reverse = "column-reverse";
    
    public const string justify_content = "justify-content";
    public const string justify_content_flex_start = "flex-start";
    public const string justify_content_flex_end = "flex-end";
    public const string justify_content_center = "center";
    public const string justify_content_space_between = "space-between";
    public const string justify_content_space_around = "space-around";
    public const string transparent = "transparent";
    
    public const string flex_wrap = "flex-wrap";
    public const string flex_wrap_nowrap = "nowrap";
    public const string flex_wrap_wrap = "wrap";
    public const string flex_wrap_wrap_reverse = "wrap-reverse";

    public const string align_content = "align-content";
    public const string align_content_flex_start = "flex-start";
    public const string align_content_flex_end = "flex-end";
    public const string align_content_center = "center";
    public const string align_content_space_between = "space-between";
    public const string align_content_space_around = "space-around";
    public const string align_content_stretch = "stretch";

    #endregion
    public List<StyleRule> Rules { get; } = new();
    
    public static StyleSheet Default => _default;
    private static StyleSheet _default;

    static StyleSheet()
    {
        _default = new StyleSheet();
        _default.Rules.Add(new StyleRule(new Selector(".root-container"))
        {
            Properties =
            {
                ["display"] = "flex",
                ["flex-direction"] = "column",
                ["width"] = "100%",
                ["height"] = "100%",
                ["background-color"] = "darkgray"
            }
        });

        // --- Общие стили для тестовых контейнеров и элементов ---
        _default.Rules.Add(new StyleRule(new Selector(".test-container"))
        {
            Properties =
            {
                ["display"] = "flex",
                ["flex-direction"] = "row",
                ["margin"] = "10px",
                ["padding"] = "10px",
                [border_width] = "2px",
                [border_color] = "red",
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".label"))
        {
            Properties =
            {
                ["display"] = "inline-block",
                ["color"] = "white",
                ["font-size"] = "22",
                ["margin-right"] = "15px",
                // Этот элемент не должен участвовать в flex-расчетах
                ["flex-shrink"] = "0",
                [border_width] = "2px",
                [border_color] = "red",
                //[padding] = "8px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".label:hover"))
        {
            Properties =
            {
                ["background-color"] = "darkblue",
                ["color"] = "red",
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".test-item"))
        {
            Properties =
            {
                ["padding"] = "10px",
                ["color"] = "black",
                ["font-size"] = "18",
                ["border-width"] = "1px",
                [border_color] = "darkblue",
                [box_sizing] = box_sizing_border_box,
            }
        });

        // --- 1. Стили для GROW теста ---
        _default.Rules.Add(new StyleRule(new Selector(".grow-container"))
        {
            Properties =
            {
                ["background-color"] = "lightgray"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".no-grow"))
        {
            Properties =
            {
                ["flex-basis"] = "150px",
                ["background-color"] = "lightblue"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".grows-1"))
        {
            Properties =
            {
                ["flex-basis"] = "100px",
                ["flex-grow"] = "1",
                ["background-color"] = "lightgreen"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".grows-2"))
        {
            Properties =
            {
                ["flex-basis"] = "100px",
                ["flex-grow"] = "2",
                ["background-color"] = "lightyellow"
            }
        });

        // --- 2. Стили для SHRINK теста ---
        // Обрати внимание: контейнер имеет фиксированную ширину, чтобы заставить элементы сжиматься
        _default.Rules.Add(new StyleRule(new Selector(".shrink-container"))
        {
            Properties =
            {
                ["background-color"] = "lightgray",
                ["width"] = "800px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".shrinks-1"))
        {
            Properties =
            {
                ["flex-basis"] = "400px",
                ["flex-shrink"] = "1",
                ["background-color"] = "lightblue"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".shrinks-2"))
        {
            Properties =
            {
                ["flex-basis"] = "200px",
                ["flex-shrink"] = "2",
                ["background-color"] = "lightgreen"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".no-shrink"))
        {
            Properties =
            {
                ["flex-basis"] = "200px",
                ["flex-shrink"] = "0",
                ["background-color"] = "lightyellow"
            }
        });

        // --- 3. Стили для ALIGNMENT теста ---
        // Высокий контейнер с выравниванием по центру
        _default.Rules.Add(new StyleRule(new Selector(".align-container"))
        {
            Properties =
            {
                ["background-color"] = "lightgray",
                ["height"] = "150px",
                [align_items] = "center"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".align-center"))
        {
            Properties =
            {
                ["height"] = "50%",
                ["background-color"] = "lightblue"
            }
        }); // Унаследует align-items: center
        _default.Rules.Add(new StyleRule(new Selector(".align-start"))
        {
            Properties =
            {
                ["height"] = "60px",
                ["align-self"] = "flex-start",
                ["background-color"] = "lightgreen"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".align-end"))
        {
            Properties =
            {
                ["height"] = "80px",
                ["align-self"] = "flex-end",
                ["background-color"] = "lightyellow"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".align-stretch"))
        {
            Properties =
            {
                ["height"] = "auto",
                [align_self] = "stretch",
                ["background-color"] = "red"
            }
        }); // height: auto важно для stretch

        _default.Rules.Add(new StyleRule(new Selector(".header"))
        {
            Properties =
            {
                ["position"] = "fixed",
                ["top"] = "0",
                ["left"] = "0",
                ["width"] = "100%",
                ["height"] = "40px",
                ["background-color"] = "black",
                ["color"] = "white",
                ["padding"] = "10px",
                ["display"] = "flex",
                ["flex-direction"] = "row",
                ["z-index"] = "100"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".main-content"))
        {
            Properties =
            {
                // Отступ, чтобы контент не уезжал под фиксированный хедер
                [padding_top] = "60px"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".menu-item"))
        {
            Properties =
            {
                ["position"] = "relative", // Важно для позиционирования дочернего dropdown
                ["padding"] = "5px 10px",
                ["margin-right"] = "10px",
                ["font-size"] = "18"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".menu-item:hover"))
        {
            Properties =
            {
                ["background-color"] = "darkblue"
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".menu-item:hover .dropdown-panel"))
        {
            Properties =
            {
                ["display"] = "flex", // Показываем панель
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".dropdown-panel"))
        {
            Properties =
            {
                ["display"] = "none", // Скрыто по умолчанию
                ["position"] = "absolute",
                ["top"] = "100%",
                ["left"] = "0",
                ["width"] = "150px",
                ["background-color"] = "white",
                ["border-width"] = "1px",
                ["border-color"] = "lightgray",
                ["flex-direction"] = "column",
                ["z-index"] = "101"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".dropdown-item"))
        {
            Properties =
            {
                ["padding"] = "10px",
                ["color"] = "black",
                ["font-size"] = "16"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".dropdown-item:hover"))
        {
            Properties =
            {
                ["background-color"] = "lightblue"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".relative-parent"))
        {
            Properties =
            {
                ["position"] = "relative", // Важно для позиционирования дочернего значка
                ["background-color"] = "gray",
                ["border-width"] = "2px",
                ["border-color"] = "white",
                ["height"] = "100%", // Растягивается на всю высоту ячейки
                ["padding"] = "10px",
                [box_sizing] = box_sizing_border_box,
            }
        });
        
        _default.Rules.Add(new StyleRule(new Selector(".align-stretch:hover .relative-parent"))
        {
            Properties =
            {
                ["color"] = "lightyellow",
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".notification-badge"))
        {
            Properties =
            {
                ["position"] = "absolute",
                ["top"] = "-10px",
                ["right"] = "-25px",
                ["width"] = "25px",
                ["height"] = "25px",
                ["background-color"] = "red",
                ["color"] = "white",
                ["font-size"] = "14",
                // border-radius пока не реализован, поэтому будет квадрат
                // Центрирование текста через Flexbox:
                ["display"] = "flex",
                ["justify-content"] = "center",
                ["text-align"] = "center",
                ["align-items"] = "center"
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".wrap-container"))
        {
            Properties =
            {
                ["background-color"] = "lightcyan",
                ["width"] = "480px", // Фиксированная ширина, чтобы заставить элементы переноситься
                ["flex-wrap"] = "wrap",
                ["align-content"] = "flex-start" // Чтобы строки прижимались к верху
            }
        });
        _default.Rules.Add(new StyleRule(new Selector(".wrap-item"))
        {
            Properties =
            {
                ["flex-basis"] = "150px", // Каждый элемент хочет 150px
                ["height"] = "50px",
                ["background-color"] = "lightcoral",
                ["margin"] = "5px",
                // Flex-grow и shrink в 0, чтобы они не меняли свой размер
                ["flex-grow"] = "0",
                ["flex-shrink"] = "0",
                // Для центрирования текста внутри
                ["display"] = "flex",
                ["justify-content"] = "center",
                ["align-items"] = "center"
            }
        });
    }
}