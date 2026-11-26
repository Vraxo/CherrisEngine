> **Status: increment 2 of 4 implemented — the depth pass, and it draws.** `CascadeSplits` (§3),
> `LightSpaceFit` (§4), `Shadows` (§2.3), `ShadowCasters` (§5.2), the depth shaders (§5.3) and
> `OpenGLShadowRenderer` with its `ShadowDepthTarget` layered attachment (§5.1) exist, and `OpenTKRenderer.RenderScenePass` runs
> the pass before `_postProcessor.BeginFrame()`. The receiver (§6) is in `scene.frag` and `skin.frag` and samples the map.
> Every claim below is implemented unless §8 or the parenthetical says otherwise. The tests pin what needs no GPU —
> splits, the fit, the caster rules, the frame's early returns and the two shaders' agreement — plus the round-trip of
> every new serialized property; nothing in CI compiles a shader, so a GLSL error is a runtime failure and not a build
> one. The Veldrid backend still renders no shadows at all (§8).

Before this layer Overcore lit everything as if nothing were ever in the way. `scene.frag` sums a directional term,
up to four point lights and up to four spot lights over an albedo, and every surface it shades is lit through every
other surface. A crate on a floor casts nothing, a wall casts no shade into the room behind it, a character has no
anchor to the ground, and an interior is lit exactly as brightly as the meadow outside it. `OpenGLSceneRenderer`
issues exactly one `DrawElements` per visible mesh and reads no depth buffer other than the one it is drawing into.

That is the largest visible gap between this renderer and the engines it is modelled on, and unlike most of the
remaining gaps it is **entirely re-derivable**: a directional light's shadow map is an orthographic projection of a
frustum slice onto a depth buffer, compared per pixel. Unity's numbers for it are published in its serialized
`QualitySettings` assets and its `Light` component reference. There is no proprietary bake, no opaque asset format and
no solver to port — which is exactly why this is the layer to take next.

Two properties of the existing engine make it cheap to build and cheap to test:

- **The vertex layout already carries everything the depth pass needs.** `Vertex` has `Position` at location 0,
  `BoneIndices` at 4 and `BoneWeights` at 5, and `skin.vert` already knows how to turn those into a deformed position.
  A shadow caster is that same deformation with no fragment work.
- **The split is already static-then-skinned.** `OpenGLSceneRenderer` draws two passes with two programs and two sets of
  global uniforms. A depth pass is a third and a fourth, sharing that structure rather than inventing one.

Everything except the upload and the draw is arithmetic over matrices and floats, which is what makes it testable
without a window, a context or a GPU — the same property that makes `tests/Overcore.Tests/Animation` cheap to run, and
the reason cascade splits, frustum fitting and texel snapping are separate pure types rather than loops inside the
renderer.

## 1. The model: one depth map per cascade, compared in the receiver

A directional light has no position, so it cannot have a perspective shadow frustum. Its "eye" is cast from a chosen
orthographic box that looks along the light's direction, and the slices of the camera's view frustum are fitted into it.
That is what a **cascade** is: a partition of the camera's view by depth, each slice with its own shadow map, each map
covering the slice as tightly as an axis-aligned orthographic box allows.

Four cascades by default, because one map covering 150 units of view depth at a usable resolution is either enormous or
a blur, and because close geometry is what the eye judges contact by.

The loop, per frame:

1. Split the camera's shadow range into `n` cascades (§3).
2. For each cascade, fit a light-space orthographic box to the slice's eight frustum corners and snap it to texel
   boundaries (§4).
3. Render every caster into that cascade's depth texture, from the light, with a depth-only program (§5).
4. In the receiver's fragment shader, pick the cascade the fragment's view depth falls in, project the fragment into
   that cascade's light space, and compare (§6).

A directional light is the first and only light this document covers. Point and spot shadows are named in §8 as the
increment that follows, and nothing here is written in a way that would have to be torn out to reach them.

## 2. The knobs, and where they live

Unity splits these across two places and Overcore keeps that split, because it is the right one: **per-light** settings
describe how one lamp casts, and **quality** settings describe how much the machine will spend on all of them at once.

### 2.1 Per light — on the `Light` component

Unity's `Light` component reference names six shadow properties. They go on `Overcore.Components.Light` with the same
names and the same meaning:

| `Light` member | Type | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `Shadows` | `ShadowType` | `Soft` | `None`, `Hard`, `Soft`. `Soft` is PCF (§6.2); `None` still lets the light be a receiver of other lights' shadows. The default is `Soft` on every light whatever its `Type`, so switching a light from point to directional does not silently leave it casting nothing — in this increment only a directional light's value has an effect, and a point light that says `Soft` is a promise the next increment keeps (§8). |
| `ShadowStrength` | `float` `0..1` | `1` | How dark the shadow is. `0.4` is a shadow that lets a fifth of the light through — Unity's answer to a scene with no bounce lighting. |
| `ShadowBias` | `float` | `0.02` | Constant depth bias, in light-space units (§6.1). |
| `ShadowNormalBias` | `float` | `0.4` | Normal offset, in shadow-map texels (§6.1). |
| `ShadowNearPlane` | `float` | `0.2` | In front of this distance from the camera no shadow is sampled. Kills the acne that only ever appears on the near clip plane. |
| `ShadowResolution` | `ShadowResolution` | `High` | `Low`, `Medium`, `High`, `VeryHigh` = 512, 1024, 2048, 4096 texels a side (§5.1). |

`Light.CullingMask` is deliberately **not** on this list, though it is on Unity's. The existing `Light` has no culling
mask, nothing in either colour pass reads a light's layers, and adding one would be a change to what a light *is* rather
than to how it casts — so a light still illuminates every layer it always did, and §5.2's caster list is filtered by
layers only once there is a mask for it to be filtered by.

`ShadowType`, `ShadowResolution` and the existing `LightType` are all drawn as enum popups by both front ends — `PropertyDrawer.cs:48` and `ComponentProperties.cs:360` — and all three round-trip through the existing serializer with no new code: `SerializeComponentProperties` reflects over public writable properties and writes each one only when it differs from a fresh instance's default, and `ComponentDeserializer.TryConvertValue` parses a `string` with `Enum.Parse(..., ignoreCase: true)` and a number with `Convert.ChangeType`. The inspector needs no new control kind either: `ShadowStrength`, `ShadowBias` and `ShadowNormalBias` are floats that `[Range]` already covers.

