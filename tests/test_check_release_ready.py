import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


CHECKER = Path(__file__).resolve().with_name("check_release_ready.py")


def make_tree(root, source_version="0.0.13", mode="开发构建", tag="v0.0.12",
              link_tag=None, executable=b"synthetic-exe", hash_override=None, notes=True):
    root = Path(root)
    (root / "src").mkdir(parents=True)
    (root / "src/UiControls.cs").write_text(
        '[assembly: System.Reflection.AssemblyVersion("' + source_version + '.0")]\n'
        '[assembly: System.Reflection.AssemblyFileVersion("' + source_version + '.0")]\n',
        encoding="utf-8")
    (root / ".github").mkdir()
    digest = hash_override or hashlib.sha256(executable).hexdigest()
    (root / ".github/release.json").write_text(json.dumps({
        "tag": tag,
        "title": "V" + tag[1:] + " · 固件联合打包工具",
        "exe_sha256": digest,
    }), encoding="utf-8")
    link = link_tag or tag
    (root / "README.md").write_text(
        "源码版本：V" + source_version + "（" + mode + "）。\n\n"
        "[下载 V" + link[1:] + " EXE](https://github.com/example/repo/releases/download/" +
        link + "/FirmwarePackager.exe)\n",
        encoding="utf-8")
    if notes:
        (root / "docs/releases").mkdir(parents=True)
        (root / "docs/releases" / (tag + ".md")).write_text("notes\n", encoding="utf-8")
    (root / "dist").mkdir()
    (root / "dist/FirmwarePackager.exe").write_bytes(executable)


def run(root):
    return subprocess.run([sys.executable, str(CHECKER), "--repo", str(root)],
                          stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)


class ReleaseReadyTests(unittest.TestCase):
    def test_development_tree_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp)
            result = run(tmp)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn("PASS release metadata", result.stdout)

    def test_release_tree_passes(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, source_version="0.0.12", mode="发布构建", tag="v0.0.12")
            result = run(tmp)
            self.assertEqual(result.returncode, 0, result.stderr)

    def test_hash_mismatch_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, hash_override="0" * 64)
            result = run(tmp)
            self.assertEqual(result.returncode, 1)
            self.assertIn("SHA-256", result.stderr)

    def test_missing_release_notes_fails(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, notes=False)
            result = run(tmp)
            self.assertEqual(result.returncode, 1)
            self.assertIn("release notes", result.stderr)

    def test_readme_link_must_match_tag(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, link_tag="v0.0.11")
            result = run(tmp)
            self.assertEqual(result.returncode, 1)
            self.assertIn("download link", result.stderr)

    def test_development_version_must_be_newer(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, source_version="0.0.12", tag="v0.0.12")
            result = run(tmp)
            self.assertEqual(result.returncode, 1)
            self.assertIn("newer", result.stderr)

    def test_release_version_must_equal_tag(self):
        with tempfile.TemporaryDirectory() as tmp:
            make_tree(tmp, source_version="0.0.13", mode="发布构建", tag="v0.0.12")
            result = run(tmp)
            self.assertEqual(result.returncode, 1)
            self.assertIn("does not match release.json tag", result.stderr)


if __name__ == "__main__":
    unittest.main()
