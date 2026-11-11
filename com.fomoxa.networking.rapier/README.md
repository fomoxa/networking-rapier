# Fomoxa Networking Rapier

`com.fomoxa.networking.rapier` is a physics backend for [`com.fomoxa.networking`](https://github.com/fomoxa/com.fomoxa.networking) built on [Rapier](https://rapier.rs). Network objects are simulated in Rapier worlds instead of Unity's PhysX and Box2D. Given the same inputs, a Unity client and a server that runs without Unity compute the same physics state.

| | |
|---|---|
| Version | 0.2.3 |
| Requires | `com.fomoxa.networking` 0.3.3 |
| Unity | 6000.5 or later |
| Platforms | Windows x64 and arm64, Linux x64 and arm64 with glibc 2.34 or newer, macOS, Android `arm64-v8a` and `armeabi-v7a`, iOS, Web |
| Rapier | `rapier3d` and `rapier2d` 0.36.0 with `enhanced-determinism` |
| License | Apache-2.0 (Rapier and the other linked crates: see `Third Party Notices.md`) |

The documentation is in [fomoxa/unity-docs](https://github.com/fomoxa/unity-docs/blob/main/rapier/overview.md).

## Why Rapier

The Rigidbody backend of `com.fomoxa.networking` drives Unity's PhysX and Box2D. They cannot restore their contact state, so a replayed tick only approximates the original one, and a server without Unity cannot run them at all.

This package keeps one Rapier world per network scene and per dimension. A snapshot holds the whole world, contacts and joints included, so a replay after `Load` reproduces the original ticks bit for bit. Rapier is pinned to one version and built with `enhanced-determinism`, and the same steps give the same state on every checked platform, under Mono, IL2CPP and .NET. A console server and a Unity client build their worlds from the same scene file and the same body descriptions, so both run one simulation.

## Installation

Add both packages from git in Window > Package Manager > + > Install package from git URL:

```
https://github.com/fomoxa/com.fomoxa.networking.git#0.3.3
https://github.com/fomoxa/networking-rapier.git?path=com.fomoxa.networking.rapier#0.2.3
```

A Unity package cannot declare a dependency on a git package, so `com.fomoxa.networking` is not listed in this package's dependencies. The package checks the Core version when it creates its worlds and throws if the project has a different one.

## Using it in Unity

1. Add `RapierPhysics` to the GameObject of your `NetworkManager` and assign it to the manager's Physics field. Set its Gravity if you need something other than (0, -9.81, 0).
2. Do the same for the server and every client. A connection needs the same physics backend on both sides; a client whose backend differs from the server's is stopped with `StopReason.PhysicsBackendMismatch`.
3. Export the network scene files. The Fomoxa Editor step writes them when you save a network scene, and on Tools > Fomoxa > Export Network Scene Files. Rapier reads the static geometry of a scene from its file, so a scene without a file has no static colliders in Rapier and logs a warning.

`NetworkObject.Body` and `NetworkObject.Body2D` then return bodies in the Rapier world of the object's scene, built from the object's colliders and its `Rigidbody` or `Rigidbody2D`. `NetworkObject.Bodies` and `Bodies2D` also list the bodies on child GameObjects, and the joint components between them become Rapier joints. A `Rigidbody2D` with Simulated turned off when the object is bound is not a body. After every tick Rapier writes each body's pose back to the `Transform` of its GameObject. The `Rigidbody` is switched to kinematic, so PhysX only does local cosmetic work.

Supported colliders:

| 3D | 2D |
|---|---|
| Box, Sphere, Capsule | Box, Circle, Capsule |
| Convex `MeshCollider` (any body) | `PolygonCollider2D` (concave ones are split into convex pieces on moving bodies) |
| Non-convex `MeshCollider`, `TerrainCollider` (static geometry only) | `EdgeCollider2D` (static geometry only) |

Meshes used by a body that is created at runtime must have Read/Write enabled. Friction, bounciness and their combine modes come from the physics material. Layers and the layer collision matrix of the project apply as in Unity. Trigger colliders become Rapier sensors.

The Rapier worlds also support:

| Feature | Documentation |
|---|---|
| Rays, shape casts, overlaps and contact points | [Physics queries](https://github.com/fomoxa/unity-docs/blob/main/physics/queries.md) |
| Forces, torques and continuous collision | [PhysicsBody](https://github.com/fomoxa/unity-docs/blob/main/physics/physics-body.md) |
| Unity's joint components, and joints created from code | [Joints](https://github.com/fomoxa/unity-docs/blob/main/rapier/joints.md) |
| `MoveCharacter`, a character controller for kinematic bodies | [Character controller](https://github.com/fomoxa/unity-docs/blob/main/rapier/character-controller.md) |
| `PlatformEffector2D`, `SurfaceEffector2D`, `AreaEffector2D`, `BuoyancyEffector2D`, `PointEffector2D` | [Effectors](https://github.com/fomoxa/unity-docs/blob/main/rapier/effectors.md) |

## Contact events

`NetworkTrigger`, `NetworkCollision`, `NetworkTrigger2D` and `NetworkCollision2D` keep their API. `RapierPhysics` takes over these components on the objects it simulates, on the static objects of the scenes it loads and on the GameObjects passed to `AddStatic(GameObject)` or `AddStatic2D(GameObject)`, and they then report the contact and intersection pairs of the Rapier world instead of Unity's callbacks. `GetContactPoints` returns the points of those pairs, in 3D as well as in 2D. Rollback restores the pairs together with the bodies.

Differences from the Rigidbody backend:

- `AdditionalSize` has no effect. Contacts are the pairs Rapier computes, within its contact prediction distance. Resting contacts are kept, so an object lying on the ground still touches it. To detect objects that are near but not touching, add a larger trigger collider.
- The edge of a contact can differ by a tick from PhysX, because the two engines compute contacts differently.
- Components added to static objects after their scene has loaded are not taken over.

## Console servers

The assembly `Fomoxa.Networking.Rapier` does not reference `UnityEngine`. A console server compiles it together with the Core and the console backend of `com.fomoxa.networking`, copies the native library next to its executable, and passes a `RapierScenes` to `StandaloneRuntime.Create`:

```csharp
var physics = new RapierScenes(new Vector3(0f, -9.81f, 0f));
var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
NetworkRuntime runtime = StandaloneRuntime.Create(
    registry, settings, new UdpTransportFactory(), prefabs, behaviours,
    new SceneFileDirectory(scenesFolder), () => clock.Elapsed, NetworkLog.Console,
    physics, new[] { FomoxaScenes.GameId });
```

The console backend loads each scene file into its own Rapier worlds and creates the bodies of entities from `StandaloneEntity.TryGetBody`, `TryGetBody2D`, `GetChildBodies` and `GetChildBodies2D`. The bodies of scene objects, child bodies included, come from the scene file.

## Keeping the simulation deterministic

Rapier gives the same result for the same world and the same operations. Game code has to keep the operations the same:

- Apply forces, impulses and state changes in prediction callbacks, in a fixed order, from inputs and the tick number.
- Avoid `Mathf.Sin`, `MathF.Cos` and other library math whose last bit can differ between runtimes in values that reach the simulation. Use tables, integer arithmetic or values from the input.
- Do not read time, random generators seeded from the clock, or frame-dependent values in simulation code.

## Limitations

- `CharacterController`, `WheelCollider`, `ConfigurableJoint`, `WheelJoint2D`, `RelativeJoint2D`, `TargetJoint2D` and `FrictionJoint2D` are not supported, and 3D has no effectors because Unity has no 3D effector components.
- Joints do not break.
- Android x86-64 has no library.
- The Web library does not work with Enable C/C++ Native Multithreading.
- Players on Linux, macOS and iOS have not been run with the hash check that the Windows, Web and Android players pass.
