using AwesomeAssertions;
using CherrisEditorSession;
using CherrisEditorSession.Host;
using Xunit;

namespace CherrisEditorSession.Tests;

public class PendingComponentTogglesTests
{
    [Fact]
    public void StaleSnapshots_DoNotUndoTheUsersClick()
    {
        PendingComponentToggles edits = new();
        Guid id = Guid.NewGuid();
        long revision = edits.Begin(id, "Light", true);

        edits.Reconcile(10);
        edits.Value(id, "Light", false).Should().BeTrue();
        edits.Acknowledge(id, "Light", revision, 12);
        edits.Reconcile(12);
        edits.Value(id, "Light", false).Should().BeTrue("a snapshot from before execution is stale");
        edits.Reconcile(13);
        edits.Value(id, "Light", true).Should().BeTrue();
        edits.Value(id, "Light", false).Should().BeFalse("after acknowledgement the scene is authoritative, including undo");
    }

    [Fact]
    public void RapidClicks_IgnoreAcknowledgementsForOlderCommands()
    {
        PendingComponentToggles edits = new();
        Guid id = Guid.NewGuid();
        long first = edits.Begin(id, "Light", true);
        long last = edits.Begin(id, "Light", false);

        edits.Acknowledge(id, "Light", first, 20);
        edits.Reconcile(21);
        edits.Value(id, "Light", true).Should().BeFalse();
        edits.Acknowledge(id, "Light", last, 22);
        edits.Reconcile(22);
        edits.Value(id, "Light", true).Should().BeFalse();
        edits.Reconcile(23);
        edits.Value(id, "Light", true).Should().BeTrue("a later snapshot may show a refused or subsequently undone edit");
    }

    [Fact]
    public void FailedEnqueue_CancelsOnlyTheMatchingEdit()
    {
        PendingComponentToggles edits = new();
        Guid id = Guid.NewGuid();
        long first = edits.Begin(id, "Light", true);
        long last = edits.Begin(id, "Light", false);
        edits.Cancel(id, "Light", first);
        edits.Value(id, "Light", true).Should().BeFalse();
        edits.Cancel(id, "Light", last);
        edits.Value(id, "Light", true).Should().BeTrue();
    }

    [Fact]
    public void SeparateComponents_AreReconciledIndependently()
    {
        PendingComponentToggles edits = new();
        Guid id = Guid.NewGuid();
        long light = edits.Begin(id, "Light", true);
        edits.Begin(id, "Camera", false);
        edits.Acknowledge(id, "Light", light, 10);
        edits.Reconcile(11);
        edits.Value(id, "Light", false).Should().BeFalse();
        edits.Value(id, "Camera", true).Should().BeFalse();
    }

    [Fact]
    public void CompletionCommand_NotifiesEvenWhenTheCommandThrows()
    {
        bool notified = false;
        CompletionCommand command = new(new ThrowingCommand(), () => notified = true);
        Action execute = () => command.Execute(null!);
        execute.Should().Throw<InvalidOperationException>();
        notified.Should().BeTrue();
    }

    private sealed class ThrowingCommand : IEditorCommand
    {
        public string Describe => "throw";
        public string? Execute(EditorCommandContext context) => throw new InvalidOperationException();
    }
}
