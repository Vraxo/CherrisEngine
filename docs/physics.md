# Physics & Runtime Lifecycle

The physics layer is BepuPhysics v2 behind a Unity-shaped API. A `Collider` is a component in its own right; a
`RigidBody` is a script that gives a shape weight and velocity. Scripts hear about contacts through
`OnCollisionEnter/Stay/Exit` and `OnTriggerEnter/Stay/Exit`, ask the world questions through the static `Physics`
class, and drive themselves with `Update`, `FixedUpdate`, `LateUpdate`, coroutines and `Invoke`.

Everything below runs headless: no window, no shader and no asset pipeline is involved, which is what makes the
physics and lifecycle tests in `tests/Overcore.Tests/World` and `tests/Overcore.Tests/Lifecycle` cheap to run.

## 1. Colliders

| Type | Settings | Notes |
| :--- | :--- | :--- |
| `BoxCollider` | `Size` (full extents), `Center` | `Size` is the whole box, as in Unity, not half of it. |
| `SphereCollider` | `Radius`, `Center` | Radius is local; the object's scale multiplies it. |
| `CapsuleCollider` | `Radius`, `Height`, `Center` | `Height` includes both rounded ends, as in Unity. The capsule stands along the object's Y axis. |

Every collider also has:

- `IsTrigger` — a volume to be reported rather than a surface to be stood on. A trigger never blocks and never
  produces contacts.
- `Friction` — `0` is ice, `1` is rubber. Two surfaces combine as the geometric mean, so the slippier one has the
  larger say.
- `Bounciness` — `0` is clay, `1` is a bouncy ball. A pair takes the livelier of the two.
- `Center` — the shape's offset from the object's origin, in local space.
- `Bounds` / `WorldCenter` / `ContainsPoint(point)` — what the shape covers in world space.

A collider on an object with no `RigidBody` is a fixed shape: level geometry, a wall, a floor. That is what makes
`BoxCollider` useful on its own, without any body to simulate.

```csharp
var floor = new Overcore.Core.GameObject("Floor");
floor.AddComponent(new BoxCollider { Size = new Vector3(20f, 1f, 20f), Friction = 0.8f });

var ball = new Overcore.Core.GameObject("Ball");
ball.Transform.WorldPosition = new Vector3(0f, 3f, 0f);
ball.AddComponent(new SphereCollider { Radius = 0.5f, Bounciness = 0.9f });
ball.AddComponent(new RigidBody { Mass = 1f });
```

### What is registered, and when

A collider joins the physics world when it wakes or is switched on (`Awake`/`OnEnable`) and leaves when it is
switched off or destroyed. Changing `Size`, `Radius`, `Height`, `Center` or `IsTrigger` rebuilds the shape without
losing its place in the world.

Without a `RigidBody`, the collider becomes a static collider in Bepu. Bepu never looks at the transform again once
it has the pose, so `PhysicsSystem` keeps the two in agreement: at the start of every fixed step it compares each
fixed shape's pose with its object's transform and moves the ones that differ. A floor slid into place before play,
a moving platform, or a wall pulled out of the way therefore collides where it is drawn.

Two fixed shapes are never tested against each other — that is what keeps a scene full of scenery cheap. Contacts,
and therefore collision callbacks, need at least one moving body in the pair.

## 2. Character controller

`CharacterController` is a kinematic, upright capsule for player and NPC locomotion. It does not add a `RigidBody`,
receive forces, or run itself: scripts decide when and how far it moves. `Move(displacement)` takes a world-space
translation and returns `CollisionFlags` (`Sides`, `Above`, `Below`); it does not apply gravity. `SimpleMove(speed)`
takes world-space speed, applies gravity, ignores the supplied Y speed, and returns whether the last move was grounded.
The component exposes `Height`, `Radius`, `Center`, `SlopeLimit`, `StepOffset`, `SkinWidth`, `MinMoveDistance`,
`DetectCollisions`, `EnableOverlapRecovery`, `IsGrounded`, `Velocity`, and the last `CollisionFlags`. `DetectCollisions`
controls whether incoming rigid bodies and other controllers are blocked by this controller; it does not disable this
controller's own `Move` collision checks. `IsGrounded` reflects below-facing contact, while the slope limit separately
determines whether that surface can support the controller for walking and platform carry. `Velocity` reflects the
controller's displacement during the most recent move, excluding carried platform displacement.

