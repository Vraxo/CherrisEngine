# Serialized Object & Asset References

Public read/write properties on `Component` and `Script` subclasses can hold direct references to live scene objects, components, and `.prefab` assets without runtime `Scene.Find(...)` boilerplate:

- `GameObject?` — any `GameObject` in the scene or prefab hierarchy
- `Transform?` — any `Transform` in the scene or prefab hierarchy
- Any `Component?` or `Script?` subclass (e.g. `Light?`, `NavMeshAgent?`, `TurretBrain?`) — filtered to objects carrying that exact component/script type
- `PrefabAsset?` — a serializable `.prefab` asset handle ready for `GameObject.Instantiate(prefab, position, rotation)`

## Script Usage

```csharp
using System.Numerics;
using Overcore.Components;
using Overcore.Core;
using Overcore.Serialization;

public class TurretController : Script
{
    public Transform? Target { get; set; }
    public Transform? Muzzle { get; set; }
    public Light? MuzzleFlash { get; set; }
    public PrefabAsset? ProjectilePrefab { get; set; }

    public void Fire()
    {
        if (ProjectilePrefab is null || Muzzle is null)
        {
            return;
        }

        GameObject? rocket = GameObject.Instantiate(
            ProjectilePrefab,
            Muzzle.WorldPosition,
            Muzzle.WorldRotation);

        if (MuzzleFlash is not null)
        {
            MuzzleFlash.Intensity = 6.0f;
        }
    }
}
```

## Serialization & Resolution

- **Scene `.yaml` files**: Scene object and component references serialize as structured YAML blocks (`Object: <Guid>`, optional `Component: <TypeName>`) and resolve in a third pass of `SceneLoader` after every `GameObject`, hierarchy link, and `Component` has been constructed. Forward references and mutual cycles (`A.Partner = B`, `B.Partner = A`) round-trip losslessly.
- **Prefab `.prefab` files**: References pointing inside the saved prefab hierarchy serialize using stable local file IDs (`Object: <FileId>`) rather than authoring GUIDs. Each loaded prefab instance resolves internal links against its own fresh hierarchy, and prefab overrides compare cleanly via `prefablink:<FileId>`.
- **`PrefabAsset` handles**: Serialized as `{ Asset: <AssetGuid>, Path: <AssetPath> }`, resolving via `AssetMeta` sidecars first with path fallback.
- **Runtime cloning (`GameObject.Instantiate`)**: Cloning a hierarchy automatically remaps internal `GameObject`, `Transform`, and `Component` references onto the newly cloned counterparts while preserving external scene references.

## Inspector Editing

- **ImGui Editor (`OvercoreEditor`)**: Renders a filtered object-picker dropdown (`None (Type)` + matching scene objects or `.prefab` assets) and accepts drag-and-drop from both the Hierarchy (`GAMEOBJECT_ID`) and Content Browser (`ASSET_PATH_PREFAB`), with full undo/redo and Reset-to-`null` support.
- **Photino/Blazor Editor (`OvercoreEditorPhotino` / `OvercoreEditorSession`)**: Exposes `PropertyKind.ObjectReference` (plus `Vector4` and `LayerMask`) with live filtered `Choices` and human-readable `ChoiceLabels`, preserving references across Play/Stop snapshots and prefab Revert/Apply.
