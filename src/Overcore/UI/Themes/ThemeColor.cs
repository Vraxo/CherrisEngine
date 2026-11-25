namespace Overcore.UI;

/// <summary>
/// One color in a theme: straight sRGB bytes, which is what CSS and ImGui both want.
/// </summary>
/// <param name="R">Red channel.</param>
/// <param name="G">Green channel.</param>
/// <param name="B">Blue channel.</param>
/// <param name="A">Alpha channel.</param>
/// <remarks>
/// Named for the UE5 palette it was introduced with; every theme uses it. The engine stores colors linearly and
/// converts, so the values a theme holds are always the sRGB color you see on screen — never the linear one.
/// </remarks>
public readonly record struct ThemeColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Creates a color from an <c>#rrggbb</c> or <c>#rrggbbaa</c> string.</summary>
    /// <param name="hex">Hex color, with or without the leading '#'.</param>
    public static ThemeColor FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);

        string value = hex.AsSpan().TrimStart('#').ToString();
        return value.Length switch
        {
            6 => new ThemeColor(
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value[2..4], 16),
                Convert.ToByte(value[4..6], 16)),
            8 => new ThemeColor(
                Convert.ToByte(value[..2], 16),
                Convert.ToByte(value[2..4], 16),
                Convert.ToByte(value[4..6], 16),
                Convert.ToByte(value[6..8], 16)),
            _ => throw new FormatException($"'{hex}' is not an #rrggbb or #rrggbbaa color.")
        };
    }

    /// <summary>Creates a color from a packed <c>0xRRGGBB</c> value.</summary>
    /// <param name="rgb">Packed red, green and blue.</param>
    /// <param name="a">Alpha, 0-255.</param>
    public static ThemeColor FromRgb(uint rgb, byte a = 255)
    {
        return new ThemeColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
    }

    /// <summary>The color as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when translucent.</summary>
    public string Hex => A == 255 ? $"#{R:x2}{G:x2}{B:x2}" : $"#{R:x2}{G:x2}{B:x2}{A:x2}";

    /// <summary>The color with a different alpha, 0 to 1.</summary>
    public ThemeColor WithAlpha(float alpha)
    {
        return this with { A = (byte)Math.Clamp(MathF.Round(alpha * 255f), 0f, 255f) };
    }

    /// <summary>The color as ImGui wants it: 0 to 1 floats.</summary>
    public System.Numerics.Vector4 ToVector4()
    {
        return new System.Numerics.Vector4(R / 255f, G / 255f, B / 255f, A / 255f);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return Hex;
    }
}
