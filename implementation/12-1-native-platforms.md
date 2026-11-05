# 12.1 Libraries for every platform

> Step 12.1 of the `unity` repository, `implementation/12-1-native-platforms.md`, which holds the full listings. The design is P27 of `design/principles.md` there, section "Hoàn thiện backend Rapier" (completing the Rapier backend; Q224). Status: Applied. The workflow passes on every job. No Unity player has been built yet: that is step 12.2.

## What changed

| Part | Change |
|---|---|
| `rust-toolchain.toml` | Rust 1.98.0 on every machine and runner |
| `build.rs` | Android links with `-Bsymbolic-functions` and aligns to 16 KB pages |
| `examples/golden.rs`, `tests/golden.txt` | One scenario for 3D and 2D, with every shape, a triangle mesh and polyline ground, forces and impulses, a snapshot replay and a raycast |
| `Tools/build-rapier.sh`, `Tools/check-golden.sh`, `Tools/install-native.sh` | Build, hash check and installation per platform |
| `.github/workflows/native-libraries.yml` | Builds every library, checks the hashes, exports `native-libraries`. It does not commit |
| `.github/workflows/ci.yml` | Fails when the native source is newer than `built-from.txt` |
| `RapierNative` | `__Internal` on iOS and Web players |
| `Runtime/Plugins` | The new libraries and a hand-written `.meta` for each one |

## Results

The first workflow run, on commit `00c4eb8`, passed every job. The hashes match `tests/golden.txt` on Linux x64 and arm64, Windows x64 and arm64, macOS arm64 and x64, the iOS arm64 simulator, an Android x86_64 emulator, Web on Node, and 32-bit ARM on QEMU.

Checks of the installed libraries:

| Check | Result |
|---|---|
| Android load segments | `0x4000` on both ABIs |
| Highest glibc version, Linux x64 and arm64 | `GLIBC_2.33` |
| Imported math functions, Linux and Android | none |
| Exported `fr_*` and `fr2_*` functions, Linux and Android | 52 |
| `dotnet test` on Linux | 46 of 46 |
| Unity EditMode on Windows, 6000.5.7f1 | 13 passed, 1 ignored (the two-sided test needs its server), no plug-in warning, no `.meta` rewritten, `found 1 plugins` for the Editor |

## Limits

- The Web library does not link with `-pthread`.
- On iOS and Web the math functions of `src/math.rs` are linked into the game binary. Another static plug-in that defines the same symbols can cause a duplicate symbol error at link time.
- Android x86-64 is not built: `AndroidArchitecture` in Unity 6000.5 lists only `ARMv7` and `ARM64`.
- The `.meta` entries for players other than Windows have only been read by the Editor, which has no Android, iOS, macOS or Linux module here. Step 12.2 checks them where it can.
- `Tools/two-sided-check.sh` does not build: `checks/two-sided` has not followed the plug-ins that `com.fomoxa.networking` 0.2.0 added, which `tests/` took in 11.29. This step did not change it.
