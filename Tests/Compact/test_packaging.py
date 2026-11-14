import pathlib
import plistlib
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'Tools/Compact/package_ipa.py'
TEMP = ROOT / 'TestResults/packaging'
TEMP.mkdir(parents=True, exist_ok=True)


class PackagingTests(unittest.TestCase):
    def fixture(self, root):
        app = root / 'Test.app'
        app.mkdir()
        (app / 'Test').write_bytes(b'test fixture, not an actual executable')
        with (app / 'Info.plist').open('wb') as stream:
            plistlib.dump(dict(CFBundleExecutable='Test', CFBundleSupportedPlatforms=['iPhoneOS']), stream)
        return app

    def test_payload_layout_and_installed_size_report(self):
        with tempfile.TemporaryDirectory(dir=TEMP) as tmp:
            root = pathlib.Path(tmp); app = self.fixture(root); output = root / 'test.ipa'
            result = subprocess.run([sys.executable, str(SCRIPT), '--app', str(app), '--output', str(output)], capture_output=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            with zipfile.ZipFile(output) as archive:
                self.assertIn('Payload/Test.app/Info.plist', archive.namelist())
                self.assertIn('Payload/Test.app/Test', archive.namelist())
            self.assertTrue(output.with_suffix('.storage.json').is_file())

    def test_oversize_app_is_rejected_before_ipa_exists(self):
        with tempfile.TemporaryDirectory(dir=TEMP) as tmp:
            root = pathlib.Path(tmp); app = self.fixture(root); output = root / 'test.ipa'
            with (app / 'large-resource').open('wb') as stream:
                stream.truncate(500_000_001)
            result = subprocess.run([sys.executable, str(SCRIPT), '--app', str(app), '--output', str(output)], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(output.exists())

    def test_simulator_app_is_rejected(self):
        with tempfile.TemporaryDirectory(dir=TEMP) as tmp:
            root = pathlib.Path(tmp); app = self.fixture(root); output = root / 'test.ipa'
            with (app / 'Info.plist').open('wb') as stream:
                plistlib.dump(dict(CFBundleExecutable='Test', CFBundleSupportedPlatforms=['iPhoneSimulator']), stream)
            result = subprocess.run([sys.executable, str(SCRIPT), '--app', str(app), '--output', str(output)], capture_output=True)
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse(output.exists())


if __name__ == '__main__':
    unittest.main()
