using AwesomeAssertions;
using System.Numerics;
using Xunit;

namespace CherrisEditorSession.Tests;

public class SceneTreeTests
{
    [Fact]
    public void Build_ReturnsChildren_InSceneOrder()
    {
        Guid rootId = Guid.NewGuid();
        Guid firstChild = Guid.NewGuid();
        Guid secondChild = Guid.NewGuid();

        SceneNodeSnapshot[] nodes =
        [
            Node(rootId, "Root"),
            Node(firstChild, "First", rootId),
            Node(secondChild, "Second", rootId),
            Node(Guid.NewGuid(), "Grandchild", firstChild)
        ];

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build(nodes);

        roots.Should().ContainSingle();
        roots[0].Node.Id.Should().Be(rootId);
        roots[0].Children.Select(child => child.Node.Name).Should().Equal("First", "Second");
        roots[0].Children[0].Children.Should().ContainSingle()
            .Which.Node.Name.Should().Be("Grandchild");
    }

    [Fact]
    public void Build_PromotesObjectsWhoseParentIsMissing()
    {
        Guid orphanParent = Guid.NewGuid();

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build([Node(Guid.NewGuid(), "Orphan", orphanParent)]);

        roots.Should().ContainSingle().Which.Node.Name.Should().Be("Orphan");
    }

    [Fact]
    public void Build_PromotesBothMembersOfAParentCycle()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build(
        [
            Node(first, "A", second),
            Node(second, "B", first)
        ]);

        // A cycle would hang the panel, so both objects come back as roots and the tree stops there.
        roots.Select(node => node.Node.Name).Should().BeEquivalentTo("A", "B");
        roots.Should().OnlyContain(node => node.Children.Count == 0);
    }

    [Fact]
    public void Build_TreatsASelfParentAsRoot()
    {
        Guid id = Guid.NewGuid();

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build([Node(id, "Loop", id)]);

        roots.Should().ContainSingle().Which.Node.Name.Should().Be("Loop");
    }

    [Fact]
    public void Flatten_SkipsChildrenOfCollapsedNodes()
    {
        Guid rootId = Guid.NewGuid();
        Guid childId = Guid.NewGuid();

        IReadOnlyList<SceneTreeNode> roots = SceneTree.Build(
        [
            Node(rootId, "Root"),
            Node(childId, "Child", rootId)
        ]);

        IReadOnlyList<(SceneNodeSnapshot Node, int Depth)> expanded = SceneTree.Flatten(roots, _ => true);
        IReadOnlyList<(SceneNodeSnapshot Node, int Depth)> collapsed = SceneTree.Flatten(roots, node => node.Id != rootId);

        expanded.Select(row => (row.Node.Name, row.Depth)).Should().Equal(("Root", 0), ("Child", 1));
        collapsed.Select(row => row.Node.Name).Should().Equal("Root");
    }

    [Fact]
    public void Build_ReturnsNothing_WhenThereAreNoNodes()
    {
        SceneTree.Build([]).Should().BeEmpty();
    }

    private static SceneNodeSnapshot Node(Guid id, string name, Guid? parentId = null) =>
        new(id, name, Vector3.Zero, Quaternion.Identity, Vector3.One, [], parentId);
}
