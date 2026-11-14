import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import check_version


def write_fixture(root, version):
    (root / "src").mkdir(exist_ok=True)
    (root / "src/UiControls.cs").write_text(
        f'[assembly: System.Reflection.AssemblyVersion("{version}.0")]\n'
        f'[assembly: System.Reflection.AssemblyFileVersion("{version}.0")]\n', encoding="utf-8")
    (root / "src/MainForm.cs").write_text(f'Text = "打包工具 · V{version}";\n', encoding="utf-8")
    (root / "app.manifest").write_text(
        f'<assembly xmlns="urn:schemas-microsoft-com:asm.v1"><assemblyIdentity version="{version}.0" /></assembly>',
        encoding="utf-8")
    (root / "README.md").write_text(f'源码版本：V{version}（未发布）\n', encoding="utf-8")
    (root / "tests").mkdir(exist_ok=True)
    (root / "tests/UiContractTests.cs").write_text(
        f'app.GetName().Version.ToString() == "{version}.0"; FileVersion == "{version}.0"; '
        f'form.Text.Contains("V{version}");', encoding="utf-8")
    (root / "tests/GuiSmokeTests.cs").write_text(f'form.Text.Contains("V{version}");', encoding="utf-8")
    (root / "tests/ReleaseUiTests.cs").write_text(f'process.MainWindowTitle.Contains("V{version}");', encoding="utf-8")


class VersionTests(unittest.TestCase):
    def test_consistent_version_and_mismatch(self):
        with tempfile.TemporaryDirectory(prefix="version-check-") as tmp:
            root = Path(tmp)
            write_fixture(root, "0.0.5")
            self.assertEqual(check_version.validate(root), (0, 0, 5))
            (root / "src/MainForm.cs").write_text('Text = "打包工具 · V0.0.4";\n', encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "window"):
                check_version.validate(root)

    def test_increment_is_exactly_one_patch(self):
        check_version.require_next((0, 0, 4), (0, 0, 5))
        for current in ((0, 0, 4), (0, 0, 6), (0, 1, 0), (1, 0, 5)):
            with self.subTest(current=current), self.assertRaises(ValueError):
                check_version.require_next((0, 0, 4), current)

    def test_missing_or_duplicate_version_fails(self):
        with tempfile.TemporaryDirectory(prefix="version-check-") as tmp:
            root = Path(tmp)
            write_fixture(root, "0.0.5")
            source = root / "src/UiControls.cs"
            source.write_text(source.read_text(encoding="utf-8") + '[assembly: AssemblyVersion("0.0.5.0")]',
                              encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "exactly one"):
                check_version.validate(root)

    def test_manifest_readme_and_test_assertion_mismatch_fails(self):
        for path in ("app.manifest", "README.md", "tests/ReleaseUiTests.cs", "src/UiControls.cs"):
            with self.subTest(path=path), tempfile.TemporaryDirectory(prefix="version-check-") as tmp:
                root = Path(tmp)
                write_fixture(root, "0.0.5")
                target = root / path
                data = target.read_text(encoding="utf-8")
                if path == "src/UiControls.cs":
                    data = data.replace('AssemblyFileVersion("0.0.5.0")', 'AssemblyFileVersion("0.0.4.0")')
                else:
                    data = data.replace("0.0.5", "0.0.4")
                target.write_text(data, encoding="utf-8")
                with self.assertRaises(ValueError):
                    check_version.validate(root)

    def test_pr_event_against_real_git_base(self):
        with tempfile.TemporaryDirectory(prefix="version-git-") as tmp:
            root = Path(tmp)
            def git(*args):
                return subprocess.check_output(["git", "-c", "user.name=CI Test", "-c",
                    "user.email=ci-test@example.invalid", *args], cwd=root, stderr=subprocess.STDOUT)
            git("init", "-q")
            write_fixture(root, "0.0.4")
            git("add", ".")
            git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD").decode().strip()
            event = root / "event.json"
            event.write_text(json.dumps({"pull_request": {"base": {"sha": base}}}), encoding="utf-8")
            env = os.environ.copy()
            env.update({"GITHUB_EVENT_NAME": "pull_request", "GITHUB_EVENT_PATH": str(event)})
            command = [sys.executable, str(check_version.ROOT / "tests/check_version.py"), "--repo", str(root)]
            write_fixture(root, "0.0.5")
            passed = subprocess.run(command, env=env, capture_output=True, text=True)
            self.assertEqual(passed.returncode, 0, passed.stderr)
            self.assertIn("0.0.4 -> 0.0.5", passed.stdout)
            write_fixture(root, "0.0.4")
            failed = subprocess.run(command, env=env, capture_output=True, text=True)
            self.assertNotEqual(failed.returncode, 0)
            self.assertIn("must be 0.0.5", failed.stderr)
            event.write_text("{}", encoding="utf-8")
            self.assertNotEqual(subprocess.run(command, env=env, capture_output=True).returncode, 0)
            event.write_text(json.dumps({"pull_request": {"base": {"sha": None}}}), encoding="utf-8")
            self.assertNotEqual(subprocess.run(command, env=env, capture_output=True).returncode, 0)


if __name__ == "__main__":
    unittest.main()
