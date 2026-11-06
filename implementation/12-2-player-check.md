# 12.2 Golden hashes inside Unity players

> Step 12.2 of the `unity` repository, `implementation/12-2-player-check.md`, which holds the full listings. The design is Q224 (5) B of `design/open-questions.md` there: a hand check on the development machine with IL2CPP Windows, Android and WebGL; the Unity license never goes to CI; macOS and iOS players are recorded as not checked. Status: Applied. Every row passes.

## What changed

| Part | Change |
|---|---|
| `examples/golden.rs`, `tests/golden.txt` | The coefficients of the positions, masses and 2D angles are now exact in `f32` (`1.125`, `0.875`, `0.0625`, `0.125`), so a fused multiply-add in IL2CPP code cannot change the inputs of the scenario |
| `checks/player/Assets/PlayerCheck/GoldenScenario.cs` | The same scenario through `RapierWorld` and `RapierWorld2D` |
| `tests/GoldenScenarioTest.cs` | CI keeps the C# scenario equal to the Rust one |
| `checks/player` | Unity project: `PlayerCheckRunner` runs the scenario when the first scene loads and compares it with `golden.txt`; `PlayerCheckBuild` builds one row |
| `Tools/player-check.sh` | Copies the repository and the Core to Windows, builds a row, checks the library in the build and runs the player where it can |

## Results

Unity 6000.5.7f1 on Windows, commit `4733b60` plus the corrections below. Every row that ran printed the 15 lines of `tests/golden.txt`.

| Row | Result |
|---|---|
| `windows-mono` | PASS, x64 |
| `windows-il2cpp` | PASS, x64; the build has `GameAssembly.dll` |
| `windows-arm64` | Built only: the `.exe` and `Plugins/ARM64/fomoxa_rapier.dll` are ARM64 |
| `webgl` | PASS in Microsoft Edge 156.0.4314.58 (64-bit) on Windows, 32-bit pointers |
| `android-arm64` | PASS on a ZTE A303ZT, Android 14 (API 34), 64-bit process |
| `android-armv7` | PASS on the same phone, 32-bit process |

Not checked as players: Linux (no Linux module on this machine), macOS and iOS (Q224 (5) B).

## Corrections made while running

- A project without `ProjectSettings` is a new project for Unity, which then adds its default packages to `manifest.json` (IAP, analytics and others). `checks/player/ProjectSettings` is now a copy of the settings of `test-project`.
- Unity 6000.5.7f1 copies every plug-in enabled for `Standalone: Win64` into a Windows build, each into the folder of its `CPU`, so an x64 build also carries `Plugins/ARM64/fomoxa_rapier.dll`. The script checks the library in the folder of the target architecture.
- The copy of the `.meta` files back to the repository skips empty folders.

## Notes for the next runs

- An Android build restarts `adb`. A phone that has not chosen "Always allow from this computer" asks again after every build.
- `windows-mono` takes about 7 minutes the first time, while Unity builds the `Library` folder, and about 2 minutes after that.
