using AwesomeAssertions;
using System.Buffers.Binary;
using CherrisEditorWeb.Host;
using Xunit;

namespace CherrisEditorWeb.Tests;

public class ViewportProtocolTests
{
    [Fact]
    public void WriteFrameHeader_DescribesAnRgba8Frame()
    {
        byte[] header = new byte[ViewportProtocol.HeaderSize];

        int written = ViewportProtocol.WriteFrameHeader(header, 1280, 720, 42);

        written.Should().Be(ViewportProtocol.HeaderSize);
        header[0].Should().Be(ViewportProtocol.FormatRgba8);
        BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1)).Should().Be(1280);
        BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5)).Should().Be(720);
        BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(9)).Should().Be(42);
    }

    [Fact]
    public void WriteFrameHeader_RejectsShortBuffers()
    {
        Action write = () => ViewportProtocol.WriteFrameHeader(new byte[4], 8, 8, 1);

        write.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateGreeting_AnnouncesProtocolAndSize()
    {
        string greeting = ViewportProtocol.CreateGreeting(1600, 900, 60);

        greeting.Should().Contain("\"type\":\"hello\"");
        greeting.Should().Contain($"\"protocol\":{ViewportProtocol.Version}");
        greeting.Should().Contain("\"width\":1600");
        greeting.Should().Contain("\"height\":900");
    }

    [Fact]
    public void TryReadCommand_ReadsResize()
    {
        RecordingSink sink = new();

        bool handled = ViewportProtocol.TryReadCommand("""{"type":"resize","width":1024,"height":768}""", sink);

        handled.Should().BeTrue();
        sink.Resizes.Should().Equal((1024, 768));
    }

    [Fact]
    public void TryReadCommand_ReadsKeyState()
    {
        RecordingSink sink = new();

        ViewportProtocol.TryReadCommand("""{"type":"key","code":"KeyW","down":true}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"key","code":"KeyW","down":false}""", sink).Should().BeTrue();

        sink.Keys.Should().Equal(("KeyW", true), ("KeyW", false));
    }

    [Fact]
    public void TryReadCommand_ReadsMouseAndWheel()
    {
        RecordingSink sink = new();

        ViewportProtocol.TryReadCommand("""{"type":"mouseButton","button":2,"down":true}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"mouseMove","dx":4.5,"dy":-2}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"mouse","x":10,"y":20}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"wheel","x":0,"y":-1}""", sink).Should().BeTrue();

        sink.Buttons.Should().Equal((2, true));
        sink.Moves.Should().Equal((4.5f, -2f));
        sink.Positions.Should().Equal((10f, 20f));
        sink.Wheels.Should().Equal((0f, -1f));
    }

    [Fact]
    public void TryReadCommand_ReadsLookDrags()
    {
        RecordingSink sink = new();

        ViewportProtocol.TryReadCommand("""{"type":"look","down":true}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"look","down":false}""", sink).Should().BeTrue();

        sink.Looks.Should().Equal(true, false);
        sink.Buttons.Should().BeEmpty("the drag is its own message, not a button press");
    }

    [Fact]
    public void TryReadCommand_ReadsLockAndFocusLoss()
    {
        RecordingSink sink = new();

        ViewportProtocol.TryReadCommand("""{"type":"lock","locked":true}""", sink).Should().BeTrue();
        ViewportProtocol.TryReadCommand("""{"type":"blur"}""", sink).Should().BeTrue();

        sink.Locks.Should().Equal(true);
        sink.FocusLosses.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"type":"unknown"}""")]
    [InlineData("""{"width":10}""")]
    public void TryReadCommand_IgnoresMalformedOrUnknownMessages(string json)
    {
        RecordingSink sink = new();

        ViewportProtocol.TryReadCommand(json, sink).Should().BeFalse();

        sink.Calls.Should().Be(0);
    }

    [Fact]
    public void TryReadCommand_PressCarriesItsOwnPositionAndAppliesItFirst()
    {
        OrderedSink sink = new();

        ViewportProtocol.TryReadCommand(
            """{"type":"mouseButton","button":0,"down":true,"x":312,"y":180}""",
            sink).Should().BeTrue();

        sink.Calls.Should().Equal("position:312:180", "button:0:down");
    }

    [Fact]
    public void TryReadCommand_PressWithoutAPositionLeavesTheLastOne()
    {
        OrderedSink sink = new();

        ViewportProtocol.TryReadCommand("""{"type":"mouseButton","button":0,"down":true}""", sink)
            .Should().BeTrue();

        sink.Calls.Should().Equal(
            new[] { "button:0:down" },
            "a press without a position says nothing about where it was");
    }

    private sealed class OrderedSink : IViewportInputSink
    {
        public List<string> Calls { get; } = [];

        public void Resize(int width, int height) => Calls.Add($"resize:{width}x{height}");

        public void KeyChanged(string code, bool isDown) => Calls.Add($"key:{code}:{isDown}");

        public void MouseButtonChanged(int button, bool isDown) =>
            Calls.Add($"button:{button}:{(isDown ? "down" : "up")}");

        public void MouseMoved(float deltaX, float deltaY) => Calls.Add($"move:{deltaX}:{deltaY}");

        public void MouseMovedTo(float x, float y) => Calls.Add($"position:{x}:{y}");

        public void Wheel(float x, float y) => Calls.Add($"wheel:{x}:{y}");

        public void MouseLockChanged(bool locked) => Calls.Add($"lock:{locked}");

        public void LookChanged(bool isDown) => Calls.Add($"look:{(isDown ? "down" : "up")}");

        public void FocusLost() => Calls.Add("blur");
    }

    private sealed class RecordingSink : IViewportInputSink
    {
        public List<(int Width, int Height)> Resizes { get; } = [];
        public List<(string Code, bool Down)> Keys { get; } = [];
        public List<(int Button, bool Down)> Buttons { get; } = [];
        public List<(float X, float Y)> Moves { get; } = [];
        public List<(float X, float Y)> Positions { get; } = [];
        public List<(float X, float Y)> Wheels { get; } = [];
        public List<bool> Locks { get; } = [];
        public List<bool> Looks { get; } = [];
        public int FocusLosses { get; private set; }

        public int Calls =>
            Resizes.Count + Keys.Count + Buttons.Count + Moves.Count +
            Positions.Count + Wheels.Count + Locks.Count + Looks.Count + FocusLosses;

        public void Resize(int width, int height) => Resizes.Add((width, height));

        public void KeyChanged(string code, bool isDown) => Keys.Add((code, isDown));

        public void MouseButtonChanged(int button, bool isDown) => Buttons.Add((button, isDown));

        public void MouseMoved(float deltaX, float deltaY) => Moves.Add((deltaX, deltaY));

        public void MouseMovedTo(float x, float y) => Positions.Add((x, y));

        public void Wheel(float x, float y) => Wheels.Add((x, y));

        public void MouseLockChanged(bool locked) => Locks.Add(locked);

        public void LookChanged(bool isDown) => Looks.Add(isDown);

        public void FocusLost() => FocusLosses++;
    }
}
