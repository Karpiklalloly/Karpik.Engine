namespace Karpik.Engine.Client.UI.Core;

public enum UiTypeId
{
    None = 0,
    Window = 1,
    Button = 2,
    Label = 3,
    InputField = 4,
    Slider = 5,
    ProgressBar = 6,
    ComboBox = 7,
    Horizontal = 8,
    Vertical = 9,
    Image = 10,
    Grid = 11,
    CheckBox = 12,
    Toggle = 13,
    ScrollView = 14,
    Viewport = 15,
}

public enum FlexDirection
{
    Row,
    Column
}

public enum JustifyContent
{
    Start,
    Center,
    End,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly
}

public enum AlignItems
{
    Start,
    Center,
    End,
    Stretch
}

public enum InteractionState
{
    Normal,
    Hovered,
    Pressed,
    Focused,
    Disabled
}

public enum PseudoState
{
    None,
    Hover,
    Active,
    Focus,
    Disabled,
    Checked
}

public enum LayoutDirection
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop
}
