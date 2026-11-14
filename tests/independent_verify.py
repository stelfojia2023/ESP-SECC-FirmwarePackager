#!/usr/bin/env python3
"""Independent reader for V1 Windows-generated fixtures; uses only Python stdlib."""
from __future__ import annotations

import hashlib
import struct
import sys
from pathlib import Path

KINDS = ("PFC", "LLC", "M4", "PLC", "ESP32")
SIZES = (1, 4097, 8193, 137, 4095)
BUDGETS = (250 * 1024, 250 * 1024, 512 * 1024, 2 * 1024 * 1024, 5 * 1024 * 1024 // 2)
BUDGET_OFFSETS = (0x001000, 0x040000, 0x07F000, 0x0FF000, 0x2FF000)


def digest(data: bytes | bytearray) -> bytes:
    return hashlib.md5(data).digest()


def read_package(path: Path) -> tuple[bytes, list[dict]]:
    data = path.read_bytes()
    magic, version, head, total, count, entry_size, directory, whole = struct.unpack_from(
        "<8sHHIHHI16s", data
    )
    assert (magic, version, head, entry_size, directory) == (b"SECCPKG1", 1, 4096, 256, 128), path
    assert total == len(data) and total <= 6 * 1024 * 1024 and total % 4096 == 0, path
    assert 1 <= count <= 5, path
    assert not any(data[40:128]) and not any(data[128 + 256 * count : 4096]), path
    zeroed = bytearray(data)
    zeroed[24:40] = bytes(16)
    assert digest(zeroed) == whole, path
    entries = []
    expected_offset = 4096
    previous = 0
    for index in range(count):
        base = 128 + index * 256
        kind, reserved, offset, size, file_md5, name_len, version_len = struct.unpack_from(
            "<HHII16sHH", data, base
        )
        assert 1 <= kind <= 5 and kind > previous and reserved == 0, path
        assert 0 < name_len <= 96 and 0 < version_len <= 64, path
        name = data[base + 32 : base + 32 + name_len].decode("utf-8", "strict")
        version_text = data[base + 128 : base + 128 + version_len].decode("utf-8", "strict")
        assert not any(data[base + 32 + name_len : base + 128]), path
        assert not any(data[base + 128 + version_len : base + 256]), path
        assert offset == expected_offset and offset % 4096 == 0 and size > 0, path
        end = offset + size
        assert end <= total and digest(data[offset:end]) == file_md5, path
        expected_offset = (end + 4095) // 4096 * 4096
        assert data[end:expected_offset] == b"\xff" * (expected_offset - end), path
        entries.append({"kind": kind, "offset": offset, "size": size, "name": name,
                        "version": version_text, "md5": file_md5.hex()})
        previous = kind
    assert expected_offset == total, path
    return data, entries


def check_combinations(root: Path) -> None:
    for mask in range(1, 32):
        data, entries = read_package(root / "组合 包" / f"combo-{mask:02}.bin")
        expected_types = [i for i in range(5) if mask & (1 << i)]
        assert [entry["kind"] for entry in entries] == [i + 1 for i in expected_types]
        for entry, index in zip(entries, expected_types):
            source = (root / "输入 固件" / f"{KINDS[index]} 中文 firmware.bin").read_bytes()
            assert len(source) == SIZES[index]
            assert data[entry["offset"] : entry["offset"] + entry["size"]] == source
            assert entry["name"] == f"{KINDS[index]} 固件 名称.bin"
            assert entry["version"] == "V000B01D15"
    print("PASS independent parser: all 31 combinations, original bytes, headers, offsets, padding and MD5")


def check_budget(root: Path) -> None:
    data, entries = read_package(root / "预算上限.bundle.bin")
    assert len(data) == 0x57F000
    assert [e["offset"] for e in entries] == list(BUDGET_OFFSETS)
    assert [e["size"] for e in entries] == list(BUDGETS)
    for e, kind in zip(entries, KINDS):
        source = (root / "预算上限 输入" / f"{kind}.bin").read_bytes()
        assert data[e["offset"] : e["offset"] + e["size"]] == source
    print("PASS independent parser: budget vector 0x57F000 and five original files")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: independent_verify.py <Windows core test result directory>")
    test_root = Path(sys.argv[1])
    check_combinations(test_root)
    check_budget(test_root)
