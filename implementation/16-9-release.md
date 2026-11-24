# 16.9 Release 0.2.6

> Step 16.9 of the `unity` repository. Its `implementation/16-9-release.md` holds the full listings. The design is Q286 of `design/open-questions.md` there: `com.fomoxa.networking` 0.4.0, which moves state, input and RPCs of a `NetworkBehaviour` to fields and parameters, and this package 0.2.6 with `RapierPackage.CoreVersion` "0.4.0". Since 0.2.5, the only other change in this package is the two-sided check of step 16.7 (`16-7-two-sided-input.md`). Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.6, `RapierPackage.CoreVersion` "0.4.0", README | ✅ |
| 2 | CI pinned to the 0.4.0 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.6` | ✅ |

## CI

`CORE_COMMIT` is `b2187bc2227a2afab0014de0c1c6fc571995598f`, the commit tagged `0.4.0` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, the test change of 16.7 was checked in that step, and the package is checked again only when its library changes (Q248, Q286 (2) A). CI runs the tests on push against the new `CORE_COMMIT`. No component of this package declares state, input or RPCs on a `NetworkBehaviour`, so the 0.4.0 Core needs no change here.
