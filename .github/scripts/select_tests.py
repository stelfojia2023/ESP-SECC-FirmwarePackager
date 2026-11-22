"""Choose conservative CI test groups from the effective Git diff."""

import argparse
import json
import os
from pathlib import Path
import re
import subprocess


GROUPS = ("filename", "core", "ftp", "header", "ui", "appearance",
          "typography", "vector", "publisher", "local-upgrade", "desktop")
FULL = frozenset(GROUPS)
NEEDS_EXE = frozenset(("filename", "core", "ftp", "header", "ui",
                       "appearance", "typography", "local-upgrade", "desktop"))

PATH_GROUPS = {
    "src/Packager.cs": {"filename", "core", "ftp", "header", "ui", "local-upgrade", "desktop"},
    "src/FilenameRules.cs": {"filename", "header", "ui", "desktop"},
    "src/FtpUploader.cs": {"ftp", "header", "ui", "desktop"},
    "src/HeaderViewer.cs": {"header", "ui", "appearance", "typography", "desktop"},
    "src/MainForm.cs": {"header", "ui", "appearance", "typography", "local-upgrade", "desktop"},
    "src/UiControls.cs": {"header", "ui", "appearance", "typography", "desktop"},
    "src/UpgradePage.cs": {"header", "ui", "appearance", "typography", "local-upgrade", "desktop"},
    "src/LocalUpgrade.cs": {"local-upgrade", "desktop"},
    "src/UpgradeTimeoutDialog.cs": {"ui", "appearance", "typography", "local-upgrade", "desktop"},
    "tests/FilenameRulesTests.cs": {"filename"},
    "tests/CoreTests.cs": {"core"},
    "tests/NewFormatContractTests.cs": {"core"},
    "tests/FtpTests.cs": {"ftp", "desktop"},
    "tests/HeaderViewerTests.cs": {"header"},
    "tests/UiContractTests.cs": {"ui"},
    "tests/AppearanceTests.cs": {"appearance"},
    "tests/TypographyTests.cs": {"typography"},
    "tests/independent_verify.py": {"core"},
    "tests/verify_vector.py": {"vector"},
    "tests/test_release_publisher.py": {"publisher"},
    ".github/scripts/publish_release.py": {"publisher"},
    "tests/GuiSmokeTests.cs": {"desktop"},
    "tests/ReleaseUiTests.cs": {"desktop"},
    "tests/ReleaseFtpDirectoryTests.cs": {"desktop"},
    "tests/IconPreservationTests.cs": {"desktop"},
    "tests/LocalUpgradeProtocolTests.cs": {"local-upgrade"},
    "tests/run-local-upgrade-protocol.ps1": {"local-upgrade"},
    "tests/LocalUpgradeUiTests.cs": {"desktop"},
    "tests/run-local-upgrade-ui.ps1": {"desktop"},
    "tests/check_docs.py": set(),
    "tests/test_check_docs.py": set(),
}


def select(paths, force_full=False, manifest_version_only=False):
    """Return the union of affected groups; unknown or empty inputs run all."""
    if force_full or not paths:
        return FULL
    selected = set()
    for path in paths:
        if path in PATH_GROUPS:
            selected.update(PATH_GROUPS[path])
        elif path == "app.manifest" and manifest_version_only:
            selected.update(("header", "ui", "appearance", "typography", "desktop"))
        elif path.endswith(".md"):
            continue
        elif path.startswith("test-vectors/"):
            selected.update(("ftp", "vector", "desktop"))
        elif path.startswith("assets/sounds/") and path.endswith(".wav"):
            selected.update(("ui", "desktop"))
        else:
            return FULL
    return frozenset(selected)


