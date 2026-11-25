using AwesomeAssertions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// The reset arrow reads from the snapshot by itself, which publishes every fifteenth frame and is polled: the
/// click would lag both before the arrow appears and after it is used. The panel therefore has to go through
/// <see cref="PendingResetStates"/> for both the arrow and the value it restores.
/// </summary>
public class InspectorResetMarkupTests
{
    [Fact]
    public void TheArrow_ReadsThePendingState()
    {
        string markup = ReadInspector();

        // The attribute keeps the code transition it is written with, so each value reads "@(CanEdit && ...".
        List<string> parameters = [.. markup.Split("ResetButton CanReset=\"@").Skip(1)];

        parameters.Should().HaveCount(2, "a transform row and a property each have one arrow");
        parameters.Should().OnlyContain(
            parameter => parameter.StartsWith("(CanEdit && ResetState(", StringComparison.Ordinal),
            "every reset button shows the click's own answer until the scene confirms it");
    }

    [Fact]
    public void TheValues_ReadThePendingState()
    {
        string markup = ReadInspector();

        markup.Should().Contain("DisplayTransformText(resetKey, row, axis)", "a reset row shows its default at once");
        markup.Should().Contain("DisplayText(propertyKey, property)");
        markup.Should().Contain("DisplayAxisText(propertyKey, property, axis)");
        markup.Should().Contain("DisplayIsOn(propertyKey, property)", "a reset toggle moves with the arrow");
        markup.Should().Contain("DisplayChoiceText(propertyKey, property)");
        markup.Should().Contain("DisplayHex(propertyKey, property)");
    }

    [Fact]
    public void ANeededSnapshot_DropsTheClick()
    {
        string markup = ReadInspector();

        markup.Should().Contain("_pendingResets.Reconcile(snapshot.Frame)");
        markup.Should().Contain("_pendingResets.Acknowledge(key, revision, frame)");
    }

    private static string ReadInspector()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "OvercoreEditorPhotino", "Components", "InspectorPanel.razor");

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("InspectorPanel.razor was not found above the test binaries.");
    }
}
