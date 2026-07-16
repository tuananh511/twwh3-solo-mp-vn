"""
find_offset.py - Tìm offset mới của một byte pattern (có thể chứa wildcard ??)
bên trong file Warhammer3.exe, dùng khi game vừa update bản mới.

Cách dùng:
    python find_offset.py "duong/dan/Warhammer3.exe" "48 8B 8F 60 0B 0E 00 48 8B 01 FF 90 B0 01 00 00 83 F8 03 8B CE 48 8B 87 10 0B 0E 00 0F 94 C1 FF C1 8B 90 04 28 00 00 3B D1"

Kết quả: in ra tất cả các vị trí (offset, dạng hex) tìm thấy pattern đó trong file.
Nếu chỉ tìm thấy đúng 1 vị trí -> đó chính là expectedOffset mới cần điền vào patches.json.
Nếu tìm thấy 0 hoặc nhiều hơn 1 vị trí -> đoạn code thật sự đã đổi, cần dùng disassembler
(x64dbg/Ghidra) để tìm lại pattern tương đương trong bản mới.
"""

import sys


def parse_pattern(pattern_str):
    """Chuyển chuỗi "48 8B ?? 60" thành list các giá trị byte (None = wildcard)."""
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
    # Byte đầu tiên của pattern không nên là wildcard để tìm nhanh hơn,
    # nhưng ở đây cứ quét đơn giản cho chắc ăn (đủ nhanh cho file vài trăm MB).
    for i in range(n - m + 1):
        ok = True
        for j in range(m):
            if pattern[j] is not None and data[i + j] != pattern[j]:
                ok = False
                break
        if ok:
            matches.append(i)
    return matches


def main():
    if len(sys.argv) != 3:
        print("Cách dùng: python find_offset.py <duong_dan_exe> \"<byte_pattern>\"")
        sys.exit(1)

    exe_path = sys.argv[1]
    pattern_str = sys.argv[2]

    pattern = parse_pattern(pattern_str)

    print(f"Đang đọc file: {exe_path} ...")
    with open(exe_path, "rb") as f:
        data = f.read()

    print(f"Kích thước file: {len(data)} bytes")
    print(f"Đang tìm pattern dài {len(pattern)} byte (có {pattern.count(None)} wildcard) ...")

    matches = find_pattern(data, pattern)

    if not matches:
        print("\nKHÔNG tìm thấy pattern này trong file.")
        print("=> Đoạn code thật sự đã thay đổi ở bản này, không chỉ dịch chuyển vị trí.")
        print("=> Cần dùng disassembler (x64dbg / Ghidra) để tìm đoạn code tương đương mới.")
    elif len(matches) == 1:
        offset = matches[0]
        print(f"\nTÌM THẤY DUY NHẤT 1 vị trí khớp!")
        print(f"Offset (hex): 0x{offset:X}")
        print(f"Offset (dec): {offset}")
        print("\n=> Dùng giá trị hex này làm 'expectedOffset' mới trong patches.json.")
    else:
        print(f"\nTìm thấy {len(matches)} vị trí khớp (không rõ vị trí nào đúng):")
        for off in matches:
            print(f"  - 0x{off:X}")
        print("\n=> Pattern không đủ 'đặc trưng' (quá ngắn hoặc quá nhiều wildcard).")
        print("=> Cần disassembler để xác nhận đúng vị trí nào là hàm cần patch.")


if __name__ == "__main__":
    main()
