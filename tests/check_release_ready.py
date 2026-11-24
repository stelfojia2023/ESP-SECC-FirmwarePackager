"""Check that release metadata, README, release notes and dist EXE agree.

Runs before pushing a release commit so the publish workflow does not fail
after the fact. Works for both a development tree (source version ahead of the
last release) and a release commit (source version equal to the release tag).
"""

import argparse
import hashlib
import json
import re
from pathlib import Path
import sys


V3 = r"(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)"
DOWNLOAD_LINK = re.compile(
    r"\[下载 V(" + V3 + r") EXE\]\(https://github\.com/[^)]*/releases/download/v(" + V3 +
    r")/FirmwarePackager\.exe\)"
)


def version_tuple(text):
    return tuple(int(part) for part in text.split("."))


def validate(root):
    root = Path(root)
    source = (root / "src/UiControls.cs").read_text(encoding="utf-8")
    match = re.search(r'AssemblyVersion\("(' + V3 + r')\.0"\)', source)
    if not match:
        raise ValueError("src/UiControls.cs: AssemblyVersion missing")
    version = match.group(1)

    manifest = json.loads((root / ".github/release.json").read_text(encoding="utf-8"))
    tag = manifest.get("tag", "")
    title = manifest.get("title", "")
    expected_hash = manifest.get("exe_sha256", "")
    if not re.fullmatch(r"v" + V3, tag):
        raise ValueError("release.json: invalid tag")
    if not title or len(title) > 120:
        raise ValueError("release.json: invalid title")
    if not re.fullmatch(r"[0-9a-f]{64}", expected_hash):
        raise ValueError("release.json: invalid exe_sha256")
    tag_version = tag[1:]

    readme = (root / "README.md").read_text(encoding="utf-8")
    source_line = re.search(r"源码版本：V(" + V3 + r")（(发布构建|开发构建)）", readme)
    if not source_line:
        raise ValueError("README: source version line missing")
    if source_line.group(1) != version:
        raise ValueError("README source version does not match AssemblyVersion")
    published = source_line.group(2) == "发布构建"

    link = DOWNLOAD_LINK.search(readme)
    if not link:
        raise ValueError("README: release download link missing")
    if link.group(1) != tag_version or link.group(2) != tag_version:
        raise ValueError("README download link does not match release.json tag")

    notes = root / "docs" / "releases" / (tag + ".md")
    if not notes.is_file():
        raise ValueError("missing release notes: " + notes.relative_to(root).as_posix())

    executable = root / "dist/FirmwarePackager.exe"
    if not executable.is_file() or executable.stat().st_size == 0:
        raise ValueError("dist/FirmwarePackager.exe is missing or empty")
    actual_hash = hashlib.sha256(executable.read_bytes()).hexdigest()
    if actual_hash != expected_hash:
        raise ValueError("dist/FirmwarePackager.exe SHA-256 does not match release.json")

    if published:
        if tag_version != version:
            raise ValueError("release build version does not match release.json tag")
    elif version_tuple(tag_version) >= version_tuple(version):
        raise ValueError("development source version must be newer than the last release")
    return version, tag, actual_hash


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        version, tag, digest = validate(args.repo)
    except (ValueError, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        print("FAIL release metadata: " + str(error), file=sys.stderr)
        return 1
    print("PASS release metadata: source V" + version + ", last release " + tag +
          ", EXE SHA-256 " + digest)
    return 0


if __name__ == "__main__":
    sys.exit(main())
