namespace Karpik.Engine.Client.UIToolkit;

public enum Unit { Px, Percent, Auto }

public struct StyleValue
{
    public float Value { get; }
    public Unit Unit { get; }
    public StyleValue(float value, Unit unit) { Value = value; Unit = unit; }
    public static StyleValue Auto => new(0, Unit.Auto);
    public static StyleValue Px(float val) => new(val, Unit.Px);
    public static StyleValue Percent(float val) => new(val, Unit.Percent);
}