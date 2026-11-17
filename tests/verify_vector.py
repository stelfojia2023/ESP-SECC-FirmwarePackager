#!/usr/bin/env python3
"""Check the delivered synthetic vector against separately stated expected values."""
import hashlib
import json
import sys
from pathlib import Path

from independent_verify import read_package


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify_one(root: Path, expected_file: str) -> None:
    expected = json.loads((root / expected_file).read_text(encoding="utf-8"))
    data, actual_entries = read_package(root / expected["bundle_file"])
    assert len(data) == expected["total_bytes"]
    assert hex(len(data)) == expected["total_hex"]
    assert data[24:40].hex() == expected["whole_md5_field_zeroed"]
    assert hashlib.md5(data).hexdigest() == expected["ordinary_final_file_md5"]
    assert sha256(data) == expected["bundle_sha256"]
    assert len(actual_entries) == len(expected["entries"])
    for actual, wanted in zip(actual_entries, expected["entries"]):
        for key in ("kind", "offset", "size", "name", "version", "md5"):
            assert actual[key] == wanted[key], key
        source = (root / "inputs" / (wanted["type"] + ".bin")).read_bytes()
        assert sha256(source) == wanted["input_sha256"]
        assert data[actual["offset"]:actual["offset"] + actual["size"]] == source
    print(f"PASS delivered vector {expected['bundle_file']}: independent header, original firmware, padding, MD5 and SHA-256 checks")


def verify(root: Path) -> None:
    for expected_file in ("expected.json", "expected-five.json"):
        verify_one(root, expected_file)
    legacy = root.parent / "tests/fixtures/legacy-v1-directory-0x80.bin"
    try:
        read_package(legacy)
    except AssertionError:
        print("PASS independent parser rejects actual legacy 0x80-directory package")
    else:
        raise AssertionError("Legacy package unexpectedly accepted")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("Usage: verify_vector.py <test-vectors directory>")
    verify(Path(sys.argv[1]))
