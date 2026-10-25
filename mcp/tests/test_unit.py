"""Unit tests that need no AutoCAD: python -m unittest discover -s mcp/tests -p "test_*.py" """

import asyncio
import os
import tempfile
import unittest
from pathlib import Path

from mdh_mcp.cad_ops import CadError, _point
from mdh_mcp.settings import RAW_TOOLS, REPO_ROOT, Settings


class SettingsTests(unittest.TestCase):
    def test_default_write_root_is_the_test_drawing_folder(self):
        os.environ.pop("MDH_MCP_WRITE_ROOTS", None)
        s = Settings.load()
        self.assertEqual([REPO_ROOT.joinpath("mcp", "tests", "drawings").resolve()], s.write_roots)
        self.assertTrue(s.is_writable_drawing(str(REPO_ROOT / "mcp" / "tests" / "drawings" / "MDH_MCP_Test.dwg")))

    def test_other_drawings_are_not_writable(self):
        s = Settings.load()
        self.assertFalse(s.is_writable_drawing(r"C:\Projects\Tower\S-101.dwg"))
        self.assertFalse(s.is_writable_drawing(""))  # unsaved drawing has no path
        # A sibling folder whose name starts with the root's name must not match.
        self.assertFalse(s.is_writable_drawing(str(REPO_ROOT / "mcp" / "tests" / "drawings-prod" / "a.dwg")))

    def test_write_roots_from_environment(self):
        with tempfile.TemporaryDirectory() as d:
            os.environ["MDH_MCP_WRITE_ROOTS"] = d
            try:
                s = Settings.load()
                self.assertTrue(s.is_writable_drawing(str(Path(d) / "x.dwg")))
                self.assertFalse(s.is_writable_drawing(str(REPO_ROOT / "mcp" / "tests" / "drawings" / "MDH_MCP_Test.dwg")))
            finally:
                del os.environ["MDH_MCP_WRITE_ROOTS"]


class PointTests(unittest.TestCase):
    def test_accepts_2d_and_3d(self):
        self.assertEqual([1.0, 2.0, 0.0], _point([1, 2], "p"))
        self.assertEqual([1.0, 2.0, 3.5], _point((1, 2, 3.5), "p"))

    def test_rejects_bad_points(self):
        for bad in (None, [1], [1, 2, 3, 4], ["1", 2], [float("nan"), 0], [1e12, 0], [True, 0]):
            with self.assertRaises(CadError, msg=repr(bad)):
                _point(bad, "p")


class RegistrationTests(unittest.TestCase):
    def test_focused_tools_registered_and_raw_tools_hidden(self):
        os.environ.pop("MDH_MCP_EXPOSE_RAW_TOOLS", None)
        cwd = os.getcwd()
        try:
            from mdh_mcp.server import build
            mcp, _ = build()
        finally:
            os.chdir(cwd)
        names = {t.name for t in asyncio.run(mcp.list_tools())}
        for n in ("autocad_get_status", "autocad_get_active_document", "autocad_list_layers", "autocad_create_line",
                  "autocad_get_entity", "autocad_update_entity", "autocad_delete_entity"):
            self.assertIn(n, names)
        for n in RAW_TOOLS:
            self.assertNotIn(n, names)
        self.assertIn("get_status", names)  # read-only upstream tools stay available


if __name__ == "__main__":
    unittest.main()
