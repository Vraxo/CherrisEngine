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

## Honest gaps — what this is not yet

Unity's prefab model is more than a link, and the rest is deliberately not here yet:

- **Overrides.** Unity records every divergence between an instance and its source
  (`m_Modifications`: property-path, value) and offers Apply/Revert. Overcore does not record divergences
  yet; editing an instance edits only that instance, and the link is metadata rather than a live
  relationship. This is the next slice.
- **Variants and nesting.** Prefabs of prefabs, and prefabs that are overrides of other prefabs.
- **Prefab mode.** Opening the asset for editing in isolation (Unity's double-click behaviour). Our
  double-click instantiates instead, and says so in the tile's tooltip.
- **Creating a prefab from an already-linked object** re-links the whole subtree to the new asset (Undo puts
  the old links back). Unity instead nests or complains; we will decide with the overrides work.

## Where the code lives

| Piece | File |
| --- | --- |
| The link record | `src/Overcore/Scenes/PrefabSource.cs` |
| `.meta` guids | `src/Overcore/Utils/AssetMeta.cs` |
| Writing `File` ids and the `Prefab` block | `src/Overcore/Scenes/SceneSerializer.cs` |
| Reading them, fresh instance ids | `src/Overcore/Scenes/SceneLoader.cs` |
| The two commands (undoable) | `src/OvercoreEditorSession/Host/EditorCommands.cs` |
| The link flag in snapshots | `src/OvercoreEditorSession/Scene/SceneSnapshot.cs` |
| Editor wiring | `HierarchyPanel.razor` (Create Prefab), `ContentBrowserPanel.razor` (Instantiate) |
