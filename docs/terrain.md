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
the terrain loads), a tint, and a tile size.

The ground is drawn by a terrain branch of the lit shader — `scene.frag` (and its identical copy
`skin.frag`) on the OpenGL path, and the same blend in the Veldrid fragment shader's set-3 terrain
bundle, so the ground paints alike on both backends. The alphamap is uploaded as two RGBA textures — layers 0–3 in the first,
4–7 in the second — whenever the paint changes, each layer's texture is sampled at its own repeat
(`Size / TileSize` across the whole ground), tinted, and blended by the alphamap weights:

```
albedo = Σ texture(layerᵢ, uv · repeatᵢ) · tintᵢ · weightᵢ
```

Separate samplers rather than an array, because GLSL indexes a sampler array only by a constant
expression. The mesher also blends the tints into vertex colours, which is the same blend without
texture detail; that is what layers past the eighth and unresolved textures draw with, so no ground
is ever black for want of a texture. (The Veldrid branch is unlit like the rest of that pipeline; it
blends the same layers and leaves the lighting to the OpenGL path.)

`Terrain.Paint(center, radius, strength, layerIndex)` and `TerrainBrush.ApplyPaint` raise one layer's
weight toward one at the stroke's centre and scale the others down, keeping every pixel summing to one.
`GetDominantLayer(u, v)` answers which layer a place wears.

## Trees

