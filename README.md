# TWWH3 Solo Multiplayer Patcher (.NET)

Bản patch bằng C#/.NET giúp chỉnh sửa Total War: WARHAMMER III để chơi các bản đồ (campaign) multiplayer một mình (solo).

Patch này **không** chứa hay phân phối file `Warhammer3.exe`. Nó chỉ chỉnh sửa file exe có sẵn trên máy bạn, sau khi kiểm tra đúng "chữ ký byte" (byte signature) và tự động tạo bản sao lưu (backup) trước khi patch.

> Fork từ [CameronComnenos/twwh3-solo-mp](https://github.com/CameronComnenos/twwh3-solo-mp), phát hành theo giấy phép MIT. Bản này sửa lỗi patch không chạy được với bản game mới, đồng thời viết lại README bằng tiếng Việt cho dễ hiểu hơn với người chưa quen dùng công cụ kiểu này.

## Giới thiệu

Các campaign multiplayer trong TWWH3 có một số khác biệt nhỏ so với campaign chơi solo bình thường. Những khác biệt này có thể cộng dồn lại và ảnh hưởng lớn hơn về sau trong game. Mục đích của bản patch này là cho phép chơi solo trong các lobby multiplayer, để bạn trải nghiệm được những khác biệt đó mà không cần người chơi thứ hai.

## Giấy phép (License)

Giấy phép MIT. Xem file [`LICENSE`](./LICENSE).

## Cách sử dụng

Chạy file thực thi (.exe) sẽ mở ra giao diện đồ họa (GUI), tại đó bạn có thể:
- Duyệt tìm file `Warhammer3.exe`
- Kiểm tra trạng thái patch
- Áp dụng patch
- Khôi phục bản sao lưu (backup)

GUI sẽ tự động tìm các bản cài Steam, kể cả khi Steam library nằm ở ổ đĩa khác.

Nếu thích dùng dòng lệnh (CLI), có thể chạy:

```
twwh3-solo-mp-patcher status  "C:\đường-dẫn\đến\Warhammer3.exe" [--compatibility]
twwh3-solo-mp-patcher apply   "C:\đường-dẫn\đến\Warhammer3.exe" [--compatibility]
twwh3-solo-mp-patcher restore "C:\đường-dẫn\đến\Warhammer3.exe"
```

Nếu không nhập đường dẫn, patcher sẽ tự tìm `Warhammer3.exe` trong thư mục hiện tại.

File exe bản release được build dưới dạng ứng dụng GUI Windows, nên khi double-click sẽ không hiện cửa sổ console. Nếu dùng qua script PowerShell, nên chạy theo cách sau để tránh bị treo:

```
Start-Process .\twwh3-solo-mp-patcher.exe -ArgumentList @("status", "C:\đường-dẫn\đến\Warhammer3.exe") -Wait -NoNewWindow
```

Khi áp dụng patch (apply), chương trình sẽ tạo file backup:

```
Warhammer3.vanilla.exe
```

Nên giữ lại file này nếu muốn khôi phục (restore) chỉ bằng một cú nhấp sau này. Khi restore, chương trình sẽ copy file backup này đè lên `Warhammer3.exe`, sau đó xóa bản backup đi — để lần apply tiếp theo có thể tạo một bản backup "sạch" (đúng bản gốc) mới. Patcher sẽ không tự ghi đè lên backup đã có sẵn qua GUI; tùy chọn `--force` trên CLI chỉ ghi đè backup khi file exe đang chọn vẫn đúng với byte gốc chưa patch.

Ngoài ra, bạn cũng có thể dùng chức năng "Xác minh tính toàn vẹn của game" (Verify integrity of game files) trên Steam để đưa `Warhammer3.exe` về trạng thái gốc.

## Chế độ tương thích (Compatibility Mode)

Chế độ mặc định là **Strict mode** (chế độ nghiêm ngặt). Chế độ này yêu cầu hash, kích thước file, offset, và byte signature của file exe phải khớp chính xác với profile đã lưu.

**Compatibility mode** là chế độ dự phòng, dùng khi game vừa có bản hotfix mới. Chế độ này cho phép hash, kích thước file và offset khác đi, nhưng vẫn từ chối patch nếu không tìm thấy đúng và duy nhất mỗi byte signature. Trên GUI, tick vào ô "Compatibility mode". Trên CLI, thêm cờ `--compatibility`.

## Build (biên dịch)

Cài đặt .NET SDK, sau đó chạy trong thư mục này:

```
.\scripts\Publish-Release.ps1
```

Hoặc double-click:

```
scripts\Publish-Release.bat
```

Script này sẽ dọn dẹp và build ra thư mục:

```
dist\
```

Lệnh publish tương đương (nếu muốn tự chạy tay):

```
dotnet publish .\TWWH3SoloMp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\dist
```

Các bản release đã build sẵn có thể tải trên trang GitHub Releases của repo này.

## Các profile patch

Dữ liệu patch nằm trong file [`patches.json`](./patches.json), được nhúng trực tiếp vào file exe khi biên dịch.

Mỗi profile có thể gồm:

- `displayVersion`: phiên bản game hiển thị cho người dùng, ví dụ `v8.0.2 Build 46904.4121883`.
- `exeSha256`: (tùy chọn) hash chính xác của file exe, dùng để kiểm tra nghiêm ngặt đúng phiên bản.
- `exeSize`: (tùy chọn) kích thước chính xác của file exe, thêm một lớp kiểm tra nghiêm ngặt nữa.
- `expectedOffset`: vị trí (offset) mong đợi trong file để patch.
- `before`: chuỗi byte cần tìm trước khi patch.
- `replaceOffset`: vị trí bắt đầu ghi đè bên trong đoạn byte `before`.
- `replace`: các byte sẽ được ghi vào.

Dùng `??` trong `before` cho các byte "linh động" (volatile), ví dụ 4 byte toán hạng tương đối (relative operand) trong lệnh `E8 call rel32`. Các byte trong `replace` không được chứa wildcard. Cách này giúp patcher so khớp trong một đoạn ngữ cảnh rộng hơn, nhưng chỉ chỉnh sửa đúng các byte lệnh cần thay đổi.

## Các patch cụ thể (bản v8.0.2 Build 46904.4121883)

### Cho phép bắt đầu đếm giờ (Allow Timer Start)

Before:
```
48 8B 8F 60 0B 0E 00 48 8B 01 FF 90 B0 01 00 00 83 F8 03 8B CE 48 8B 87 10 0B 0E 00 0F 94 C1 FF C1 8B 90 04 28 00 00 3B D1
```

Replace tại offset `31`:
```
90 90
```

### Chặn việc kết thúc game bắt buộc do phiên chơi solo bị coi là "không hợp lệ" (Suppress Solo Invalid-Session Force Game Over)

Before:
```
48 8D 05 ?? ?? ?? ?? 48 8D 55 F0 48 89 45 F0 C6 45 F8 01 E8 ?? ?? ?? ?? 40 88 B3 F0 0C 0E 00 48 39 B3 48 50 07 00
```

Replace tại offset `19`:
```
90 90 90 90 90
```

## Xử lý sự cố (Troubleshooting)

- **Dòng hiện màu đỏ/cam "unapplied" khi kiểm tra trạng thái (Check Status) ở compatibility mode**: đây chỉ là màu hiển thị trạng thái, không phải lỗi. Cứ chạy Apply Patch bình thường.
- **Lỗi "Backup already exists" khi Apply**: patcher đã có sẵn một bản backup `Warhammer3.vanilla.exe` từ phiên bản game trước đó. Đổi tên hoặc xóa file này (dù sao nó cũng đã lỗi thời từ khi Steam cập nhật game), rồi apply lại — bản backup mới, đúng, sẽ được tự động tạo lại.

## Lưu ý

- Patcher sẽ từ chối chạy (fail closed) nếu thiếu hoặc không rõ ràng về byte signature.
- Patcher yêu cầu cả byte pattern và file offset đều phải khớp đúng như mong đợi.
- Không phân phối file exe game đã patch. Chỉ nên chia sẻ patcher/mã nguồn này.
