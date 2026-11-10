# 12.11 Release 0.2.1

> Step 12.11 of the `unity` repository, part 4. Its `implementation/12-11-release.md` holds the full listings for this repository. The design is Q248 of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.1, which ships Fomoxa.Net 0.1.4, and this package 0.2.1 with `RapierPackage.CoreVersion` "0.3.1". Nothing else in this package changed. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.1, `RapierPackage.CoreVersion` "0.3.1", README | ✅ |
| 2 | CI pinned to the 0.3.1 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.1` | ✅ |

## CI

`CORE_COMMIT` is `7cfe0e8`, the commit tagged `0.3.1` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, and the package is checked again only when its library changes (Q248). The `GoldenScenario` of the player check runs only Rapier worlds, so its hashes cannot change. CI runs the tests on push against the new `CORE_COMMIT`.
