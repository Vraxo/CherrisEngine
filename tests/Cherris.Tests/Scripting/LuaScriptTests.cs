using System.Numerics;
using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using Cherris.Scripting;
using Cherris.Serialization;
using Cherris.Utils;
using Xunit;

namespace Cherris.Tests.Scripting;

public class LuaScriptTests
{
    [Fact]
    public void LuaCallsLifecycleAndTransformsItsOwnObject()
    {
        const string source = """
            function start()
                set_position(1, 2, 3)
            end
            function update(dt)
                translate(dt * 2, 0, 0)
            end
            """;
        var gameObject = new GameObject("Lua object");
        var instance = new LuaScriptLanguage().CreateInstance(source, "Scripts/move.lua", gameObject);

        instance.Start();
        instance.Update(0.5f);

        gameObject.Transform.Position.Should().Be(new Vector3(2, 2, 3));
    }

    [Fact]
    public void LuaGlobalsAreIsolatedPerInstance()
    {
        const string source = """
            count = 0
            function update(dt)
                count = count + 1
                set_position(count, 0, 0)
            end
            """;
        var first = new GameObject();
        var second = new GameObject();
        var language = new LuaScriptLanguage();
        var one = language.CreateInstance(source, "one.lua", first);
        var two = language.CreateInstance(source, "two.lua", second);

        one.Update(1);
        one.Update(1);
        two.Update(1);

        first.Transform.Position.X.Should().Be(2);
        second.Transform.Position.X.Should().Be(1);
    }

    [Fact]
    public void LuaCanReadPositionAndDoesNotExposeFileOrModuleLoading()
    {
        const string source = """
            assert(io == nil and os == nil and require == nil and dofile == nil)
            function start()
                local x, y, z = get_position()
                set_position(x + 1, y + 2, z + 3)
            end
            """;
        var gameObject = new GameObject();
        var instance = new LuaScriptLanguage().CreateInstance(source, "safe.lua", gameObject);
        instance.Start();
        gameObject.Transform.Position.Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void LanguageScriptLoadsProjectSourceAndRuns()
    {
        string root = Path.Combine(Path.GetTempPath(), "CherrisLuaTests", Guid.NewGuid().ToString("N"));
        string? previousRoot = ProjectFiles.ProjectRoot;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Scripts"));
            File.WriteAllText(Path.Combine(root, "Scripts", "move.lua"), "function update(dt) translate(dt, 0, 0) end");
            ProjectFiles.ProjectRoot = root;
            var gameObject = new GameObject();
            var component = gameObject.AddComponent(new LanguageScript { SourcePath = "Scripts/move.lua" });

            component.Start();
            component.Update(0.5f);

            gameObject.Transform.Position.X.Should().Be(0.5f);
        }
        finally
        {
            ProjectFiles.ProjectRoot = previousRoot;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LanguageScriptRoundTripsItsEnabledToggle_ButNotTheEditorsHoldOnIt()
    {
        var gameObject = new GameObject("test");
        gameObject.AddComponent(new LanguageScript
        {
            Language = "lua",
            SourcePath = "Scripts/move.lua",
            Enabled = false,
            IsSuspended = true
        });
        string yaml = new SceneSerializer().SerializeSceneToString([gameObject]);
        var loader = new SceneLoader(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);

        var result = loader.LoadSceneFromYaml(yaml);

        result.Should().ContainSingle();
        var script = result[0].GetComponent<LanguageScript>();
        script.Should().NotBeNull();
        script!.Language.Should().Be("lua");
        script.SourcePath.Should().Be("Scripts/move.lua");
        script.Enabled.Should().BeFalse("the author's toggle is part of the scene, as it is in Unity");
        script.IsSuspended.Should().BeFalse("the editor's hold on a script is not saved with it");
        yaml.Should().Contain("Enabled: false").And.NotContain("IsSuspended");
    }

    [Theory]
    [InlineData("../secret.lua")]
    [InlineData("Scripts/../secret.lua")]
    [InlineData("/Scripts/main.lua")]
    [InlineData("C:\\Scripts\\main.lua")]
    [InlineData("")]
    public void ScriptPathsCannotEscapeTheScriptsFolder(string path)
    {
        Action validate = () => LanguageScript.ValidateSourcePath(path);
        validate.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InvalidLuaReportsAnError()
    {
        Action load = () => new LuaScriptLanguage().CreateInstance("function !", "broken.lua", new GameObject());
        load.Should().Throw<Exception>();
    }
}
