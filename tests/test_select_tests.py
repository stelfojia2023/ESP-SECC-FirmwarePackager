import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("select_tests", ROOT / ".github" / "scripts" / "select_tests.py")
scope = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(scope)


class SelectionTests(unittest.TestCase):
    def test_document_only_avoids_windows_suites(self):
        self.assertEqual(scope.select(["README.md", "docs/测试用例.md", "AGENTS.md"]), set())

    def test_unions_affected_groups(self):
        selected = scope.select(["src/FtpUploader.cs", "src/FilenameRules.cs"])
        self.assertEqual(selected, {"ftp", "filename", "header", "ui", "desktop"})
        self.assertNotIn("core", selected)

    def test_package_changes_cover_consumers(self):
        selected = scope.select(["src/Packager.cs"])
        self.assertEqual(selected, {"filename", "core", "ftp", "header", "ui", "desktop"})

    def test_ui_changes_cover_appearance_and_font(self):
        selected = scope.select(["src/MainForm.cs"])
        self.assertEqual(selected, {"header", "ui", "appearance", "typography", "desktop"})

    def test_tests_and_fixtures_select_their_own_suites(self):
        self.assertEqual(scope.select(["tests/FtpTests.cs"]), {"ftp", "desktop"})
        self.assertEqual(scope.select(["test-vectors/inputs/M4.bin"]), {"ftp", "vector", "desktop"})
        self.assertEqual(scope.select(["tests/ReleaseUiTests.cs"]), {"desktop"})
        self.assertEqual(scope.select([".github/scripts/publish_release.py"]), {"publisher"})

    def test_unknown_empty_and_configuration_changes_are_full(self):
        for paths in ([], ["new-module/file.cs"], ["build.ps1"],
                      [".github/workflows/windows-build.yml"], ["tests/run-selected.ps1"]):
            with self.subTest(paths=paths):
                self.assertEqual(scope.select(paths), scope.FULL)

    def test_full_is_forced_for_release_and_manual_runs(self):
        base = "a" * 40
        self.assertIsNone(scope.event_base("push", {"before": base, "head_commit": {"message": "Release v0.0.5"}}))
        self.assertIsNone(scope.event_base("workflow_dispatch", {}))
        self.assertIsNone(scope.event_base("push", {"before": "0" * 40}))
        self.assertIsNone(scope.event_base("pull_request", {"pull_request": {"base": []}}))
        self.assertEqual(scope.select(["README.md"], True), scope.FULL)

    def test_git_diff_includes_both_sides_of_a_rename(self):
        with tempfile.TemporaryDirectory(prefix="ci-selection-") as tmp:
            root = Path(tmp)
            def git(*args):
                return subprocess.check_output(["git", "-c", "user.name=CI Test", "-c",
                    "user.email=ci-test@example.invalid", *args], cwd=root, stderr=subprocess.STDOUT)
            git("init", "-q")
            (root / "旧说明.md").write_text("old", encoding="utf-8")
            git("add", ".")
            git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD").decode().strip()
            git("mv", "旧说明.md", "new.txt")
            git("commit", "-qm", "rename")
            paths = scope.changed_paths(root, base)
            self.assertEqual(set(paths), {"旧说明.md", "new.txt"})
            self.assertEqual(scope.select(paths), scope.FULL)
            (root / "new.txt").write_text("unstaged", encoding="utf-8")
            (root / "new-file.bin").write_bytes(b"fixture")
            local = scope.changed_paths(root, base, include_worktree=True)
            self.assertIn("new-file.bin", local)
            self.assertIn("new.txt", local)
            groups, reason = scope.from_event(root, "push", {"before": "f" * 40, "head_commit": {"message": "docs"}})
            self.assertEqual(groups, scope.FULL)
            self.assertEqual(reason, "unable to determine changed files")

    def test_github_outputs_for_docs_ftp_and_release(self):
        with tempfile.TemporaryDirectory(prefix="ci-output-") as tmp:
            root = Path(tmp)
            def git(*args):
                return subprocess.check_output(["git", "-c", "user.name=CI Test", "-c",
                    "user.email=ci-test@example.invalid", *args], cwd=root, stderr=subprocess.STDOUT)
            def run_event(base, message):
                event = root / "event.json"
                output = root / "github-output.txt"
                event.write_text(json.dumps({"before": base, "head_commit": {"message": message}}), encoding="utf-8")
                output.write_text("", encoding="utf-8")
                env = os.environ.copy()
                env.update({"GITHUB_EVENT_NAME": "push", "GITHUB_EVENT_PATH": str(event), "GITHUB_OUTPUT": str(output)})
                env.pop("GITHUB_STEP_SUMMARY", None)
                subprocess.run([sys.executable, str(ROOT / ".github/scripts/select_tests.py"), "--repo", str(root)],
                               env=env, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
                return output.read_text(encoding="utf-8")
            git("init", "-q")
            (root / "README.md").write_text("one", encoding="utf-8")
            (root / "src").mkdir()
            (root / "src/FtpUploader.cs").write_text("one", encoding="utf-8")
            git("add", "."); git("commit", "-qm", "base")
            base = git("rev-parse", "HEAD").decode().strip()
            (root / "README.md").write_text("two", encoding="utf-8")
            git("add", "README.md"); git("commit", "-qm", "docs")
            self.assertEqual(run_event(base, "docs"), "groups=\nbuild=false\n")
            base = git("rev-parse", "HEAD").decode().strip()
            (root / "src/FtpUploader.cs").write_text("two", encoding="utf-8")
            git("add", "src/FtpUploader.cs"); git("commit", "-qm", "ftp")
            self.assertEqual(run_event(base, "ftp"), "groups=ftp,header,ui,desktop\nbuild=true\n")
            self.assertEqual(run_event(base, "Release v0.0.5"),
                             "groups=" + ",".join(scope.GROUPS) + "\nbuild=true\n")


if __name__ == "__main__":
    unittest.main()
