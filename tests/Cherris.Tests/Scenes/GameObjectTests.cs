using System.Numerics;
using AwesomeAssertions;
using Cherris.Components;
using Cherris.Core;
using Xunit;

namespace Cherris.Tests;

public class GameObjectTests
{
    [Fact]
    public void AddAndRemoveComponentsMaintainsOwnershipAndLookup()
    {
        var gameObject = new GameObject("lamp");
        var first = new Light();
        var second = new Light();

        gameObject.AddComponent(first).Should().BeSameAs(first);
        gameObject.AddComponent(second);

        first.GameObject.Should().BeSameAs(gameObject);
        gameObject.GetComponent<Light>().Should().BeSameAs(first);
        gameObject.GetComponents<Light>().Should().Equal(first, second);

        gameObject.RemoveComponent<Light>();
        gameObject.GetComponents<Light>().Should().Equal(second);
        gameObject.RemoveComponent(second);
        gameObject.GetComponent<Light>().Should().BeNull();
    }

    [Fact]
    public void ObjectsWithoutMeshesUseTheirPositionForBounds()
    {
        var gameObject = new GameObject();
        gameObject.Transform.Position = new Vector3(1, 2, 3);

        var bounds = gameObject.GetWorldSpaceAABB();

        bounds.Min.Should().Be(new Vector3(1, 2, 3));
        bounds.Max.Should().Be(new Vector3(1, 2, 3));
    }

    [Fact]
    public void LightsHaveSelectableBoundsAroundTheirPosition()
    {
        var gameObject = new GameObject();
        gameObject.Transform.Position = new Vector3(1, 2, 3);
        gameObject.AddComponent(new Light());

        var bounds = gameObject.GetWorldSpaceAABB();

        bounds.Min.Should().Be(new Vector3(0.75f, 1.75f, 2.75f));
        bounds.Max.Should().Be(new Vector3(1.25f, 2.25f, 3.25f));
    }

    [Fact]
    public void ReparentingUpdatesChildrenAndWorldTransform()
    {
        var parent = new GameObject();
        var otherParent = new GameObject();
        var child = new GameObject();
        parent.Transform.Position = new Vector3(10, 0, 0);
        otherParent.Transform.Position = new Vector3(0, 5, 0);
        child.Transform.Position = new Vector3(1, 2, 3);

        child.Transform.Parent = parent.Transform;
        parent.Transform.Children.Should().ContainSingle().Which.Should().BeSameAs(child.Transform);
        Vector3.Transform(Vector3.Zero, child.Transform.GetModelMatrix()).Should().Be(new Vector3(11, 2, 3));

        child.Transform.Parent = otherParent.Transform;
        parent.Transform.Children.Should().BeEmpty();
        otherParent.Transform.Children.Should().ContainSingle().Which.Should().BeSameAs(child.Transform);
        Vector3.Transform(Vector3.Zero, child.Transform.GetModelMatrix()).Should().Be(new Vector3(1, 7, 3));
    }
}
