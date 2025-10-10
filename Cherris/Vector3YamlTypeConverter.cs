using System;
using System.Globalization;
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cherris;

public class Vector3YamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Vector3);
    }

    // --- MODIFICATION: Added 'ObjectDeserializer' parameter to match the interface ---
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        // We are expecting a sequence (e.g., [x, y, z])
        var sequence = parser.Consume<SequenceStart>();

        var x = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var y = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var z = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);

        parser.Consume<SequenceEnd>();

        return new Vector3(x, y, z);
    }

    // --- MODIFICATION: Added 'ObjectSerializer' parameter to match the interface ---
    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        var vector = (Vector3)value;
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
        emitter.Emit(new Scalar(vector.X.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(vector.Y.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(vector.Z.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new SequenceEnd());
    }
}