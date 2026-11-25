using AwesomeAssertions;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;
using Xunit;

namespace Overcore.Tests.Lifecycle;

/// <summary>
/// Changing scenes at run time: loading a file closes the one that was running, and what was asked to outlive it
/// is carried across. Nothing here touches the asset pipeline — the files are written by the serializer itself.
/// </summary>
public class SceneManagerTests : IDisposable
{
    private readonly List<string> _files = [];
    private readonly SceneManager _scenes = new();

    public SceneManagerTests()
    {
        SceneLoader loader = new(null!);
        ComponentDeserializer.RegisterFactories(loader, null!);
        _scenes.SceneLoader = loader;
    }

    public void Dispose()
    {
        _scenes.Dispose();

        foreach (string file in _files)
        {
            File.Delete(file);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Writes a scene file holding one object per name, and returns its path.</summary>
    private string WriteScene(params string[] names)
    {
        string path = Path.Combine(Path.GetTempPath(), $"overcore-scene-{Guid.NewGuid():N}.scene");

        new SceneSerializer().SaveScene(names.Select(name => new GameObject(name)), path);
        _files.Add(path);

        return path;
    }

    /// <summary>Loads a scene and holds the manager to its promise that the file was readable.</summary>
    private Scene LoadScene(string path)
    {
        Scene? loaded = _scenes.LoadScene(path);
        loaded.Should().NotBeNull($"the scene file '{Path.GetFileName(path)}' should be loadable");

        return loaded!;
    }

    [Fact]
    public void LoadingASceneRunsIt()
    {
        Scene scene = LoadScene(WriteScene("Arena", "Spawn"));

        _scenes.ActiveScene.Should().BeSameAs(scene);
        Scene.Active.Should().BeSameAs(scene);
        scene.IsStarted.Should().BeTrue("a loaded scene runs straight away");
        scene.GameObjects.Select(go => go.Name).Should().Contain("Arena").And.Contain("Spawn");
        scene.GameObjects.Should().OnlyContain(go => go.scene == scene);
    }

    [Fact]
    public void LoadingASceneClosesTheOneItReplaces()
    {
        Scene old = LoadScene(WriteScene("Old"));
        GameObject oldObject = old.GameObjects[0];

        Scene fresh = LoadScene(WriteScene("New"));

        _scenes.OpenScenes.Should().ContainSingle().Which.Should().BeSameAs(fresh);
        old.IsStarted.Should().BeFalse();
        old.GameObjects.Should().BeEmpty();
        oldObject.IsDestroyed.Should().BeTrue("the objects of an unloaded scene are destroyed with it");
    }

    [Fact]
    public void WhatWasToldToOutliveItsSceneIsCarriedAcross()
    {
        Scene old = LoadScene(WriteScene("Old"));

        GameObject passenger = new("Passenger");
        old.AddGameObject(passenger);

        _scenes.DontDestroyOnLoad(passenger);
        _scenes.PersistentObjects.Should().Contain(passenger);

        Scene fresh = LoadScene(WriteScene("New"));

        passenger.IsDestroyed.Should().BeFalse();
        passenger.scene.Should().BeSameAs(fresh);
        fresh.GameObjects.Should().Contain(passenger);
        old.GameObjects.Should().NotContain(passenger);

        _scenes.UndoDontDestroyOnLoad(passenger).Should().BeTrue();
        _scenes.PersistentObjects.Should().BeEmpty();
    }

    [Fact]
    public void ASceneLoadThatFailsLeavesTheRunningSceneAlone()
    {
        Scene running = LoadScene(WriteScene("Kept"));

        _scenes.LoadScene(Path.Combine(Path.GetTempPath(), "overcore-does-not-exist.scene")).Should().BeNull();

        _scenes.ActiveScene.Should().BeSameAs(running);
        running.IsStarted.Should().BeTrue();
    }

    [Fact]
    public void WithoutALoaderNothingIsLoaded()
    {
        _scenes.SceneLoader = null;

        Scene running = new("Running", []);
        _scenes.OpenScene(running);

        _scenes.LoadScene(WriteScene("Ignored")).Should().BeNull();
        _scenes.ActiveScene.Should().BeSameAs(running);
    }

    [Fact]
    public void UnloadingFallsBackToTheSceneOpenedBeforeIt()
    {
        Scene first = new("First", []);
        Scene second = new("Second", []);

        _scenes.OpenScene(first);
        _scenes.OpenScene(second);

        _scenes.UnloadScene(second).Should().BeTrue();

        _scenes.ActiveScene.Should().BeSameAs(first);
        Scene.Active.Should().BeSameAs(first);
        second.GameObjects.Should().BeEmpty();

        _scenes.UnloadScene("First").Should().BeTrue();

        _scenes.ActiveScene.Should().BeNull();
        Scene.Active.Should().BeNull();
        _scenes.UnloadScene("First").Should().BeFalse();
        _scenes.UnloadScene(first).Should().BeFalse();
    }

    [Fact]
    public void TheActiveSceneChangeIsAnnounced()
    {
        List<Scene> announced = [];
        _scenes.OnActiveSceneChanged += scene => announced.Add(scene);

        Scene first = new("First", []);
        Scene second = new("Second", []);

        _scenes.OpenScene(first);
        _scenes.OpenScene(second);
        _scenes.SetActiveScene(first);
        _scenes.UnloadScene(second);

        // Opening announces, and switching back announces; unloading a scene that is not the active one does not.
        announced.Should().Equal(first, second, first);
    }

    [Fact]
    public void AFramesWorkWithoutASceneIsHarmless()
    {
        // Editors and hosts call this before anything is open; it must not fall over.
        _scenes.Update(0.016f);

        _scenes.GameObjects.Should().BeEmpty();
        _scenes.Lights.Should().BeEmpty();
        _scenes.MainCamera.Should().BeNull();
    }
}
