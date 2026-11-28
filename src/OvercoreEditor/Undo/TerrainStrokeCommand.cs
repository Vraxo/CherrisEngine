using Overcore.Terrain;
using TerrainComponent = Overcore.Components.Terrain;

namespace OvercoreEditor.Undo.Commands;

/// <summary>
/// One brush stroke against a terrain, as history: the ground before the stroke and after it, heights and
/// paint together, so undoing a stroke is the stroke never having happened.
/// </summary>
/// <remarks>
/// The snapshots are the data's own flat arrays (<see cref="TerrainData.GetHeightmap"/> and
/// <see cref="TerrainData.GetAlphamap"/>). A stroke can only be undone while the terrain still has the
/// resolution and layer count it had when the stroke fell — adding a layer mid-history makes the alphamap
/// snapshot the wrong size, in which case undo restores what it can (the heights) rather than throwing:
/// a stale paint snapshot is a smaller loss than a crashed undo.
/// </remarks>
public sealed class TerrainStrokeCommand : ICommand
{
    private readonly TerrainComponent _terrain;
    private readonly float[] _heightsBefore;
    private readonly float[] _alphamapBefore;
    private readonly float[] _heightsAfter;
    private readonly float[] _alphamapAfter;

    public TerrainStrokeCommand(
        TerrainComponent terrain,
        float[] heightsBefore,
        float[] alphamapBefore,
        float[] heightsAfter,
        float[] alphamapAfter)
    {
        _terrain = terrain;
        _heightsBefore = heightsBefore;
        _alphamapBefore = alphamapBefore;
        _heightsAfter = heightsAfter;
        _alphamapAfter = alphamapAfter;
    }

    public void Execute() => Apply(_heightsAfter, _alphamapAfter);

    public void Undo() => Apply(_heightsBefore, _alphamapBefore);

    private void Apply(float[] heights, float[] alphamap)
    {
        TerrainData data = _terrain.TerrainData;

        if (heights.Length == data.HeightmapResolution * data.HeightmapResolution)
        {
            data.SetHeightmap(heights);
        }

        if (alphamap.Length == data.AlphamapResolution * data.AlphamapResolution * data.Layers.Count)
        {
            data.SetAlphamap(alphamap);
        }

        _terrain.Rebuild();
    }
}
