# Animation & Skinning

> **Status: increment 1 of 4 is implemented.** The importer, the skeleton data model, clip data and the 32-bit index
> path exist; sampling, the `Animator` and GPU skinning do not. §8 says exactly which is which, and the rest of this
> document is the contract the remaining increments follow — the semantics, the file formats and the API shape — so
> that the code and its tests have one ground truth to agree on, the way [`prefabs.md`](prefabs.md) and
> [`physics.md`](physics.md) do for the layers that already exist.

Overcore draws static geometry and nothing else: `ModelLoader` reads `POSITION`, `NORMAL` and `TEXCOORD_0` out of a
glTF primitive and throws the node graph away, `Vertex` has no room for bone weights, and a scene frame has no step
between `Update` and `LateUpdate` where a pose could be computed. A character cannot walk, a door cannot open and a
camera cannot fly a path.

This is the layer that fixes that. It is Unity's animation stack, reimplemented: a **clip** is keyframed curves, an
**Animator** picks clips through a state machine, a **SkinnedMeshRenderer** deforms a mesh by a **skeleton** of
transforms, and animation **events** call back into scripts.

Everything below except the two GPU passes is plain arithmetic over arrays, which is what makes it testable without a
window, a context or a GPU — the same property that makes `tests/Overcore.Tests/World` cheap to run.

## 1. Import: a model is a hierarchy, not a bag of meshes

The first thing that has to change is what a `.gltf`/`.glb` becomes. Today `ResourceManager.GetMesh(name)` returns one
`Mesh` per logical mesh, so a file is a dictionary of shapes with no relationship to each other.

A model imports as **objects**: one `GameObject` per glTF node, named after the node, parented to match the node graph,
carrying the node's local translation/rotation/scale on its `Transform`. A node with a mesh gets a `MeshRenderer` (or a
`SkinnedMeshRenderer`, when a skin names it) whose `Mesh` carries the primitives of that mesh. This is what a user
sees in Unity's hierarchy after dragging in a `.fbx`, and it is what makes the rest of this document possible — a
skeleton is nothing but a subtree of transforms.

| glTF | Overcore |
| :--- | :--- |
| node graph | `GameObject` hierarchy, one per node, local TRS on the `Transform` |
| `Mesh` + `Primitive` | one `Mesh` per glTF mesh: primitives appended into one vertex buffer, indices widened to `uint`, winding reversed to the engine's clockwise front faces |
| `NORMAL`, `TEXCOORD_0`, `COLOR_0` | already read; `COLOR_0` is read now instead of a fixed white |
| `JOINTS_0`, `WEIGHTS_0` | `Mesh.Bones` / `Mesh.Weights`, four per vertex (§3) |
| `Skin` + `inverseBindMatrices` | `Skeleton` — the bone transforms and their bind-pose inverses (§3) |
| `Animation` + channels | `AnimationClip` curves (§2), written to an `.anim` asset on import |
| node with `mesh` + no skin | `MeshRenderer` |
| node with `mesh` + a skin that reaches it | `SkinnedMeshRenderer` |

Two limits move with this change and are worth stating up front: indices become **`uint`** (a skinned character
routinely passes 65,535 vertices, and `ushort` silently wraps), and the vertex struct is no longer 48 bytes (§3.4).

Import writes what it found as assets, not as scene state — see §6 for the files and their guids.

## 2. `AnimationClip`: keyframed curves

A clip is data, and only data: a name, a length, a wrap mode, a list of curves and a list of events. Sampling it is a
pure function of time, so it can be tested exactly.

```csharp
public sealed class AnimationClip
{
    public string Name { get; set; }
    public float Length { get; set; }          // seconds, derived from the longest curve
    public float FrameRate { get; set; }       // 30 by default, for the editor's key rows
    public WrapMode WrapMode { get; set; }     // enum in §2.3
    public IList<AnimationCurve> Curves { get; }
    public IList<AnimationEvent> Events { get; }

    public void Sample(float time, AnimationPose pose, float weight = 1f);
}
```

A curve addresses one property of one transform, the same three-part path Unity stores per binding:

| Part | Value |
| :--- | :--- |
| Bone path | Hierarchy path from the clip's root, `"Hips/Spine/Chest"`, `""` for the root itself |
| Property | `localPosition`, `localRotation`, `localScale` |
| Axis | `x`/`y`/`z` for position and scale; `x`/`y`/`z`/`w` for rotation; none for a whole-vector curve |

