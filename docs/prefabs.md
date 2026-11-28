# Prefabs: asset identity and live instances

Overcore's prefab layer is Unity's data model, reimplemented: a prefab is an **asset** on disk, an object in
a scene can be an **instance** of one, and every object of an instance keeps a **link** to where it lives in
which asset. This document is the format and the semantics — the ground truth the code and tests follow.

## Asset identity: the `.meta` sidecar

Every asset can carry a sidecar named after it, the way Unity writes one per asset:

```
Cube.prefab
Cube.prefab.meta      ←  guid: 3f2b1c4e5d6a7b8c9d0e1f2a3b4c5d6e
```

The sidecar holds one line: `guid:` and 32 lowercase hex characters. The guid is created the first time it
is asked for and never changes afterwards; links quote it, so renaming or moving the asset with its sidecar
breaks nothing. An asset that is not a file on disk (one served from an `Assets.pak` bundle) derives a
stable guid from its path instead — editor-authored prefabs are always disk files and always get sidecars.
See `Overcore/Utils/AssetMeta.cs`.

## Inside a prefab asset

A `.prefab` file is the scene YAML's object list, restricted to one subtree, with one extra key per object:

```yaml
GameObjects:
- Id: 7c9e…          # the id the object had when saved; instance objects never reuse it
  Name: Rope
  File: 1            # the object's id WITHIN the asset — what instance links point at
  Components: …
- Id: 4a1b…
  Name: Knot
  File: 2
  Parent: 7c9e…
  Components: …
```

`File` ids are drawn from the file's write order (root first, depth-first) and are stable: object `File: 2`
today is the same object tomorrow. `SceneSerializer.SavePrefab` writes them and returns the written order;
`SceneLoader.LoadPrefab` reads them back.

## In a scene: the link

An object that is part of a prefab instance says so in the scene file:

```yaml
- Id: 08f2…          # the scene's own object id — always fresh per instance
  Name: Rope
  Prefab:
    Asset: 3f2b1c4e5d6a7b8c9d0e1f2a3b4c5d6e   # the prefab asset's guid
    File: 1                                  # the object's id within that asset
  Components: …
```

In memory this is `GameObject.PrefabSource` — a `(AssetGuid, FileId)` pair on **every** object of the
instance, the same shape as Unity's corresponding-source pair. Two instances of one asset share the pair
values but never object ids: `LoadPrefab` always builds fresh objects.

## What the editor does with it

- **Create Prefab** (hierarchy row menu): writes `<Name>.prefab` (then `<Name> 1.prefab`, …) into the
  project folder and turns the saved objects into the asset's first instance. One undoable step; Undo takes
  the links back but leaves the asset on disk — it is a project file from the moment it is written.
- **Instantiate in Scene** (content browser: double-click a prefab tile, or right-click →
  *Instantiate in Scene*): adds a fresh instance at the scene root. One undoable step; Undo takes the whole
  subtree back out, Redo puts the same objects back.
- The hierarchy marks a prefab's rows with a small stack-of-cubes glyph, in the theme's primary role.
- **Overrides** (inspector, on any linked object): what has drifted from the asset, with Revert / Apply
  per value and Revert All / Apply All for the subtree. See the next section.

At run time the same thing is `GameObject.Instantiate(object)`: a fresh clone with its own ids that keeps the
link to the asset. It is described in [Physics & Runtime Lifecycle](physics.md#7-instantiating-destroying-and-changing-scenes).

## Prefab overrides

An override is a divergence between an instance and its source — named as a (property path, source value)
pair, exactly what Unity's `m_Modifications` records, except ours are **computed, not recorded**. Nothing
is stored at edit time: the render thread keeps a `PrefabSourceIndex` of what the assets hold (a `.meta`
guid resolves to the asset, values read through the session's loader, cached until the file changes) and
each scene snapshot carries every object's drift, found by comparing values side by side.

- **Paths** are named `PrefabPaths`: `"Name"`, `"Transform.Position|Rotation|Scale"` (rotation in Euler
  degrees), `"Enabled:<ComponentType>"`, and `"<ComponentType>.<Property>"` for one property of one
  component — one component per type per object, the same convention the inspector and SetProperty use.
- **Values travel as display text** (`PrefabValues`), formatted the same way on both sides — the
  instance's inspector text versus the source's inspector text — so an override is exactly "this row in
  the inspector reads differently than the prefab's". Comparing text means floating-point formatting can
  hide a drift smaller than the display precision (0.###), the same limit the inspector itself has.
- **Revert** writes the source's value back into the instance. One value, one undoable step; the undo
  puts the instance's own value back. **Revert All** walks the subtree and takes one step in total.
- **Apply** writes the instance's value into the asset — load, change, save — and the step remembers the
  asset's bytes so Undo restores the file exactly. **Apply All** rewrites each touched asset once. An
  apply moves the asset to this instance: every *other* instance now differs from the asset, and shows
  its drift honestly the next time it is inspected.
- **The marked rows**: an overridden row's label goes bold in place (Unity's mark), and an Overrides
  dropdown on the object header lists each drift with its source value and the two directions.

## Honest gaps — what this is not yet

Unity's prefab model is more than a link, and the rest is deliberately not here yet:

- **Live propagation.** Applying an override rewrites the asset, but other loaded instances are not
  reloaded — they keep their own values (and now legitimately differ). Unity re-imports instances at
  once; we reload nothing while a scene is open.
- **Structural overrides.** An instance that gained a component or child that the source has no path for
  cannot be reverted or applied — the diff only covers values with a source counterpart. Unity records
  added components (`m_AddedComponents`); we simply do not track them yet.
- **Variants and nesting.** Prefabs of prefabs, and prefabs that are overrides of other prefabs.
- **Prefab mode.** Opening the asset for editing in isolation (Unity's double-click behaviour). Our
  double-click instantiates instead, and says so in the tile's tooltip.
- **Creating a prefab from an already-linked object** re-links the whole subtree to the new asset (Undo puts
  the old links back). Unity instead nests or complains; we will decide with the overrides work.

## Where the code lives

| Piece | File |
| --- | --- |
| The link record | `src/Overcore/Scenes/PrefabSource.cs` |
| `.meta` guids (and guid → asset) | `src/Overcore/Utils/AssetMeta.cs` |
| Writing `File` ids and the `Prefab` block; stamping the links | `src/Overcore/Scenes/SceneSerializer.cs` |
| Reading them, fresh instance ids | `src/Overcore/Scenes/SceneLoader.cs` |
| Naming an override's path | `src/OvercoreEditorSession/Scene/PrefabPaths.cs` |
| Values as display text | `src/OvercoreEditorSession/Scene/PrefabValues.cs` |
| What the assets hold, and the diff | `src/OvercoreEditorSession/Scene/PrefabSourceIndex.cs` |
| The drift a snapshot carries | `src/OvercoreEditorSession/Scene/SceneSnapshot.cs` |
| The four commands (undoable) | `src/OvercoreEditorSession/Host/PrefabOverrideCommands.cs` |
| Create / Instantiate commands | `src/OvercoreEditorSession/Host/Commands/PrefabCommands.cs` |
| Editor wiring | `HierarchyPanel.razor` (Create Prefab), `ContentBrowserPanel.razor` (Instantiate), `InspectorPanel.razor` (Overrides) |
