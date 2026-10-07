# networking-rapier

This repository holds the Rapier physics backend of `com.fomoxa.networking`. The Unity package `com.fomoxa.networking.rapier` calls the FFI crate `fomoxa-rapier`, which is built on `rapier3d` and `rapier2d` 0.36.0 with the `enhanced-determinism` feature. Native libraries for Windows x64 and Linux x64 are committed together with the source. The repository also has the tests and a two-sided check between a console server and a Unity client.

The backend is optional, so it lives in its own repository (Q163 (2) C). The Core does not know about Rapier, and a Rapier fix only needs a new release of this package. The design is in the `unity` repository (`design/principles.md` P27, `design/open-questions.md` Q163 – Q166). The implementation notes are in [`implementation/`](implementation/README.md), and usage is described in the [package README](com.fomoxa.networking.rapier/README.md).

```
Unity game / console server
    |
com.fomoxa.networking.rapier     RapierPhysics (Unity), RapierScenes, RapierWorld, RapierWorld2D        ← this repository
    |
fomoxa-rapier (Rust, cdylib)     C ABI fr_*, fr2_*: worlds, bodies, snapshots, restore, contacts, hash  ← this repository
    |
rapier3d, rapier2d =0.36.0
```

## Layout

```
native/fomoxa-rapier/            FFI crate
com.fomoxa.networking.rapier/    Unity package (Runtime/Rapier has no UnityEngine reference; Runtime/Unity; Runtime/Plugins; Tests/Unity)
tests/                           dotnet tests of the tier 1 part
test-project/                    Unity project that runs the package tests
checks/two-sided/                console server of the two-sided check
Tools/                           library builds, Unity tests on Windows, two-sided check, third-party notices
implementation/                  implementation notes for steps 08.13 – 08.16
```

## Development dependencies

- The `unity` repository cloned next to this one (`../unity`). The dotnet tests, the Unity project and the two-sided server take the Core from `../unity/com.fomoxa.networking`. There is no CI yet, because how CI gets the Core has not been decided.
- Rust (stable) for the Linux build, and the Windows `cargo.exe` for the Windows build from WSL.
- .NET SDK 8 on Linux, and .NET SDK 9 on Windows for the two-sided server.
- Unity 6000.5.7f1 on Windows for the Unity tests and the two-sided check.

## Commands

| Task | Command |
|---|---|
| Build the libraries | `Tools/build-rapier.sh linux`, `Tools/build-rapier.sh windows`, `Tools/build-rapier.sh all` |
| dotnet tests | `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj` |
| Unity EditMode tests | `Tools/unity-windows-check.sh` |
| Two-sided check | `Tools/two-sided-check.sh linux`, `Tools/two-sided-check.sh windows` |
| Third-party notices | `python3 Tools/third-party-notices.py` |

The native libraries are built by hand and committed; there is no CI that builds or releases them. After changing the crate, rebuild both platforms, then run the tests on both platforms and the two-sided check.

## License

Apache-2.0 ([`LICENSE.md`](LICENSE.md)). Rapier and the other crates statically linked into the libraries: [`Third Party Notices.md`](com.fomoxa.networking.rapier/Third%20Party%20Notices.md).
