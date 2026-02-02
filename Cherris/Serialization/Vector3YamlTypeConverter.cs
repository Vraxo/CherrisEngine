using System.Globalization;
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cherris.Serialization;

public class Vector3YamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Vector3);
    }

    // The nestedObjectDeserializer parameter is required by the interface, even if unused here.
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        // We are expecting a sequence (e.g., [x, y, z])
        parser.Consume<SequenceStart>();

        float x = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        float y = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        float z = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);

        parser.Consume<SequenceEnd>();

        return new Vector3(x, y, z);
    }

    // The nestedObjectSerializer parameter is required by the interface, even if unused here.
    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        Vector3 vector = (Vector3)value;
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
        emitter.Emit(new Scalar(vector.X.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(vector.Y.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(vector.Z.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new SequenceEnd());
    }
}