"""Packs the service into one zip for AWS Lambda.

    python -m vigil.api.lambda_pack <folder of Linux packages> <zip to write>

The folder is what ``pip install --target`` made from the Linux builds of the service's
packages: the README beside this file has the command, under "On AWS Lambda". The zip holds
those packages and ``vigil/api``, ``vigil/analysis`` and ``vigil/detective``, with ``vigil``
at its root so the handler is ``vigil.api.lambda_handler.handler``.

Lambda allows 250 MB unpacked. The packages are over that with their own test suites in, so
those are left out, along with caches, type stubs and C sources. The few test folders that the
packages import while running are kept (``KEPT``).
"""

import os
import sys
import zipfile
from pathlib import Path

#: Lambda's limit on a function's code once unpacked, in bytes.
UNPACKED_LIMIT = 262_144_000

#: The largest zip Lambda takes directly, in bytes. A larger one is created from a bucket.
DIRECT_LIMIT = 52_428_800

LEFT_OUT_FOLDERS = {"__pycache__", "bin", "include", "tests", "test", "wrapt-stubs"}
LEFT_OUT_ENDINGS = (".pyc", ".pyi", ".pyx", ".pxd", ".h", ".c", ".cpp", ".a")

#: Test folders that are imported at run time: numpy.testing reads numpy/_core/tests/_natype.py.
KEPT = {os.path.join("numpy", "_core", "tests")}

#: The parts of ``vigil`` the service loads. The pipeline is not one of them.
PARTS = ("api", "analysis", "detective")


def files(root: Path):
    """Each file under ``root`` that belongs in the zip, with its path relative to ``root``."""
    for folder, folders, names in os.walk(root):
        relative = os.path.relpath(folder, root)
        relative = "" if relative == "." else relative
        folders[:] = [name for name in folders
                      if name not in LEFT_OUT_FOLDERS or os.path.join(relative, name) in KEPT]
        for name in names:
            if not name.endswith(LEFT_OUT_ENDINGS):
                yield Path(folder) / name, os.path.join(relative, name)


def pack(packages: Path, target: Path) -> tuple[int, int]:
    """Writes the zip. Returns how many files went in and their size unpacked, in bytes."""
    package = Path(__file__).resolve().parents[1]              # python/vigil
    count = 0
    unpacked = 0
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as out:

        def add(path: Path, name: str) -> None:
            nonlocal count, unpacked
            # Written on Windows, read on Linux: forward slashes, and readable by the function's user.
            entry = zipfile.ZipInfo(name.replace(os.sep, "/"), date_time=(2026, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            entry.external_attr = (0o755 if ".so" in name else 0o644) << 16
            data = path.read_bytes()
            out.writestr(entry, data, compresslevel=9)
            count += 1
            unpacked += len(data)

        for path, name in files(packages):
            add(path, name)
        add(package / "__init__.py", os.path.join("vigil", "__init__.py"))
        for part in PARTS:
            for path, name in files(package / part):
                add(path, os.path.join("vigil", part, name))
    return count, unpacked


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    packages, target = Path(sys.argv[1]), Path(sys.argv[2])
    count, unpacked = pack(packages, target)
    zipped = target.stat().st_size
    print(f"{count} files")
    print(f"unpacked: {unpacked / 1e6:.1f} MB of the {UNPACKED_LIMIT / 1e6:.1f} MB Lambda allows")
    print(f"zipped:   {zipped / 1e6:.1f} MB"
          + ("" if zipped <= DIRECT_LIMIT else ", too large to hand to Lambda directly, so upload it to a private bucket first"))
    if unpacked > UNPACKED_LIMIT:
        print("This is over Lambda's limit and will be refused.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
