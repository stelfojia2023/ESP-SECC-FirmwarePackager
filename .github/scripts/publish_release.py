"""Publish the tested release from a specific source commit, without replacing existing assets."""

import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time


class PublishError(Exception):
    pass


def digest(data):
    return hashlib.sha256(data).hexdigest()


class GitHubCli:
    def __init__(self, repository):
        self.repository = repository

    def run(self, *args):
        proc = subprocess.run(["gh", *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        if proc.returncode:
            error = proc.stderr.strip()
            token = os.environ.get("GH_TOKEN", "")
            if token:
                error = error.replace(token, "[redacted]")
            error = re.sub(r"(?i)([?&](?:token|access_token|x-amz-signature)=)[^\s&]+", r"\1[redacted]", error)
            raise PublishError("GitHub CLI failed: " + error[:1000])
        return proc.stdout

    def api(self, endpoint, *args):
        return json.loads(self.run("api", endpoint, *args))

    def pages(self, endpoint):
        pages = self.api(endpoint, "--paginate", "--slurp")
        return [item for page in pages for item in page]

    def releases(self):
        return self.pages("repos/" + self.repository + "/releases?per_page=100")

    def tag_target(self, tag):
        refs = self.api("repos/" + self.repository + "/git/matching-refs/tags/" + tag)
        matches = [ref for ref in refs if ref.get("ref") == "refs/tags/" + tag]
        if len(matches) > 1:
            raise PublishError("Multiple matching tag references.")
        if not matches:
            return None
        obj = matches[0]["object"]
        for _ in range(5):
            if obj["type"] == "commit":
                return obj["sha"]
            if obj["type"] != "tag":
                break
            obj = self.api("repos/" + self.repository + "/git/tags/" + obj["sha"])["object"]
        raise PublishError("Tag does not resolve to a commit.")

    def create_draft(self, tag, source, title, notes, files):
        self.run("release", "create", tag, *(str(p) for p in files), "--repo", self.repository,
                 "--target", source, "--title", title, "--notes-file", str(notes), "--draft")

    def assets(self, release_id):
        return self.pages("repos/" + self.repository + "/releases/" + str(release_id) + "/assets?per_page=100")

    def upload(self, tag, paths):
        self.run("release", "upload", tag, *(str(p) for p in paths), "--repo", self.repository)

    def publish(self, release_id):
        return self.api("repos/" + self.repository + "/releases/" + str(release_id),
                        "--method", "PATCH", "-F", "draft=false", "-f", "make_latest=true")

    def download(self, tag, directory):
        self.run("release", "download", tag, "--repo", self.repository, "--pattern", "FirmwarePackager.exe",
                 "--pattern", "SHA256SUMS.txt", "--dir", str(directory))


def select_release(releases, tag):
    matches = [item for item in releases if item.get("tag_name") == tag]
    if len(matches) > 1:
        raise PublishError("Multiple releases use the requested tag.")
    return matches[0] if matches else None


def validate_manifest(manifest, source, message, executable):
    tag = manifest.get("tag", "")
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", tag):
        raise PublishError("Invalid release tag.")
    if not re.fullmatch(r"[0-9a-f]{40}", source) or message != "Release " + tag:
        raise PublishError("Source commit or release message does not match.")
    expected = manifest.get("exe_sha256", "")
    if not re.fullmatch(r"[0-9a-f]{64}", expected) or digest(executable) != expected:
        raise PublishError("The release EXE does not match its recorded SHA-256.")
    title = manifest.get("title", "")
    if not title or len(title) > 120:
        raise PublishError("Invalid release title.")
    return tag, expected, title


def verify_assets(assets, expected):
    names = [item.get("name") for item in assets]
    if len(names) != len(set(names)) or any(name not in expected for name in names):
        raise PublishError("Unexpected or duplicate release assets.")
    for item in assets:
        want = expected[item["name"]]
        if item.get("state") != "uploaded" or item.get("size") != want["size"]:
            raise PublishError("Release asset upload is incomplete.")
        if item.get("digest") != "sha256:" + want["digest"]:
            raise PublishError("Release asset SHA-256 does not match.")
    return [name for name in expected if name not in names]


def ready_assets(client, release_id, expected):
    for attempt in range(6):
        assets = client.assets(release_id)
        if all(item.get("digest") for item in assets) or attempt == 5:
            return verify_assets(assets, expected)
        time.sleep(2)
    raise PublishError("Release asset digest is unavailable.")


def publish(client, source, message, manifest, executable_path, notes_path, temp_path):
    executable = executable_path.read_bytes()
    tag, expected_hash, title = validate_manifest(manifest, source, message, executable)
    quote = chr(96)
    notes = notes_path.read_text(encoding="utf-8").rstrip() + (
        "\n\n源码提交：" + quote + source + quote + "\n\nEXE SHA-256：" + quote + expected_hash + quote + "\n"
    )
    checksum = (expected_hash + "  FirmwarePackager.exe\n").encode("ascii")
    checksum_path = temp_path / "SHA256SUMS.txt"
    checksum_path.write_bytes(checksum)
    notes_copy = temp_path / "release-notes.md"
    notes_copy.write_text(notes, encoding="utf-8")
    expected = {
        "FirmwarePackager.exe": {"size": len(executable), "digest": expected_hash, "path": executable_path},
        "SHA256SUMS.txt": {"size": len(checksum), "digest": digest(checksum), "path": checksum_path},
    }
    release = select_release(client.releases(), tag)
    ref = client.tag_target(tag)
    if ref is not None and ref != source:
        raise PublishError("Existing tag points to a different commit; it was not changed.")
    if release is None:
        if ref is not None:
            raise PublishError("Tag already exists without a release; it was not changed.")
        client.create_draft(tag, source, title, notes_copy, [executable_path, checksum_path])
        release = select_release(client.releases(), tag)
        if release is None or not release.get("draft"):
            raise PublishError("Expected draft release was not created.")
    body = (release.get("body") or "").replace("\r\n", "\n").strip()
    if release.get("name") != title or body != notes.strip() or release.get("prerelease"):
        raise PublishError("Existing release differs from the requested one; it was not changed.")
    ref = client.tag_target(tag)
    if ref is None and (not release.get("draft") or release.get("target_commitish") != source):
        raise PublishError("Existing release cannot be tied to the requested commit.")
    if ref is not None and ref != source:
        raise PublishError("Existing release tag points to a different commit.")

    missing = ready_assets(client, release["id"], expected)
    if missing:
        if not release.get("draft"):
            raise PublishError("Published release is missing assets; it was not changed.")
        client.upload(tag, [expected[name]["path"] for name in missing])
        if ready_assets(client, release["id"], expected):
            raise PublishError("Release assets are incomplete.")
    if release.get("draft"):
        result = client.publish(release["id"])
        if result.get("draft") or result.get("prerelease"):
            raise PublishError("Release was not published as a full release.")
    for _ in range(5):
        if client.tag_target(tag) == source:
            break
        time.sleep(2)
    else:
        raise PublishError("Published tag does not point to the tested source commit.")
    download = temp_path / "download"
    download.mkdir()
    client.download(tag, download)
    if digest((download / "FirmwarePackager.exe").read_bytes()) != expected_hash or (
        download / "SHA256SUMS.txt").read_bytes() != checksum:
        raise PublishError("Downloaded release assets do not match the tested files.")
    return tag, expected_hash


def main():
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    source = os.environ.get("RELEASE_SOURCE_SHA", "")
    message = os.environ.get("RELEASE_COMMIT_MESSAGE", "")
    if not re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) or not os.environ.get("GH_TOKEN"):
        raise PublishError("Repository or GitHub Actions token is unavailable.")
    root = Path.cwd()
    manifest = json.loads((root / ".github" / "release.json").read_text(encoding="utf-8"))
    tag = manifest.get("tag", "")
    if not re.fullmatch(r"v[0-9]+\.[0-9]+\.[0-9]+", tag):
        raise PublishError("Invalid release tag.")
    with tempfile.TemporaryDirectory(prefix="release-publish-") as tmp:
        tag, expected = publish(GitHubCli(repository), source, message, manifest,
                                root / "dist" / "FirmwarePackager.exe",
                                root / "docs" / "releases" / (tag + ".md"), Path(tmp))
    print("Published and downloaded " + tag + "; EXE SHA-256 " + expected)


if __name__ == "__main__":
    try:
        main()
    except (PublishError, OSError, ValueError, KeyError, json.JSONDecodeError) as error:
        print("Release publication failed: " + str(error))
        raise SystemExit(1)
