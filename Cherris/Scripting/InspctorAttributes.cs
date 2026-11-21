namespace Cherris.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class RangeAttribute : Attribute
{
    public float Min { get; }
    public float Max { get; }
    public float Speed { get; }

    public RangeAttribute(float min, float max, float speed = 0.01f)
    {
        Min = min;
        Max = max;
        Speed = speed;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public class ColorUsageAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public class DragDropTargetAttribute : Attribute
{
    public string PayloadType { get; }

    public DragDropTargetAttribute(string payloadType)
    {
        PayloadType = payloadType;
    }
}