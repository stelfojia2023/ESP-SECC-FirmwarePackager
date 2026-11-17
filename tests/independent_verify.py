#!/usr/bin/env python3
"""Independent reader for V1 Windows-generated fixtures; uses only Python stdlib."""
from __future__ import annotations

import hashlib
import struct
import sys
import unicodedata
from pathlib import Path

KINDS = ("PFC", "LLC", "M4", "PLC", "ESP32")
SIZES = (1, 4097, 8193, 137, 4095)
BUDGETS = (250 * 1024, 250 * 1024, 512 * 1024, 2 * 1024 * 1024, 5 * 1024 * 1024 // 2)
BUDGET_OFFSETS = (0x001000, 0x040000, 0x07F000, 0x0FF000, 0x2FF000)


def digest(data: bytes | bytearray) -> bytes:
    return hashlib.md5(data).digest()


def read_package(path: Path) -> tuple[bytes, list[dict]]:
    data = path.read_bytes()
    assert len(data) >= 4096, path
    magic, version, head, total, count, entry_size, directory, whole = struct.unpack_from(
        "<8sHHIHHI16s", data
    )
    assert (magic, version, head, entry_size, directory) == (b"SECCPKG1", 1, 4096, 256, 0x28), path
    assert total == len(data) and total <= 6 * 1024 * 1024 and total % 4096 == 0, path
    assert 1 <= count <= 5, path
    assert not any(data[0x28 + 256 * count : 4096]), path
    zeroed = bytearray(data)
    zeroed[24:40] = bytes(16)
    assert digest(zeroed) == whole, path
    entries = []
    expected_offset = 4096
    previous = 0
    for index in range(count):
        base = 0x28 + index * 256
        kind, name_len, offset, size, file_md5, version_len = struct.unpack_from(
            "<HHII16sH", data, base
        )
        assert 1 <= kind <= 5 and kind > previous, path
        assert 0 < name_len <= 39 and 0 < version_len <= 39, path
        name = data[base + 0x1E : base + 0x1E + name_len].decode("utf-8", "strict")
        version_text = data[base + 0x46 : base + 0x46 + version_len].decode("utf-8", "strict")
        assert name.strip() and version_text.strip(), path
        assert not any(unicodedata.category(c) == "Cc" for c in name + version_text), path
        assert not any(data[base + 0x1E + name_len : base + 0x46]), path
        assert not any(data[base + 0x46 + version_len : base + 0x6E]), path
        assert not any(data[base + 0x6E : base + 0x100]), path
        assert offset == expected_offset and offset % 4096 == 0 and size > 0, path
        assert kind != 5 or size <= BUDGETS[4], path
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


def check_limits(root: Path) -> None:
    for kind, limit in zip(KINDS, BUDGETS):
        data, entries = read_package(root / f"limit-{kind}.bin")
        assert len(entries) == 1 and entries[0]["size"] == limit
        if kind != "ESP32":
            _, entries = read_package(root / f"over-budget-{kind}.bin")
            assert len(entries) == 1 and entries[0]["size"] == limit + 1
    assert not (root / "rejected-ESP32.bin").exists()
    data, entries = read_package(root / "exact-6MiB.bin")
    assert len(data) == 6 * 1024 * 1024 and len(entries) == 1
    print("PASS independent parser: soft budgets, ESP32 limit and exact 6 MiB package")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: independent_verify.py <Windows core test result directory>")
    test_root = Path(sys.argv[1])
    check_combinations(test_root)
    check_budget(test_root)
    check_limits(test_root)

