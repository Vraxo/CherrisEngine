using Overcore.Core;
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Overcore.Serialization;

/// <summary>
/// Serializes and deserializes <see cref="Vector4"/> as a four-element YAML flow sequence <c>[x, y, z, w]</c>,
/// matching <see cref="Vector2YamlTypeConverter"/> and <see cref="Vector3YamlTypeConverter"/>.
/// </summary>
public sealed class Vector4YamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type) => type == typeof(Vector4);

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        parser.Consume<SequenceStart>();

        float x = float.Parse(parser.Consume<Scalar>().Value, System.Globalization.CultureInfo.InvariantCulture);
        float y = float.Parse(parser.Consume<Scalar>().Value, System.Globalization.CultureInfo.InvariantCulture);
        float z = float.Parse(parser.Consume<Scalar>().Value, System.Globalization.CultureInfo.InvariantCulture);
        float w = float.Parse(parser.Consume<Scalar>().Value, System.Globalization.CultureInfo.InvariantCulture);

        parser.Consume<SequenceEnd>();
        return new Vector4(x, y, z, w);
    }

    public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serializer)
    {
        Vector4 v = value is Vector4 vec ? vec : Vector4.Zero;

        emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Flow));
        emitter.Emit(new Scalar(v.X.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(v.Y.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(v.Z.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(v.W.ToString("G", System.Globalization.CultureInfo.InvariantCulture)));
        emitter.Emit(new SequenceEnd());
    }
}
