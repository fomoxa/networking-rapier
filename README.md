# networking-rapier

This repository holds the Rapier physics backend of `com.fomoxa.networking`. The Unity package `com.fomoxa.networking.rapier` calls the FFI crate `fomoxa-rapier`, which is built on `rapier3d` and `rapier2d` 0.36.0 with the `enhanced-determinism` feature. Native libraries for every supported platform are committed together with the source (§ Native libraries). The repository also has the tests and a two-sided check between a console server and a Unity client.

The backend is optional, so it lives in its own repository (Q163 (2) C). The Core does not know about Rapier, and a Rapier fix only needs a new release of this package. The design is in the `unity` repository (`design/principles.md` P27, `design/open-questions.md` Q163 to Q166). The implementation notes are in [`implementation/`](implementation/README.md), and usage is described in the [package README](com.fomoxa.networking.rapier/README.md).

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
checks/player/                   Unity project that runs the golden hashes inside players
Tools/                           library builds, Unity tests on Windows, two-sided check, third-party notices
implementation/                  implementation notes of the steps done in this repository
```

## Development dependencies

- The `unity` repository cloned next to this one (`../unity`). The dotnet tests, the Unity project and the two-sided server take the Core from `../unity/com.fomoxa.networking`. The dotnet tests take another Core with `-p:Unity=<path to com.fomoxa.networking>`.
- `rustup` for the Linux build, and the Windows `cargo.exe` for the Windows build from WSL. `native/fomoxa-rapier/rust-toolchain.toml` pins Rust 1.98.0, and `rustup` installs it on the first build.
- .NET SDK 8 on Linux, and .NET SDK 9 on Windows for the two-sided server.
- Unity 6000.5.7f1 on Windows for the Unity tests and the two-sided check.

## Commands

| Task | Command |
|---|---|
| Build the libraries of this machine | `Tools/build-rapier.sh linux`, `Tools/build-rapier.sh windows`, `Tools/build-rapier.sh all` |
| Golden hashes | `Tools/check-golden.sh linux-x64` (the script lists the other platforms) |
| Player check | `Tools/player-check.sh windows-mono` (the script lists the other rows) |
| Install the libraries from the workflow | `Tools/install-native.sh native-libraries.zip <commit of the run>` |
| dotnet tests | `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj` |
| Unity EditMode tests | `Tools/unity-windows-check.sh` |
| Two-sided check | `Tools/two-sided-check.sh linux`, `Tools/two-sided-check.sh windows` |
| Third-party notices | `python3 Tools/third-party-notices.py` |

CI (`.github/workflows/ci.yml`) runs the dotnet tests on Linux on every push and pull request. It checks out the public [`fomoxa/com.fomoxa.networking`](https://github.com/fomoxa/com.fomoxa.networking) at the commit in `CORE_COMMIT` and passes it with `-p:Unity`, so a change in the Core reaches CI only when that commit is moved on purpose. Move it when this package starts using a newer Core.

CI also fails when the native source changed after the commit in `native/fomoxa-rapier/built-from.txt`, the commit the committed libraries were built from.

## Native libraries

The workflow `.github/workflows/native-libraries.yml` builds the libraries for every platform, runs `examples/golden.rs` where a runner can execute it and compares the output with `native/fomoxa-rapier/tests/golden.txt`. A difference fails the job. The workflow exports one artifact, `native-libraries`, and never commits or pushes.

| Platform | Library in `Runtime/Plugins` | Golden hashes run on |
|---|---|---|
| Linux x64, arm64 | `Linux/x86_64`, `Linux/arm64` | the runner |
| Windows x64, arm64 | `Windows/x86_64`, `Windows/arm64` | the runner |
| macOS arm64, x64 | `macOS` (universal) | one runner each |
| iOS device, simulator | `iOS` (xcframework) | the arm64 simulator; the device library is only built |
| Android `arm64-v8a`, `armeabi-v7a` | `Android/*` | an x86_64 emulator with the Android libc; the ARM libraries are only built |
| Web | `WebGL` (static library, Emscripten) | Node |

The workflow also runs the hashes on 32-bit ARM under QEMU, for the 32-bit code path of `armeabi-v7a`.

After a change to the crate: push it, wait for the workflow, download `native-libraries`, run `Tools/install-native.sh` with the commit of that run, run the tests and commit the libraries with `built-from.txt`. A commit that changes `tests/golden.txt` states why the state of the simulation changed.

`Tools/player-check.sh` runs the same scenario through the C# API inside Unity players (Windows Mono and IL2CPP, Web, Android) and compares it with `golden.txt`. It needs the Unity modules of those platforms and, for Android, a phone with USB debugging; it does not run in CI.

The Web library does not link with **Enable C/C++ Native Multithreading** (`-pthread`), because stable Rust has no standard library built with `atomics`.

The crate defines the single-precision math functions that Rapier and parry call (`acosf`, `atan2f`, `cosf`, `sinf`, `sincosf`, `expf`, `log2f`, `powf`) in `src/math.rs`, on top of the `libm` crate, and on Linux links with `-Bsymbolic-functions` so the library calls its own copies. Every library therefore computes these functions the same way, and the Linux libraries need no newer glibc than 2.34 even when they are built on a newer system. Check with `objdump -T` that a rebuilt `.so` imports no math function and no `GLIBC` version above 2.34.

## License

Apache-2.0 ([`LICENSE.md`](LICENSE.md)). Rapier and the other crates statically linked into the libraries: [`Third Party Notices.md`](com.fomoxa.networking.rapier/Third%20Party%20Notices.md).