### 2.2 Per renderer — on `MeshRenderer`

Two more, because "does this cast" and "does this receive" are properties of a renderer, not of a lamp:

| `MeshRenderer` member | Type | Default | Meaning |
| :--- | :--- | :--- | :--- |
| `CastShadows` | `ShadowCastingMode` | `On` | `Off` (never drawn into a depth map), `On`, `TwoSided` (no face culling in the depth pass), `ShadowsOnly` (drawn into depth maps, skipped in the colour pass). |
| `ReceiveShadows` | `bool` | `true` | Whether the receiver shader samples at all. Off is what makes a fake shadow blob or a baked look possible. |

`ShadowsOnly` is the mode that makes an invisible caster — a collider-shaped box standing in for a detailed prop — a
one-line answer instead of a second material trick.

### 2.3 Per scene — the `Shadows` component on an object

Cascade count, distance and splits are quality settings in Unity, held in `QualitySettings.asset` and overridable per
camera. Overcore has no quality levels, so they belong to the scene: a 20×20 interior and a 2 km valley do not want the
same 150-unit shadow distance, and the scene file is where that difference is written down.

They are a **`Shadows` component** on a single object, not a root-level key, and that is forced by the loader rather
than chosen for taste. `SceneLoader.DeserializeObjects` parses the whole scene as
`Dictionary<string, List<Dictionary<string, object>>>` and looks up exactly `"GameObjects"`: a root-level mapping such
as `Shadows:` is not a list of objects, and the deserialize fails before the first object is read. A component needs no
loader change at all — one factory registration, and `SerializeComponentProperties` writes its non-default values as it
already does for every other component:

```yaml
GameObjects:
- Id: 9b3f…
  Name: Sun
  Components:
    Light: …
    Shadows:
      Distance: 150
      Cascades: 4
      Splits: [0.06666667, 0.2, 0.46666667]
      Resolution: High
```

| Member | Default | Notes |
| :--- | :--- | :--- |
| `Distance` | `150` | Metres of camera view depth covered by shadows. Past it, nothing is shadowed. |
| `Cascades` | `4` | `1`, `2`, `4`. Unity offers 1/2/4 for the same reason: 3 does not divide a view nicely and the split formula in §3 degenerates. |
| `Splits` | derived from `Cascades` (§3.2) | The `n-1` cumulative boundaries, normalised to `0..1` of `Distance`. Present in the file so a hand-tuned split survives a save. |
| `Resolution` | `High` | The per-cascade default; a light's own `ShadowResolution` overrides it. |
| `Softness` | `1.0` | The PCF tap spacing, in texels (§6.2). `1` is a tent one texel wide; larger is a wider, softer edge. |
| `BlendBand` | `0.1` | The fraction of a cascade's range over which it cross-fades into the next (§6.3). `0` is a hard switch. |

**Exactly one is consulted.** A scene may hold several — one per region is a reasonable thing to want — and the one in
force is the first on an object that is `ActiveInHierarchy` with the component `Enabled`, which is the same "first
enabled wins" rule `Scene.FindMainComponents` already applies to `Camera` (its `MainCamera ??=` line) and `Skybox`. The rest are ignored without a warning, because a
scene that has two is a scene that has not decided yet, not one that is broken.

**A scene with no `Shadows` component keeps loading.** Absent means the defaults above, and the built-in shadow settings
are structurally complete, so every `.yaml` on disk loads and renders — with shadows now on, which is the point of the
layer, and is worth saying out loud because it is the one backward-compatible change that visibly changes every
existing project. There is therefore nothing to "add" to an existing scene to get shadows; adding the component is how a
project *departs* from the defaults.

Serializing it is the one place this document asks for new code in an existing file. `SerializeComponentProperties`
skips a property whose value equals a fresh instance's, and `Splits` is a list whose default is **not** a constant: it
depends on `Cascades`. The component therefore exposes `Splits` as a stored list that its `Cascades` setter rewrites in
place, so a fresh instance is born holding the derived default for its own `Cascades` and equality still works. `Splits`
is only written to the file once a hand edit has made it differ, and `Cascades` alone round-trips through the ordinary
path. A list-valued property is the one shape this serializer does not handle defensively — `TryConvertFromList` converts a
YAML sequence only to `Vector2` and `Vector3` and returns false for anything else, which reaches the inspector as a
conversion warning — so `Splits` is `float[]`, read back by the component's own load path rather than by reflection. That is stated here because it is the sort of detail that is discovered by a saved scene coming back
with four cascades and the wrong splits.

## 3. Cascade splits

### 3.1 The rule

Cascade `i` (1-based) ends at view depth

```
splitIndex[i] = (2^i - 1) / (2^n - 1)      i = 1..n
farDepth[i]   = shadowDistance * splitIndex[i]
nearDepth[1]  = camera.NearClipPlane
nearDepth[i]  = farDepth[i-1]
```

For `n = 4` that is `(1, 3, 7, 15) / 15` = `0.0667`, `0.2`, `0.4667`, `1.0`; for `n = 2` it is `(1, 3) / 3` =
`0.3333`, `1.0`. At `Distance = 150` the four cascades end at **10, 30, 70 and 150 metres**, and their *widths* are
**10, 20, 40 and 80** — each cascade is exactly twice as deep as the one inside it.

