# Animation & Skinning

> **Status: all four increments are implemented.** The importer, the skeleton data model, clip data, the 32-bit index
> path, sampling into a pose, blending, wrap arithmetic, event dispatch, the `Animator` state machine, and GPU skinning
> on the OpenGL backend all exist. §8 says exactly what is and is not there — root motion, the `.controller` asset and
> the Veldrid skinning path are the three things left — and the rest of this document is the contract those follow: the
> semantics, the file formats and the API shape, so that the code and its tests have one ground truth to agree on, the
> way [`prefabs.md`](prefabs.md) and [`physics.md`](physics.md) do for the layers that already exist.

Before this layer Overcore drew static geometry and nothing else: `ModelLoader` read `POSITION`, `NORMAL` and
`TEXCOORD_0` out of a glTF primitive and threw the node graph away, `Vertex` had no room for bone weights, and a scene
frame had no step between `Update` and `LateUpdate` where a pose could be computed. A character could not walk, a door
could not open and a camera could not fly a path.

This is the layer that changed that, and it is Unity's animation stack reimplemented: a **clip** is keyframed curves,
an **Animator** picks clips through a state machine, a **SkinnedMeshRenderer** deforms a mesh by a **skeleton** of
transforms, and animation **events** call back into scripts.

Everything below except the skinning upload and the draw itself is plain arithmetic over arrays, which is what makes
it testable without a window, a context or a GPU — the same property that makes `tests/Overcore.Tests/World` cheap to
run, and the reason the palette packing is a separate, exhaustively tested type rather than a loop inside the renderer.

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

Keys are `(time, value, inTangent, outTangent)`. Unity's `AnimationClip` is uniform in interpolation mode, but glTF
puts the mode on each *sampler*, so the mode travels on the curve and the clip carries a default. The importer sets it
per channel — `LINEAR` → `Linear`, `STEP` → `Constant`, `CUBICSPLINE` → `Cubic` with the sampler's tangents — which is
what lets one clip hold a stepped visibility track and a cubic limb at once. Quaternion curves are sampled
component-wise and then **normalized**, so a long stretch of component interpolation cannot drift into a non-unit
rotation.

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
same transform as the glTF specification's column-vector form with the multiplication order reversed.

The reason a mesh drawn at its bind pose is pixel-identical to the unskinned mesh follows from it, with one detail worth
being exact about: the palette is in the renderer's *parent* space, not its own, because a glTF `inverseBindMatrices`
accessor holds each joint's inverse in the scene the file was authored in. The vertex therefore still takes the ordinary
`model * view * projection` path — the palette is applied first, then the renderer's world matrix, as for any other mesh
— and the invariant that makes bind pose a no-op is

```
jointMatrix[j] * rendererWorld == identity    (when the pose equals the bind pose)
```

At the origin this is the stronger and simpler "every `jointMatrix` is the identity"; off the origin only the pair
collapses. **That cancellation is the single most valuable test in this layer** — it catches a transposed matrix, a
wrong multiplication order, a wrong bind inverse and a renderer term baked in at the wrong end, all at once.

Bone matrices are rebuilt from the pose on the CPU once per frame and only for bones whose world transform actually
changed; a 60-bone character on a clip touching 12 bones costs 12 matrix products, not 60.

### 3.2 Influences

Four bones per vertex, as in Unity's default `SkinnedMeshRenderer` quality and glTF's `JOINTS_0`/`WEIGHTS_0`. Weights
are normalized on import, and a vertex whose weights sum to zero is pinned to bone `0` rather than collapsing to the
origin — the failure mode that makes an imported character disappear to a point.

The storage formats are part of the contract, not an implementation detail. A conforming file stores `JOINTS_0` as
`UNSIGNED_BYTE` or `UNSIGNED_SHORT` — never a float — and `WEIGHTS_0` as either float or integer with `normalized:
true`, where the four weights of a vertex sum to 255 (`UNSIGNED_BYTE`) or 65535 (`UNSIGNED_SHORT`). The importer reads
each accessor through its declared encoding, so both integer forms arrive as ordinary `0..1` weights. Slots that carry
no weight are dropped and the survivors compacted to the front, which keeps a shader from multiplying by a
zero-weighted bone left at index `0`.

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

**A texel is a column of the joint matrix, not a row**, and that is the one detail worth being exact about because the
wrong choice is silent for every unrotated bone. In this engine's row-vector order (`[x y z 1] * M`) a bone's
translation lives in the fourth *row* — `M41`, `M42`, `M43`. Packing the first three rows and reconstructing the
fourth column as `(0, 0, 0, 1)` therefore throws the translation away, and a skinned character rotates about the origin
instead of moving with its bones. Packing the first three *columns* keeps the translation as the fourth component of
each texel, and is what `mat4(column0, column1, column2, column3)` in GLSL already wants, so the shader reassembles the
matrix with no transposition. The dropped column is still the affine `(0, 0, 0, 1)` one, so reconstructing it is safe.

