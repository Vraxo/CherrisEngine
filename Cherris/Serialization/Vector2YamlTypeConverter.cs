using System;
using System.Globalization;
using System.Numerics;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cherris;

public class Vector2YamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(Vector2);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        parser.Consume<SequenceStart>();
        var x = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var y = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        parser.Consume<SequenceEnd>();
        return new Vector2(x, y);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        var vector = (Vector2)value;
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
        emitter.Emit(new Scalar(vector.X.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(vector.Y.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new SequenceEnd());
    }
}