```csharp
public class PlayerMotion : Script
{
    public float Speed { get; set; } = 5f;

    public override void Update(float deltaTime)
    {
        CharacterController? controller = GameObject?.GetComponent<CharacterController>();
        if (controller is null) return;
        float x = (Input.IsKeyDown(Key.D) ? 1f : 0f) - (Input.IsKeyDown(Key.A) ? 1f : 0f);
        float z = (Input.IsKeyDown(Key.W) ? 1f : 0f) - (Input.IsKeyDown(Key.S) ? 1f : 0f);
        controller.SimpleMove(new Vector3(x, 0f, z) * Speed);
    }

    public override void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Log($"Controller hit {hit.GameObject.Name} at {hit.Point}");
    }
}
```

Capsule sweeps use conservative advancement against the geometric separation of the capsule and each collider; the
box distance minimization is numerical, so contact tolerances can still differ from PhysX. Movement slides against
contact planes, attempts a step-up only while supported, and resolves initial penetrations against static, non-trigger
geometry by pushing along the deepest contact normal with a per-move recovery limit. Moving platforms carry a grounded
controller by the platform's translation and rotation. This is not a claim of bit-for-bit PhysX parity: movement does not automatically impart velocity to rigid bodies,
though their solver contacts are blocked by the controller's static capsule when `DetectCollisions` is enabled and
scripts can use the hit callback to apply forces. Trigger overlaps are checked at the end of each
`Move`, and capsule sweeps along the controller's resolved movement segments detect triggers crossed entirely within
one move. Enter/Stay/Exit are delivered to scripts on both objects;
The controller is also a `Collider`, so ordinary collider queries and trigger callbacks receive it as a collider.
Controller capsules block one another when both have collision detection enabled; their movement contacts are handled
by the controller sweeps rather than the rigid-body contact solver, and are exposed through
`ControllerColliderHit.OtherController`. The controller itself does not receive fixed-step `OnCollision*` callbacks;
its movement contacts are delivered synchronously from `Move` through `OnControllerColliderHit`. Test
coverage exists in source, but the .NET test suite has not been run in this environment.

## 3. Rigid bodies

`RigidBody` takes its shape from the `Collider` on the same object, falling back to the object's mesh bounds when
there is none.

| Member | Meaning |
| :--- | :--- |
| `Mass` | Kilograms. `IsStatic` or `IsKinematic` bodies ignore it. |
| `IsStatic` | Never moves and never integrates: a wall that happens to need a body reference. |
| `IsKinematic` | Moved by code only; it pushes other bodies and is never pushed back. |
| `UseGravity` | When false the body's weight is cancelled each step instead of being integrated. |
| `Velocity` / `AngularVelocity` | Read/write, in world space. |
| `AddForce` / `AddForceAtPosition` / `AddTorque` | Queued and spent at the next fixed step, so the number of frames per second does not change the result. |
| `AddForce(..., ForceMode.Impulse)` etc. | The same calls with an instant change instead of a continuous one. |
| `SetVelocity` / `GetVelocity` | Direct access to the integrated velocity. |
| `Teleport(position, rotation?)` | Moves the body without letting physics push back; `MovePosition`/`MoveRotation` are the same call. |
| `Rebuild()` | Rebuilds the body after a shape change (the collider does this for you). |

A kinematic body follows its transform. A dynamic body writes its pose back to the transform after every step, so
reading `Transform.WorldPosition` in `Update` gives the position the simulation settled on.

## 4. Contacts and triggers

```csharp
public class Ball : Script
{
    public override void OnCollisionEnter(Collision collision)
    {
        Log($"hit {collision.GameObject?.Name} at {collision.ContactCount} point(s)");
        Vector3 normal = collision.Contacts[0].Normal;   // points away from the other surface
    }

    public override void OnTriggerEnter(Collider other) => Log($"entered {other}");

    public override void OnCollisionExit(Collision collision) => Log($"left {collision.GameObject?.Name}");
}
```