That is not an invented formula. It is what Unity's own `QualitySettings.asset` contains: `shadowCascade2Split:
0.33333334` and `shadowCascade4Split: {x: 0.06666667, y: 0.19999999, z: 0.46666664}`, in every shipped quality level
from `Good` to `Ultra` in Unity's own sample projects. The halving ratio is what makes the sequence worth copying: a
cascade twice as deep covers twice the volume at the same texel count, so its texel density is half — and it is half
because that is where the eye stops being able to tell.

### 3.2 Overriding it

A scene may write any strictly increasing `Splits` list of `n-1` values in `0..1`. Two rules are enforced on load
rather than trusted:

- a value that is not finite, at or below the previous one, or not inside `(0, 1)` is **clamped** to keep the list
  strictly increasing, and the clamp is logged once;
- a list of the wrong length is **discarded** and the derived default is used, with a warning naming the scene file.

A split list that disagrees with `Cascades` is the kind of thing a hand-edited file produces, and it must not be a
divided-by-zero later.

### 3.3 Why not the logarithmic split

The other published rule is the practical split (Zhang et al., *GPU Gems 3*, chapter 10), which blends a uniform
division with a logarithmic one by a factor `λ`:

```
linear[i] = near + (far - near) * i / n
log[i]    = near * (far / near)^(i/n)
practical[i] = λ * log[i] + (1 - λ) * linear[i]
```

It produces better texel distribution for a very deep view frustum — a 10 km flight sim wants it — and it is worse than
the halving rule for the 100–300 metre scenes this engine is built for, because near the camera it spends a cascade
boundary the frame does not need. It is documented here as the known alternative and the shape a `Splits` list can be
filled from by hand; it is not the default, and no code path computes it. If it ever becomes one, it is a `Splits`
producer and nothing else in this document changes.

## 4. Fitting the light's box to a slice

### 4.1 Corners, then a box

For a cascade covering `[nearDepth, farDepth]`, in the camera's own space:

```
halfHeight = tan(fovY / 2)
halfWidth  = halfHeight * aspect
corner[k]  = cameraWorld * ( ±halfWidth * d, ±halfHeight * d, -d )
```

for the four sign combinations at each of `d = nearDepth` and `d = farDepth` — eight points. -Z is forward, matching
`Transform.Forward` and `Camera.GetViewMatrix`.

Transform all eight by the light's `view` matrix, take the componentwise min and max, and that is the orthographic box:
`left/right` from `x`, `bottom/top` from `y`, and the near/far pair from `z`. The projection is
`Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, near, far)`.

**One remap is needed, and forgetting it is invisible.** The projection maps a corner to an NDC depth in `[0, 1]` —
`System.Numerics` follows the Direct3D convention, where the near plane is 0 — and OpenGL's viewport transform then
writes `(ndc + 1) / 2` into the depth buffer, so the shadow map holds `[0.5, 1]`. A receiver that computed the same
matrix and compared its `[0, 1]` value against the texture would find the stored depth greater than the fragment's for
every fragment of every object: `step()` returns 1, the surface is fully lit, and the feature is present, compiling,
running and doing nothing at all. The remap is therefore in the shader, `storedDepth = (projected.z + 1.0) * 0.5`, and
it is asserted from both ends — `TheProjectionsDepthIsDirect3DConventionAndTheShaderRemapsIt` pins that this side really
does emit `[0, 1]`, and `ShadowShaderTests` pins that both receivers remap.

The alternative — baking the remap into the matrix — was rejected because it would make every test of §4's extents and
snapping assert against OpenGL's clip convention, which is a property of the backend rather than of the fit.

The light's `view` matrix is `CreateLookAt(eye, eye + lightDirection, up)` where `eye` is the centre of the slice's
bounding sphere and `lightDirection` is the direction the light *travels* — the negated `uDirLight.direction` the
renderer already uploads as "direction to light", since `scene.frag` computes `dot(normal, lightDir)` against a vector
pointing at the sun. Getting this backwards produces shadows pointing away from the sun, which is a bug that looks like
a working feature in a single-caster test scene and is obvious the moment two objects are near each other.

`up` is `Vector3.UnitY` unless the light points within a degree of straight down or straight up, in which case it is
`Vector3.UnitZ`. A degenerate up vector in `CreateLookAt` gives a NaN matrix, and a NaN matrix gives a black screen
rather than an error — the same class of failure `Camera.GetProjectionMatrix` already guards against.

### 4.2 Texel snapping, and why it is the most valuable test here

An orthographic box fitted tightly to eight corners has a size and a centre that move continuously as the camera moves,
so every step of the camera slides the depth map by a fraction of a texel and every shadow edge in the scene **crawls**.
The fix is to stop deriving the box's *size* from the fit at all:

```
radius     = half the diagonal of the slice's own frustum geometry   // from nearDepth, farDepth, fovY, aspect
texelSize  = 2 * radius / resolution
centre     = slice bounding-sphere centre, in light space
centre    = floor(centre / texelSize + 0.5) * texelSize             // snapped to the texel grid
box        = centre ± radius, on x and y
```

Three properties of that, each of which is the reason for the line above it:

- **`radius` is computed from the slice's geometry, never from the fitted box.** For a perspective frustum slice it is a
  function of `nearDepth`, `farDepth`, `fovY` and `aspect` alone — all constants for a frame in which the camera has not
  moved its lens — so translating or rotating the camera does not change it. A radius read back from the fit's min/max
  would change with every camera step, which makes the texel grid itself move and defeats the whole exercise.
- **The box is square and its extent is fixed at `radius`.** A size that varies per frame cannot be snapped to a grid
  that also stays put. A square also keeps the texel size identical in both axes, so the snap moves an integer number of
  texels in each and a diagonal shadow edge does not jitter on one axis and not the other.
- **Only the centre is snapped**, and `x`/`y` alone. The depth extent is widened to cover the slice and never quantised:
  depth resolution is precious and there is no visual gain from snapping it.

Only `x` and `y` are quantised. Depth is left alone: it gains nothing from a texel grid, since the depth buffer's own
resolution is what matters there, and quantising it pushes a corner out of the box on the one frame the rounding goes
the wrong way.

**The invariant, stated precisely,** because the looser version of this sentence is false and was written here first: a
camera translating by less than one texel does *not* leave the whole matrix bit-identical — the box is anchored to the
world, so the camera's distance along the light is a real change. What holds, and is what stops crawling, is three
things:

- **The extent, and therefore the texel size, is identical** for any camera translation that does not change the lens or
  the cascade's depths. This is the property a fit read back from min/max would break.
- **The snapped centre is always an exact whole number of texels** from the origin along each of the light's `x` and `y`
  axes — not merely close to one.
- **The snapped coordinate is a staircase.** As the camera moves, it holds still, then steps by exactly one whole texel
  — it never takes a fractional value. A depth map anchored this way is identical for a whole band of camera positions
  and then changes in one jump; the crawl this removes is a map that slides by a fraction of a texel every frame.

Together those are what "the shadow does not crawl" means, and each is a plain assertion. They catch a snap applied
before the fit instead of after, a snap in a mirrored basis, a snap that quantises depth, and a fit that depends on the
camera's absolute position — the failure that only appears once a level is 20 km from the origin. This is the layer's
equivalent of `palette * rendererWorld == identity` in the skinning layer, and it needs no GPU to check.

## 5. The depth pass

### 5.1 The target

`FramebufferBuilder` can attach a depth **renderbuffer** (`WithDepthStencil()`), which cannot be sampled. Shadows need a
depth **texture**, so the builder grows one method:

```csharp
FramebufferBuilder WithShadowDepthArray(int resolution, int layers)
```

The four cascades are **four layers of one `Texture2DArray`**, not four textures, and the reason is a language rule
rather than taste: GLSL 330 lets a sampler array be indexed only by a constant expression, so a receiver that picked its
cascade at run time could not write `uShadowMaps[cascade]`. A `sampler2DArray` is sampled with an arbitrary integer
layer, which is exactly the operation the receiver needs, so one texture with a layer per cascade is what makes §6.3
expressible in the shader version this engine compiles.

The builder allocates `Texture2DArray` storage of `DepthComponent24` with `layers` slices and a side of `resolution`,
sets nearest filtering and clamps to edge in all three dimensions. Each cascade is attached in turn with
`GL.FramebufferTextureLayer` to `DepthAttachment` of one framebuffer, `glDrawBuffer(GL_NONE)` and `glReadBuffer(GL_NONE)`
once — a depth-only framebuffer declares no colour attachment, and saying so is not optional: a framebuffer the driver
thinks is incomplete draws nothing and raises nothing. Status is checked with `GL.CheckFramebufferStatus` **after each
attachment**, and an incomplete one throws naming the layer rather than rendering a blank frame.

**Not a `sampler2DArrayShadow`, and not `TextureCompareMode = CompareRefToTexture`.** An earlier draft of this document
specified hardware comparison and the implementation does the comparison itself after an ordinary fetch. Two reasons,
and the second is the one that decided it: a slope-scaled bias has to be applied to the fragment's depth *before* the
test, and a hardware comparison's reference value is the one the shader passes in but its configured function and mode
are properties of the sampler, fixed when the program is compiled. `ShadowType.Hard` and `ShadowType.Soft` then cannot
share a program, which costs a whole second pair of lit shaders to save nine ALU instructions. An ordinary fetch of a
depth texture returns the stored depth in `.r`, so the test is `step(projected.z - bias, sampled)` — the same
comparison, written where the bias can be seen.

`DepthComponent24` rather than `DepthComponent32F`: 24 bits over a directional cascade's depth range is 16.7 million
levels across a few hundred metres, far past what the bias in §6.1 can meaningfully use, and 32-bit float depths cost
twice the bandwidth in the pass that runs four times a frame. There is **no reverse-Z** here and no infinite far plane:
both exist to rescue precision in a perspective projection running from a near plane to infinity, and an orthographic
box has neither problem.

`TextureWrapMode` gains a clamp-to-edge value for this — the existing `_wrapMode` defaults to `Repeat`, and a repeated
shadow map wraps the far side of a cascade onto its near side, which reads as a shadow that is present and in the wrong
place.

The array is allocated once at the resolution in force — the casting directional light's `ShadowResolution`, spelled in
texels by `Shadows.TexelsOf` — with `Cascades` layers, and reallocated only when either changes. `ShadowFrame.Build`
takes an explicit override as well, which is what a test uses to pin the two apart.
There is one directional light casting per frame, so there is one allocation to make; the note exists because the
obvious alternative, a texture array per light, is what a two-directional-light future would need and is not what this
increment builds.

### 5.2 What is drawn

Every `GameObject` that is `ActiveInHierarchy`, has a `MeshRenderer` with a non-null `Mesh`, `Enabled` true and
`CastShadows` not `Off` — **and its `Skybox`, if any, is excluded**: the sky is a source of light, not an occluder.
That exclusion is the same one both existing passes already apply, and it is easy to lose in a third pass written by
copying the first.

**Which light.** Casters are drawn for the one directional light the frame shades from — the first enabled one the
scene lists, which is exactly the light `Program.UploadLights` picks with
`lights.FirstOrDefault(l => l.Type == LightType.Directional)`. A scene with two directional lights already shades from
the first alone, so it now shadows from the first alone too, and the second's `Shadows` setting is inert rather than
half-honoured.

Each caster is tested against the cascade's slice before it is drawn: its world-space AABB — `GameObject.GetWorldSpaceAABB`,
which already exists for selection — widened by its own half diagonal and compared against the min and max of the
cascade's eight world corners. It is a box-versus-box test over a conservative corner set, so it never skips something a
cascade could have seen; a caster smaller than the widening is drawn in every cascade, which is the direction a
conservative test has to fail in. What it buys is the difference between every caster in every cascade and roughly one
cascade's worth per caster, which for a 500-object scene is 2,000 draws a frame against about 520.

### 5.3 The two programs

Two shader files, for the same reason the scene has two: a shader assembled by `#define` substitution is a shader
nobody can read in the repository, and a static vertex must not pay for a bone fetch it never uses.

