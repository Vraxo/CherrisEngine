using System;
using System.Globalization;
using Veldrid;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Cherris;

public class RgbaFloatYamlTypeConverter : IYamlTypeConverter
{
    public bool Accepts(Type type)
    {
        return type == typeof(RgbaFloat);
    }

    public object ReadYaml(IParser parser, Type type, ObjectDeserializer nestedObjectDeserializer)
    {
        parser.Consume<SequenceStart>();
        var r = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var g = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var b = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        var a = float.Parse(parser.Consume<Scalar>().Value, CultureInfo.InvariantCulture);
        parser.Consume<SequenceEnd>();
        return new RgbaFloat(r, g, b, a);
    }

    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer nestedObjectSerializer)
    {
        var color = (RgbaFloat)value;
        emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Flow));
        emitter.Emit(new Scalar(color.R.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(color.G.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(color.B.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new Scalar(color.A.ToString(CultureInfo.InvariantCulture)));
        emitter.Emit(new SequenceEnd());
    }
}