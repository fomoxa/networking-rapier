# Fomoxa Networking Rapier

`com.fomoxa.networking.rapier` is a physics backend for [`com.fomoxa.networking`](https://github.com/fomoxa/com.fomoxa.networking) built on [Rapier](https://rapier.rs). Network objects are simulated in Rapier worlds instead of Unity's PhysX. Given the same inputs, a Unity client and a server that runs without Unity compute the same physics state, on Windows or Linux.

| | |
|---|---|
| Version | 0.1.0 |
| Requires | `com.fomoxa.networking` 0.1.0 |
| Unity | 6000.5 or later |
| Platforms | Windows x64, Linux x64 (Editor, players, console servers) |
| Rapier | `rapier3d` and `rapier2d` 0.36.0 with `enhanced-determinism` |
| License | Apache-2.0 (Rapier and the other linked crates: see `Third Party Notices.md`) |

## Why Rapier

The Rigidbody backend of `com.fomoxa.networking` drives Unity's PhysX. PhysX cannot restore its contact state, so a replayed tick only approximates the original one, and a server without Unity cannot run PhysX at all.

This package keeps one Rapier world per network scene and per dimension. A snapshot holds the whole world, contacts included, so a replay after `Load` reproduces the original ticks bit for bit. Rapier is pinned to one version and built with `enhanced-determinism`, and the same steps give the same state on Windows and Linux, under Mono and under .NET. A console server and a Unity client build their worlds from the same scene file and the same body descriptions, so both run one simulation.

## Installation

Add both packages from git in Window > Package Manager > + > Install package from git URL:

```
https://github.com/fomoxa/com.fomoxa.networking.git
https://github.com/fomoxa/networking-rapier.git?path=com.fomoxa.networking.rapier
```

A Unity package cannot declare a dependency on a git package, so `com.fomoxa.networking` is not listed in this package's dependencies. The package checks the Core version when it creates its worlds and throws if the project has a different one.

## Using it in Unity

1. Add `RapierPhysics` to the GameObject of your `NetworkManager` and assign it to the manager's Physics field. Set its Gravity if you need something other than (0, -9.81, 0).
2. Do the same for the server and every client. A connection needs the same physics backend on both sides; a client whose backend differs from the server's is stopped with `StopReason.PhysicsBackendMismatch`.
3. Export the network scene files. The Fomoxa Editor step writes them when you save a network scene, and on Tools > Fomoxa > Export Network Scene Files. Rapier reads the static geometry of a scene from its file, so a scene without a file has no static colliders in Rapier and logs a warning.

`NetworkObject.Body` and `NetworkObject.Body2D` then return bodies in the Rapier world of the object's scene, built from the object's colliders and its `Rigidbody` or `Rigidbody2D`. After every tick Rapier writes each body's pose back to the object's `Transform`. The `Rigidbody` is switched to kinematic, so PhysX only does local cosmetic work.

Supported colliders:

| 3D | 2D |
|---|---|
| Box, Sphere, Capsule | Box, Circle, Capsule |
| Convex `MeshCollider` (any body) | `PolygonCollider2D` (concave ones are split into convex pieces on moving bodies) |
| Non-convex `MeshCollider`, `TerrainCollider` (static geometry only) | `EdgeCollider2D` (static geometry only) |

Meshes used by a body that is created at runtime must have Read/Write enabled. Friction, bounciness and their combine modes come from the physics material. Layers and the layer collision matrix of the project apply as in Unity. Trigger colliders become Rapier sensors.

## Contact events

`NetworkTrigger`, `NetworkCollision`, `NetworkTrigger2D` and `NetworkCollision2D` keep their API. `RapierPhysics` takes over these components on the objects it simulates and on the static objects of the scenes it loads, and they then report the contact and intersection pairs of the Rapier world instead of PhysX callbacks. Rollback restores the pairs together with the bodies.

Differences from the Rigidbody backend:

- `AdditionalSize` has no effect. Contacts are the pairs Rapier computes, within its contact prediction distance. Resting contacts are kept, so an object lying on the ground still touches it. To detect objects that are near but not touching, add a larger trigger collider.
- The edge of a contact can differ by a tick from PhysX, because the two engines compute contacts differently.
- Components added to static objects after their scene has loaded are not taken over.

## Console servers

The assembly `Fomoxa.Networking.Rapier` does not reference `UnityEngine`. A console server compiles it together with the Core and the console backend of `com.fomoxa.networking`, copies the native library next to its executable, and passes a `RapierScenes` to `StandaloneRuntime.Create`:

```csharp
var settings = new NetworkSettings { PhysicsBackend = PhysicsBackend.Rapier };
var physics = new RapierScenes(new Vector3(0f, -9.81f, 0f));
NetworkRuntime server = StandaloneRuntime.Create(registry, settings, new UdpTransportFactory(), prefabs, behaviours, new SceneFileDirectory("Scenes"), clock, log, physics);
```

The console backend loads each scene file into its own Rapier world and creates the bodies of entities from `StandaloneEntity.TryGetBody` and `TryGetBody2D`.

## Keeping the simulation deterministic

Rapier gives the same result for the same world and the same operations. Game code has to keep the operations the same:

- Apply forces, impulses and state changes in prediction callbacks, in a fixed order, from inputs and the tick number.
- Avoid `Mathf.Sin`, `MathF.Cos` and other library math whose last bit can differ between runtimes in values that reach the simulation. Use tables, integer arithmetic or values from the input.
- Do not read time, random generators seeded from the clock, or frame-dependent values in simulation code.

## Limitations

- Windows x64 and Linux x64 only. macOS, Android, iOS and WebGL clients cannot use this backend yet.
- IL2CPP players have not been checked for determinism against Mono and .NET.
- Joints, effectors (`PlatformEffector2D` and the others), continuous collision settings and contact points are not supported.
- One body per network object.
