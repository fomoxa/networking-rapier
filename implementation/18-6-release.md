# 18.6 Release 0.4.0

> Step 18.6 of the `unity` repository. Its `implementation/18-6-release.md` holds the full listings. The design is Q334 of `design/open-questions.md` there: `com.fomoxa.networking` 0.6.0 and this package 0.4.0 with `RapierPackage.CoreVersion` "0.6.0". Since 0.3.0, `RapierScenes` keeps one pair of worlds per scene instance and takes a `NetworkScene` instead of a scene id (step 18.3 of the `unity` repository), and `RapierPhysics` rebuilds the bodies of an object that moves to another instance (step 18.4). Step 18.5 added `checks/rooms` and `Tools/rooms-check.sh`. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.4.0, `RapierPackage.CoreVersion` "0.6.0", README | ✅ |
| 2 | CI pinned to the 0.6.0 Core | ✅ |
| 3 | dotnet tests, Unity tests, two-sided check, player check, `Tools/rooms-check.sh check` | ✅ |
| 4 | Tag `0.4.0` on `main` | ✅ |

## CI

`CORE_COMMIT` is `934ca9bd4aa2b17fcfd30cf7f6b38f6812f55c32`, the commit tagged `0.6.0` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

The C# code of the package changed, so every check of this repository ran with the 0.6.0 Core (Q334 (3) A), and the `check` part of `Tools/rooms-check.sh` ran as well (Q334 (5) A). The results are in section 5 of `implementation/18-6-release.md` of the `unity` repository.

`checks/rooms/Packages/manifest.json` no longer lists `com.unity.modules.imgui`, because `com.fomoxa.networking` 0.6.0 depends on it (Q334 (4) A).