| File | Declares | Emits |
| :--- | :--- | :--- |
| `shadow.vert` | `aPosition` at location 0 | `gl_Position = lightViewProjection * model * vec4(aPosition, 1)` |
| `shadow_skinned.vert` | locations 0, 4, 5 | the same, after the palette's `SkinMatrix()` |
| `shadow.frag` | — | nothing; the depth buffer is the output |

`shadow_skinned.vert` contains, character for character, the `BoneMatrix` and `SkinMatrix` functions of `skin.vert`,
including `int texel = bone * 3`, the `mat4(column0, column1, column2, vec4(0.0, 0.0, 0.0, 1.0))` reconstruction and the
identity fallback for an unweighted vertex. They are copied rather than shared because GLSL 330 has no include, and
because the test in §9 asserts the two agree on the constants that tie the palette to the shader — a lookup shared by
`#include` would move that assertion from the shader source to a build step and weaken it.

`shadow.vert` must declare **no** bone attributes. That is asserted.

The static program takes exactly two uniforms, `model` and `lightViewProjection`; the skinned one takes those two and a
third, the palette sampler on the bone unit. Not `view`, not `projection`, not `uTiling`, not the lights: a depth pass
that uploads the frame's lighting uniforms is a pass someone forgot to trim.

`shadow.frag` is a `#version` line and an empty `main`, and it exists because `ShaderProgram` always compiles a vertex and a
fragment source pair — the depth buffer is written by the rasteriser before any fragment shader runs, so the fragment
stage has nothing to do and nothing to write. It is a file rather than a null argument because that is the shape
`ShaderProgram` already takes for every other program in the engine, and one empty shader is a smaller price than a
second constructor overload on it.

