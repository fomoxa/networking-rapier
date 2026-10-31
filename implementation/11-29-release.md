# 11.29: release 0.1.1

Step 11.29 of the `unity` repository releases `com.fomoxa.networking` 0.2.0 (`design/open-questions.md` Q222 there). This package moves to 0.1.1 at the same time and needs that Core version.

| | Work | Status |
|---|---|:---:|
| 1 | `package.json` 0.1.1, `RapierPackage.CoreVersion` "0.2.0", README version and requirement | ✅ |
| 2 | Test project references the two Core plug-ins it needs since 11.15 and 11.19 | ✅ |
| 3 | CI pinned to the 0.2.0 Core | ✅ |

## Test project

The Core needs `BouncyCastle.Cryptography.dll` since step 11.15 (encrypted UDP, client TLS) and `Fomoxa.Net.WebSockets.dll` since step 11.19. `tests/Fomoxa.Networking.Rapier.Tests.csproj` compiles the Core from source and referenced only `Fomoxa.Attributes.dll` and `Fomoxa.Net.dll`, so it no longer built against the current Core. CI did not show it because it was pinned to `2570d7f`, a Core from before 11.15. The project now references both DLLs from the Core's `Runtime/Plugins/`.

## CI

`CORE_COMMIT` is `003a5a4`, the `git subtree split` of `com.fomoxa.networking` at the `unity` commit of step 11.29, which is the commit tagged `0.2.0` in `fomoxa/com.fomoxa.networking`. Push the Core before this repository, or CI cannot fetch the commit.

## Results

- `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj -c Release`, against the working tree of the Core and against the tree of `003a5a4`: 46/46.
- Unity EditMode (`Tools/unity-windows-check.sh`) with the Core of the `unity` working tree at 11.29: 14 tests, 13 passed, 1 skipped, 0 failed.
