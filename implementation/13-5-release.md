# 13.5 Release 0.2.4

> Step 13.5 of the `unity` repository. Its `implementation/13-5-2-release.md` holds the full listings. The design is Q265 of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.4, which adds the debug tools of stage 13, and this package 0.2.4 with `RapierPackage.CoreVersion` "0.3.4". Nothing else in this package changed. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.4, `RapierPackage.CoreVersion` "0.3.4", README | ✅ |
| 2 | CI pinned to the 0.3.4 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.4` | ✅ |

## CI

`CORE_COMMIT` is `6c307840f16140cf5353d9233b58ab70bb75f1c5`, the commit tagged `0.3.4` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, and the package is checked again only when its library changes (Q248, Q265 (2) A). CI runs the tests on push against the new `CORE_COMMIT`. The 0.3.4 Core changes the signature of `IClientPredictionBackend.EndCorrection`, which this package does not implement.