`BonePalette` does the packing as arithmetic over an array, which is what makes the half of skinning a shader depends
on the half CI can check: the stride, the column order, the translation surviving, and a bind pose producing the
identity for every bone. Only the upload itself is left to the eye.

Two programs, not one with a branch. `scene.vert` declares no bone attributes at all, so a static vertex pays nothing
for a fetch it never makes; `skin.vert` is the stage that reads locations four and five. A vertex with no weight at all
takes the identity rather than a zero matrix, so a mesh with no bone data draws where it was modelled instead of
collapsing to a point.

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

The layer is being built in four increments. Three have landed; the files are named in §11.

### Landed: increment 1 — import

- `ModelImporter` reads a `.gltf`/`.glb` into **objects**: one `GameObject` per glTF node, parented to match the node
  graph, local TRS taken from the file. The old `ModelLoader`, which flattened a file into a name → mesh dictionary,
  is gone.
- `Vertex` carries four bone indices and four weights, read from `JOINTS_0`/`WEIGHTS_0` through their declared
  encoding (integer or normalized-integer storage included), normalized on import, with a vertex weighted to no bone
  at all pinned to bone 0 instead of collapsing to the origin.
- `Mesh.Indices` is `uint[]`, and the OpenGL, Veldrid and skybox paths draw 32-bit indices. A model past 65,535
  vertices no longer wraps its index array.
- `Skeleton` holds the bones and their inverse bind matrices, and computes the joint matrix palette. A mesh drawn at
  its bind pose produces the identity for every joint — pinned by test.
- `AnimationClip` and `AnimationCurve` hold the curves an importer reads out of a file's channels, addressed by node
  path, with cubic tangents preserved and quaternion keys made continuous so a component-wise blend cannot spin the
  long way round.
- `SkinnedMesh` links a mesh to its skeleton, and `ModelAssets` caches an import once per file so a resource manager
  can still resolve a mesh by the name a scene wrote.

Two limits of that work were worth stating plainly at the time. **Nothing deformed a vertex yet** — a skinned mesh drew
at its bind pose, which is what the importer left its bones in — which increment 4 closed. And the **importer does not
write `.anim`/`.controller` files**: clips live in memory, keyed by file and name, because serializing an object
reference is its own piece of work (§6). That second limit still stands.

### Landed: increment 2 — sampling

- `AnimationPose` holds bone transforms keyed by the same hierarchy paths a clip's curves are addressed by, so
  sampling needs no scene: it is arithmetic over a dictionary. `ApplyTo` writes a pose onto a hierarchy, `CaptureFrom`
  fills one from a hierarchy, and `Resolve` turns a path into a `Transform`.
- `AnimationClip.Sample(time, pose, weight)` and `SampleAt(...)` walk the curves grouped by bone path, so a bone's
  position is blended as one value rather than three independent writes. A weight of one overwrites, zero is a no-op,
  and two clips at half each land halfway between them. A bone a clip does not address is left alone — the mask that
  keeps a hand-authored clip on a hand's worth of bones from flattening the body.
- A position or scale driven by only some of its axes keeps the pose's value on the others, so a clip that animates
  height alone does not slide the object to the origin.
- Rotation curves are normalized after sampling and slerped, not lerped, so a sign flip mid-clip takes the short way
  round and a component-interpolated quaternion stays a rotation.
- Interpolation mode travels on the curve, so a stepped channel stays stepped and a cubic one keeps the file's
  tangents.
- `AnimationClip.SampleAnimation(root, time)` applies a clip in one call, with no clock — the inspector scrubber.
- `Animation` — the legacy component: one clip, a clock, a wrap mode with a per-object override, `Weight` and
  `RotationWeight`, `Play`, `PlayAt` and `Rewind`. `PlayAt` fires no events, because a scrub is not playback.
- `AnimationEventDispatch` fires the events a frame *crossed*, in time order, once each, splitting correctly across a
  loop boundary; a missing handler is logged once rather than every frame.
- `Scene.Update` gained `AdvanceAnimation` between `AdvanceCoroutines(CoroutinePhase.Update, …)` and
  `AdvanceLateUpdate()` — §4.4's placement, so a clip overrides a transform written in `Update` and a script reading
  the final pose in `LateUpdate` sees this frame's.

