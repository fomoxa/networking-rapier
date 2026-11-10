# 12.10 Release 0.2.0

> Step 12.10 of the `unity` repository, part 4. Its `implementation/12-10-release.md` holds the full listings for this repository. The design is Q244 (1) A of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.0 and this package 0.2.0, released once for the whole of stage 12 (Q225 (9) B). Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.0, `RapierPackage.CoreVersion` "0.3.0" | ✅ |
| 2 | Package README for the features and platforms of stage 12; `implementation/README.md` | ✅ |
| 3 | CI pinned to the 0.3.0 Core | ✅ |
| 4 | Tests, two-sided check, player check of 12.2 again | ✅ |
| 5 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.0` | ✅ |

## CI

`CORE_COMMIT` is `09658b8`, the `git subtree split` of `com.fomoxa.networking` at the `unity` commit of the release, which is the commit tagged `0.3.0` in `fomoxa/com.fomoxa.networking`. `RapierPackage.CoreVersion` is "0.3.0", so the tests fail against an earlier Core. Push the Core before this repository, or CI cannot fetch the commit.

## Results

| Check | Result |
|---|---|
| `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj`, Core of the `unity` working tree at the release | 102/102 |
| The same against the tree of the `0.3.0` split (`-p:Unity`), and `dotnet build bench/Fomoxa.Rapier.Bench.csproj` as in CI | 102/102; the bench builds |
| Unity EditMode (`Tools/unity-windows-check.sh`) | 38 tests, 37 passed, 1 skipped, 0 failed |
| Two-sided check, Windows server | PASS: 1485 common ticks, the same world hash on every one, 0 reconcile mismatches |

## Player check

Unity 6000.5.7f1 on Windows. Every row that ran printed the 46 lines of `tests/golden.txt`, which now include the queries, forces, contacts, joints, character and effector lines of steps 12.3 to 12.9.

| Row | Result |
|---|---|
| `windows-mono` | PASS, x64 |
| `windows-il2cpp` | PASS, x64 |
| `windows-arm64` | Built only: `Plugins/ARM64/fomoxa_rapier.dll` is ARM64 |
| `webgl` | PASS in Microsoft Edge 157.0.4341.42 on Windows |
| `android-arm64` | PASS on a ZTE A303ZT, Android 14 (API 34), 64-bit process |
| `android-armv7` | PASS on the same phone, 32-bit process, on the second run |

The first `android-armv7` run reported FAIL without any line. Android 14 shows a warning dialog over 32-bit apps (`DeprecatedAbiDialog`), the dialog keeps the focus, and Unity does not run its first frame until it is dismissed, which did not happen within the 120 seconds the script waits. The second run installed the same APK and started it the same way; once the dialog was dismissed, the app printed the 46 lines, equal to `golden.txt`, and PASS within 4 seconds.

## Notes for the next runs

- On Android 14 and later, dismiss the 32-bit warning dialog on the phone during `android-armv7`, or the row times out.