A terrain grows trees from **prototypes** (`TerrainTreePrototype`: a mesh name and a tint — add them
from the inspector's Tree Prototypes list, or `data.AddTreePrototype(...)`) and **instances**
(`data.AddTree(prototype, place, facing, girth, height)`; `TerrainBrush.PlantTrees` scatters them with
room to grow, `EraseTrees` uproots a disc). The editor's **Trees** brush plants on drag and uproots on
Shift-drag, as one undo step per stroke.

An instance stores no height: the forest is meshed standing on the ground as it is when the mesh is
built, so sculpting a hill lifts its trees, and a tree whose cell becomes a hole stops drawing. The
whole forest is one merged mesh on a child object the terrain owns (`Trees`), drawn and shadowed
through the ordinary mesh path; it is terrain property, not scene content, so the scene file writes
prototypes as text and the planted trees as one packed `Trees: <base64>` blob inside the terrain's own
block, and the child is rebuilt from that on load. Consequences worth knowing:

- **NavMesh bakes walk under trees, not over them** — the forest child is excluded from bakes; agents
  still walk around a wood only where the ground itself leads them.
- **Trunks are solid; canopies are not.** Each prototype names a trunk radius and height (a quarter
  metre by two metres, say; zero radius grows a ghost). The terrain's physics mesh grows an
  eight-sided prism per trunk, and the analytic queries — rays, sphere overlaps, capsule separation —
  test the same trunk as a cylinder, so a dropped ball rests on a trunk top and a character controller
  shoulders off bark, while walking under the canopy meets nothing. A sculpt that moves the ground
  moves the trunks with it, since both meshes rebuild together.
- A prototype's mesh resolves by name through the resource manager when the scene loads
  (`Terrain.TreeMeshResolver` is the wire); an unresolved mesh grows invisible trees — plant a
  different prototype or fix the name in the inspector.
- Merging is one draw for the whole forest at any size, paid for in memory per tree; GPU instancing is
  the known upgrade path and needs no data change when it lands.

## Detail (grass)

Detail prototypes (`TerrainDetailPrototype`: a tint, an optional cutout texture name, a tuft size) grow
where their **density** is painted — one 0..1 channel per prototype over the alphamap's grid
(`TerrainBrush.ApplyDetail`, signed: a negative stroke thins). The editor's **Detail** brush grows on
drag and thins on Shift-drag.

The detail mesh is crossed-quad tufts — two quads through each other, dark at the root and full at the
tip — scattered per pixel by dice seeded from the pixel itself, so the field is identical every rebuild
and a sculpt slides the ground under a fixed field rather than reshuffling it. Up to four tufts grow
from a full pixel (`TerrainDetailMesher.MaxTuftsPerPixel`). Like the forest it rides on a terrain-owned
child (`Detail`), is left out of scene files (density travels as a `Detail: <base64>` blob) and out of
NavMesh bakes, and it is not solid: grass is walked through. One material serves the whole field, so
only the first prototype's texture is sampled (with a 0.5 alpha cutout); the rest wear their tints.
The field moves: both backends' vertex shaders bend each tuft by the square of its height up the
card, phased by where it stands, under `Terrain.DetailWindStrength` (metres of tip travel, 0.12 by
default; 0 for a still day) — only the detail child is ever bent, never the ground or the trees.
Detail has no distance fade: a very large terrain should keep its alphamap resolution (and so its
detail pixels) modest, or grow detail only where players walk.

## Chunks and level of detail

A terrain draws as one mesh by default. Set `ChunksPerSide` (1–8, on the component, saved with the
scene) and the ground splits into that many chunks per side, each on its own terrain-owned child —
which frustum-culls and shadow-draws like any object, and is rebuilt from the data on load like the
other owned children. Each chunk carries a **level of detail**: full detail within `LodDistance`
metres of the camera (80 by default), one step coarser per doubling of distance after that, a step
meaning half the vertices per side and a quarter of the triangles. The scene re-aims levels every
frame from its camera (`Terrain.UpdateLod`), remeshing only chunks whose level changed. Chunk borders
share heightmap points, so neighbours meet crack-free at the same level; adjacent chunks at different
levels can show hairline seams on close inspection — the classic LOD trade, accepted at distance.

Chunks are a drawing decision only: the physics mesh, the analytic queries and NavMesh bakes (which
read the data's own full-detail mesh for a chunked terrain) never simplify. A hole at coarse level
drops any quad covering it, so distant holes read slightly larger, never as ground that isn't there.

## Neighbours

Big ground is tiled from several terrains placed corner to corner. Two abutting grounds crack along
their seam whenever their edge heights differ, so an edge can be **stitched**: both sides take the
point-for-point average of the two edge profiles (`data.StitchEdge(neighbour, TerrainEdge.Right)`, or
`terrain.StitchTo(neighbour, edge)`, or the inspector's Stitch Neighbour Edge buttons, which find the
terrain standing across each edge geometrically — nothing about the pairing is stored, so there is
nothing to serialize and nothing to go stale when a terrain moves).

Equal resolutions meet exactly; unequal ones meet as closely as their grids allow, which is why tiled
ground wants matching resolutions. A stitch is a bake, not a weld: sculpt either edge afterwards and
the seam reopens until it is stitched again. Neighbours are found by their corners, so tiled terrains
are assumed unturned, the ordinary way ground is laid.

## Holes

A hole is a whole **cell** — the quad between four grid points — with no ground in it (`IsHole`,
`SetHole`, `IsHoleAt`, `HolesResolution = HeightmapResolution − 1`). Unity paints holes into a texture
and clips fragments; a cell grid is coarser and exactly as honest, and because a hole is an absence in
the geometry itself, everything downstream agrees about it for free:

- the mesher drops the cell's quad, so there is nothing to draw;
- the raycast's cell walk skips it, so rays fall through and may meet ground behind or below;
- the collider's Bepu mesh is built from the mesher's triangles, so dynamic bodies fall through too,
  and the capsule separation treats a hole as no surface, so a character controller drops in;
- a NavMesh bake, which collects `MeshRenderer` triangles, walks around it.

Cut them with `Terrain.ApplyHoles(center, radius, hole: true)` or the editor brush's **Holes** mode
(hold Shift to fill them back in). In the scene file the hole grid travels bit-packed (eight cells to a
byte) as `Holes: <base64>`, written only when at least one hole exists. Sphere/box overlap queries
approximate around holes, as they do around ridges; rays, casts and the solver are exact.

## Sculpting in the editor

Select the terrain, open its inspector, and tick **Sculpt in Viewport**. The viewport mouse now belongs
to the brush (object picking and the transform gizmo stand down while it is armed): a ring shows the
brush's size on the ground, and dragging sculpts — Raise and Lower are rates, so a held brush keeps
working, while Flatten, Smooth and Paint pull toward their target at the brush strength. Pick the mode,
size (metres) and strength beside the arming checkbox; Flatten adds its target height, Paint adds the
layer to paint with. The layer list below edits tints and adds layers, armed or not.

Each stroke — mouse-down to mouse-up — is **one undo step**: `TerrainStrokeCommand` holds the heights
and alphamap from before and after the stroke and restores them wholesale, rebuilding the mesh, so
Ctrl+Z is the stroke never having happened. A stroke that changed nothing leaves no history.

Picking is `Terrain.TryHit`, the world-space form of `TerrainRaycast.TryHitLocal` — a 2D grid DDA with
exact triangle tests in the terrain's own metres. The physics raycast in `ColliderGeometry` uses the
same local walk, so the ground an editor sculpts is the ground play mode collides with; edit mode needs
it because there is no physics world to ask there.

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

Detail distance fade, continuous (Unity-style) neighbour stitching that re-sews itself as you
sculpt, chunk skirts to hide mixed-level seams entirely, and layers past the eighth in the shaders'
blend are future increments. The data model does not need to change for any of them.
