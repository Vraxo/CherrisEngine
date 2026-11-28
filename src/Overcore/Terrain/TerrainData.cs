using System.Numerics;

namespace Overcore.Terrain;

/// <summary>
/// One paintable surface of a terrain, Unity's <c>TerrainLayer</c>: the texture a splat of ground wears and
/// the colour it is tinted by.
/// </summary>
/// <remarks>
/// The texture is named rather than held, the way <see cref="Overcore.Core.Material"/> names its texture, so a
/// layer survives in a scene file as text and the renderer resolves the name when the terrain is built. The
/// tint is what the mesher blends into the terrain's vertex colours, so a layer reads differently from its
/// neighbours even before its texture is bound.
/// </remarks>
public sealed class TerrainLayer
{
    /// <summary>The layer's name, as an editor would list it.</summary>
    public string Name { get; set; } = "Layer";

    /// <summary>The project-relative texture the layer paints with, or empty for a flat tint.</summary>
    public string TextureName { get; set; } = string.Empty;

    /// <summary>The colour the layer's texture (or the bare ground) is multiplied by.</summary>
    public Vector3 Tint { get; set; } = Vector3.One;

    /// <summary>How many metres one tile of the texture covers, which is the layer's texture tiling.</summary>
    public Vector2 TileSize { get; set; } = new(10f, 10f);

    public TerrainLayer()
    {
    }

    public TerrainLayer(string name, string textureName, Vector3 tint)
    {
        Name = name;
        TextureName = textureName;
        Tint = tint;
    }
}

/// <summary>
/// The ground itself as data, Unity's <c>TerrainData</c>: a grid of heights over a rectangle of world, plus
/// the per-pixel blend of <see cref="TerrainLayer"/>s painted over it.
/// </summary>
/// <remarks>
/// <para>
/// Heights are stored in metres, one <see cref="float"/> per grid point, row-major with X fastest:
/// <c>heights[z * HeightmapResolution + x]</c>. Unity stores them normalised to the terrain's height and
/// rescales on read; storing metres keeps <see cref="SampleHeight"/> and the brushes honest arithmetic with
/// no hidden factor, at the cost of the file saying what the world says — which is the trade this engine's
/// YAML makes everywhere else too.
/// </para>
/// <para>
/// The alphamap (Unity's control texture) has its own resolution and one weight per layer per pixel, the
/// weights of a pixel summing to one. It is stored interleaved — pixel first, layer fastest — so a pixel's
/// whole blend is one short run of memory.
/// </para>
/// <para>
/// Every write raises <see cref="Version"/> (heights) or <see cref="AlphamapVersion"/> (paint), which is how
/// the <see cref="Overcore.Components.Terrain"/> component knows its mesh is stale without the data knowing
/// the component exists.
/// </para>
/// </remarks>
public sealed class TerrainData
{
    /// <summary>The smallest heightmap allowed: a single quad still needs its four corners.</summary>
    public const int MinimumResolution = 2;

    /// <summary>The largest heightmap allowed, Unity's own ceiling for one terrain.</summary>
    public const int MaximumResolution = 4097;

    private float[] _heights;
    private float[] _alphamap;
    private bool[] _holes;
    private int _holeCount;

    /// <summary>Grid points along one side of the heightmap. The grid is square, as Unity's is.</summary>
    public int HeightmapResolution { get; }

    /// <summary>Pixels along one side of the alphamap. It may differ from the heightmap's resolution.</summary>
    public int AlphamapResolution { get; }

    /// <summary>The terrain's extent in metres: X and Z are its footprint, Y is the height a full-scale sculpt could reach (heights themselves are stored in metres and may exceed it).</summary>
    public Vector3 Size { get; set; }

    /// <summary>The layers painted over the ground, in blend order. The first layer is the base coat.</summary>
    public List<TerrainLayer> Layers { get; } = [];

    /// <summary>Raised by every height write. Monotonic for the life of the data.</summary>
    public int Version { get; private set; }

    /// <summary>Raised by every alphamap write and by adding a layer.</summary>
    public int AlphamapVersion { get; private set; }

    /// <summary>Raised by every hole write.</summary>
    public int HolesVersion { get; private set; }