### 5.4 Depth-pass state

Rasteriser state is deliberately the opposite of the colour pass in the two places it matters:

- **Front-face culling.** Rendering back faces into the depth map moves every surface's recorded depth behind its
  front face, so a lit surface compares against a depth that is already behind it. This is the single change that buys
  the most freedom in §6.1's bias, and it is why the bias can be as small as `0.02`.
- **No MSAA, no blending.** Not applicable and not free.
- **A polygon offset only if front-face culling is not enough.** It is a bigger hammer than bias and it produces
  peter-panning at grazing angles, so it is the fallback rather than the first move.

`ShadowCastingMode.TwoSided` is exactly the mode that turns face culling off for a caster — a foliage card or a
double-sided wall has no back face to cull to.

## 6. The receiver

### 6.1 Bias: two forms, and what each one costs

A surface's own depth, sampled in its own depth map, is offset from the recorded depth by the interpolation between
texel centres. Exactly zero bias is acne; too much is peter-panning. Two biases, applied together, because they fail in
different places:

**Constant + slope-scaled depth bias**, applied in light space before projection:

```
ndotl = clamp(dot(normal, lightDirectionToLight), 0, 1)
bias  = shadowBias * tan(acos(ndotl))            // grows to infinity at the horizon
bias  = min(bias, shadowBias * maxSlope)         // clamped, or a grazing floor goes black
```

Unity's `shadowBias` is this constant, and the `tan(acos())` shape is what lets one number work on both a floor facing
the sun and a wall catching it edge-on. The clamp matters: without it, a floor seen at a grazing angle flickers between
lit and unlit along its horizon and looks like z-fighting.

**Normal offset**, applied to the receiver's world position:

```
offset = normal * shadowNormalBias * texelSizeWorld
```

moving the sample point off the surface before it is projected. This is the one that fixes acne on a curved surface
without darkening the shadow's contact point, because it moves *along* the normal rather than *along* the view ray. It
needs the receiver's world normal, which `scene.vert` and `skin.vert` already compute and pass as `Normal`; it must be
applied to the world position **before** the light matrix and not to the resulting depth, because offsetting a depth
value moves the sample in the light's direction rather than the surface's.

The two failure modes, and the honest trade:

| Symptom | Cause | Move |
| :--- | :--- | :--- |
| Striped acne over a lit surface | Bias below the depth quantisation | Raise `ShadowBias`; confirm front-face culling is on in the depth pass. |
| Shadow detached from the caster's feet | Bias too high, or the normal offset scaled by view depth | Lower `ShadowBias`, raise `ShadowNormalBias`. |
| Pinholes and light leaks at a silhouette | Resolution too low for the distance | Raise `ShadowResolution` or lower `Distance`; the two are one dial. |

### 6.2 PCF

`ShadowType.Soft` takes a 3×3 weighted tap set around the fragment's texel, with each tap's offset one texel — the
radius is `Shadows.PcfTexels`, a constant rather than a scene knob. A wider tent is a wider penumbra and also a
penumbra that overflows the map's edge by more than a texel, and no scene in this engine has asked for either yet; the
uniform exists so that it costs nothing to add. What is *not* fixed is the penumbra's width in world units: one texel
of a 512 map covers eight times the ground one texel of a 4096 map does, so a low resolution is both coarser and
softer, which is the honest behaviour rather than a bug to smooth over. Weight is the standard separable tent
(`1, 2, 1` per axis). `ShadowType.Hard` takes the single centre tap, and is the setting that makes a 4× MSAA frame look
crisper at the cost of stair-stepped edges.

Nine taps at 2048² × 4 cascades is the cost this buys; a 4-tap rotated poisson would be cheaper and is a deliberate
non-goal here, since the tent is what makes a shadow edge read as soft rather than noisy and the engine's scenes are
small.

The result is `visibility` in `0..1`, then blended by the per-light strength:

```
shadowFactor = mix(1.0, visibility, shadowStrength)
```

and **multiplied into the light's diffuse and specular terms only** — not the ambient term, not the emissive term, not
the skybox. `shadowStrength` interpolating toward `1.0` rather than toward `0.0` is what makes `0` mean "no shadow" and
`1` mean "black".

### 6.3 Choosing the cascade

The fragment shader picks by **view depth**, not by projection: `depth = dot(fragmentWorld - cameraPosition,
cameraForward)`, compared against the cascade's `nearDepth`/`farDepth`. View depth and not `gl_FragCoord.z`, because
clip-space depth is non-linear in a perspective projection and its comparison in a shadow map's linear orthographic
depth would be meaningless.

Below `ShadowNearPlane` and past `Distance`, no sample is taken and the factor is `1.0`.

In the last `BlendBand` of each cascade's range — 10% by default — the two adjacent cascades are both sampled and mixed
linearly, so the factor at the boundary is identical from either side. The outermost cascade has no successor and ends
in a hard cut to fully lit at `Distance`, which is why `Distance` should be chosen beyond where the eye is judging
contact. Unity's built-in pipeline switches hard and offers blending as an option; we blend unconditionally, because the
seam is unavoidable without it on the one surface every outdoor scene has — a long floor running away from the camera —
and because a visible seam is a worse artefact than one extra tap on a thin band.

