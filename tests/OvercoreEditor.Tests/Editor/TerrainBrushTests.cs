using AwesomeAssertions;
using Overcore.Core;
using Overcore.Terrain;
using OvercoreEditor.UI.Viewport;
using OvercoreEditor.Undo.Commands;
using System.Numerics;
using Xunit;
using TerrainComponent = Overcore.Components.Terrain;

namespace OvercoreEditor.Tests.Editor;

/// <summary>
/// Covers the editor's side of terrain sculpting: the stroke as history, and the brush's per-frame
/// strength, which is what turns a held mouse into a rate rather than a teleport.
/// </summary>
public class TerrainBrushTests
{
    [Fact]
    public void StrokeCommand_UndoRestoresTheGround_AndRedoReappliesIt()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(11, new Vector3(10f, 10f, 10f));

        float[] heightsBefore = terrain.TerrainData.GetHeightmap();
        float[] alphamapBefore = terrain.TerrainData.GetAlphamap();
        byte[] holesBefore = terrain.TerrainData.GetHolesPacked();

        terrain.Sculpt(new Vector2(0.5f, 0.5f), 0.3f, 2f, TerrainBrushMode.Raise);
        terrain.TerrainData.GetHeight(5, 5).Should().BeApproximately(2f, 1e-3f);

        TerrainStrokeCommand stroke = new(
            terrain,
            heightsBefore, alphamapBefore, holesBefore,
            terrain.TerrainData.GetHeightmap(), terrain.TerrainData.GetAlphamap(), terrain.TerrainData.GetHolesPacked());

        stroke.Undo();
        terrain.TerrainData.GetHeight(5, 5).Should().Be(0f);
        terrain.Mesh!.Vertices.Max(vertex => vertex.Position.Y).Should().Be(0f, "undo rebuilds the drawn ground too");

        stroke.Execute();
        terrain.TerrainData.GetHeight(5, 5).Should().BeApproximately(2f, 1e-3f);
    }

    [Fact]
    public void StrokeCommand_WithAStaleAlphamap_RestoresHeightsAndSurvives()
    {
        GameObject ground = new("Ground");
        TerrainComponent terrain = ground.AddComponent<TerrainComponent>();
        terrain.TerrainData = new TerrainData(5, new Vector3(4f, 4f, 4f));

        float[] heightsBefore = terrain.TerrainData.GetHeightmap();
        terrain.TerrainData.SetHeight(2, 2, 3f);

        // A one-float alphamap is the wrong size for this data: undo must not throw over it.
        TerrainStrokeCommand stroke = new(
            terrain, heightsBefore, [1f], [0], terrain.TerrainData.GetHeightmap(), [1f], [0]);

        Action undo = () => stroke.Undo();
        undo.Should().NotThrow();
        terrain.TerrainData.GetHeight(2, 2).Should().Be(0f);
    }

    [Fact]
    public void BrushStrength_IsARate_PerFrameSlice()
    {
        TerrainBrushSettings settings = new() { Strength = 4f, Mode = TerrainBrushMode.Raise };

        settings.StrokeStrength(0.05f).Should().BeApproximately(0.2f, 1e-4f, "4 m/s for a twentieth of a second");
        settings.StrokeStrength(10f).Should().BeApproximately(0.4f, 1e-4f, "a long frame is clamped, not multiplied");

        settings.Mode = TerrainBrushMode.Smooth;
        settings.Strength = 20f;
        settings.StrokeStrength(0.1f).Should().Be(1f, "a pull arrives at its target but never past it");
        settings.Strength = 4f;
        settings.StrokeStrength(0.01f).Should().BeApproximately(0.04f, 1e-4f);
    }
}
