"""Check version consistency and the one-patch-per-PR rule."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
V3 = r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)"


def one_version(text, pattern, label):
    matches = re.findall(pattern, text)
    if len(matches) != 1:
        raise ValueError(f"{label}: expected exactly one version")
    return tuple(int(part) for part in matches[0])


def assembly_version(text):
    return one_version(text, r'AssemblyVersion\("' + V3 + r'\.0"\)', "AssemblyVersion")


def validate(root):
    root = Path(root)
    source = (root / "src/UiControls.cs").read_text(encoding="utf-8")
    version = assembly_version(source)
    file_version = one_version(source, r'AssemblyFileVersion\("' + V3 + r'\.0"\)', "AssemblyFileVersion")
    form = (root / "src/MainForm.cs").read_text(encoding="utf-8")
    window_version = one_version(form, r'Text\s*=\s*"打包工具 · V' + V3 + r'"', "window")
    manifest = ET.parse(root / "app.manifest").getroot()
    identity = manifest.find("{urn:schemas-microsoft-com:asm.v1}assemblyIdentity")
    if identity is None:
        raise ValueError("app.manifest: assemblyIdentity missing")
    manifest_version = one_version(identity.get("version", ""), r'^' + V3 + r'\.0$', "app.manifest")
    readme = (root / "README.md").read_text(encoding="utf-8")
    readme_version = one_version(readme, r'源码版本：V' + V3 + r'（', "README")
    ui_test = (root / "tests/UiContractTests.cs").read_text(encoding="utf-8")
    gui_test = (root / "tests/GuiSmokeTests.cs").read_text(encoding="utf-8")
    release_test = (root / "tests/ReleaseUiTests.cs").read_text(encoding="utf-8")
    others = {
        "AssemblyFileVersion": file_version,
        "window": window_version,
        "app.manifest": manifest_version,
        "README": readme_version,
        "UI assembly assertion": one_version(ui_test, r'Version\.ToString\(\)\s*==\s*"' + V3 + r'\.0"', "UI assembly assertion"),
        "UI file assertion": one_version(ui_test, r'FileVersion\s*==\s*"' + V3 + r'\.0"', "UI file assertion"),
        "UI title assertion": one_version(ui_test, r'form\.Text\.Contains\("V' + V3 + r'"\)', "UI title assertion"),
        "GUI title assertion": one_version(gui_test, r'form\.Text\.Contains\("V' + V3 + r'"\)', "GUI title assertion"),
        "Release title assertion": one_version(release_test, r'process\.MainWindowTitle\.Contains\("V' + V3 + r'"\)', "Release title assertion"),
    }
    for label, actual in others.items():
        if actual != version:
            raise ValueError(f"{label} {format_version(actual)} does not match {format_version(version)}")
    return version


def format_version(version):
    return ".".join(str(part) for part in version)


def require_next(base, current):
    expected = (base[0], base[1], base[2] + 1)
    if current != expected:
        raise ValueError(f"PR version must be {format_version(expected)} after {format_version(base)}, got {format_version(current)}")


def git_base_version(root, base):
    if not re.fullmatch(r"[0-9a-fA-F]{40,64}", base):
        raise ValueError("base must be a complete commit SHA")
    result = subprocess.run(["git", "show", base + ":src/UiControls.cs"], cwd=root,
                            check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return assembly_version(result.stdout.decode("utf-8"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", type=Path, default=ROOT)
    parser.add_argument("--base", help="complete main-branch commit SHA")
    args = parser.parse_args()
    try:
        current = validate(args.repo)
        base = args.base
        if base is None and os.environ.get("GITHUB_EVENT_NAME") == "pull_request":
            event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
            base = event["pull_request"]["base"]["sha"]
            if not isinstance(base, str):
                raise ValueError("PR base SHA is missing")
        if base is not None:
            previous = git_base_version(args.repo, base)
            require_next(previous, current)
            print(f"PASS PR version: {format_version(previous)} -> {format_version(current)}")
        else:
            print(f"PASS version consistency: {format_version(current)}")
        return 0
    except (ValueError, OSError, KeyError, TypeError, ET.ParseError, subprocess.CalledProcessError) as error:
        print(f"FAIL version check: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