### 6.4 Where it goes in the shader

`scene.frag` and `skin.frag` both gain the same receiver code, and the composite is untouched. In order, per fragment:

1. Compute `albedo`, `normal`, `viewDir` as today.
2. Compute the shadow factor **once**, from the directional light's direction, before the light loops.
3. `finalColor += CalcDirLight(...) * shadowFactor` — the directional term only.
4. Point and spot contributions unchanged; ambient and emissive unchanged.

The composite's exposure, Reinhard tonemap and explicit gamma 2.2 stay exactly where they are, **after** all of this,
in linear scene space. This is worth stating because [`color-consistency.md`](color-consistency.md) records how much
trouble a gamma mismatch between the editor viewport and the runtime caused; a shadow layer that sampled its depth map
through the same sRGB path as its colour would reintroduce it in a place that is much harder to see.

`MeshRenderer.ReceiveShadows = false` is a per-draw uniform that makes the receiver return `1.0` immediately, not a
second shader file. That is the one place a branch earns its keep, because the alternative is two more programs and the
loops around them.

### 6.5 The receiver's uniform contract

The two lit programs and the tests agree on a fixed set of names, and — as with `bone * 3` in the skinning layer — the
names are the contract, not an implementation detail. A rename on either side binds silently nothing, and the symptom
is a scene that is simply, uniformly lit:

| Uniform | Type | Meaning |
| :--- | :--- | :--- |
| `uShadowMap` | `sampler2DArray` | The cascades, one layer each. Owns a texture unit of its own — the third, after the material's diffuse and the bone palette. |
| `uShadowMatrices[4]` | `mat4[4]` | `lightViewProjection` per cascade. Fixed at four, as `uPointLights` and `uSpotLights` already are. |
| `uCascadeSplits` | `vec4` | The four `farDepth` values in view depth, `(10, 30, 70, 150)` at the defaults. |
| `uCascadeCount` | `int` | How many of the four are real this frame. |
| `uShadowDistance` | `float` | `Distance`. |
| `uShadowNearPlane` | `float` | The first cascade's **near range**, in camera view depth — the value below which §6.3 takes no sample. Not `CascadeFit.NearPlane`, which is a distance along the light's axis and is a different quantity in a different space. |
| `uShadowBias` / `uShadowNormalBias` | `float` | §6.1's two biases. |
| `uShadowStrength` | `float` | §6.2's blend toward `1.0`. |
| `uShadowSoftness` / `uShadowBlendBand` | `float` | The PCF tent's radius in texels and the cross-fade's fraction — `Shadows.PcfTexels` and `Shadows.BlendBand`, or `0` for a hard shadow. §6.2 says why they are constants rather than scene settings. |
| `uShadowTexelSize` | `float` | World units per texel, per cascade packed into a `vec4`, for the normal offset. |
| `uReceiveShadows` | `int` | `0` short-circuits the whole receiver. |
| `uCameraForward` | `vec3` | The view depth's axis (§6.3). Already derivable from the existing view matrix, but uploaded so the shader does not invert a matrix per fragment. |

A light whose `Shadows` is `None` never reaches the shader: `ShadowFrame.Build` returns `ShadowFrame.None`, the depth
pass draws nothing, and the receiver uploads `uReceiveShadows = 0`. That is a stronger version of the original intent
below it — no separate "shadows off" code path in the shader, only a uniform that short-circuits the receiver — and it
costs nothing: the pass that would have drawn four depth maps is skipped entirely rather than run and then ignored.

`uReceiveShadows = 0` is also what a per-renderer `ReceiveShadows = false` uploads, so a renderer that does not receive
in a scene that does is the same short circuit rather than a per-object shader variant.

## 7. Frame order and what it costs

`OpenTKRenderer.RenderScenePass` gains one step, and it goes first:

```
1. shadow pass, cascade 0..n-1  (depth only, no colour, no MSAA)
2. skybox                       (unchanged, no shadows)
3. static scene pass            (with receiver)
4. skinned scene pass           (with receiver)
5. grid, gizmos, colliders      (editor only, unchanged)
6. resolve MSAA
7. bloom
8. composite (exposure, tonemap, gamma)
```

The shadow pass is the one thing in the frame that runs **before** `_postProcessor.BeginFrame()` binds the scene's MSAA
target: it draws to its own depth-only framebuffers and must not touch that target, the resolve, or the bloom chain. The
skybox is *inside* `BeginFrame()` and stays there — it is scene content and it owns the depth buffer the scene is drawn
against. Getting this backwards renders four cascades into the scene's colour target and one very confused frame.

Cost, stated plainly: **the draw call count per object goes from one to `n + 1`** — four cascades plus the colour pass
at the default — and the same for a skinned object, whose vertices additionally sample the bone palette once per
cascade. §5.2's per-cascade slice test cuts that back to roughly one cascade's worth per caster, so a scene of 500
casters goes from 500 draws a frame to about 520 rather than 2,500. It is a box test, not a hierarchy, so the CPU side is
still one iteration per object per cascade; a tree is what removes that and is not what this builds.
than after it, and the reason `Distance` and `Cascades` are exposed on the scene's `Shadows` component: turning a scene
down to 2 cascades cuts the shadow pass in half immediately, with no code change.

The four depth passes do not pay for MSAA, do not resolve, do not bloom and do not run a fragment shader, so their cost
is the vertex and raster work and not the per-pixel cost of the colour pass. That is what makes `n + 1` draws affordable
here where `n + 1` full shading passes would not be.

## 8. Honest gaps — what this is not

- **One directional light casts.** Two directional lights already shade as one, and now cast as one (§5.2). Unity
  shadows every light that says it should; matching that is a texture array per light and a second cascade set, and it
  belongs with the point/spot work rather than before it.
- **Directional only.** Point and spot shadows are not here: a point light needs six cube faces with per-face fits, and
  a spot light needs a perspective fit and its own radial bias problem. Neither is a small edit to any of the above, so
  §1's model deliberately says "a directional light". The practical consequence today is that a scene lit mostly by a
  lamp in a corner still lights through walls; the directional light is the one that matters outdoors and the one whose
  fit is worth getting right first.
