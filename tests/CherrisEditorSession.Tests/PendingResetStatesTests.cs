using AwesomeAssertions;
using CherrisEditorSession;
using Xunit;

namespace CherrisEditorSession.Tests;

/// <summary>
/// The reset arrow has to answer the click, not the next snapshot: the scene is published every fifteenth
/// frame and polled by the UI, so a row that only reads the snapshot lags by a noticeable fraction of a second
/// both before the arrow appears and after it is clicked.
/// </summary>
public class PendingResetStatesTests
{
    [Fact]
    public void AnEdit_ShowsTheArrowBeforeTheNextSnapshot()
    {
        PendingResetStates states = new();
        states.Begin("0:Light.Intensity", canReset: true, frame: 30);

        states.Value("0:Light.Intensity", snapshotValue: false).Should().BeTrue(
            "the property differs from its default the moment it is edited");
    }

    [Fact]
    public void AReset_HidesTheArrowAndShowsTheDefaultAtOnce()
    {
        PendingResetStates states = new();
        states.Begin("0:Light.Intensity", canReset: false, frame: 30, text: "1");

        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeFalse(
            "the arrow goes as soon as it is clicked, not when the snapshot agrees");
        states.Text("0:Light.Intensity").Should().Be("1", "the field shows the default the reset is putting back");
    }

    [Fact]
    public void ASnapshotFromBeforeTheCommand_DoesNotUndoTheClick()
    {
        PendingResetStates states = new();
        states.Begin("0:Light.Intensity", canReset: false, frame: 30, text: "1");

        // The snapshot was published between the click and the frame that drained the command.
        states.Reconcile(45);
        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeFalse(
            "a snapshot taken before the command ran is stale, whatever its frame");

        states.Acknowledge("0:Light.Intensity", 1, beforeExecutionFrame: 45);
        states.Reconcile(45);
        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeFalse("the command's own frame proves nothing yet");

        states.Reconcile(60);
        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeTrue(
            "a snapshot after the command ran shows the engine's own answer, including after an undo");
        states.Text("0:Light.Intensity").Should().BeNull();
    }

    [Fact]
    public void AnUnansweredEdit_IsLetGoSoARowCannotStick()
    {
        PendingResetStates states = new();
        states.Begin("0:Light.Intensity", canReset: true, frame: 30);

        states.Reconcile(30 + PendingResetStates.MaxHoldFrames);
        states.Value("0:Light.Intensity", snapshotValue: false).Should().BeTrue("still within the hold");
        states.Text("0:Light.Intensity").Should().BeNull();

        states.Reconcile(30 + PendingResetStates.MaxHoldFrames + 1);
        states.Value("0:Light.Intensity", snapshotValue: false).Should().BeFalse("a command that never ran cannot pin the row");
    }

    [Fact]
    public void RapidClicks_IgnoreAcknowledgementsForOlderEdits()
    {
        PendingResetStates states = new();
        long first = states.Begin("o:transform.Scale", canReset: true, frame: 10);
        long last = states.Begin("o:transform.Scale", canReset: false, frame: 10);

        states.Acknowledge("o:transform.Scale", first, beforeExecutionFrame: 20);
        states.Reconcile(21);
        states.Value("o:transform.Scale", snapshotValue: true).Should().BeFalse("the older edit's frame says nothing about the newer one");

        states.Acknowledge("o:transform.Scale", last, beforeExecutionFrame: 35);
        states.Reconcile(36);
        states.Value("o:transform.Scale", snapshotValue: true).Should().BeTrue();
    }

    [Fact]
    public void FailedEnqueue_CancelsOnlyTheMatchingEdit()
    {
        PendingResetStates states = new();
        long first = states.Begin("0:Light.Intensity", canReset: true, frame: 10);
        long last = states.Begin("0:Light.Intensity", canReset: false, frame: 10);

        states.Cancel("0:Light.Intensity", first);
        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeFalse("a newer edit is still pending");

        states.Cancel("0:Light.Intensity", last);
        states.Value("0:Light.Intensity", snapshotValue: true).Should().BeTrue("nothing is pending, so the snapshot shows");
    }

    [Fact]
    public void SeparateRows_AreReconciledIndependently()
    {
        PendingResetStates states = new();
        states.Begin("o:transform.Position", canReset: true, frame: 10);
        long scale = states.Begin("o:transform.Scale", canReset: false, frame: 10);

        states.Acknowledge("o:transform.Scale", scale, beforeExecutionFrame: 20);
        states.Reconcile(21);

        states.Value("o:transform.Position", snapshotValue: false).Should().BeTrue();
        states.Value("o:transform.Scale", snapshotValue: true).Should().BeTrue("the reset is confirmed by the scene");
    }

    [Fact]
    public void ARowWithNoText_ShowsTheSnapshot()
    {
        PendingResetStates states = new();
        states.Begin("o:transform.Rotation", canReset: true, frame: 10);

        states.Text("o:transform.Rotation").Should().BeNull("a typed value is already in the field");
    }
}