A limit of the component was worth stating at the time: **nothing deformed a vertex yet.** `Animation` moved the bone
transforms a skeleton's joint palette is computed from, but no shader read that palette, so a rigged character's
transforms animated while its mesh still drew at its bind pose. Increment 4 closed that; the OpenGL backend now skins.

### Landed: increment 3 — the `Animator`

- `AnimatorController` is the controller as **data**, separate from the component that plays it, so a state machine can
  be built and reasoned about without a scene, a clock or a pose: parameters by name and by hash, states by name,
  layers in order, Any State transitions, and a 1D blend tree.
- Four parameter types — `float`, `int`, `bool` and `trigger` — settable by name or by hash, with `SetFloat`, `SetInt`,
  `SetBool`, `SetTrigger` and `ResetTrigger`, and `GetFloat`/`GetInt`/`GetBool` reading them back. A name the
  controller does not have is ignored rather than thrown, so a script outliving a renamed parameter goes quiet instead
  of taking the frame down.
- The name hash is **FNV-1a over the name's UTF-8 bytes**. It is deliberately *not* Unity's algorithm, which is why
  anything that persists a hash persists the name beside it — a number alone would mean nothing to a reader of the
  file.
- Six condition modes: `If`, `IfNot`, `Greater`, `Less`, `Equals`, `NotEqual`. Conditions on a transition are ANDed, and
  an unconditional transition is always ready. A threshold comparison against a bool is treated as unmet rather than
  guessed at, a bare `If`/`IfNot` against a number means "non-zero", and a condition naming a parameter that is not
  there **fails**, so a broken reference stalls the machine instead of taking the frame down.
- Transitions carry a destination, a duration in seconds, an optional normalized exit time, a normalized offset into
  the destination, and their ordered conditions. They are tried in asset order and the first whose conditions pass is
  the one taken — Any State first, so an interrupt beats the current state's own exits.
- A frame takes **at most one transition**. The frame that enters a state may take that state's exit, and stops there,
  so a chain of states fires one per frame rather than running the whole graph in a single tick.
- A transition is a cross-fade, not a cut: the source keeps playing on its own clock while the destination runs on a
  second one from the offset the transition names, and the destination is faded in over the source by how far through
  the frame is. On landing the state adopts the destination's accrued clock, so the blend holds its final frame
  instead of snapping back to the start of the destination.
- A trigger is consumed by the transition that reads it, so one press is one jump; two presses before the machine looks
  are still one trigger, which is why it is stored as a flag rather than counted.
- Layers are ordered, each with a weight and an `Override` or `Additive` blend mode. An additive layer contributes only
  its difference from a reference pose, with rotations diffed as quaternions and applied by slerping from the identity
  — a component-wise scale of a quaternion would not be a partial turn.
- A 1D blend tree samples the two clips bracketing the parameter and fades the far one in over the near one. Outside
  its range the nearest clip plays alone rather than fading toward a weight the tree does not hold. Entries are sorted
  by threshold on every sample, so a file may list them in any order.
- `Scene.Update` advances the `Normal` and `UnscaledTime` animators between the `Update` coroutines and `LateUpdate`;
  `AnimatePhysics` animators are stepped at the end of `OnFixedStep` instead, and skipped by the frame step, so nothing
  advances twice in one frame.
- `SetFloat(name, target, dampTime, deltaTime)` approaches its target with a critically damped spring rather than a
  linear ramp: it starts from rest, cannot overshoot, and settles, which is what `dampTime` names.

Two things are worth stating plainly. **Exit times are checked against the time spent in the state, unwrapped** —
checking them against the wrapped clock would make `exitTime = 1` unreachable on a looping clip, because the clock
reaches the end, wraps to near zero, and never shows the crossing. And **layers ignore avatar masks entirely**, which
is deliberate: a mask needs a skeleton to name bones in, and the controller is data that outlives any one rig (§4.3).

Still not implemented, and named here so it is not mistaken for done: **root motion**, and the `OnAnimatorMove` hook
that would drive it (§5). The `.controller` file is still only a contract (§6) — controllers live in memory.

### Then: increment 4 — GPU skinning

### Landed: increment 4 — GPU skinning

- `BonePalette` packs a skeleton's joint matrices into the texture layout: three `RGBA32F` texels a bone, each a
  **column** of the matrix, twelve floats instead of sixteen. A quarter less upload traffic on the one buffer that
  changes every frame, and the column that is dropped is always the affine `(0, 0, 0, 1)` one.
- `OpenGLBoneTexture` owns the texture handle and the upload; `OpenGLSkinnedMeshData` owns the geometry plus the
  palette, and rebuilds the palette only when a joint actually moved. A character standing still costs one draw call
  and no texture traffic.
