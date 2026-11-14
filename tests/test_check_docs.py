import importlib.util
from pathlib import Path
import tempfile
import unittest


SPEC = importlib.util.spec_from_file_location("check_docs", Path(__file__).with_name("check_docs.py"))
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


class DocumentationLinkTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="docs-test-")
        self.root = Path(self.tmp.name)
        (self.root / "docs").mkdir()
        (self.root / "docs" / "测试 文件.md").write_text("# ok", encoding="utf-8")

    def tearDown(self):
        self.tmp.cleanup()

    def test_existing_unicode_space_anchor_and_remote_links(self):
        (self.root / "README.md").write_text(
            "[good](<docs/测试 文件.md#heading>) [remote](https://example.invalid/) [local](#x)",
            encoding="utf-8",
        )
        count, errors = checker.check(self.root)
        self.assertEqual(count, 1)
        self.assertEqual(errors, [])

    def test_missing_case_mismatch_and_outside_root_fail(self):
        (self.root / "README.md").write_text(
            "[missing](missing.md) [case](Docs/测试%20文件.md) [outside](../other.md)",
            encoding="utf-8",
        )
        count, errors = checker.check(self.root)
        self.assertEqual(count, 3)
        self.assertEqual(len(errors), 3)

    def test_code_blocks_and_inline_code_are_not_links(self):
        (self.root / "README.md").write_text(
            "`[x](missing.md)`\n```md\n[x](missing.md)\n```\n", encoding="utf-8",
        )
        self.assertEqual(checker.check(self.root), (0, []))


if __name__ == "__main__":
    unittest.main()
