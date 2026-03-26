namespace Karpik.Engine.Client.UI.Core;

public struct ButtonData
{
    public string Text;
    public Color Background;
    public Color TextColor;
    public float FontSize;
    public Action<int>? OnClick;
}

public struct LabelData
{
    public string Text;
    public Color TextColor;
    public float FontSize;
    public TextAlignment Alignment;
}

public struct ImageData
{
    public TextureId Texture;
    public Color Tint;
    public ImageStretch Stretch;
}

public enum ImageStretch
{
    None,
    Fill,
    Uniform,
    UniformToFill
}

public struct PanelData
{
    public Color Background;
    public bool ClipChildren;
}

public struct InputFieldData
{
    public string Text;
    public string Placeholder;
    public Color TextColor;
    public Color Background;
    public Color PlaceholderColor;
    public float FontSize;
    public int MaxLength;
    public bool IsPassword;
    public bool IsReadOnly;
}

public struct SliderData
{
    public float Value;
    public float MinValue;
    public float MaxValue;
    public float Step;
    public Color FillColor;
    public Color TrackColor;
}

public struct ProgressBarData
    {
        public float Value;
        public float MinValue;
        public float MaxValue;
        public Color FillColor;
        public Color BackgroundColor;
    }

public struct CheckBoxData
{
    public bool IsChecked;
    public string Text;
    public Color CheckColor;
    public Color BoxColor;
}

public struct ToggleData
{
    public bool IsOn;
    public Color OnColor;
    public Color OffColor;
}

public struct ComboBoxData
{
    public int SelectedIndex;
    public string[] Options;
    public bool IsOpen;
}

public struct ScrollViewData
{
    public float ScrollX;
    public float ScrollY;
    public float ContentWidth;
    public float ContentHeight;
    public float ViewportWidth;
    public float ViewportHeight;
}