    /// <summary>
    /// Cells along one side of the hole grid. A hole belongs to a cell — the quad between four grid
    /// points — rather than to a point or a pixel, because a hole is a piece of ground that is not there:
    /// the mesher drops the cell's quad, the raycast skips it, and the physics mesh, being built from the
    /// mesher's triangles, has the same hole in it. Unity paints holes into a texture and clips fragments;
    /// a cell grid is coarser and exactly as honest, and everything downstream agrees about it for free.
    /// </summary>
    public int HolesResolution => HeightmapResolution - 1;

    /// <summary>How many cells are holes. Zero is the ordinary ground.</summary>
    public int HoleCount => _holeCount;

    /// <summary>The highest stored height, in metres. Kept by scanning, so it is exact rather than tracked.</summary>
    public float MaxHeight
    {
        get
        {
            float max = 0f;
            foreach (float height in _heights)
            {
                max = MathF.Max(max, height);
            }

            return max;
        }
    }

    /// <summary>The lowest stored height, in metres.</summary>
    public float MinHeight
    {
        get
        {
            float min = 0f;
            foreach (float height in _heights)
            {
                min = MathF.Min(min, height);
            }

            return min;
        }
    }

    public TerrainData(int heightmapResolution = 65, Vector3? size = null, int? alphamapResolution = null)
    {
        if (heightmapResolution is < MinimumResolution or > MaximumResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heightmapResolution), heightmapResolution,
                $"A heightmap resolution is between {MinimumResolution} and {MaximumResolution}.");
        }

        int alphaResolution = alphamapResolution ?? heightmapResolution;
        if (alphaResolution is < MinimumResolution or > MaximumResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alphamapResolution), alphaResolution,
                $"An alphamap resolution is between {MinimumResolution} and {MaximumResolution}.");
        }

        HeightmapResolution = heightmapResolution;
        AlphamapResolution = alphaResolution;
        Size = size ?? new Vector3(100f, 20f, 100f);
        _heights = new float[heightmapResolution * heightmapResolution];
        _alphamap = new float[alphaResolution * alphaResolution];
        _holes = new bool[(heightmapResolution - 1) * (heightmapResolution - 1)];

        Layers.Add(new TerrainLayer("Grass", string.Empty, new Vector3(0.35f, 0.55f, 0.25f)));
        ResetAlphamap();
    }

    /// <summary>The height at one grid point, in metres.</summary>
    public float GetHeight(int x, int z)
    {
        ValidateIndices(x, z);
        return _heights[(z * HeightmapResolution) + x];
    }

    /// <summary>Writes the height at one grid point, in metres.</summary>
    public void SetHeight(int x, int z, float height)
    {
        ValidateIndices(x, z);
        _heights[(z * HeightmapResolution) + x] = height;
        Version++;
    }

    /// <summary>A copy of a rectangle of heights, indexed <c>[z, x]</c> within the rectangle, as Unity's <c>GetHeights</c> returns.</summary>
    public float[,] GetHeights(int xBase, int zBase, int width, int depth)
    {
        ValidateRegion(xBase, zBase, width, depth);
        float[,] result = new float[depth, width];
        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                result[z, x] = _heights[((zBase + z) * HeightmapResolution) + xBase + x];
            }
        }

        return result;
    }

    /// <summary>Writes a rectangle of heights, indexed <c>[z, x]</c> within the rectangle, as Unity's <c>SetHeights</c> takes.</summary>
    public void SetHeights(int xBase, int zBase, float[,] heights)
    {
        ArgumentNullException.ThrowIfNull(heights);
        int depth = heights.GetLength(0);
        int width = heights.GetLength(1);
        ValidateRegion(xBase, zBase, width, depth);

        for (int z = 0; z < depth; z++)
        {
            for (int x = 0; x < width; x++)
            {
                _heights[((zBase + z) * HeightmapResolution) + xBase + x] = heights[z, x];
            }
        }

        Version++;
    }

    /// <summary>The whole heightmap as a flat copy, row-major with X fastest. Editing the copy changes nothing until it is written back.</summary>
    public float[] GetHeightmap() => [.. _heights];

    /// <summary>Replaces the whole heightmap from a flat row-major array of <c>resolution²</c> heights.</summary>
    public void SetHeightmap(ReadOnlySpan<float> heights)
    {
        if (heights.Length != _heights.Length)
        {
            throw new ArgumentException(
                $"A heightmap for this terrain holds {_heights.Length} heights, not {heights.Length}.", nameof(heights));
        }

        heights.CopyTo(_heights);
        Version++;
    }

    /// <summary>
    /// The height of the ground at a normalised place, bilinear between the four surrounding grid points —
    /// Unity's <c>SampleHeight</c> without the world transform, which the caller owns.
    /// </summary>
    /// <param name="u">Across the terrain, 0 at its −X edge and 1 at its +X edge. Clamped.</param>
    /// <param name="v">Along the terrain, 0 at its −Z edge and 1 at its +Z edge. Clamped.</param>
    /// <returns>The height in metres.</returns>
    public float SampleHeight(float u, float v)
    {
        float gridX = Math.Clamp(u, 0f, 1f) * (HeightmapResolution - 1);
        float gridZ = Math.Clamp(v, 0f, 1f) * (HeightmapResolution - 1);

        int x0 = (int)MathF.Floor(gridX);
        int z0 = (int)MathF.Floor(gridZ);
        int x1 = Math.Min(x0 + 1, HeightmapResolution - 1);
        int z1 = Math.Min(z0 + 1, HeightmapResolution - 1);
        float fx = gridX - x0;
        float fz = gridZ - z0;

        float top = float.Lerp(_heights[(z0 * HeightmapResolution) + x0], _heights[(z0 * HeightmapResolution) + x1], fx);
        float bottom = float.Lerp(_heights[(z1 * HeightmapResolution) + x0], _heights[(z1 * HeightmapResolution) + x1], fx);
        return float.Lerp(top, bottom, fz);
    }

    /// <summary>The height of the ground at a place in the terrain's own metres, origin at its corner.</summary>
    public float SampleHeightLocal(Vector3 localPosition) =>
        SampleHeight(localPosition.X / Size.X, localPosition.Z / Size.Z);

    /// <summary>
    /// Which way the ground faces at a normalised place, from the heights around it — Unity's
    /// <c>GetInterpolatedNormal</c>. Central differences in the interior, one-sided at the border.
    /// </summary>
    public Vector3 GetNormal(float u, float v)
    {
        float gridX = Math.Clamp(u, 0f, 1f) * (HeightmapResolution - 1);
        float gridZ = Math.Clamp(v, 0f, 1f) * (HeightmapResolution - 1);
        int x = (int)MathF.Round(gridX);
        int z = (int)MathF.Round(gridZ);
        return GetNormalAtGrid(x, z);
    }

    /// <summary>Which way the ground faces at one grid point.</summary>
    public Vector3 GetNormalAtGrid(int x, int z)
    {
        ValidateIndices(x, z);
        float spacingX = Size.X / (HeightmapResolution - 1);
        float spacingZ = Size.Z / (HeightmapResolution - 1);

        int xLow = Math.Max(x - 1, 0);
        int xHigh = Math.Min(x + 1, HeightmapResolution - 1);
        int zLow = Math.Max(z - 1, 0);
        int zHigh = Math.Min(z + 1, HeightmapResolution - 1);

        float slopeX = (_heights[(z * HeightmapResolution) + xHigh] - _heights[(z * HeightmapResolution) + xLow])
            / (spacingX * (xHigh - xLow));
        float slopeZ = (_heights[(zHigh * HeightmapResolution) + x] - _heights[(zLow * HeightmapResolution) + x])
            / (spacingZ * (zHigh - zLow));

        return Vector3.Normalize(new Vector3(-slopeX, 1f, -slopeZ));
    }

    /// <summary>Adds a layer to paint with. Existing paint keeps its weights; the new layer starts at zero everywhere.</summary>
    public TerrainLayer AddLayer(TerrainLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Layers.Add(layer);

        float[] expanded = new float[AlphamapResolution * AlphamapResolution * Layers.Count];
        int oldChannels = Layers.Count - 1;
        int pixels = AlphamapResolution * AlphamapResolution;
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            for (int channel = 0; channel < oldChannels; channel++)
            {
                expanded[(pixel * Layers.Count) + channel] = _alphamap[(pixel * oldChannels) + channel];
            }
        }

        _alphamap = expanded;
        AlphamapVersion++;
        return layer;
    }

    /// <summary>Every layer's weight at a normalised place, bilinear over the alphamap. The weights sum to one.</summary>
    public float[] GetAlphamapWeights(float u, float v)
    {
        float[] weights = new float[Layers.Count];
        float gridX = Math.Clamp(u, 0f, 1f) * (AlphamapResolution - 1);
        float gridZ = Math.Clamp(v, 0f, 1f) * (AlphamapResolution - 1);

        int x0 = (int)MathF.Floor(gridX);
        int z0 = (int)MathF.Floor(gridZ);
        int x1 = Math.Min(x0 + 1, AlphamapResolution - 1);
        int z1 = Math.Min(z0 + 1, AlphamapResolution - 1);
        float fx = gridX - x0;
        float fz = gridZ - z0;

        for (int layer = 0; layer < Layers.Count; layer++)
        {
            float top = float.Lerp(Weight(x0, z0, layer), Weight(x1, z0, layer), fx);
            float bottom = float.Lerp(Weight(x0, z1, layer), Weight(x1, z1, layer), fx);
            weights[layer] = float.Lerp(top, bottom, fz);
        }

        return weights;
    }

    /// <summary>Writes every layer's weight at one alphamap pixel, renormalised so the pixel still sums to one.</summary>
    public void SetAlphamapWeights(int x, int z, ReadOnlySpan<float> weights)
    {
        if (x < 0 || x >= AlphamapResolution || z < 0 || z >= AlphamapResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"An alphamap pixel is inside {AlphamapResolution}².");
        }

        if (weights.Length != Layers.Count)
        {
            throw new ArgumentException($"One weight per layer ({Layers.Count}) is required.", nameof(weights));
        }

        float sum = 0f;
        foreach (float weight in weights)
        {
            sum += MathF.Max(weight, 0f);
        }

        int pixel = ((z * AlphamapResolution) + x) * Layers.Count;
        for (int layer = 0; layer < Layers.Count; layer++)
        {
            _alphamap[pixel + layer] = sum > 0f ? MathF.Max(weights[layer], 0f) / sum : (layer == 0 ? 1f : 0f);
        }

        AlphamapVersion++;
    }

    /// <summary>The index of the layer with the greatest weight at a normalised place: the ground's face there.</summary>
    public int GetDominantLayer(float u, float v)
    {
        float[] weights = GetAlphamapWeights(u, v);
        int best = 0;
        for (int layer = 1; layer < weights.Length; layer++)
        {
            if (weights[layer] > weights[best])
            {
                best = layer;
            }
        }

        return best;
    }

    /// <summary>The tint a place on the ground wears: its layers' tints blended by their weights.</summary>
    public Vector3 GetBlendedTint(float u, float v)
    {
        float[] weights = GetAlphamapWeights(u, v);
        Vector3 tint = Vector3.Zero;
        for (int layer = 0; layer < weights.Length && layer < Layers.Count; layer++)
        {
            tint += Layers[layer].Tint * weights[layer];
        }

        return tint;
    }

    /// <summary>The whole alphamap as a flat copy, pixel-major with the layer fastest.</summary>
    public float[] GetAlphamap() => [.. _alphamap];

    /// <summary>Replaces the whole alphamap. Its length must be <c>alphamapResolution² × layer count</c>.</summary>
    public void SetAlphamap(ReadOnlySpan<float> alphamap)
    {
        if (alphamap.Length != _alphamap.Length)
        {
            throw new ArgumentException(
                $"An alphamap for this terrain holds {_alphamap.Length} weights, not {alphamap.Length}.", nameof(alphamap));
        }

        alphamap.CopyTo(_alphamap);
        AlphamapVersion++;
    }

    /// <summary>Paints the first layer everywhere and clears the rest: a fresh coat of ground.</summary>
    public void ResetAlphamap()
    {
        Array.Clear(_alphamap);
        int pixels = AlphamapResolution * AlphamapResolution;
        for (int pixel = 0; pixel < pixels; pixel++)
        {
            _alphamap[pixel * Layers.Count] = 1f;
        }

        AlphamapVersion++;
    }

    /// <summary>True when the cell whose near corner is a grid point is a hole: no ground there.</summary>
    /// <param name="cellX">The cell's column, 0..<see cref="HolesResolution"/>.</param>
    /// <param name="cellZ">The cell's row, 0..<see cref="HolesResolution"/>.</param>
    public bool IsHole(int cellX, int cellZ)
    {
        ValidateCell(cellX, cellZ);
        return _holes[(cellZ * HolesResolution) + cellX];
    }

    /// <summary>Cuts a hole in one cell, or fills it back in. Writing the state it already has changes nothing.</summary>
    public void SetHole(int cellX, int cellZ, bool hole)
    {
        ValidateCell(cellX, cellZ);
        int index = (cellZ * HolesResolution) + cellX;
        if (_holes[index] == hole)
        {
            return;
        }

        _holes[index] = hole;
        _holeCount += hole ? 1 : -1;
        HolesVersion++;
    }

    /// <summary>True when the cell under a normalised place is a hole.</summary>
    public bool IsHoleAt(float u, float v)
    {
        int cellX = Math.Clamp((int)MathF.Floor(Math.Clamp(u, 0f, 1f) * HolesResolution), 0, HolesResolution - 1);
        int cellZ = Math.Clamp((int)MathF.Floor(Math.Clamp(v, 0f, 1f) * HolesResolution), 0, HolesResolution - 1);
        return _holes[(cellZ * HolesResolution) + cellX];
    }

    /// <summary>The whole hole grid as a flat copy, row-major with X fastest, one entry per cell.</summary>
    public bool[] GetHoles() => [.. _holes];

    /// <summary>Replaces the whole hole grid from a flat row-major array of <c>HolesResolution²</c> entries.</summary>
    public void SetHoles(ReadOnlySpan<bool> holes)
    {
        if (holes.Length != _holes.Length)
        {
            throw new ArgumentException(
                $"A hole grid for this terrain holds {_holes.Length} cells, not {holes.Length}.", nameof(holes));
        }

        holes.CopyTo(_holes);
        _holeCount = 0;
        foreach (bool hole in _holes)
        {
            _holeCount += hole ? 1 : 0;
        }

        HolesVersion++;
    }

    /// <summary>The hole grid packed eight cells to a byte, first cell in the lowest bit: the scene file's coat for it.</summary>
    public byte[] GetHolesPacked()
    {
        byte[] packed = new byte[(_holes.Length + 7) / 8];
        for (int index = 0; index < _holes.Length; index++)
        {
            if (_holes[index])
            {
                packed[index / 8] |= (byte)(1 << (index % 8));
            }
        }

        return packed;
    }

    /// <summary>Replaces the hole grid from its packed form. See <see cref="GetHolesPacked"/>.</summary>
    public void SetHolesPacked(ReadOnlySpan<byte> packed)
    {
        if (packed.Length != (_holes.Length + 7) / 8)
        {
            throw new ArgumentException(
                $"A packed hole grid for this terrain is {(_holes.Length + 7) / 8} bytes, not {packed.Length}.", nameof(packed));
        }

        _holeCount = 0;
        for (int index = 0; index < _holes.Length; index++)
        {
            _holes[index] = (packed[index / 8] & (1 << (index % 8))) != 0;
            _holeCount += _holes[index] ? 1 : 0;
        }

        HolesVersion++;
    }

    private void ValidateCell(int cellX, int cellZ)
    {
        if (cellX < 0 || cellX >= HolesResolution || cellZ < 0 || cellZ >= HolesResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellX), $"A cell is inside the {HolesResolution}² hole grid.");
        }
    }

    private float Weight(int x, int z, int layer) =>
        _alphamap[(((z * AlphamapResolution) + x) * Layers.Count) + layer];

    private void ValidateIndices(int x, int z)
    {
        if (x < 0 || x >= HeightmapResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(x), x, $"A heightmap column is inside {HeightmapResolution}.");
        }

        if (z < 0 || z >= HeightmapResolution)
        {
            throw new ArgumentOutOfRangeException(nameof(z), z, $"A heightmap row is inside {HeightmapResolution}.");
        }
    }

    private void ValidateRegion(int xBase, int zBase, int width, int depth)
    {
        if (xBase < 0 || zBase < 0 || width < 0 || depth < 0
            || xBase + width > HeightmapResolution || zBase + depth > HeightmapResolution)
        {
            throw new ArgumentOutOfRangeException(
                nameof(xBase), $"The region ({xBase}, {zBase}) + {width}×{depth} leaves the {HeightmapResolution}² heightmap.");
        }
    }
}
