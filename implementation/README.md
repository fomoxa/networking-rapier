# Rapier backend implementation (8b)

This folder holds the implementation notes of the 8b sub-steps done in the `networking-rapier` repository. The design is in the `unity` repository, in `design/principles.md` P27 (section "Server console và 8b", console server and 8b) and `design/open-questions.md` Q163 – Q166. The plan and the contract of each sub-step are in the 8b section of `implementation/08-prediction-physics.md` in the `unity` repository.

The order of authority is the same as in the `unity` repository: `design/` of the `unity` repository, then the 8b plan in its `implementation/`, then the files in this folder, then the code.

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
| — | CI | — | not yet: how CI gets the Core is not decided |

The part shared by every backend (the public physics interface, `NetworkPhysics`, body colliders, the collider converter, scene files, console backend physics, the contact source plug-in point) is done in the `unity` repository, in steps 08.8 – 08.12 and the `Fomoxa.Unity` part of 08.15.

## Layout

```
native/fomoxa-rapier/            FFI crate (rapier3d, rapier2d =0.36.0, enhanced-determinism)
com.fomoxa.networking.rapier/    Unity package
  Runtime/Rapier/                assembly Fomoxa.Networking.Rapier, no UnityEngine reference
  Runtime/Unity/                 assembly Fomoxa.Unity.Rapier
  Runtime/Plugins/               prebuilt native libraries (Linux x64, Windows x64)
  Tests/Unity/                   Unity tests; TwoSided/ is the two-sided check
tests/                           dotnet tests of the tier 1 part
test-project/                    Unity project that runs the package tests
checks/two-sided/                console server of the two-sided check
Tools/                           library builds, Unity tests on Windows, two-sided check, third-party notices
implementation/                  this folder
```

## Core

The dotnet tests, the Unity project and the two-sided server take the Core from `../unity/com.fomoxa.networking`, that is, the `unity` repository cloned next to this one. How CI gets the Core is not decided; the options are a copy with a `SOURCE` file, a submodule pinned to a commit, or a tag. The 8b plan in the `unity` repository records a pinned submodule, and P12 records that CI takes the Core at a pinned tag. Once one is chosen, the paths `../../unity` and `../../../unity` in `tests/*.csproj`, `checks/two-sided/*.csproj`, `test-project/Packages/manifest.json` and `Tools/*.sh` change accordingly.

## Commands

| Task | Command |
|---|---|
| Build the Linux and Windows libraries | `Tools/build-rapier.sh linux`, `Tools/build-rapier.sh windows`, `Tools/build-rapier.sh all` |
| dotnet tests (Linux) | `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj` |
| Unity EditMode tests (Windows, from WSL) | `Tools/unity-windows-check.sh` |
| Two-sided check | `Tools/two-sided-check.sh linux`, `Tools/two-sided-check.sh windows` |
| Third-party notices | `python3 Tools/third-party-notices.py` |

Under WSL, `dotnet` needs `LANG=C.UTF-8 LC_ALL=C.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`. The Windows build uses the Windows `cargo.exe`. The dotnet tests on Windows run with `dotnet.exe`, with `TargetFramework` changed to `net9.0` in the copied project, because the machine only has SDK 9.

The native libraries are built by hand and committed, following the fomoxac rule; there is no CI that builds or releases them (Q163 (2) C). After changing the crate, rebuild both platforms, then run the tests on both platforms and the two-sided check. If the dependency tree changed, run `Tools/third-party-notices.py` again.
