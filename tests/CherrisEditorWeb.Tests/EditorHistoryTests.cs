using AwesomeAssertions;
using CherrisEditorWeb.Host;
using Xunit;

namespace CherrisEditorWeb.Tests;

/// <summary>
/// Covers the stacks themselves, apart from any command: what the Edit menu reads, and what happens at the
/// bottom of the history.
/// </summary>
public class EditorHistoryTests
{
    [Fact]
    public void EmptyHistory_HasNothingToOffer()
    {
        EditorHistory history = new();

        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse();
        history.UndoLabel.Should().BeNull("an empty menu item has nothing to name");
        history.RedoLabel.Should().BeNull();
        history.TakeUndo().Should().BeNull();
        history.TakeRedo().Should().BeNull();
    }

    [Fact]
    public void UndoAndRedo_MoveCommandsBetweenTheStacks()
    {
        EditorHistory history = new();
        StubCommand first = new("first");
        StubCommand second = new("second");

        history.Record(first);
        history.Record(second);

        history.UndoLabel.Should().Be("second");

        history.TakeUndo().Should().BeSameAs(second);
        history.UndoLabel.Should().Be("first");
        history.RedoLabel.Should().Be("second");

        history.TakeRedo().Should().BeSameAs(second);
        history.RedoLabel.Should().BeNull();
        history.UndoLabel.Should().Be("second");
    }

    [Fact]
    public void Recording_ClearsTheRedoBranch()
    {
        EditorHistory history = new();

        history.Record(new StubCommand("first"));
        history.TakeUndo();
        history.CanRedo.Should().BeTrue();

        history.Record(new StubCommand("second"));

        history.CanRedo.Should().BeFalse("the branch that was undone is not what the new edit builds on");
    }

    [Fact]
    public void History_KeepsTheNewestEdits_AndForgetsTheOldest()
    {
        EditorHistory history = new();

        for (int index = 0; index < 250; index++)
        {
            history.Record(new StubCommand("edit " + index));
        }

        history.UndoLabel.Should().Be("edit 249");

        int undone = 0;

        while (history.CanUndo)
        {
            history.TakeUndo();
            undone++;
        }

        undone.Should().Be(200, "the stack is capped so a long session cannot grow without bound");
    }

    [Fact]
    public void Clear_DropsWhatBothStacksWereHolding()
    {
        EditorHistory history = new();

        history.Record(new StubCommand("first"));
        history.Record(new StubCommand("second"));
        history.TakeUndo();

        history.Clear();

        history.CanUndo.Should().BeFalse();
        history.CanRedo.Should().BeFalse("a scene switch takes the redo branch with it");
        history.UndoLabel.Should().BeNull();
    }

    private sealed class StubCommand(string describe) : IUndoableCommand
    {
        public string Describe => describe;

        public string? Execute(EditorCommandContext context) => null;

        public string? Undo(EditorCommandContext context) => null;

        public string? Redo(EditorCommandContext context) => null;
    }
}
