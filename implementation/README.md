# Rapier backend implementation

This folder holds the implementation notes of the steps done in the `networking-rapier` repository. The design is in the `unity` repository, in `design/principles.md` P27 and `design/open-questions.md`. The plan and the contract of each step are in `implementation/` of the `unity` repository: the 8b section of `08-prediction-physics.md` for steps 08.13 to 08.17, and the step files of stages 11 and 12, which hold the full listings for this repository.

The order of authority is the same as in the `unity` repository: `design/` of the `unity` repository, then its `implementation/`, then the files in this folder, then the code.

## Status

| Step | Work | File | Status |
|---|---|---|:---:|
| 08.13 | 3D crate, tier 1 Rapier world (`IPhysicsWorld`), per-body `Load`, state hash, snapshot cost | [`08-13-tier1-rapier-3d.md`](08-13-tier1-rapier-3d.md) | ✅ |
| 08.14 | 2D (`rapier2d`, `IPhysicsWorld2D`) | [`08-14-tier1-rapier-2d.md`](08-14-tier1-rapier-2d.md) | ✅ |
| 08.15 | `RapierScenes` (`IPhysicsScenes`), contact sets of a world | [`08-15-tier1-rapier-scenes.md`](08-15-tier1-rapier-scenes.md) | ✅ |
| 08.15 | Unity part: `RapierPhysics`, contact source, Unity test project | [`08-15-tier2-rapier-unity.md`](08-15-tier2-rapier-unity.md) | ✅ |
| 08.16 | Two-sided check: Rapier console server with a Rapier Unity client over UDP | [`08-16-two-sided-check.md`](08-16-two-sided-check.md) | ✅ |
| 08.17 | Static groups added and removed at runtime, unowned bodies, body motion (crate, `RapierWorld*`, `RapierScenes`) | [`08-17-tier1-rapier-statics.md`](08-17-tier1-rapier-statics.md) | ✅ |
| 08.17 | Unity part: the new members of `RapierPhysics`, contact components of static groups | [`08-17-tier2-rapier-unity.md`](08-17-tier2-rapier-unity.md) | ✅ |
| 11.10 | Moved bodies: the crate, `RapierWorld*`, `RapierScenes` implement the Core's `IPhysicsMotion` (step 11.10 of the `unity` repository) | [`11-10-tier1-rapier-motion.md`](11-10-tier1-rapier-motion.md) | ✅ |
| 11.10 | Unity part: `RapierPhysics` forwards `IPhysicsMotion` | [`11-10-tier2-rapier-unity.md`](11-10-tier2-rapier-unity.md) | ✅ |
| 11.29 | Release 0.1.1 with `com.fomoxa.networking` 0.2.0 | [`11-29-release.md`](11-29-release.md) | ✅ |
| 12.1 | Libraries for every platform, built in CI, golden hashes | [`12-1-native-platforms.md`](12-1-native-platforms.md) | ✅ |
| 12.2 | Golden hashes inside Unity players | [`12-2-player-check.md`](12-2-player-check.md) | ✅ |
| 12.3 | Query filter, shape casts, shape overlaps, query pipeline | [`12-3-queries.md`](12-3-queries.md) | ✅ |
| 12.4 | Torques, forces at a point, continuous collision | [`12-4-forces.md`](12-4-forces.md) | ✅ |
| 12.5 | Contact points | [`12-5-contacts.md`](12-5-contacts.md) | ✅ |
| 12.6 | Several bodies per object | [`12-6-bodies.md`](12-6-bodies.md) | ✅ |
| 12.7 | Joints | [`12-7-joints.md`](12-7-joints.md) | ✅ |
| 12.8 | Character controller | [`12-8-character.md`](12-8-character.md) | ✅ |
| 12.9 | Effectors | [`12-9-effectors.md`](12-9-effectors.md) | ✅ |
| 12.10 | `Rigidbody2D` components that are not simulated | [`12-10-simulated-bodies.md`](12-10-simulated-bodies.md) | ✅ |
| 12.10 | Release 0.2.0 with `com.fomoxa.networking` 0.3.0 | [`12-10-release.md`](12-10-release.md) | ✅ |
| 12.11 | Release 0.2.1 with `com.fomoxa.networking` 0.3.1 | [`12-11-release.md`](12-11-release.md), [`12-13-release.md`](12-13-release.md) | ✅ |
| 12.15 | Release 0.2.3 with `com.fomoxa.networking` 0.3.3 | [`12-15-release.md`](12-15-release.md) | ✅ |

The part shared by every backend (the public physics interface, `NetworkPhysics`, body colliders, the collider converter, scene files, console backend physics, the contact source plug-in point) is done in the `unity` repository, in steps 08.8 to 08.12, the `Fomoxa.Unity` part of 08.15 and the Core parts of steps 11.10, 12.3 to 12.6, 12.9 and 12.10.

## Layout

```
native/fomoxa-rapier/            FFI crate (rapier3d, rapier2d =0.36.0, enhanced-determinism)
com.fomoxa.networking.rapier/    Unity package
  Runtime/Rapier/                assembly Fomoxa.Networking.Rapier, no UnityEngine reference
  Runtime/Unity/                 assembly Fomoxa.Unity.Rapier
  Runtime/Plugins/               native libraries of every platform, from the native-libraries workflow
  Tests/Unity/                   Unity tests; TwoSided/ is the two-sided check
tests/                           dotnet tests of the tier 1 part
test-project/                    Unity project that runs the package tests
checks/two-sided/                console server of the two-sided check
checks/player/                   Unity project that runs the golden hashes inside players
bench/                           ragdoll benchmark
Tools/                           library builds, Unity tests on Windows, two-sided check, third-party notices
implementation/                  this folder
```

## Core

The dotnet tests, the Unity project and the two-sided server take the Core from `../unity/com.fomoxa.networking`, that is, the `unity` repository cloned next to this one. CI takes the public `fomoxa/com.fomoxa.networking` at the commit in `CORE_COMMIT` of `.github/workflows/ci.yml`, and a step that needs a newer Core moves that commit after the Core has been pushed.

## Commands

The commands are listed in the [repository README](../README.md#commands).

Under WSL, `dotnet` needs `LANG=C.UTF-8 LC_ALL=C.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. The Windows build uses the Windows `cargo.exe`. The dotnet tests on Windows run with `dotnet.exe`, with `TargetFramework` changed to `net9.0` in the copied project, because the machine only has SDK 9.

The workflow `native-libraries` builds the native libraries, and they are committed with `native/fomoxa-rapier/built-from.txt`. The repository README, section Native libraries, describes the steps after a change to the crate. If the dependency tree changed, run `Tools/third-party-notices.py` again.