def manifest_only_changes_version(repo, base, include_worktree=False):
    """Allow a narrow selection only when the manifest differs solely by identity version."""
    try:
        old = subprocess.check_output(["git", "show", base + ":app.manifest"], cwd=repo, stderr=subprocess.PIPE)
        if include_worktree:
            new = (Path(repo) / "app.manifest").read_bytes()
        else:
            new = subprocess.check_output(["git", "show", "HEAD:app.manifest"], cwd=repo, stderr=subprocess.PIPE)
        pattern = rb'(<assemblyIdentity\s+version=")[^"]+(")'
        old_normalized, old_count = re.subn(pattern, rb'\g<1>VERSION\2', old)
        new_normalized, new_count = re.subn(pattern, rb'\g<1>VERSION\2', new)
        return old != new and old_count == new_count == 1 and old_normalized == new_normalized
    except (OSError, subprocess.CalledProcessError):
        return False


def event_base(event_name, event):
    """Find a base commit; release, dispatch and malformed events run all."""
    try:
        if event_name == "push":
            message = (event.get("head_commit") or {}).get("message", "")
            if message.startswith("Release v"):
                return None
            base = event.get("before")
        elif event_name == "pull_request":
            base = (event.get("pull_request") or {}).get("base", {}).get("sha")
        else:
            return None
    except (AttributeError, TypeError):
        return None
    if not isinstance(base, str) or not re.fullmatch(r"[0-9a-fA-F]{40,64}", base):
        return None
    return None if set(base) == {"0"} else base


def changed_paths(repo, base, head="HEAD", include_worktree=False):
    revisions = [base] if include_worktree else [base, head]
    result = subprocess.run(
        ["git", "diff", "--name-only", "--no-renames", "-z", *revisions, "--"],
        cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
    )
    paths = {os.fsdecode(item) for item in result.stdout.split(b"\0") if item}
    if include_worktree:
        extra = subprocess.run(
            ["git", "ls-files", "--others", "--exclude-standard", "-z"],
            cwd=repo, stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True,
        )
        paths.update(os.fsdecode(item) for item in extra.stdout.split(b"\0") if item)
    return sorted(paths)


def from_event(repo, event_name, event):
    base = event_base(event_name, event)
    if base is None:
        return FULL, "release, manual run or unavailable base"
    try:
        paths = changed_paths(repo, base)
    except (OSError, subprocess.CalledProcessError):
        return FULL, "unable to determine changed files"
    version_only = "app.manifest" in paths and manifest_only_changes_version(repo, base)
    return select(paths, manifest_version_only=version_only), "effective diff" if paths else "empty diff"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--repo", default=".")
    parser.add_argument("--base", help="compare this commit to HEAD for local selection")
    args = parser.parse_args()
    if args.base:
        try:
            paths = changed_paths(args.repo, args.base, include_worktree=True)
            version_only = "app.manifest" in paths and manifest_only_changes_version(args.repo, args.base, True)
            groups = select(paths, manifest_version_only=version_only)
            reason = "effective diff"
        except (OSError, subprocess.CalledProcessError):
            groups, reason = FULL, "unable to determine changed files"
    else:
        try:
            event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
            groups, reason = from_event(args.repo, os.environ.get("GITHUB_EVENT_NAME", ""), event)
        except (OSError, KeyError, ValueError, TypeError):
            groups, reason = FULL, "unavailable event metadata"
    ordered = [group for group in GROUPS if group in groups]
    group_text = ",".join(ordered)
    build = bool(groups & NEEDS_EXE)
    print("Selected groups: " + (group_text or "documentation only") + "; reason: " + reason)
    print("Windows EXE build: " + ("yes" if build else "no"))
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        with open(output, "a", encoding="utf-8") as f:
            f.write("groups=" + group_text + "\n")
            f.write("build=" + ("true" if build else "false") + "\n")
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as f:
            f.write("### Test selection\n\n")
            f.write("- Groups: " + (group_text or "documentation only") + "\n")
            f.write("- Reason: " + reason + "\n")
            if "desktop" in groups:
                f.write("- Native desktop tests: compiled only in CI; run affected tests on Windows.\n")


if __name__ == "__main__":
    main()
