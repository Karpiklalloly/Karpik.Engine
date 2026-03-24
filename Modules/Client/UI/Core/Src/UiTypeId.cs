namespace Karpik.Engine.Client.UI
{
    /// <summary>
    /// Identifier for a UI type (struct of props).
    /// </summary>
    public enum UiTypeId : int
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
        Loc = 10,
        Spacing = 11,
        WorldWindow = 12
    }
}