- **No occlusion culling, and no culling hierarchy.** §5.2 tests each caster's box against each cascade's slice, so an
  object is not drawn into a cascade that cannot see it — but the test is an iteration over a flat list, so it is
  `casters × cascades` box tests a frame. A BVH over the scene's objects is what removes that, and it is what a scene
  with tens of thousands of casters would need; §8's "occlusion" half of the phrase is untouched either way.
- **No alpha-tested casters.** `Vertex` carries no texture alpha and no `cutout` material flag, so a chain-link fence or
  a leaf card casts the shadow of its quad, not of its holes. This needs a material flag and an `alpha < cutoff` discard
  in the depth shader — small, but it is a change to `Material` and to the mesh pipeline's contract, not a local one.
- **Nothing is baked.** No lightmaps, no light probes, no static shadow masks. `ShadowStrength` below `1` is a
  stand-in for the ambient occlusion that real-time shadows alone do not provide, and it is a much blunter instrument.
- **No contact shadows, no screen-space ray marching.** A shadow's contact point is as tight as the cascade's texel size,
  which at 150 units over 4 cascades is coarse at the outer ring.
- **No cascade shadows for an orthographic camera.** `Camera` is perspective only, and the split formula in §3 assumes a
  perspective view. An orthographic camera would need an extent-based fit, which is a different §4.
- **The Veldrid backend renders no shadows.** It does not skin either (`animation.md` §8), and the editor runs OpenTK;
  a scene drawn through Veldrid lights as today, and the renderer logs a warning the first time it happens rather than
  silently differing.
- **A third texture unit, so a fragment shader can sample at most three things.** The material's diffuse is on unit 0,
  the bone palette on unit 1, and the cascades now on unit 2. That is fine for every material this engine has, and it is
  the constraint that will bite the first time a normal map or an emissive map is added — which is worth writing down
  before it is discovered as a driver limit.
- **The shaders are the part CI cannot run.** Split arithmetic, the light-space fit, texel snapping, bias and the
  cascade choice are all pure functions and all tested exactly. The draw itself needs a context, so a green run proves
  the matrices and the CPU/GPU contract, not the picture. That is a real limit of the increment, not a gap papered over.
- **A shadow is not a shadow without a receiver.** An object with `ReceiveShadows` off casts but is not darkened, and so
  does everything else if the frame never uploads §6.5's uniforms — which is the shape this bug takes in practice. This is exactly right and
  is still worth naming, because "my caster casts nothing" usually means the *floor* is the problem.

## 9. What the tests pin

Everything but the draw is deterministic, so the tests are exact rather than approximate — with two deliberate
exceptions, both float32 facts rather than choices: the fit's tolerances are `1e-3`-scale because a dot product hundreds
of texels from the origin loses more than that, and the slice-width test composes two rules (the near plane and the
staircase) so it is stated at a near plane of zero. A `…` below is an assertion this layer relies on and does not yet
make: mostly the receiver's arithmetic, which is GLSL and therefore outside anything CI can execute.

| Test | Pins |
| :--- | :--- |
| ✔ Four cascades split at 1/15, 3/15, 7/15 of the distance | §3.1's formula, against Unity's published `shadowCascade4Split` |
| ✔ Two cascades split at 1/3 | the same formula at `n = 2`, against `shadowCascade2Split` |
| ✔ Slice widths double: 1:2:4:8 | the property the split exists for |
| ✔ A split list out of order is clamped, not trusted | §3.2, and that a hand-edited file cannot divide by zero |
| ✔ A split list of the wrong length falls back to the derived default, and says it did | §3.2's recovery path |
| ✔ A scene with no `Shadows` component gets 150 / 4 / the derived splits | that every existing `.yaml` still loads |
| ✔ Two `Shadows` components resolve to the first on an active, enabled object | §2.3's "exactly one is consulted" |
| ✔ The light's box contains all eight slice corners, at every light angle and slice | §4.1 |
| ✔ A light pointing straight down — or up — does not produce a NaN matrix | the degenerate up vector, §4.1 |
| ✔ The light matrix points the shadow away from the sun, not into it | the negated direction convention |
| ✔ The extent and texel size are identical for any camera translation that leaves the lens alone | the property a min/max fit breaks, §4.2 |
| ✔ The snapped centre is an exact whole number of texels from the origin, on x and y | that the snap is a snap and not an approximation, §4.2 |
| ✔ The snapped x is a staircase: still, then exactly one texel, never a fraction | §4.2's third clause, and that nothing jitters at a fraction of a texel |
| ✔ The depth of the centre is not quantised | §4.2, the axis that is deliberately left alone |
| ✔ The fit's basis is the view matrix's own, and `axisX` is the negation of `cross(up, forward)` | the mirrored-basis bug §4.1 names |
| … A shadow framebuffer is refused, not rendered, when the status is incomplete | §5.1 |
| ✔ `shadow.vert` declares no `aBones` and no `uBoneTexture`; `shadow_skinned.vert` reads 4 and 5 | the two-program split, as `SkinningShaderTests` does for `scene.vert` / `skin.vert` |
| ✔ `shadow_skinned.vert` computes `bone * 3` and rebuilds `vec4(0.0, 0.0, 0.0, 1.0)` | that the palette contract of `skin.vert` is not forked |
| ✔ The receiver samples a `sampler2DArray` and never indexes an array of samplers | §5.1's GLSL 330 constraint |
| ✔ `scene.frag` and `skin.frag` declare the same receiver uniform names | §6.5, in both lit programs at once |
| ✔ The receiver multiplies the `CalcDirLight` term and no other | §6.4's order, asserted on the shader's own text |
| ✔ The depth shaders upload `model` and `lightViewProjection` and nothing else | that the pass was trimmed |
| … A more grazing normal biases more, up to the clamp | the slope term and its ceiling |
| … A normal offset moves the sample along the normal, not along the view | §6.1's two-bias split |
| … Cascade selection uses view depth, and switches at the split | §6.3 |
| … The last 10% of a cascade mixes both | the blend band, and that it lands exactly on the outer cascade |
| … `ShadowStrength = 0` leaves the surface fully lit; `1` fully dark | the `mix` direction, §6.2 |
| … The shadow factor multiplies the directional term and not ambient or emissive | §6.4's order |
| ✔ A `Skybox` object is in no cascade's caster list | the exclusion both colour passes already make |
| ✔ `CastShadows = Off` removes a caster; `ShadowsOnly` still draws in the depth pass and not in the colour pass | §5.2 and §2.2 |
| ✔ Every new serialized property survives a save and a load, and a default writes nothing | §2 and §2.3, through the real `SceneSerializer` and `SceneLoader` |
| ✔ `Cascades` rewrites `Splits`, and a load agrees with the instance that was saved | §2.3's round-trip property |
| ✔ `Build` returns `None` for no camera, no casting directional light, a light whose `Shadows` is `None`, and an empty scene | §5.2's "which light", and that a half-built frame is never returned |
| ✔ `Build` takes the first casting light, and its direction opposes `LightSpaceFit.DirectionToLight` | the same choice `UploadLights` makes, and the sign convention |
| ✔ An explicit resolution overrides the light's; with none, the light's `ShadowResolution` decides | that the per-light setting is not inert |
| ✔ A child of an inactive object casts nothing, and gets its shadow back when the parent re-activates | §5.2's `ActiveInHierarchy` rule |
| ✔ `TwoSided` culls nothing; every other caster culls front faces | §5.4 |
| ✔ `ShadowFrame.None` is a disabled frame, not null | that the Veldrid backend can be handed it |
| ✔ The fit's projection emits NDC depth in `[0, 1]`, so the buffer holds `[0.5, 1]` and the shader remaps | the convention mismatch that makes every shadow invisible, §4.1 |

