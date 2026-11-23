# 15.4 Release 0.2.5

> Step 15.4 of the `unity` repository. Its `implementation/15-4-release.md` holds the full listings. The design is Q273 of `design/open-questions.md` there: `com.fomoxa.networking` 0.3.5, which adds the error policy changes of stage 15, and this package 0.2.5 with `RapierPackage.CoreVersion` "0.3.5". Nothing else in this package changed. Status: Applied.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.2.5, `RapierPackage.CoreVersion` "0.3.5", README | ✅ |
| 2 | CI pinned to the 0.3.5 Core | ✅ |
| 3 | `rapier-effectors` fast-forwarded into `main`, tag `0.2.5` | ✅ |

## CI

`CORE_COMMIT` is `deefd43056a7aeade7e298def78b67554b496070`, the commit tagged `0.3.5` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Checks

None of the checks of this repository ran for this release: the C# code and the native libraries of the package did not change, and the package is checked again only when its library changes (Q248, Q273 (2) A). CI runs the tests on push against the new `CORE_COMMIT`. The 0.3.5 Core turns `HandlerExceptionArgs` into a class and adds a check of state marks on the server; this package uses neither.
