using System.Drawing;
using Karpik.Engine.Shared.Network.Core;

namespace SSSuperGame.Tests;

/// <summary>Collects network payload values in memory for codec tests.</summary>
public sealed class FakeWriter : IWriter
{
    public readonly List<object?> Values = new();

    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(float value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(int value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(long value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(bool value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(string value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(byte value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(ushort value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="value">The value to write.</param>
    public void Put(double value) => Values.Add(value);
    /// <summary>Appends a typed value to the in-memory packet.</summary>
    /// <param name="color">The color.</param>
    public void Put(Color color) => Values.Add(color);
    /// <summary>Clears the in-memory packet.</summary>
    public void Reset() => Values.Clear();
}

/// <summary>Reads network payload values written by <see cref="FakeWriter"/>.</summary>
public sealed class FakeReader : IReader
{
    private readonly List<object?> _values;
    private int _index;

    /// <summary>Creates a reader over values recorded by a fake writer.</summary>
    /// <param name="values">The values to read.</param>
    public FakeReader(List<object?> values)
    {
        _values = values;
    }

    /// <summary>Number of unread values in the fake reader.</summary>
    public int AvailableBytes => _values.Count - _index;

    /// <summary>Releases no resources in the in-memory reader.</summary>
    public void Recycle()
    {
    }

    /// <summary>Reads the next value as a floating-point number.</summary>
    /// <returns>The next floating-point value.</returns>
    public float GetFloat() => (float)_values[_index++]!;
    /// <summary>Reads the next value as a byte.</summary>
    /// <returns>The next byte.</returns>
    public byte GetByte() => (byte)_values[_index++]!;
    /// <summary>Reads the next value as an unsigned short.</summary>
    /// <returns>The next unsigned short.</returns>
    public ushort GetUShort() => (ushort)_values[_index++]!;
    /// <summary>Reads the next value as an integer.</summary>
    /// <returns>The next integer.</returns>
    public int GetInt() => (int)_values[_index++]!;
    /// <summary>Reads the next value as a long integer.</summary>
    /// <returns>The next long integer.</returns>
    public long GetLong() => (long)_values[_index++]!;
    /// <summary>Reads the next value as a double-precision number.</summary>
    /// <returns>The next double-precision value.</returns>
    public double GetDouble() => (double)_values[_index++]!;
    /// <summary>Reads the next value as a Boolean.</summary>
    /// <returns>The next Boolean value.</returns>
    public bool GetBool() => (bool)_values[_index++]!;
    /// <summary>Reads the next value as a string.</summary>
    /// <returns>The next string.</returns>
    public string GetString() => (string)_values[_index++]!;
    /// <summary>Reads the next value as a color.</summary>
    /// <returns>The next color.</returns>
    public Color GetColor() => (Color)_values[_index++]!;
}
