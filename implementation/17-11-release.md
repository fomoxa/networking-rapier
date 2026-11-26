# 17.11 Release 0.3.0

> Step 17.11 of the `unity` repository. Its `implementation/17-11-release.md` holds the full listings. The design is Q302 of `design/open-questions.md` there: `com.fomoxa.networking` 0.5.0 and this package 0.3.0 with `RapierPackage.CoreVersion` "0.5.0". Since 0.2.6, the Unity part of this package takes and returns Unity types (`NetworkRapier`, `NetworkJoints`, `NetworkCharacterMove`; step 17.5 of the `unity` repository), and `RapierPhysics` implements `BodyOf(Transform)` and `Body2DOf(Transform)` (step 17.10). Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.3.0, `RapierPackage.CoreVersion` "0.5.0", README | ✅ |
| 2 | CI pinned to the 0.5.0 Core | ✅ |
| 3 | dotnet tests, Unity tests, two-sided check, player check | ✅ |
| 4 | Tag `0.3.0` on `main` | ✅ |

## CI

`CORE_COMMIT` is `6782a6ad26c93e8b6ede641b7a03413d40e95847`, the commit tagged `0.5.0` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

The C# code of the package changed, so every check of this repository ran with the 0.5.0 Core (Q302 (3) A). The results are in section 4 of `implementation/17-11-release.md` of the `unity` repository.
