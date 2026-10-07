# Thi công backend Rapier (8b)

Repo này chứa backend physics Rapier của `com.fomoxa.networking`: package `com.fomoxa.networking.rapier`, crate FFI `fomoxa-rapier`, binary native, test và phép kiểm hai phía. Thiết kế nằm ở repo `unity`: `design/principles.md` P27 (mục "Server console và 8b"), `design/open-questions.md` Q163 – Q166. Kế hoạch và hợp đồng của các bước con nằm ở `implementation/08-prediction-physics.md` của repo `unity` (mục 8b). Tệp trong thư mục này là bản thi công của các bước con làm ở repo này.

Thứ tự thẩm quyền như repo `unity`: `design/` của repo `unity` → kế hoạch 8b ở `implementation/` của repo `unity` → tệp trong thư mục này → code.

## Trạng thái

| Bước | Việc | Tệp | Trạng thái |
|---|---|---|:---:|
| 08.13 | Crate 3D, thế giới Rapier tầng 1 (`IPhysicsWorld`), `Load` theo body, băm trạng thái, đo chi phí chụp | [`08-13-tier1-rapier-3d.md`](08-13-tier1-rapier-3d.md) | ✅ |
| 08.14 | 2D (`rapier2d`, `IPhysicsWorld2D`) | [`08-14-tier1-rapier-2d.md`](08-14-tier1-rapier-2d.md) | ✅ |
| 08.15 | `RapierScenes` (`IPhysicsScenes`), tập chạm của thế giới | [`08-15-tier1-rapier-scenes.md`](08-15-tier1-rapier-scenes.md) | ✅ |
| 08.15 | Phần Unity: `RapierPhysics`, nguồn tập chạm, project Unity kiểm | [`08-15-tier2-rapier-unity.md`](08-15-tier2-rapier-unity.md) | ✅ |
| 08.16 | Kiểm hai phía: server console Rapier với client Unity Rapier qua UDP | [`08-16-two-sided-check.md`](08-16-two-sided-check.md) | ✅ |
| — | CI | — | chưa có: nguồn lấy Core cho CI chưa định |

Phần dùng chung của mọi backend (interface physics công khai, `NetworkPhysics`, collider của body, bộ chuyển collider, tệp scene, physics của backend console, điểm cắm nguồn tập chạm) làm ở repo `unity`, bước 08.8 – 08.12 và phần `Fomoxa.Unity` của 08.15.

## Cấu trúc

```
native/fomoxa-rapier/            crate FFI (rapier3d, rapier2d =0.36.0, enhanced-determinism)
com.fomoxa.networking.rapier/    package Unity
  Runtime/Rapier/                assembly Fomoxa.Networking.Rapier, không tham chiếu UnityEngine
  Runtime/Unity/                 assembly Fomoxa.Unity.Rapier
  Runtime/Plugins/               binary native đã build (Linux x64, Windows x64)
  Tests/Unity/                   test Unity; TwoSided/ là phép kiểm hai phía
tests/                           test dotnet của phần tầng 1
test-project/                    project Unity chạy test của package
checks/two-sided/                server console của phép kiểm hai phía
Tools/                           build binary, chạy test Unity trên Windows, kiểm hai phía, ghi chú bên thứ ba
implementation/                  thư mục này
```

## Core

Test `dotnet`, project Unity và server kiểm hai phía lấy Core từ thư mục `../unity/com.fomoxa.networking` cạnh repo này (repo `unity` clone cùng cấp). Cách CI lấy Core (bản chép có tệp `SOURCE`, submodule ghim commit, hay tag) chưa định; khi định xong, các đường dẫn `../../unity`, `../../../unity` trong `tests/*.csproj`, `checks/two-sided/*.csproj`, `test-project/Packages/manifest.json` và `Tools/*.sh` đổi theo. Kế hoạch 8b của repo `unity` ghi submodule ghim commit; P12 của design ghi CI lấy Core theo tag đã ghim.

## Lệnh

| Việc | Lệnh |
|---|---|
| Build binary Linux, Windows | `Tools/build-rapier.sh linux`, `Tools/build-rapier.sh windows`, `Tools/build-rapier.sh all` |
| Test `dotnet` (Linux) | `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj` |
| Test Unity EditMode (Windows, từ WSL) | `Tools/unity-windows-check.sh` |
| Kiểm hai phía | `Tools/two-sided-check.sh linux`, `Tools/two-sided-check.sh windows` |
| Ghi chú bên thứ ba | `python3 Tools/third-party-notices.py` |

Môi trường WSL cần `LANG=C.UTF-8 LC_ALL=C.UTF-8 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` cho `dotnet`. Build Windows dùng `cargo.exe` của Windows; test `dotnet` trên Windows chạy bằng `dotnet.exe` với `TargetFramework` đổi sang `net9.0` trong bản chép (máy chỉ có SDK 9).

Binary native được commit, build bằng tay theo luật của fomoxac; không có CI build hay phát hành binary (Q163 (2) C). Đổi crate thì build lại cả hai nền tảng, chạy lại test hai nền tảng và phép kiểm hai phía, rồi chạy lại `Tools/third-party-notices.py` nếu cây phụ thuộc đổi.
