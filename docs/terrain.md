# Terrain (`Terrain`, `TerrainData`, `TerrainCollider`)

Overcore's sculpted ground, modelled on Unity's Terrain system and built from parts the engine already
had: a heightfield becomes a `Mesh`, the mesh draws through the ordinary `MeshRenderer` path (shadows,
NavMesh baking and all), and the same heights answer physics questions analytically.

## The pieces

| Type | Where | What it is |
| :--- | :--- | :--- |
| `TerrainData` | `src/Overcore/Terrain/TerrainData.cs` | The ground as data: a square grid of heights in metres, an alphamap, and the `TerrainLayer`s painted over it. No scene, no GPU — it can be built and tested bare. |
| `TerrainBrush` | `src/Overcore/Terrain/TerrainBrush.cs` | One sculpting or painting stroke as arithmetic: Raise, Lower, Flatten, Smooth, Paint, all with a cosine falloff that reaches zero at the rim. |
| `TerrainMesher` | `src/Overcore/Terrain/TerrainMesher.cs` | Heightfield → `Mesh`, whole or in chunks. Vertex colours carry the blended layer tints, which the scene shader multiplies its texture by. |
| `Terrain` | `src/Overcore/Components/Terrain.cs` | The component (Add Component → **Terrain**). Owns a `MeshRenderer` on the same object and rebuilds it when the data changes — at once from `Sculpt`/`Paint`, and once a frame from the scene for edits made straight against the data. |
| `TerrainCollider` | `src/Overcore/Physics/TerrainCollider.cs` | The solid half (Add Component → **Terrain Collider**), placed beside a `Terrain`. |

## Heights and sampling

Heights are stored in **metres**, one `float` per grid point, row-major with X fastest
(`z * resolution + x`). Unity stores them normalised to the terrain height; metres keep `SampleHeight`,
the brushes and the physics honest arithmetic with no hidden factor.

- `GetHeight` / `SetHeight` — one grid point; `GetHeights` / `SetHeights` — a rectangle, `[z, x]` indexed.
- `SampleHeight(u, v)` — bilinear height at a normalised place (0..1 across the footprint), clamped.
- `GetNormal(u, v)` — central differences over the heights around the point, one-sided at the border.
- `Size` is the footprint in metres (X/Z) and a nominal height range (Y); heights are metres regardless.

Square resolutions from 2 to 4097 are accepted; 65, 129, 257… (2ⁿ+1) behave best with chunking.

## Painting

Each pixel of the alphamap holds one weight per layer, summing to one. `Layers[0]` is the base coat a
fresh terrain is fully painted with. A layer is a name, a texture name (project-relative, resolved when
the terrain loads), a tint, and a tile size. The mesher blends the tints into vertex colours, so painted
regions read correctly even before textures are bound; the material the ground draws with is resolved
from the first layer's texture, falling back to white.

`Terrain.Paint(center, radius, strength, layerIndex)` and `TerrainBrush.ApplyPaint` raise one layer's
weight toward one at the stroke's centre and scale the others down, keeping every pixel summing to one.
`GetDominantLayer(u, v)` answers which layer a place wears.

## Sculpting from a script

```csharp
public class RaiseHill : Script
{
    public override void Start()
    {
        Terrain terrain = GameObject.GetComponent<Terrain>();
        terrain.Sculpt(new Vector2(0.5f, 0.5f), 0.25f, 3f, TerrainBrushMode.Raise);
        terrain.Paint(new Vector2(0.5f, 0.5f), 0.25f, 1f, layerIndex: 1);
    }
}
```

Centres and radii are normalised, so a brush does not change size when the terrain does. Editing
`terrain.TerrainData` directly also works: the data's version counters tell the scene to rebuild the
mesh (and the collider's physics mesh) on the next frame.

## Collision and queries

`TerrainCollider` serves two masters, from the same heights:

- **Dynamic bodies** meet a Bepu triangle mesh built from the heightfield (static; a terrain is ground,
  not a vehicle — a dynamic `RigidBody` on the same object is not supported).
- **Queries** — `Physics.Raycast`, sphere/capsule casts, overlaps, `Collider.ContainsPoint`, and with
  them the `CharacterController`'s ground probes — test the heightfield analytically: a grid DDA plus
  exact triangle tests for rays, surface samples for capsule separation.

The terrain's origin is its corner, like Unity's: the object's transform position is the (−X, −Z)
corner, and the ground extends `Size.X` × `Size.Z` from it. Non-uniform scale is honoured by the
queries; normals under non-uniform scale are approximate, as they are everywhere in this engine.

## In the scene file

A terrain serialises as scalar settings plus bulk data in base64 float bytes — a ground is a binary
thing wearing a YAML coat, and spelling 16,000 heights as decimal text would make the file neither
readable nor honest:

```yaml
Terrain:
  Size: [30, 12, 30]
  HeightmapResolution: 65
  AlphamapResolution: 65
  Layers:
  - Name: Grass
    TextureName: ''
    Tint: [0.35, 0.55, 0.25]
    TileSize: [10, 10]
  Heights: <base64 floats>
  Alphamap: <base64 floats>
```

The owned `MeshRenderer` is not written; it is rebuilt from the data on load.

## Not yet

Terrain holes, trees and grass detail, neighbour stitching between separate terrains, heightmap LOD
beyond chunking, and per-layer texture blending in the shader (layers currently blend through vertex
colour tints over one material texture) are future increments. The data model does not need to change
for any of them.