`Collision` carries:

- `Collider` — the other collider (never the one the script is on).
- `GameObject` / `Transform` — the other object, for convenience.
- `Contacts` / `ContactCount` — up to four `ContactPoint`s: `Point`, `Normal`, `Separation`,
  `ThisCollider`, `OtherCollider`.
- `RelativeVelocity` — the closing velocity of the pair.

Both objects involved hear about the contact, each with the normal and the colliders the other way round. Enter is
reported once when a pair starts touching, stay once per step while it lasts, and exit once when it comes apart.

Triggers are tested every step against every other collider (they have no shape in Bepu, so nothing else can test
them). Unlike Unity, an overlap is reported even when neither object is a rigid body: a player trigger with no body
still hears about a passing crate. A trigger reports `OnTriggerExit` and never `OnCollisionExit`, and nothing it
touches is blocked.

### Layers

`Physics.IgnoreLayerCollision(a, b, true)` stops a pair of layers from ever touching; `GetIgnoreLayerCollision` and
`ResetLayers` complete the set. Layers are the object's `GameObject.Layer`; `Layers` names them and
`LayerMask.GetMask("Ground", "Water")` builds a mask for queries.

## 5. Queries

```csharp
if (Physics.Raycast(origin, direction, out RaycastHit hit, 100f, LayerMask.GetMask("Ground")))
{
    Log($"{hit.GameObject?.Name} {hit.Distance:0.0}m away, normal {hit.Normal}");
}
```

| Query | Answers |
| :--- | :--- |
| `Raycast` | The nearest shape along a ray. |
| `RaycastAll` | Every shape along a ray, nearest first. |
| `Linecast` | A ray with a fixed end. |
| `SphereCast` / `SphereCastAll` | A sphere swept along a direction; the hit is where the sphere's surface touches. |
| `OverlapSphere` / `OverlapBox` | The colliders inside a volume. |
| `CheckSphere` / `CheckBox` | Whether anything is inside a volume. |

Every query reads the colliders' live transforms, so it always agrees with what is on screen, including fixed
shapes that have just been moved. They take a layer mask and a `QueryTriggerInteraction`:

- `UseGlobal` — the default; `Physics.QueriesHitTriggers` decides (it is true, as in Unity).
- `Ignore` / `Collide` — per-call override.

`RaycastHit` reports `Point`, `Normal`, `Distance`, `Origin`, `Collider`, and the `GameObject`/`Transform` behind
that collider.

`Physics.Gravity` (default `(0, -9.81, 0)`) and `Physics.FixedDeltaTime` (default `1/60`) are settable and apply to
the whole world.

Bepu 2.4 has no general overlap query, so the volume queries test each collider's shape directly. That is exact and
plenty fast for the scene sizes this engine targets, and it is why the queries do not need the simulation to be
running.

## 6. Script lifecycle

For each script, in this order:

1. `Awake` — once, when the object first appears (scene start, `Instantiate`, `AddComponent` on a live object).
2. `OnEnable` — every time the script becomes active: after `Awake`, and again after each `SetActive(true)` or
   `Enabled = true`.
3. `Start` — once, before the first frame the script ticks.
4. `FixedUpdate` — once per physics step, before the step integrates.
5. `Update(deltaTime)` — once per frame; `deltaTime` is scaled by `Time.TimeScale`.
6. `LateUpdate` — after every `Update` in the scene.
7. `OnDisable` — when switched off or destroyed while active.
8. `OnDestroy` — once, when the script goes away.

A script that is switched off ticks nothing: no `Update`, no `FixedUpdate`, no coroutines, no collision callbacks.
A component on a switched-off object is not active in the hierarchy either, and neither is anything under it.

A script that throws is logged and suspended (`Script.IsSuspended`) rather than throwing every frame.

## 7. Time, coroutines and `Invoke`