- `skin.vert` / `skin.frag` are a second program, not a branch in `scene.vert`: the static stage declares no bone
  attributes, so a static vertex pays nothing. The skinning stage reconstructs the dropped column, blends four
  influences by weight, normalizes them before use, and falls back to the identity for a vertex no bone moves.
- `OpenGLSceneRenderer` draws a static pass and then a skinned pass, each with its own program and its own global
  uniforms. The bone texture lives on unit 1, away from the material's diffuse on unit 0.
- `MeshRenderer` carries a `SkinnedBackendData` slot beside `BackendData`, so a skinned mesh's palette state is not
  thrown away by the geometry it shares with the static path.

Three things this increment does **not** do, named so they are not mistaken for done:

- **The Veldrid backend does not skin.** The editor runs OpenTK (`Program.cs`), and the Veldrid path is the alternate
  one; a skinned mesh drawn there sits at its bind pose, and the first time it happens the renderer logs a warning
  saying so rather than leaving a character quietly still.
- **There is no frustum culling**, so `UpdateWhenOffscreen` is still ignored (§3.3).
- **The shaders are the part CI cannot run.** Everything the shader depends on — the stride, the column order, the
  translation surviving the round trip, bind pose producing the identity — is asserted against the shader source and
  the packing code, but nothing here executes a draw. That is a real limit of the increment, not a gap papered over.

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
- **Skinning is OpenGL only.** The Veldrid backend has the static pipeline, so a skinned mesh drawn through it sits at
  its bind pose. The editor runs OpenTK, and the renderer logs a warning the first time it happens rather than leaving a
  character quietly still (§3.4).
- **The shaders are never executed by the tests.** The stride, the column order, the translation and the bind-pose
  identity are all asserted against the packing code and the shader source, but a draw needs a context. That is the one
  part of this layer a green CI run does not prove.

## 10. What the tests pin

Everything except the shader is a deterministic function, so the tests are exact rather than approximate. Pinned
today (`✔`) and what the remaining increments add (`…`):

