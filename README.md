# networking-rapier

Repo này chứa backend physics Rapier của `com.fomoxa.networking`. Package Unity `com.fomoxa.networking.rapier` gọi crate FFI `fomoxa-rapier`, crate này dựng trên `rapier3d` và `rapier2d` 0.36.0 với feature `enhanced-determinism`. Binary native cho Windows x64 và Linux x64 được commit cùng mã nguồn. Repo cũng có test và phép kiểm hai phía giữa server console và client Unity.

Backend là tùy chọn nên nằm ở repo riêng (Q163 (2) C). Core không biết Rapier, và một bản sửa của Rapier chỉ cần phát hành lại package này. Thiết kế nằm ở repo `unity` (`design/principles.md` P27, `design/open-questions.md` Q163 – Q166). Bản thi công ở [`implementation/`](implementation/README.md), hướng dẫn dùng ở [README của package](com.fomoxa.networking.rapier/README.md).

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

- Repo `unity` clone cạnh repo này (`../unity`). Test `dotnet`, project Unity và server kiểm hai phía lấy Core từ `../unity/com.fomoxa.networking`. Repo chưa có CI vì cách CI lấy Core chưa định.
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

Binary native được build bằng tay rồi commit; không có CI build hay phát hành binary. Sau khi đổi crate, build lại cả hai nền tảng rồi chạy lại test trên hai nền tảng và phép kiểm hai phía.

## Giấy phép

Apache-2.0 ([`LICENSE.md`](LICENSE.md)). Rapier và các crate được link tĩnh vào binary: [`Third Party Notices.md`](com.fomoxa.networking.rapier/Third%20Party%20Notices.md).