## 10. Where the code lives

`✔` means it exists; no mark means it is an increment still to come, and `✔` inside the path column means a piece this
layer reuses rather than adds.

| Piece | File |
| :--- | :--- |
| ✔ Split arithmetic, cascade ranges, the split sanitiser | `src/Overcore/Rendering/Shadows/CascadeSplits.cs` |
| ✔ The slice's corners, the light-space fit, texel snapping | `src/Overcore/Rendering/Shadows/LightSpaceFit.cs` |
| ✔ The per-scene `Shadows` component, its defaults and its resolution table | `src/Overcore/Rendering/Shadows/Shadows.cs` |
| ✔ Which objects cast, and from which side of their faces | `src/Overcore/Rendering/Shadows/ShadowCasters.cs` |
| ✔ A frame's cascades, gathered once, with `None` as the disabled value | `src/Overcore/Rendering/Shadows/ShadowFrame.cs` |
| ✔ Per-light shadow properties | `src/Overcore/Rendering/Light.cs` |
| ✔ Per-renderer casting and receiving modes | `src/Overcore/Rendering/MeshRenderer.cs` |
| ✔ The layered depth texture and its per-layer framebuffer | `src/Overcore/Rendering/OpenTK/ShadowDepthTarget.cs` |
| ✔ The four depth passes, the caster cull and the pass's GL state | `src/Overcore/Rendering/OpenTK/OpenGLShadowRenderer.cs` |
| ✔ The receiver's uniforms and their upload | `src/Overcore/Rendering/OpenTK/OpenGLSceneRenderer.cs` |
| ✔ The component factory, and the mesh renderer's non-nested properties | `src/Overcore/Scenes/ComponentDeserializer.cs` |
| ✔ The mesh renderer's cast/receive properties in the scene file | `src/Overcore/Scenes/SceneSerializer.cs` |
| ✔ The bone texture the skinned caster reuses | ✔ `src/Overcore/Rendering/OpenTK/OpenGLBoneTexture.cs`, `OpenGLSkinnedMeshData.cs` |
| ✔ The vertex layout both passes share | ✔ `src/Overcore/Rendering/Vertex.cs` |
| ✔ The frame order the pass slots into | ✔ `src/Overcore/Rendering/OpenTK/OpenTKRenderer.cs` |
| ✔ The depth shaders | `src/OvercoreEditor/EditorResources/Shaders/shadow.vert`, `shadow_skinned.vert`, `shadow.frag` |
| ✔ The receiver in the lit shaders | `src/OvercoreEditor/EditorResources/Shaders/scene.frag`, `skin.frag` |
| ✔ The CPU-side tests: splits, the fit, the caster rules, the frame, the round-trips | `tests/Overcore.Tests/Rendering/{CascadeSplits,LightSpaceFit,Shadows,ShadowCasters,ShadowFrame,ShadowShader}Tests.cs`, `tests/Overcore.Tests/Scenes/ShadowSerializationTests.cs` |
| Inspector rows for the new properties | `src/OvercoreEditor/Inspector/`, `src/OvercoreEditorSession/Scene/ComponentProperties.cs` |
| ✔ A `ComponentCatalog.BuiltIn` entry, so either front end can add a `Shadows` component | `src/Overcore/Components/ComponentCatalog.cs` |

Three notes on what is deliberately *not* in that table. There is no `FramebufferBuilder` change: the builder's
`Build()` returns a colour target with an optional depth **renderbuffer**, which cannot be sampled, and the shadow target
wants the opposite shape — a samples-able depth texture with no colour at all. Bending one builder to produce both would
put a `layers` parameter on a method that has no use for one in nine of its ten calls, so `ShadowDepthTarget` owns its
own object. There is no `ShadowSoftness`/`BlendBand` on `Shadows` either: the receiver needs a constant, and a scene knob
that only the receiver reads is a knob with no second reader — §6.2 says what it costs to add one later. And the
inspector rows are genuinely not done. A `Shadows` component can be added from either front end and its values are
saved and loaded, but tuning it means editing the file or using the generic property rows, and the enums and `[Range]`
floats are only as good as whatever row picks them up. The one test this touches is
`EditorCommandTests.Catalog_ListsCreatableComponentsWithUniqueNames`, which instantiates every catalog entry — so it now
also asserts that `new Shadows()` is constructible and is the type the menu names.

The Veldrid backend needs nothing: `IRenderer.RenderFrame`'s signature is unchanged, and its `Render` takes no shadow
frame because it has no depth pass to give one to.
