using Veldrid;

namespace Cherris;

public class OutlineProfile
{
    public string Name { get; set; }
    public RgbaFloat Color { get; set; } = RgbaFloat.Red;
    public float Thickness { get; set; } = 2.0f;
}