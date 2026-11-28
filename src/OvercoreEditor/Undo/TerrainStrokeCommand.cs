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
    private readonly byte[] _holesBefore;
    private readonly byte[] _treesBefore;
    private readonly float[] _heightsAfter;
    private readonly float[] _alphamapAfter;
    private readonly byte[] _holesAfter;
    private readonly byte[] _treesAfter;

    public TerrainStrokeCommand(
        TerrainComponent terrain,
        float[] heightsBefore,
        float[] alphamapBefore,
        byte[] holesBefore,
        byte[] treesBefore,
        float[] heightsAfter,
        float[] alphamapAfter,
        byte[] holesAfter,
        byte[] treesAfter)
    {
        _terrain = terrain;
        _heightsBefore = heightsBefore;
        _alphamapBefore = alphamapBefore;
        _holesBefore = holesBefore;
        _treesBefore = treesBefore;
        _heightsAfter = heightsAfter;
        _alphamapAfter = alphamapAfter;
        _holesAfter = holesAfter;
        _treesAfter = treesAfter;
    }

    public void Execute() => Apply(_heightsAfter, _alphamapAfter, _holesAfter, _treesAfter);

    public void Undo() => Apply(_heightsBefore, _alphamapBefore, _holesBefore, _treesBefore);

    private void Apply(float[] heights, float[] alphamap, byte[] holes, byte[] trees)
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

        if (holes.Length == (data.HolesResolution * data.HolesResolution + 7) / 8)
        {
            data.SetHolesPacked(holes);
        }

        // A forest snapshot is trusted only while every tree in it still names a prototype the terrain
        // has: prototypes are never removed, so this is a formality, but a stale one is skipped rather
        // than half-applied.
        try
        {
            data.SetTreesPacked(trees);
        }
        catch (ArgumentException)
        {
            // The packed form no longer fits this data (see the class remarks on stale snapshots).
        }

        _terrain.Rebuild();
    }
}
