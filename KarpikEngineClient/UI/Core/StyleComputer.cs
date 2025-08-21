using s = Karpik.Engine.Client.UIToolkit.StyleSheet;

namespace Karpik.Engine.Client.UIToolkit
{
    public class StyleComputer
    {
        // Список наследуемых свойств.
        private static readonly HashSet<string> InheritableProperties = new()
        {
            s.color, "font-family", s.font_size, "font-style", "font-weight", 
            s.line_height, "text-align", "visibility", "white-space"
        };

        // Карта для разворачивания shorthands. Ключ - shorthand, значение - массив longhands.
        private static readonly Dictionary<string, string[]> ShorthandMap = new()
        {
            { s.margin, [s.margin_top, s.margin_right, s.margin_bottom, s.margin_left] },
            { s.padding, [s.padding_top, s.padding_right, s.padding_bottom, s.padding_left] },
            // { s.border_width, [s.border_top_width, s.border_right_width, s.border_bottom_width, s.border_left_width] },
            // Добавьте сюда другие shorthands по мере необходимости (например, border-color, border-style, font, border)
        };

        public void ComputeStyles(UIElement root, s styleSheet)
        {
            // Начинаем рекурсивный процесс с корневого элемента.
            ComputeStylesForNode(root, styleSheet, null);
        }

        private void ComputeStylesForNode(UIElement element, s styleSheet, Dictionary<string, string> parentComputedStyle)
        {
            // 1. Наследование
            var computedStyle = new Dictionary<string, string>();
            if (parentComputedStyle != null)
            {
                foreach (var prop in InheritableProperties)
                {
                    if (parentComputedStyle.TryGetValue(prop, out var value))
                    {
                        computedStyle[prop] = value;
                    }
                }
            }

            // 2. Сбор и применение правил
            var applicableRules = new List<StyleRule>();
            foreach (var rule in styleSheet.Rules)
            {
                if (DoesSelectorMatch(rule.Selector, element))
                {
                    applicableRules.Add(rule);
                }
            }
            // Сортируем правила по специфичности, чтобы более специфичные применялись последними.
            applicableRules.Sort(static (a, b) => a.Selector.CompareTo(b.Selector));

            // Собираем все свойства из правил и инлайновых стилей в один временный словарь.
            // Это реализует каскад: более поздние значения перезаписывают более ранние.
            var appliedProperties = new Dictionary<string, string>();
            foreach (var rule in applicableRules)
            {
                foreach (var prop in rule.Properties)
                {
                    appliedProperties[prop.Key] = prop.Value;
                }
            }
            
            // Инлайновые стили имеют наивысший приоритет.
            foreach (var prop in element.InlineStyles)
            {
                appliedProperties[prop.Key] = prop.Value;
            }

            // 3. Разворачивание Shorthands и финальное применение
            ExpandAndApplyProperties(appliedProperties, computedStyle);

            // 4. Сохранение результата и рекурсия
            element.ComputedStyle = computedStyle;

            foreach (var child in element.Children)
            {
                ComputeStylesForNode(child, styleSheet, element.ComputedStyle);
            }
        }

        private void ExpandAndApplyProperties(Dictionary<string, string> applied, Dictionary<string, string> computed)
        {
            // Применяем shorthands в первую очередь
            foreach (var shorthandPair in ShorthandMap)
            {
                string shorthandName = shorthandPair.Key;
                if (applied.TryGetValue(shorthandName, out var shorthandValue))
                {
                    var longhands = shorthandPair.Value;
                    var values = ParseShorthandValues(shorthandValue);

                    // Разворачиваем shorthand "10px 5px" в 4 значения
                    string top = values.Length > 0 ? values[0] : "0";
                    string right = values.Length > 1 ? values[1] : top;
                    string bottom = values.Length > 2 ? values[2] : top;
                    string left = values.Length > 3 ? values[3] : right;
                    
                    var expandedValues = new[] { top, right, bottom, left };

                    // Применяем развернутые значения
                    for (int i = 0; i < longhands.Length; i++)
                    {
                        // ВАЖНО: Применяем значение из shorthand, только если в applied
                        // НЕТ более специфичного longhand-свойства.
                        if (!applied.ContainsKey(longhands[i]))
                        {
                            computed[longhands[i]] = expandedValues[i];
                        }
                    }
                }
            }

            // Теперь применяем все остальные (longhand) свойства.
            // Они перезапишут любые унаследованные или "неспецифичные" shorthand значения.
            foreach (var prop in applied)
            {
                if (!ShorthandMap.ContainsKey(prop.Key))
                {
                    computed[prop.Key] = prop.Value;
                }
            }
        }

        private string[] ParseShorthandValues(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return [];
            return value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private bool DoesSelectorMatch(Selector selector, UIElement element)
        {
            var rawSelector = selector.Raw;
        
            bool requiresHover = rawSelector.Contains(":hover");
            bool requiresActive = rawSelector.Contains(":active");

            if (requiresHover && !element.IsHovered) return false;
            if (requiresActive && !element.IsActive) return false;

            string baseSelector = rawSelector.Replace(":hover", "").Replace(":active", "");

            if (baseSelector.StartsWith('#'))
            {
                return element.Id == baseSelector.Substring(1);
            }
            if (baseSelector.StartsWith('.'))
            {
                // Для простоты пока считаем, что селектор по классу - это один класс.
                // В реальном движке нужно было бы парсить ".class1.class2"
                return element.Classes.Contains(baseSelector.Substring(1));
            }
            
            // Можно добавить поддержку селекторов по тегу (типу элемента), если у вас есть такая концепция.
            // return element.TagName == baseSelector;

            return false;
        }
    }
}