Keys are `(time, value, inTangent, outTangent)`. Interpolation is the clip's, not the curve's — Unity's
`AnimationClip` is uniform, and the importer takes the mode from the glTF sampler
(`LINEAR` → `Linear`, `STEP` → `Constant`, `CUBICSPLINE` → `Cubic` with the sampler's tangents). Quaternion curves are
sampled component-wise and then **normalized**, so a long slerp-free stretch cannot drift into a non-unit rotation.

### 2.1 Sampling rules

- Linear interpolation between the two keys that bracket the time, clamped at the ends.
- Rotation keys are blended with a shortest-path slerp, not a component lerp: a quaternion flipping sign mid-clip is
  the difference between a 350° spin and a 10° one.
- `Constant` holds the earlier key's value until the next key's exact time.
- Cubic uses the standard Hermite form with `inTangent`/`outTangent` scaled by the segment length, matching glTF's
  cubic-spline semantics.
- Sampling a whole-vector curve (position/scale) and sampling its three axis curves are the same result; the importer
  emits the form the file used, and the sampler does not care.

### 2.2 Weight and blending

`Sample(time, pose, weight)` accumulates into a pose with linear identity-preserving blending:

- position and scale blend linearly: `lerp(current, sampled, weight)`
- rotation blends with `slerp(current, sampled, weight)`

A weight of `1` overwrites, `0` is a no-op, and a pose blended by two clips at `0.5` each is halfway between them. Bones
a clip does not address are left alone, which is what keeps a hand-authored clip on a hand's worth of bones from
flattening the rest of the body.

### 2.3 Wrap modes

| `WrapMode` | Behaviour outside `0..Length` |
| :--- | :--- |
| `Once` | Play once, then hold the last frame. |
| `Loop` | Repeat, `time mod Length`. |
| `PingPong` | Play forward, then backward, without a duplicate end frame. |
| `ClampForever` | Hold the first frame before `0`, the last after `Length`. |
| `Default` | `Once`, except on an `Animation` it inherits the component's wrap mode. |

### 2.4 Events

An `AnimationEvent` is `functionName`, `time`, and an optional `floatParameter`/`intParameter`/`stringParameter`.
Events fire on the frame that crosses their time, in time order, addressed to every component on the object the clip
drives — the way `Invoke` already resolves a method name in `Scripting/Script.cs`.

A long frame fires every event it crossed, once each, in ascending time order. An event whose method is missing, or
whose signature matches nothing the component offers, is logged once and does not fire again that play.

## 3. Skeletons and skinning

### 3.1 What a skeleton is

A `Skeleton` is the list of bone transforms a mesh is bound to, plus their bind-pose inverses:

```csharp
public sealed class Skeleton
{
    public IList<Transform> Bones { get; }              // index order is the weight order in the mesh
    public Transform? RootBone { get; set; }
    public IReadOnlyList<Matrix4x4> BindPoseInverses { get; }
}
```

The bind pose is the pose the mesh was modelled in. A vertex placed with bone `j` at weight `w` moves by

```
jointMatrix[j] = inverseBindMatrix[j] * boneWorld[j] * inverse(rendererWorld)
vertexLocal    = Σ w[j] * (vertexLocal * jointMatrix[j])
```

Written in the row-vector order this engine's `Matrix4x4` and `Transform` already use (`local * parent`), which is the
same transform as the glTF specification's column-vector form with the multiplication order reversed. The reason a mesh
drawn at its bind pose is pixel-identical to the unskinned mesh follows from it: every `jointMatrix` is the identity
when the pose equals the bind pose. **That identity is the single most valuable
test in this layer** — it catches a transposed matrix, a wrong multiplication order and a wrong bind inverse at once.

Bone matrices are rebuilt from the pose on the CPU once per frame and only for bones whose world transform actually
changed; a 60-bone character on a clip touching 12 bones costs 12 matrix products, not 60.

### 3.2 Influences

Four bones per vertex, as in Unity's default `SkinnedMeshRenderer` quality and glTF's `JOINTS_0`/`WEIGHTS_0`. Weights
are normalized on import (a `WEIGHTS_0` accessor with `normalized: true` is rescaled from its integer storage), and a
vertex whose weights sum to zero is pinned to bone `0` rather than collapsing to the origin — the failure mode that
makes an imported character disappear to a point.

A mesh that needs more than four influences is imported at four: the smallest weights are dropped and the rest
re-normalized. That is a real, visible difference from a source file that meant it; §9 says so.

### 3.3 The renderer

`SkinnedMeshRenderer` is a component of its own, not a flag on `MeshRenderer`:

| Member | Meaning |
| :--- | :--- |
| `Mesh` / `MeshName` | The mesh being deformed, shared between instances. |
| `Skeleton` | Bones and bind inverses; `bones` and `rootBone` in Unity's spelling. |
| `Material` | As `MeshRenderer` — one material, one draw. |
| `LocalBounds` | The mesh's bounds in bind pose. Used for picking and for the editor's gizmo. |
| `UpdateWhenOffscreen` | When false, the renderer skips evaluation while its bounds are outside the view. Ignored today, because nothing in this engine culls yet. |

Bounds are the bind-pose box taken through `RootBone`'s world transform, not the undeformed mesh's box: a character
whose arm swings outside its bind box would otherwise be unpickable in the viewport.

### 3.4 The GPU side

A second vertex layout, and a second shader, alongside `scene.vert`/`scene.frag`:

| Attribute | Location | Type |
| :--- | :--- | :--- |
| `aPosition`, `aNormal`, `aColor`, `aTexCoord` | 0–3 | as today |
| `aBones` | 4 | `uvec4` (4 × `uint`, or `ushort` packed) |
| `aWeights` | 5 | `vec4`, normalized |

Bone matrices go up as a **bone texture** — a `RGBA32F` 2D texture of 3 texels per bone — rather than a uniform array,
because a uniform block's size is driver-limited and a palette that silently truncates at 64 bones is a bug that only
appears on other people's hardware. The vertex shader samples the three texels, multiplies, and continues into
exactly the lighting `scene.frag` already does. One extra `DrawElements` path, no change to the lighting model.

The palette is re-uploaded only when the pose changed since the last draw, so a static prop made of skinned parts
costs nothing per frame.

## 4. `Animator`: the state machine

`Animator` owns a controller asset, a set of parameters, and the state machine that decides which clips run and how
they blend.

### 4.1 Parameters

`float`, `int`, `bool` and `trigger`, addressed by name and by hash:

```csharp
animator.SetFloat("Speed", 3.2f);
animator.SetBool("Grounded", true);
animator.SetTrigger("Jump");

if (animator.GetCurrentAnimatorStateInfo(0).IsName("Run")) { … }
```

Unity hashes a parameter name with `Animator.StringToHash`, whose algorithm is not published. We use FNV-1a over the
name's UTF-8 bytes: stable across runs and platforms, and **not** interchangeable with a hash baked by Unity's editor.
Anything that stores a hash must store the name too. A trigger is a bool that a transition **consumes** when it is
taken — set it, let the transition fire, and it is false again; a trigger set twice before it is read is still one
trigger, exactly as in Unity.

### 4.2 States and transitions

A state is a motion (a clip, or a 1D blend tree) plus `speed` and `cycleOffset`. A transition is a source, a
destination, a duration in seconds, an optional `exitTime` (normalized, `0..1`, honoured when `hasExitTime` is set), a
normalized `transitionOffset`, and an ordered list of conditions:

| Condition mode | True when |
| :--- | :--- |
| `If` / `IfNot` | A bool is true / false. |
| `Greater` / `Less` | A float or int is above / below a threshold. |
| `Equals` / `NotEqual` | An int or float equals / differs from a value. |

Conditions within a transition are **AND**ed; transitions leaving the same state are tried in asset order and the
first whose conditions all pass is taken. A transition is evaluated once per frame, after the animation update, in
`Update`'s phase — not inside the sampling loop — so a state cannot change twice in one frame.

The Any State source is supported (interrupt anything, most often a death or a hit reaction); Entry and Exit are not,
because they only mean something with sub-state machines, which are not in scope.

### 4.3 Layers and blending

Layers are an ordered list, each with a weight and a blending mode:

| Mode | Result |
| :--- | :--- |
| `Override` | The layer's pose replaces the lower layers by its weight. |
| `Additive` | The layer's pose is added on top of the lower ones, as a delta from its reference pose. |

A layer **ignores Avatar masks**; every layer drives every bone it addresses. That is the honest simplification: a
mask is the one part of layering that needs the Avatar concept this engine does not have (§9).

A 1D blend tree interpolates two clips whose thresholds bracket the parameter, which covers the overwhelming majority
of Speed-driven locomotion. 2D blend spaces are out of scope.

### 4.4 Where the clock comes from

`Animator.UpdateMode` decides which phase evaluates the machine, and the phase already exists in `Scene`:

| Mode | Phase |
| :--- | :--- |
| `Normal` | After `Update` coroutines, before `LateUpdate`. |
| `AnimatePhysics` | In the fixed step, after physics — for a pose that drives colliders. |
| `UnscaledTime` | As `Normal`, by `Time.UnscaledDeltaTime`. |

`Scene.Update` gains one call between `AdvanceCoroutines(CoroutinePhase.Update, …)` and `AdvanceLateUpdate()`:

```csharp
AdvanceCoroutines(CoroutinePhase.Update, deltaTime);
AdvanceAnimation(deltaTime);          // ← the new step, §4.4
AdvanceLateUpdate();
```

That placement is Unity's, and it matters: a script that moves a transform in `Update` is overridden by the clip,
and a script that wants the final pose reads it in `LateUpdate`.

An `Animator` with no controller, or with `Enabled == false`, writes nothing — the pose stays exactly where the last
evaluation left it. On `Awake` the mesh is drawn at its bind pose.

### 4.5 Scrubbing and the editor

`Animator.Play(stateName, layer, normalizedTime)` sets the state and position without a transition;
`AnimationClip.SampleAnimation(GameObject root, float time)` applies a clip at a time in one call, which is what the
inspector's scrubber uses. Neither needs the state machine, so neither needs the render thread to be playing.

## 5. Events, scripts and the frame

Two hooks join animation to scripting, both through machinery that exists:

- **Animation events** → a reflected call on the object, exactly as `Invoke(nameof(...), delay)` resolves now.
- **`OnAnimatorMove`** → called on scripts before the Animator writes its pose, for the root-motion case. Root motion
  itself (`applyRootMotion`, `deltaPosition`, `deltaRotation`) is **not** implemented; §9.

## 6. Files and identity

Assets follow the prefab conventions already in the tree: YAML inside, a `.meta` sidecar with a guid, links that quote
the guid (`Scenes/PrefabSource.cs`, `Utils/AssetMeta.cs`). **None of these files are written yet** — the shapes below
are the contract for the increment that reads and writes them, and clips currently live in memory instead.

| Asset | Extension | Contents |
| :--- | :--- | :--- |
| Clip | `.anim` | `Name`, `Length`, `FrameRate`, `WrapMode`, `Curves:`, `Events:` |
| Controller | `.controller` | `Parameters:`, `Layers:` → `States:` / `Transitions:` |
| Skeletons | `<Model>.skeletons.yaml` | `Bones:` (node paths, in weight order), `BindPoseInverses:` |

```yaml
# Run.anim
Name: Run
Length: 0.8333
FrameRate: 30
WrapMode: Loop
Curves:
- Path: ""            # the clip's root
  Property: localPosition
  Axis: y
  Keys:
  - Time: 0.0
    Value: 0.0
  - Time: 0.4166
    Value: 0.06
Events:
- Function: Footstep
  Time: 0.25
  StringParameter: grass
```

A clip that came out of a model is written next to it as `<ModelName>@<ClipName>.anim`, so the guid survives the model
being re-imported. Unity keeps clips as sub-assets of the model; Overcore has no sub-assets, so it writes sibling files
and reuses their `.meta` guids instead. Re-import overwrites the file and **keeps** the guid — a scene that referenced
`Run` keeps referencing it.

Object references inside components (bone lists, the Animator's controller) serialize by the scene's own object `Id`,
which every scene YAML already carries, or by asset guid when the target is a file. That is a new capability for
`SceneSerializer`, whose property walk today hands every public read/write value straight to YamlDotNet: there is no
scheme for a reference, so a `Transform` would be written as a structural copy rather than a link.

## 7. What a script sees

```csharp
public class Player : Script
{
    private Animator _animator = null!;

    public override void Awake() => _animator = GetComponent<Animator>()!;

    public override void Update(float deltaTime)
    {
        float speed = Input.IsKeyDown(Key.W) ? 3f : 0f;
        _animator.SetFloat("Speed", speed, dampTime: 0.1f, deltaTime);

        if (Input.WasKeyPressed(Key.Space))
        {
            _animator.SetTrigger("Jump");
        }
    }

    public void Footstep(string surface) => Log($"step on {surface}");
}
```

`SetFloat`/`SetBool`/`SetInt` with a `dampTime` moves the parameter toward the target at that rate instead of jumping,
which is how a locomotion blend stops snapping — the same call Unity's `Animator` offers.

## 8. What exists, and what does not

The layer is being built in four increments. This is the state of each, and the files are named in §11.

### Landed: increment 1 — import

- `ModelImporter` reads a `.gltf`/`.glb` into **objects**: one `GameObject` per glTF node, parented to match the node
  graph, local TRS taken from the file. The old `ModelLoader`, which flattened a file into a name → mesh dictionary,
  is gone.
- `Vertex` carries four bone indices and four weights, read from `JOINTS_0`/`WEIGHTS_0`, normalized on import, with a
  zero-weight vertex pinned to bone 0 instead of collapsing to the origin.
- `Mesh.Indices` is `uint[]`, and the OpenGL, Veldrid and skybox paths draw 32-bit indices. A model past 65,535
  vertices no longer wraps its index array.
- `Skeleton` holds the bones and their inverse bind matrices, and computes the joint matrix palette. A mesh drawn at
  its bind pose produces the identity for every joint — pinned by test.
- `AnimationClip` and `AnimationCurve` hold the curves an importer reads out of a file's channels, addressed by node
  path, with cubic tangents preserved and quaternion keys made continuous so a component-wise blend cannot spin the
  long way round.
- `SkinnedMesh` links a mesh to its skeleton, and `ModelAssets` caches an import once per file so a resource manager
  can still resolve a mesh by the name a scene wrote.

Two limits of that work are worth stating plainly. **Nothing deforms a vertex yet**: a skinned mesh draws at its bind
pose, which is what the importer left its bones in, so a rigged character imports and animates nothing. And the
**importer does not write `.anim`/`.controller` files** — clips live in memory, keyed by file and name, because
serializing an object reference is its own piece of work (§6).

### Next: increment 2 — sampling

`AnimationClip.Sample` into an `AnimationPose`, weight blending, the wrap arithmetic applied per frame, and event
dispatch. `AnimationCurve.Evaluate` and `AnimationClip.WrapTime` already exist and are tested, so this increment is the
part that walks a clip's curves, maps their bone paths onto transforms, and turns the result into a pose that a
renderer or a transform hierarchy can consume.

### Then: increment 3 — the `Animator`

Parameters, states, transitions, layers, 1D blend, and the frame step in `Scene`. Nothing of it exists yet.

### Then: increment 4 — GPU skinning

The skinning vertex shader and the bone-matrix texture. This is the only increment that needs a graphics context, and
therefore the only one CI cannot check.

## 9. Honest gaps — what this is not

- **No Avatar, no humanoid rig, no retargeting, no IK.** A humanoid clip authored for one skeleton does not fit another.
  This is the largest omission and the one that most clearly bounds "fully reverse engineered": generic (non-humanoid)
  animation only.
- **No root motion.** `applyRootMotion` is accepted and ignored; a walk cycle slides on the spot until a script drives
  the transform.
- **Four influences per vertex.** Extra weights are dropped and re-normalized (§3.2).
- **No Avatar masks on layers.** Every layer drives every bone it addresses.
- **No 2D blend spaces, no sub-state machines, no Entry/Exit nodes, no `StateMachineBehaviour`.** A state cannot run
  code on entry or exit; a script hears about transitions only by polling.
- **No animation curves on arbitrary properties.** Only `localPosition`, `localRotation` and `localScale`. Unity animates
  any serialized field; we animate transforms.
- **No morph targets / blend shapes.** glTF's `targets` and `WEIGHTS_*` are ignored, and the importer records a
  warning for a clip that tries to animate them rather than skipping the channel silently.
- **Clips are in memory only.** An import produces them, `ModelAssets` holds them by file and name, and nothing writes
  them to `.anim` yet (§6).
- **A material's base colour has nowhere to go without a texture.** The engine's albedo is vertex colour × texture, so a
  file's base colour is carried in the emissive slot when no texture names one. That is a compromise the lighting model
  will outgrow.
- **Hashes are ours, not Unity's** (§4.1).
- **Packed assets** (`Assets.pak`) can carry `.anim`/`.controller`, but a model imported from a pack gets no sibling
  files written — clips for a packed model must be packed alongside it, authored separately.
- **No frustum culling**, so `UpdateWhenOffscreen` has nothing to skip (§3.3). When culling arrives, it belongs here.

## 10. What the tests pin

Everything except the shader is a deterministic function, so the tests are exact rather than approximate. Pinned
today (`✔`) and what the remaining increments add (`…`):

| Test | Pins |
| :--- | :--- |
| ✔ Pose at bind pose → every joint matrix is identity | matrix order, bind inverse direction (§3.1) |
| ✔ The joint palette cancels a renderer that is not at the origin | the renderer term is not baked into the pose |
| ✔ Node graph survives import, mesh node keeps its parent and local TRS | the whole point of §1 |
| ✔ Winding is reversed on import | the engine's clockwise front faces (§1) |
| ✔ Four weights normalize to one; a zero-weight vertex pins to bone 0 | §3.2 |
| ✔ Curve keys stay sorted when a file lists them out of order | key bracketing (§2.1) |
| ✔ A repeated key time does not divide by zero | the degenerate segment (§2.1) |
| ✔ `Loop` folds past the end, including negative time; `PingPong` reverses | wrap math (§2.3) |
| ✔ A clip's curves address the node path the hierarchy registered | §2 binding |
| … Sample at a key's exact time returns that key's value | key bracketing, off-by-one at the ends |
| … Sample mid-segment matches a hand-computed lerp/slerp | interpolation form |
| … A quaternion curve with a sign flip takes the short way round | shortest-path slerp (§2.1) |
| … `Sample(w = 0)` and `Sample(w = 1)` are no-op and overwrite | blend identity (§2.2) |
| … Two transitions from one state take the first whose conditions all pass | transition ordering (§4.2) |
| … A consumed trigger is false on the next evaluation | trigger semantics (§4.1) |
| … A frame spanning three event times fires all three, in order | event dispatch (§2.4) |
| … Re-import keeps a clip's guid | asset identity (§6) |

## 11. Where the code lives

`✔` means it exists; `…` means it is the next increments' work.

| Piece | File |
| :--- | :--- |
| ✔ Node graph → objects, meshes, skins, clips | `src/Overcore/Rendering/ModelImporter.cs` |
| ✔ The import cache, keyed by file | `src/Overcore/Rendering/ModelAssets.cs` |
| ✔ `Vertex` with bone data | `src/Overcore/Rendering/Vertex.cs` |
| ✔ `Mesh` with 32-bit indices | `src/Overcore/Rendering/Mesh.cs` |
| ✔ `Skeleton`, bind inverses, joint palette | `src/Overcore/Animation/Skeleton.cs` |
| ✔ `AnimationClip`, `AnimationCurve`, `AnimationEvent` | `src/Overcore/Animation/AnimationClip.cs`, `AnimationCurve.cs` |
| ✔ `SkinnedMesh` — the mesh-to-bones link | `src/Overcore/Components/SkinnedMesh.cs` |
| ✔ 32-bit index buffers and the six-attribute vertex layout | `OpenGLMeshRendererData.cs`, `VeldridMeshRendererData.cs`, `VeldridRenderer.cs` |
| ✔ Mesh lookup through the import cache | `src/Overcore/Rendering/ResourceManager.cs`, `ResourceManagerBase.cs` |
| … `AnimationPose`, blending, pose → transforms | `src/Overcore/Animation/AnimationPose.cs` |
| … `Animator`, parameters, state machine | `src/Overcore/Animation/Animator.cs` |
| … Controller asset (YAML) | `src/Overcore/Animation/AnimatorController.cs` |
| … GL bone texture + skinning shaders | `src/Overcore/Rendering/OpenTK/OpenGLSkinnedMeshData.cs`, `EditorResources/Shaders/skin.*` |
| … The frame step | `src/Overcore/Scenes/Scene.cs` (`AdvanceAnimation`) |
| … Reading/writing `.anim`, `.controller` | `src/Overcore/Scenes/SceneSerializer.cs`, `SceneLoader.cs` |
| … Inspector scrubber, state list | `src/OvercoreEditorSession/Scene/`, `src/OvercoreEditorPhotino/Components/` |