| Test | Pins |
| :--- | :--- |
| ✔ Pose at bind pose → every joint matrix is identity at the origin | matrix order, bind inverse direction (§3.1) |
| ✔ The joint palette cancels a renderer that is not at the origin (`palette * rendererWorld == I`) | the renderer term is applied at the right end (§3.1) |
| ✔ Node graph survives import, mesh node keeps its parent and local TRS | the whole point of §1 |
| ✔ Winding is reversed on import | the engine's clockwise front faces (§1) |
| ✔ Four weights normalize to one; slots with no weight compact to the front | §3.2 |
| ✔ A vertex weighted to no bone at all pins to bone 0 | §3.2 |
| ✔ Curve keys stay sorted when a file lists them out of order | key bracketing (§2.1) |
| ✔ A repeated key time does not divide by zero | the degenerate segment (§2.1) |
| ✔ `Loop` folds past the end, including negative time; `PingPong` reverses | wrap math (§2.3) |
| ✔ A clip's curves address the node path the hierarchy registered | §2 binding |
| ✔ Sample at a key's exact time returns that key's value | key bracketing, off-by-one at the ends |
| ✔ Sample mid-segment matches a hand-computed lerp | interpolation form |
| ✔ A quaternion curve with a sign flip takes the short way round | shortest-path slerp (§2.1) |
| ✔ A sampled rotation is unit length even when its axes interpolate independently | normalization (§2.1) |
| ✔ A stepped curve holds its key; a cubic one follows the file's tangents | interpolation mode per channel (§2) |
| ✔ `Sample(w = 0)` and `Sample(w = 1)` are no-op and overwrite | blend identity (§2.2) |
| ✔ Two clips at half weight each meet in the middle | blend identity (§2.2) |
| ✔ A bone the clip does not address keeps the value the pose held | the mask (§2.2) |
| ✔ An axis the clip does not drive keeps the pose's value | a missing axis is not zero |
| ✔ The pose is written onto the hierarchy, and a paused clock still applies it | the component's clock (§4.4) |
| ✔ `Update` runs the clip after `Update` and before `LateUpdate` | the frame placement (§4.4) |
| ✔ The name hash is FNV-1a over the UTF-8 bytes, and is the same every run | §4.1, and that a stored hash still means something |
| ✔ Parameters are addressable by name and by hash; a missing one is ignored, not thrown | §4.1 |
| ✔ Conditions are ANDed, and each of the six modes reads its type correctly | §4.2 |
| ✔ A condition naming a parameter that is not there fails, so the machine stalls | §4.2 |
| ✔ Two transitions from one state take the first whose conditions all pass | transition ordering (§4.2) |
| ✔ A consumed trigger is false on the next evaluation | trigger semantics (§4.1) |
| ✔ Two trigger presses before the machine looks are still one trigger | §4.1 |
| ✔ An Any State transition interrupts the state that is playing | Any State ordering (§4.2) |
| ✔ A frame takes one transition: a chain of exits advances one state per frame | §4.2 |
| ✔ A cross-fade blends the two states and lands exactly on the destination | §4.2 |
| ✔ A zero-duration transition is a cut, in the frame it starts | §4.2 |
| ✔ An exit time holds the state until it is reached — including `1.0` on a looping clip | exit times (§4.2) |
| ✔ A layer at zero weight contributes nothing; an additive layer adds only its delta | §4.3 |
| ✔ A 1D blend tree mixes the clips bracketing its parameter and clamps outside its range | §4.3 |
| ✔ A blend tree sorts its entries, so a file may list them in any order | §4.3 |
| ✔ `Normal` animators step before `LateUpdate`; `AnimatePhysics` ones step in the fixed step instead | the frame placement (§4.4) |
| ✔ A damped `SetFloat` closes on its target without overshooting it | §4.1 |
| ✔ A bone packs three texels of four floats, never a zero-width texture | the palette layout (§3.4) |
| ✔ A packed texel is a matrix **column**, so a translation survives the round trip | the row/column order (§3.4) |
| ✔ A translation lands in the fourth component of each column, and picks up no scale | the bug this pins (§3.4) |
| ✔ Packing several bones lays them out back to back at `bone * 3` | the shader's stride (§3.4) |
| ✔ A too-small palette buffer is refused, not overrun | §3.4 |
| ✔ At bind pose the packed palette is the identity for every bone | §3.1's invariant, in the packed form |
| ✔ The shader reads `bone * 3` and reassembles `mat4(column0, column1, column2, …)` | the CPU/GPU contract (§3.4) |
| ✔ The static shader declares no bone attributes; the skinning one reads locations 4 and 5 | the two-program split (§3.4) |
| ✔ An unweighted vertex takes the identity rather than a zero matrix | the "disappears to a point" failure (§3.2) |
| ✔ A frame spanning three event times fires all three, in order | event dispatch (§2.4) |
| ✔ An event fires once, not every frame the playhead sits past it | half-open intervals (§2.4) |
| ✔ A frame that wrapped fires the events on both sides of the loop | loop dispatch (§2.4) |
| ✔ A missing handler is logged once, not once per frame | diagnostic economy (§2.4) |
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
| ✔ `AnimationPose`, blending, pose → transforms | `src/Overcore/Animation/AnimationPose.cs` |
| ✔ `AnimationPlayer` — the one-clip player | `src/Overcore/Animation/AnimationPlayer.cs` |
| ✔ Event dispatch by reflection | `src/Overcore/Animation/AnimationEventDispatch.cs` |
| ✔ The controller as data: parameters, states, transitions, layers, 1D blend | `src/Overcore/Animation/AnimatorController.cs` |
| ✔ `Animator` — the state machine and the frame step | `src/Overcore/Animation/Animator.cs` |
| ✔ The physics-phase step | `src/Overcore/Scenes/Scene.cs` (`AdvanceAnimation`, `AdvancePhysicsAnimation`) |
| … Controller asset (YAML) | `src/Overcore/Scenes/SceneSerializer.cs`, `SceneLoader.cs` |
| ✔ The palette packing — texel stride, column order, identity fill | `src/Overcore/Animation/BonePalette.cs` |
| ✔ The bone texture and its upload | `src/Overcore/Rendering/OpenTK/OpenGLBoneTexture.cs` |
| ✔ Skinned draw state, palette rebuilt only when the pose moved | `src/Overcore/Rendering/OpenTK/OpenGLSkinnedMeshData.cs` |
| ✔ The skinning shaders | `src/OvercoreEditor/EditorResources/Shaders/skin.vert`, `skin.frag` |
| ✔ The static-then-skinned passes | `src/Overcore/Rendering/OpenTK/OpenGLSceneRenderer.cs` |
| … Skinning on the Veldrid backend | `src/Overcore/Rendering/VeldridRenderer.cs` |
| … Reading/writing `.anim`, `.controller` | `src/Overcore/Scenes/SceneSerializer.cs`, `SceneLoader.cs` |
| … Inspector scrubber, state list | `src/OvercoreEditorSession/Scene/`, `src/OvercoreEditorPhotino/Components/` |
