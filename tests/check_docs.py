"""Check local Markdown links in the repository without network access."""

import argparse
from pathlib import Path
import posixpath
import re
from urllib.parse import unquote, urlsplit


LINK = re.compile(r"!?\[[^\]]+\]\((<[^>]+>|[^)\s]+)(?:\s+[\"'][^)]*[\"'])?\)")
FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
INLINE_CODE = re.compile(r"`[^`]*`")


def check(root):
    root = Path(root).resolve()
    entries = {p.relative_to(root).as_posix() for p in root.rglob("*")
               if ".git" not in p.relative_to(root).parts}
    markdown = sorted(p for p in root.rglob("*.md") if ".git" not in p.relative_to(root).parts)
    errors, count = [], 0
    for path in markdown:
        fence = None
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            marker = FENCE.match(line)
            if marker:
                token = marker.group(1)
                if fence is None:
                    fence = token
                elif token[0] == fence[0] and len(token) >= len(fence):
                    fence = None
                continue
            if fence is not None:
                continue
            for match in LINK.finditer(INLINE_CODE.sub("", line)):
                target = match.group(1).strip("<>")
                uri = urlsplit(target)
                if uri.scheme or uri.netloc or not uri.path:
                    continue
                count += 1
                dest = unquote(uri.path)
                if dest.startswith("/"):
                    relative = posixpath.normpath(dest.lstrip("/"))
                else:
                    parent = path.parent.relative_to(root).as_posix()
                    relative = posixpath.normpath(posixpath.join(parent, dest))
                if relative == ".." or relative.startswith("../") or relative not in entries:
                    errors.append(f"{path.relative_to(root)}:{number}: {target}")
    return count, errors


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("root", nargs="?", default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    count, errors = check(args.root)
    for error in errors:
        print("BROKEN " + error)
    print(f"Checked {count} local Markdown links; broken={len(errors)}")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
