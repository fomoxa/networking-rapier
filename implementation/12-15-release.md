# 12.15 Release 0.2.3

> Step 12.15 of the `unity` repository. Its `implementation/12-15-fomoxac-023.md` holds the full listings. The design is Q253 of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.3, which bundles `fomoxac` 0.2.3, and this package 0.2.3 with `RapierPackage.CoreVersion` "0.3.3". Nothing else in this package changed. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.3, `RapierPackage.CoreVersion` "0.3.3", README | ✅ |
| 2 | CI pinned to the 0.3.3 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.3` | ✅ |

## CI

`CORE_COMMIT` is `2049134f25fc95775fdee11d5e0929f12f7c3667`, the commit tagged `0.3.3` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, and the package is checked again only when its library changes (Q248). CI runs the tests on push against the new `CORE_COMMIT`.