`Time` is advanced by the frame loop, once per frame, so everything downstream moves at the same speed:

| Member | Meaning |
| :--- | :--- |
| `DeltaTime` | Scaled seconds since the last frame. |
| `UnscaledDeltaTime` | The same, ignoring `TimeScale`. |
| `TimeScale` | `0` freezes the world, `2` runs it at double speed. |
| `FixedDeltaTime` | The physics step, `1/60` by default. |
| `FrameCount` | Frames since the loop started. |
| `ElapsedTime` | Scaled time since the loop started. |

```csharp
public override void Start()
{
    StartCoroutine(Fade());
    Invoke(nameof(Shout), 2f);          // once, after two seconds
    InvokeRepeating(nameof(Tick), 0f, 1f);
}

private IEnumerator Fade()
{
    yield return new WaitForSeconds(0.5f);
    yield return new WaitForFixedUpdate();
    yield return new WaitUntil(() => Input.WasKeyPressed(Key.Space));
    yield return null;                   // next frame
}
```

Supported yields: `WaitForSeconds`, `WaitForFixedUpdate`, `WaitForEndOfFrame`, `WaitUntil`, `WaitWhile`, `null`
(next frame), a nested `IEnumerator` (entered right away, the outer routine carries on in the frame it finishes),
and another `Coroutine`. `StopCoroutine(routine)`, `StopAllCoroutines()` and `StopCoroutine(IEnumerator)` stop them;
a stopped routine never runs again.

Coroutines count time on the scene's own clock, which is scaled by `Time.TimeScale` and reset when the scene starts
or restarts, so a paused game pauses its waits too.

## 8. Instantiating, destroying and changing scenes

```csharp
GameObject clone = GameObject.Instantiate(prefab, position, rotation);
GameObject.Destroy(clone);              // goes at the end of the frame
GameObject.Destroy(clone, 1.5f);        // ... or after a delay
GameObject.DestroyImmediate(clone);     // now, for editor code
```

`Instantiate` copies the object, everything under it, and every component on it: fresh ids, fresh components, the
same prefab link and the same shared assets. The clone joins the scene the original is in and is woken immediately.
Script fields are copied by value — a list field is shared with the original until the clone replaces it.

`Destroy` never pulls the rug out from under a running frame: the object stays alive (and marked
`GameObject.IsDestroyed`) until the end of the frame, when `OnDisable` and `OnDestroy` run and it leaves the scene.

`GameObject.Find`, `FindWithTag` and `FindGameObjectsWithTag` search the active scene. `GetComponentInChildren` and
`GetComponentInParent` walk the hierarchy; `Transform.SetParent(parent, worldPositionStays: true)` reparents while
keeping the object where it appears, and `Transform.Translate`/`Rotate`/`LookAt` move it.

`SceneManager` runs scenes and changes them:

```csharp
using SceneManager scenes = new();
scenes.OpenScene(new Scene("Level", [player]));
scenes.Start();

scenes.LoadScene("Assets/Scenes/Arena.scene");   // closes the running scene, keeps what was marked
scenes.DontDestroyOnLoad(player);                // player survives the change
scenes.UnloadScene("Arena");                     // closes it and destroys what is in it
```

`DontDestroyOnLoad` objects are lifted out of the old scene before it is unloaded and put into the new one after it
starts. `OnActiveSceneChanged` announces every switch, and `Scene.Active` always names the scene that is running.

## 9. Limits worth knowing

- No continuous collision detection: a fast, small body can tunnel through a thin wall in one step. Use thicker
  colliders, or sweep with `Physics.SphereCast` yourself.
- No joints, no character controller, no mesh (concave) colliders. Convex shapes only, in the three forms above.
- A moving fixed shape is refreshed at the start of each step by comparing poses; thousands of them are cheap but
  not free.
- A trigger reports everything overlapping it, including colliders with no body — a deviation from Unity that
  makes triggers useful without adding a body to every visitor.
- Physics runs at a fixed 60 Hz. `FixedUpdate` is where forces belong; `Update` is where input and presentation
  belong.
