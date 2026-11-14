import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("publisher", ROOT / ".github" / "scripts" / "publish_release.py")
publisher = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(publisher)


class FakeGitHub:
    def __init__(self):
        self.release = None
        self.ref = None
        self.payloads = {}
        self.calls = []
        self.bad_metadata = False
        self.bad_download = False

    def releases(self):
        return [] if self.release is None else [self.release.copy()]

    def tag_target(self, tag):
        return self.ref

    def create_draft(self, tag, source, title, notes, files):
        self.calls.append("create")
        self.release = {"id": 101, "tag_name": tag, "name": title, "target_commitish": source,
                        "draft": True, "prerelease": False, "body": notes.read_text(encoding="utf-8")}
        self.payloads = {p.name: p.read_bytes() for p in files}

    def assets(self, release_id):
        result = []
        for name, data in self.payloads.items():
            checksum = publisher.digest(data)
            if self.bad_metadata and name == "FirmwarePackager.exe":
                checksum = "0" * 64
            result.append({"name": name, "state": "uploaded", "size": len(data), "digest": "sha256:" + checksum})
        return result

    def upload(self, tag, paths):
        self.calls.append("upload")
        for path in paths:
            if path.name in self.payloads:
                raise AssertionError("Existing asset must not be replaced")
            self.payloads[path.name] = path.read_bytes()

    def publish(self, release_id):
        self.calls.append("publish")
        self.release["draft"] = False
        self.ref = self.release["target_commitish"]
        return self.release.copy()

    def download(self, tag, directory):
        self.calls.append("download")
        for name, data in self.payloads.items():
            if self.bad_download and name == "FirmwarePackager.exe":
                data += b"bad"
            (directory / name).write_bytes(data)


class PublisherTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.exe = self.root / "FirmwarePackager.exe"
        self.exe.write_bytes(b"synthetic release executable")
        self.notes = self.root / "notes.md"
        self.notes.write_text("Test release notes\n", encoding="utf-8")
        self.source = "a" * 40
        self.manifest = {"tag": "v0.0.2", "title": "V0.0.2 test", "exe_sha256": publisher.digest(self.exe.read_bytes())}
        self.client = FakeGitHub()

    def tearDown(self):
        self.temp.cleanup()

    def run_publish(self, message="Release v0.0.2"):
        with tempfile.TemporaryDirectory(dir=self.root) as tmp:
            return publisher.publish(self.client, self.source, message, self.manifest, self.exe, self.notes, Path(tmp))

    def test_create_verify_publish_download_and_idempotence(self):
        tag, checksum = self.run_publish()
        self.assertEqual((tag, checksum), ("v0.0.2", self.manifest["exe_sha256"]))
        self.assertEqual(self.client.calls, ["create", "publish", "download"])
        self.assertFalse(self.client.release["draft"])
        self.assertEqual(self.client.ref, self.source)
        self.run_publish()
        self.assertEqual(self.client.calls, ["create", "publish", "download", "download"])

    def test_existing_tag_or_unrelated_draft_is_not_changed(self):
        self.client.ref = "b" * 40
        with self.assertRaises(publisher.PublishError):
            self.run_publish()
        self.assertEqual(self.client.calls, [])
        self.client.ref = None
        self.client.release = {"id": 1, "tag_name": "v0.0.2", "name": "someone else's draft",
                               "target_commitish": self.source, "draft": True, "prerelease": False, "body": "other"}
        with self.assertRaises(publisher.PublishError):
            self.run_publish()
        self.assertEqual(self.client.calls, [])

    def test_partial_matching_draft_resumes_without_overwrite(self):
        self.run_publish()
        self.client.calls.clear()
        self.client.release["draft"] = True
        self.client.ref = None
        del self.client.payloads["SHA256SUMS.txt"]
        self.run_publish()
        self.assertEqual(self.client.calls, ["upload", "publish", "download"])

    def test_bad_asset_metadata_blocks_publication(self):
        self.client.bad_metadata = True
        with self.assertRaises(publisher.PublishError):
            self.run_publish()
        self.assertEqual(self.client.calls, ["create"])
        self.assertTrue(self.client.release["draft"])

    def test_wrong_input_never_contacts_github(self):
        with self.assertRaises(publisher.PublishError):
            self.run_publish("other message")
        self.assertEqual(self.client.calls, [])
        self.exe.write_bytes(b"changed")
        with self.assertRaises(publisher.PublishError):
            self.run_publish()
        self.assertEqual(self.client.calls, [])

    def test_failed_download_is_not_reported_as_success(self):
        self.client.bad_download = True
        with self.assertRaises(publisher.PublishError):
            self.run_publish()
        self.assertEqual(self.client.calls, ["create", "publish", "download"])

    def test_cli_error_redacts_token(self):
        error = mock.Mock(returncode=1, stdout="", stderr="synthetic-token ?token=temporary-value")
        with mock.patch.dict(os.environ, {"GH_TOKEN": "synthetic-token"}), mock.patch.object(publisher.subprocess, "run", return_value=error):
            with self.assertRaises(publisher.PublishError) as caught:
                publisher.GitHubCli("example/repo").run("api", "example")
        self.assertNotIn("synthetic-token", str(caught.exception))
        self.assertNotIn("temporary-value", str(caught.exception))


if __name__ == "__main__":
    unittest.main()
