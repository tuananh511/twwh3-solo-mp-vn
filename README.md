# TWWH3 Solo MP VN

> Patch Total War: WARHAMMER III để chơi campaign multiplayer một mình (solo).

![Release](https://img.shields.io/github/v/release/tuananh511/twwh3-solo-mp-vn?label=release)
![License](https://img.shields.io/github/license/tuananh511/twwh3-solo-mp-vn)
![Build](https://img.shields.io/github/actions/workflow/status/tuananh511/twwh3-solo-mp-vn/build.yml?label=build)

## Overview

Đây là bản fork tiếng Việt của [CameronComnenos/twwh3-solo-mp](https://github.com/CameronComnenos/twwh3-solo-mp), phát hành theo giấy phép MIT.

Patch này **không** chứa hay phân phối file `Warhammer3.exe`. Nó chỉ chỉnh sửa file exe có sẵn trên máy bạn, và tự tạo bản sao lưu (backup) trước khi patch.

## Features

- Chỉnh sửa `Warhammer3.exe` để mở khóa campaign multiplayer chơi solo
- Tự động sao lưu (backup) file gốc trước khi patch
- Khôi phục bản gốc bất cứ lúc nào (Restore Backup)
- Công cụ dò offset mới (`Tim-Offset-Moi.bat`) khi game update và patch cũ chưa tương thích

## Installation

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

**Yêu cầu:** .NET SDK 10 để build, Windows, game cài qua Steam.

## Usage

Mở `dist\twwh3-solo-mp-patcher.exe`, chọn file `Warhammer3.exe`, chọn đúng bản game ở dropdown "Game version", rồi:

- **Check Status** để xem tình trạng
- **Apply Patch** để áp dụng
- **Restore Backup** để khôi phục bản gốc

### Khi game update mà chưa có bản vá tương ứng

Trong thư mục `scripts\`, double-click **`Tim-Offset-Moi.bat`**. Công cụ sẽ hỏi trực tiếp:

1. Đường dẫn tới `Warhammer3.exe` (đảm bảo file đang là bản gốc, chưa patch — dùng Steam "Xác minh tính toàn vẹn tệp trò chơi" nếu cần)
2. Chọn 1 profile gần nhất làm mẫu
3. Nếu tìm được offset mới → xác nhận ghi vào `patches.json`
4. Build lại như bước 3 ở Installation, xong

Nếu công cụ báo "không tìm thấy" hoặc "nhiều vị trí" — nghĩa là đoạn code game thật sự đã thay đổi, cần người biết dùng disassembler (x64dbg, Ghidra) hỗ trợ. Báo lên GitHub Issues kèm bản game bạn đang gặp.

## Roadmap

- [ ] Cập nhật offset tự động theo bản patch mới của game
- [ ] Gộp các bản vá cộng đồng phát hiện thêm

## License

MIT License — fork từ [CameronComnenos/twwh3-solo-mp](https://github.com/CameronComnenos/twwh3-solo-mp).
