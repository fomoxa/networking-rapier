# networking-rapier

Backend physics Rapier cho `com.fomoxa.networking` (repo `unity`): package Unity `com.fomoxa.networking.rapier`, crate FFI `fomoxa-rapier` trên `rapier3d`, `rapier2d` 0.36.0 với `enhanced-determinism`, binary native cho Windows x64 và Linux x64, test và phép kiểm hai phía giữa server console và client Unity.

Backend là tùy chọn và nằm ở repo riêng (Q163 (2) C): Core không biết Rapier, bản sửa của Rapier phát hành lại package này mà không phát hành lại `com.fomoxa.networking`. Thiết kế ở repo `unity` (`design/principles.md` P27, `design/open-questions.md` Q163 – Q166); bản thi công ở [`implementation/`](implementation/README.md); hướng dẫn dùng ở [README của package](com.fomoxa.networking.rapier/README.md).

```
game Unity / server console
    |
com.fomoxa.networking.rapier     RapierPhysics (Unity), RapierScenes, RapierWorld, RapierWorld2D    ← repo này
    |
fomoxa-rapier (Rust, cdylib)     C ABI fr_*, fr2_*: thế giới, body, chụp, khôi phục, tập chạm, băm  ← repo này
    |
rapier3d, rapier2d =0.36.0
```

## Cấu trúc

```
native/fomoxa-rapier/            crate FFI
com.fomoxa.networking.rapier/    package Unity (Runtime/Rapier không tham chiếu UnityEngine; Runtime/Unity; Runtime/Plugins; Tests/Unity)
tests/                           test dotnet của phần tầng 1
test-project/                    project Unity chạy test của package
checks/two-sided/                server console của phép kiểm hai phía
Tools/                           build binary, test Unity trên Windows, kiểm hai phía, ghi chú bên thứ ba
implementation/                  bản thi công các bước 08.13 – 08.16
```

## Phụ thuộc lúc phát triển

- Repo `unity` clone cạnh repo này (`../unity`): test `dotnet`, project Unity và server kiểm hai phía lấy Core từ `../unity/com.fomoxa.networking`. Cách CI lấy Core chưa định, nên repo chưa có CI.
- Rust (stable) cho build Linux; `cargo.exe` của Windows cho build Windows từ WSL.
- .NET SDK 8 trên Linux; .NET SDK 9 trên Windows cho server kiểm hai phía.
- Unity 6000.5.7f1 trên Windows cho test Unity và phép kiểm hai phía.

## Lệnh

| Việc | Lệnh |
|---|---|
| Build binary | `Tools/build-rapier.sh linux`, `Tools/build-rapier.sh windows`, `Tools/build-rapier.sh all` |
| Test `dotnet` | `dotnet test tests/Fomoxa.Networking.Rapier.Tests.csproj` |
| Test Unity EditMode | `Tools/unity-windows-check.sh` |
| Kiểm hai phía | `Tools/two-sided-check.sh linux`, `Tools/two-sided-check.sh windows` |
| Ghi chú bên thứ ba | `python3 Tools/third-party-notices.py` |

Binary native được commit và build bằng tay; không có CI build hay phát hành binary. Đổi crate thì build lại cả hai nền tảng, chạy lại test hai nền tảng và phép kiểm hai phía.

## Giấy phép

Apache-2.0 ([`LICENSE.md`](LICENSE.md)). Rapier và các crate được link tĩnh vào binary: [`Third Party Notices.md`](com.fomoxa.networking.rapier/Third%20Party%20Notices.md).
