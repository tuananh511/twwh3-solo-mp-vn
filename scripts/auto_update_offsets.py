# -*- coding: utf-8 -*-
"""
auto_update_offsets.py
Công cụ tự động cho người không rành kỹ thuật: quét file Warhammer3.exe,
tìm offset mới cho từng patch, và tự ghi vào patches.json.

Chạy bằng cách double-click file "Tim-Offset-Moi.bat" đi kèm,
hoặc chạy tay: python auto_update_offsets.py
Không cần gõ tham số dòng lệnh - chương trình sẽ hỏi trực tiếp.
"""

import json
import hashlib
import os
import sys
import shutil
import datetime


def parse_pattern(pattern_str):
    tokens = pattern_str.strip().split()
    result = []
    for tok in tokens:
        if tok == "??":
            result.append(None)
        else:
            result.append(int(tok, 16))
    return result


def find_pattern(data, pattern):
    n = len(data)
    m = len(pattern)
    matches = []
    for i in range(n - m + 1):
        ok = True
        for j in range(m):
            if pattern[j] is not None and data[i + j] != pattern[j]:
                ok = False
                break
        if ok:
            matches.append(i)
            if len(matches) > 5:
                break
    return matches


def ask_yes_no(question):
    while True:
        ans = input(f"{question} (y/n): ").strip().lower()
        if ans in ("y", "yes", "c", "co", "có"):
            return True
        if ans in ("n", "no", "k", "khong", "không"):
            return False
        print("Gõ 'y' hoặc 'n' thôi nhé.")


def main():
    print("=" * 70)
    print(" CÔNG CỤ TỰ ĐỘNG TÌM OFFSET MỚI CHO patches.json")
    print("=" * 70)
    print()

    # 1. Tìm patches.json cùng thư mục với script, hoặc thư mục cha
    script_dir = os.path.dirname(os.path.abspath(__file__))
    candidates = [
        os.path.join(script_dir, "patches.json"),
        os.path.join(script_dir, "..", "patches.json"),
    ]
    patches_path = None
    for c in candidates:
        if os.path.isfile(c):
            patches_path = os.path.abspath(c)
            break

    if patches_path is None:
        patches_path = input(
            "Không tự tìm thấy patches.json. Dán đường dẫn đầy đủ tới file patches.json: "
        ).strip().strip('"')

    if not os.path.isfile(patches_path):
        print(f"LỖI: không tìm thấy file {patches_path}")
        sys.exit(1)

    print(f"Đang dùng file cấu hình: {patches_path}\n")

    with open(patches_path, "r", encoding="utf-8") as f:
        data_json = json.load(f)

    profiles = data_json.get("profiles", [])
    if not profiles:
        print("LỖI: patches.json không có profile nào.")
        sys.exit(1)

    # 2. Hỏi đường dẫn file exe của game
    exe_path = input(
        "Dán đường dẫn đầy đủ tới file Warhammer3.exe (bản game hiện tại, CHƯA patch): "
    ).strip().strip('"')

    if not os.path.isfile(exe_path):
        print(f"LỖI: không tìm thấy file {exe_path}")
        sys.exit(1)

    print("\nĐang đọc file game (có thể mất vài giây với file lớn)...")
    with open(exe_path, "rb") as f:
        exe_data = f.read()
    exe_size = len(exe_data)
    exe_sha256 = hashlib.sha256(exe_data).hexdigest()

    print(f"Kích thước file: {exe_size} bytes")
    print(f"SHA-256: {exe_sha256}\n")

    # 3. Chọn 1 profile mẫu để lấy danh sách các patch (before/replace) cần tìm lại
    print("Các profile hiện có trong patches.json:")
    for i, p in enumerate(profiles):
        print(f"  [{i}] {p.get('displayVersion', p.get('name'))}")
    idx_str = input(
        "\nChọn số thứ tự profile GẦN NHẤT với bản game bạn đang có (để dùng làm mẫu byte pattern): "
    ).strip()
    try:
        template_idx = int(idx_str)
        template_profile = profiles[template_idx]
    except (ValueError, IndexError):
        print("Lựa chọn không hợp lệ.")
        sys.exit(1)

    # 4. Quét từng patch trong profile mẫu
    print("\nĐang quét file game để tìm từng đoạn patch...\n")
    new_patches = []
    all_ok = True

    for patch in template_profile["patches"]:
        name = patch["name"]
        before = parse_pattern(patch["before"])
        print(f"--- {name} ---")
        print(f"  {patch.get('description', '')}")

        matches = find_pattern(exe_data, before)

        if len(matches) == 0:
            print("  => KHÔNG tìm thấy. Đoạn code này đã thay đổi thật sự trong bản game mới.")
            print("  => Cần disassembler (x64dbg/Ghidra) để tìm lại, không tự động được.")
            all_ok = False
        elif len(matches) == 1:
            offset = matches[0]
            print(f"  => Tìm thấy 1 vị trí duy nhất: 0x{offset:X}")
            new_patch = dict(patch)  # copy before/replace/replaceOffset/description/name
            new_patch["expectedOffset"] = f"0x{offset:X}"
            new_patches.append(new_patch)
        else:
            print(f"  => Tìm thấy {len(matches)} vị trí (không rõ ràng), cần disassembler để xác nhận.")
            all_ok = False
        print()

    if not all_ok:
        print("=" * 70)
        print("Không thể tự động hoàn tất - một số đoạn patch cần xử lý thủ công.")
        print("Hãy báo lại thông tin ở trên (bản game, kết quả từng patch) lên GitHub Issues để được hỗ trợ.")
        print("=" * 70)
        input("\nNhấn Enter để thoát...")
        sys.exit(0)

    # 5. Tất cả patch đều tìm thấy đúng 1 vị trí -> hỏi tên/version rồi ghi vào JSON
    print("=" * 70)
    print("TẤT CẢ patch đều tìm thấy vị trí mới thành công!")
    print("=" * 70)

    display_version = input(
        "\nNhập tên hiển thị cho bản game này (vd: v8.2.0 Build 12345.6789): "
    ).strip()
    if not display_version:
        display_version = f"Bản tự động {datetime.date.today().isoformat()}"

    new_profile = {
        "name": display_version,
        "displayVersion": display_version,
        "platform": template_profile.get("platform", "Steam"),
        "exeSha256": exe_sha256,
        "exeSize": exe_size,
        "patches": new_patches,
    }

    print("\nProfile mới sẽ được thêm:")
    print(json.dumps(new_profile, indent=2, ensure_ascii=False))

    if not ask_yes_no("\nGhi profile này vào patches.json?"):
        print("Đã huỷ, không ghi gì cả.")
        sys.exit(0)

    # Backup file cũ trước khi ghi
    backup_path = patches_path + f".backup-{datetime.datetime.now().strftime('%Y%m%d-%H%M%S')}"
    shutil.copy2(patches_path, backup_path)
    print(f"\nĐã sao lưu file cũ tại: {backup_path}")

    # Thêm profile mới vào đầu danh sách (ưu tiên match trước)
    data_json["profiles"].insert(0, new_profile)

    with open(patches_path, "w", encoding="utf-8") as f:
        json.dump(data_json, f, indent=2, ensure_ascii=False)

    print(f"Đã ghi thành công vào: {patches_path}")
    print("\nBước tiếp theo: chạy lại '.\\scripts\\Publish-Release.ps1' để build bản patcher mới,")
    print("rồi mở app, Check Status để xác nhận báo xanh.")
    input("\nNhấn Enter để thoát...")


if __name__ == "__main__":
    main()
