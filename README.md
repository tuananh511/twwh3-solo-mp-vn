# TWWH3 Solo Multiplayer Patcher (.NET)

Bản patch giúp chỉnh sửa Total War: WARHAMMER III để chơi các bản đồ (campaign) multiplayer một mình (solo).

Patch này **không** chứa hay phân phối file `Warhammer3.exe`. Nó chỉ chỉnh sửa file exe có sẵn trên máy bạn, và tự tạo bản sao lưu (backup) trước khi patch.

> Fork từ [CameronComnenos/twwh3-solo-mp](https://github.com/CameronComnenos/twwh3-solo-mp), phát hành theo giấy phép MIT.

## Cách dùng

**1. Clone repo**
```
git clone https://github.com/tuananh511/twwh3-solo-mp-vn.git
cd twwh3-solo-mp-vn
```

**2. Cài .NET SDK 10** (chỉ cần 1 lần duy nhất trên máy)
```
winget install Microsoft.DotNet.SDK.10
```
Cài xong, mở PowerShell mới, kiểm tra: `dotnet --version`

**3. Build**
```
.\scripts\Publish-Release.ps1
```
File chạy được sẽ nằm ở `dist\twwh3-solo-mp-patcher.exe`

**4. Chạy app**
Mở `dist\twwh3-solo-mp-patcher.exe`, chọn file `Warhammer3.exe`, chọn đúng bản game ở dropdown "Game version", rồi:
- **Check Status** để xem tình trạng
- **Apply Patch** để áp dụng
- **Restore Backup** để khôi phục bản gốc

## Khi game update mà chưa có bản vá tương ứng

Trong thư mục `scripts\`, double-click **`Tim-Offset-Moi.bat`**. Công cụ sẽ hỏi trực tiếp:
1. Đường dẫn tới `Warhammer3.exe` (đảm bảo file đang là bản gốc, chưa patch — dùng Steam "Xác minh tính toàn vẹn tệp trò chơi" nếu cần)
2. Chọn 1 profile gần nhất làm mẫu
3. Nếu tìm được offset mới → xác nhận ghi vào `patches.json`
4. Build lại bước 3 ở trên, xong

Nếu công cụ báo "không tìm thấy" hoặc "nhiều vị trí" — nghĩa là đoạn code game thật sự đã thay đổi, cần người biết dùng disassembler (x64dbg, Ghidra) hỗ trợ. Báo lên GitHub Issues kèm bản game bạn đang gặp.

## Yêu cầu

- .NET SDK 10 để build
- Windows, game cài qua Steam
