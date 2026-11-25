using AwesomeAssertions;
using Xunit;

namespace OvercoreEditorSession.Tests;

/// <summary>
/// Covers the trigger behind "the scripts changed on disk": that a save is noticed once, that rewriting identical
/// bytes is not a change, and that events from the rest of the project are ignored.
/// </summary>
public class ProjectScriptWatcherTests
{
    [Fact]
    public void Check_ReportsNothingUntilTheSourcesChange()
    {
        using TemporaryProject project = new();
        Write(project, "Scripts/Player.cs", "public class Player { }");

        int reports = 0;
        using ProjectScriptWatcher watcher = new(project.Root, () => reports++);

        watcher.Check().Should().BeFalse("the session has just compiled what is there");
        reports.Should().Be(0);
    }

    [Fact]
    public void Check_ReportsAnAddedChangedAndDeletedScript_OnceEach()
    {
        using TemporaryProject project = new();

        int reports = 0;
        using ProjectScriptWatcher watcher = new(project.Root, () => reports++);

        string file = Path.Combine(project.Root, "Scripts", "Player.cs");

        Write(project, "Scripts/Player.cs", "public class Player { }");
        watcher.Check().Should().BeTrue("a new script is a change");
        reports.Should().Be(1);

        watcher.Check().Should().BeFalse("nothing changed since the last check");
        reports.Should().Be(1);

        File.WriteAllText(file, "public class Player { int health; }");
        watcher.Check().Should().BeTrue("new bytes are a change");
        reports.Should().Be(2);

        File.Delete(file);
        watcher.Check().Should().BeTrue("a deleted script is a change");
        reports.Should().Be(3);
    }

    [Fact]
    public void Check_IgnoresIdenticalBytesWrittenAgain()
    {
        using TemporaryProject project = new();
        Write(project, "Scripts/Player.cs", "public class Player { }");

        int reports = 0;
        using ProjectScriptWatcher watcher = new(project.Root, () => reports++);

        // What a touch, a checkout that restores the same file, or an editor that saves without editing looks like.
        File.WriteAllText(Path.Combine(project.Root, "Scripts", "Player.cs"), "public class Player { }");

        watcher.Check().Should().BeFalse("the hash is over bytes, not timestamps");
        reports.Should().Be(0);
    }

    [Fact]
    public void Check_IgnoresTheRestOfTheProject()
    {
        using TemporaryProject project = new();
        Write(project, "Scripts/Player.cs", "public class Player { }");

        int reports = 0;
        using ProjectScriptWatcher watcher = new(project.Root, () => reports++);

        // Scenes, assets and thumbnails are written all the time; none of them is something to compile.
        Write(project, "Levels/Main.yaml", "name: Main");
        Write(project, "Scripts/notes.txt", "not a script");

        watcher.Check().Should().BeFalse();
        reports.Should().Be(0);
    }

    [Fact]
    public void Check_SeesScriptsWrittenInSubfolders()
    {
        using TemporaryProject project = new();

        int reports = 0;
        using ProjectScriptWatcher watcher = new(project.Root, () => reports++);

        Write(project, "Scripts/Combat/Damage.cs", "public class Damage { }");

        watcher.Check().Should().BeTrue("the compiler reads subfolders too");
        reports.Should().Be(1);
    }

    [Fact]
    public async Task Watcher_ReportsASavedScriptOnceItGoesQuiet()
    {
        using TemporaryProject project = new();

        TaskCompletionSource reported = new(TaskCreationOptions.RunContinuationsAsynchronously);

        using ProjectScriptWatcher watcher = new(project.Root, () => reported.TrySetResult());

        Write(project, "Scripts/Player.cs", "public class Player { }");

        // The quiet period is 400 ms and a file system reports within a few; the bound is only there so a runner
        // that never delivers an event fails the test instead of hanging it.
        Task finished = await Task.WhenAny(reported.Task, Task.Delay(TimeSpan.FromSeconds(15)));

        finished.Should().BeSameAs(reported.Task, "a save in another window is what the watcher exists for");
    }

    /// <summary>Writes a file with the separators a project uses, creating the folders it needs.</summary>
    private static void Write(TemporaryProject project, string relativePath, string content)
    {
        string path = Path.Combine(project.Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
