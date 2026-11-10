# 12.13 Release 0.2.2

> Step 12.13 of the `unity` repository. Its `implementation/12-13-macos-fomoxac.md` holds the full listings for this repository. The design is Q250 of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.2, which adds the macOS `fomoxac` binary, and this package 0.2.2 with `RapierPackage.CoreVersion` "0.3.2". Nothing else in this package changed. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.2, `RapierPackage.CoreVersion` "0.3.2", README | ✅ |
| 2 | CI pinned to the 0.3.2 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.2` | ✅ |

## CI

`CORE_COMMIT` is `0351dee376a05b2098e05fed8d671b5be1c8359b`, the commit tagged `0.3.2` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, and the package is checked again only when its library changes (Q248). CI runs the tests on push against the new `CORE_COMMIT`.
