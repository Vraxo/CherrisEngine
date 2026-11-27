using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace OvercoreEditorSession.Host;

/// <summary>
/// The wire protocol between the host and the browser client.
/// </summary>
/// <remarks>
/// <para>Server to client, text: one JSON greeting on connect.</para>
/// <para>
/// Server to client, binary: a frame message, <see cref="HeaderSize"/> bytes of header followed by
/// tightly packed RGBA8 pixels in top-down row order.
/// </para>
/// <code>
/// byte 0      format     (1 = RGBA8)
/// bytes 1-4   width      little-endian int32
/// bytes 5-8   height     little-endian int32
/// bytes 9-12  sequence   little-endian int32
/// bytes 13-   pixels     width * height * 4 bytes
/// </code>
/// <para>
/// Client to server, text: JSON commands, one per message, dispatched to an
/// <see cref="IViewportInputSink"/> by <see cref="TryReadCommand"/>.
/// </para>
/// </remarks>
public static class ViewportProtocol
{
    /// <summary>Protocol version sent in the greeting; bump when the layouts change.</summary>
    public const int Version = 1;

    /// <summary>Size in bytes of the binary frame header.</summary>
    public const int HeaderSize = 13;

    /// <summary>Format byte for RGBA8 pixels.</summary>
    public const byte FormatRgba8 = 1;

    /// <summary>Writes the binary frame header and returns <see cref="HeaderSize"/>.</summary>
    public static int WriteFrameHeader(Span<byte> destination, int width, int height, int sequence)
    {
        if (destination.Length < HeaderSize)
        {
            throw new ArgumentException($"Header needs {HeaderSize} bytes.", nameof(destination));
        }

        destination[0] = FormatRgba8;
        BinaryPrimitives.WriteInt32LittleEndian(destination[1..5], width);
        BinaryPrimitives.WriteInt32LittleEndian(destination[5..9], height);
        BinaryPrimitives.WriteInt32LittleEndian(destination[9..13], sequence);

        return HeaderSize;
    }

    /// <summary>Builds the JSON greeting sent to a client as soon as it connects.</summary>
    public static string CreateGreeting(int width, int height, double targetFrameRate)
    {
        return JsonSerializer.Serialize(new
        {
            type = "hello",
            protocol = Version,
            width,
            height,
            fps = targetFrameRate
        });
    }

    /// <summary>
    /// Parses one client command and applies it to <paramref name="sink"/>.
    /// </summary>
    /// <returns>False for malformed JSON or unknown command types.</returns>
    public static bool TryReadCommand(string json, IViewportInputSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out JsonElement typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            switch (typeElement.GetString())
            {
                case "resize":
                    sink.Resize(ReadInt(root, "width"), ReadInt(root, "height"));
                    return true;

                case "key":
                    sink.KeyChanged(ReadString(root, "code") ?? string.Empty, ReadBool(root, "down"));
                    return true;

                case "textInput":
                    sink.TextInput(ReadString(root, "text") ?? string.Empty);
                    return true;

                case "mouseButton":
                    // A press carries where it happened, so a click that arrives before any move still picks
                    // where it landed. The position goes in first: the press is what triggers the pick.
                    if (root.TryGetProperty("x", out _) && root.TryGetProperty("y", out _))
                    {
                        sink.MouseMovedTo(ReadFloat(root, "x"), ReadFloat(root, "y"));
                    }

                    sink.MouseButtonChanged(ReadInt(root, "button"), ReadBool(root, "down"));
                    return true;

                case "mouseMove":
                    sink.MouseMoved(ReadFloat(root, "dx"), ReadFloat(root, "dy"));
                    return true;

                case "mouse":
                    sink.MouseMovedTo(ReadFloat(root, "x"), ReadFloat(root, "y"));
                    return true;

                case "wheel":
                    sink.Wheel(ReadFloat(root, "x"), ReadFloat(root, "y"));
                    return true;

                case "look":
                    sink.LookChanged(ReadBool(root, "down"));
                    return true;

                case "pageLog":
                    // A line the page wants in the editor's log, for the input and drawing problems a webview
                    // cannot show from the host's side.
                    sink.PageLog(ReadString(root, "text") ?? string.Empty);
                    return true;

                case "lock":
                    sink.MouseLockChanged(ReadBool(root, "locked"));
                    return true;

                case "blur":
                    sink.FocusLost();
                    return true;

                default:
                    return false;
            }
        }
    }

    /// <summary>Encodes a UTF-8 payload as an array segment, for socket accepts.</summary>
    public static ArraySegment<byte> ToUtf8Segment(string text)
    {
        return new ArraySegment<byte>(Encoding.UTF8.GetBytes(text));
    }

    private static int ReadInt(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.TryGetInt32(out int value)
            ? value
            : 0;
    }

    private static float ReadFloat(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.TryGetDouble(out double value)
            ? (float)value
            : 0f;
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.True;
    }

    private static string? ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
    }
}
