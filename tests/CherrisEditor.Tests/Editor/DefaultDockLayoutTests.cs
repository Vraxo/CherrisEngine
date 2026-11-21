using System.Numerics;
using AwesomeAssertions;
using CherrisEditor;
using Xunit;

namespace CherrisEditor.Tests.Editor;

public class DefaultDockLayoutTests
{
    [Fact]
    public void FirstRunAndLegacyFloatingPanelsReceiveDefaults()
    {
        DefaultDockLayout.NeedsDefault("").Should().BeTrue();
        DefaultDockLayout.NeedsDefault("[Window][Viewport]\nPos=10,10\nSize=32,32\n").Should().BeTrue();
    }

    [Fact]
    public void ExistingDockedOrPreviouslyInitializedLayoutsArePreserved()
    {
        DefaultDockLayout.NeedsDefault("[Window][Hierarchy]\nDockId=0x00000042,0\n").Should().BeFalse();
        DefaultDockLayout.NeedsDefault("[Window][CherrisDefaultLayoutV1]\nPos=0,0\n").Should().BeFalse();
        DefaultDockLayout.NeedsDefault("[Window][Other]\nDockId=0x00000042,0\n").Should().BeTrue();
    }

    [Fact]
    public void DefaultLayoutPlacesPanelsInSeparateDockNodes()
    {
        var ids = new DefaultDockLayout.NodeIds(1, 2, 3, 4, 5, 6, 7);

        string layout = DefaultDockLayout.Create(ids, Vector2.Zero, new Vector2(1000, 800)).Replace("\r\n", "\n");

        layout.Should().Contain("[Window][Hierarchy]\nPos=0,0\nSize=320,240\nCollapsed=0\nDockId=0x00000002,0");
        layout.Should().Contain("[Window][Inspector]\nPos=0,0\nSize=320,240\nCollapsed=0\nDockId=0x00000005,0");
        layout.Should().Contain("[Window][Viewport]\nPos=0,0\nSize=320,240\nCollapsed=0\nDockId=0x00000006,0");
        layout.Should().Contain("DockNode ID=0x00000007 Parent=0x00000004 SizeRef=560,224");
        layout.Should().Contain("DockId=0x00000007,1");
        DefaultDockLayout.NeedsDefault(layout).Should().BeFalse();
    }
}
