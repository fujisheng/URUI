#!/usr/bin/env python3
"""Compile/execute borrowed .cginc scalar math; does not verify GPU compilation."""
import pathlib
import subprocess
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
HEADER = ROOT / "Packages/com.fujisheng.urui/Runtime/Resources/URUI/BorrowedTextureSampling.cginc"
FIXTURE = ROOT / "Tools/Tests/BorrowedTextureSampling/borrowed_texture_sampling.cpp"


def main():
    if not HEADER.is_file():
        raise SystemExit("FAIL: shared borrowed-texture sampling helper is missing")
    with tempfile.TemporaryDirectory(prefix="urui-borrowed-shader-") as directory:
        for name, flags in (("linear", []), ("gamma", ["-DUNITY_COLORSPACE_GAMMA=1"])):
            binary = pathlib.Path(directory) / name
            subprocess.run(["c++", "-std=c++17", "-Wall", "-Wextra", "-Werror", *flags,
                            str(FIXTURE), "-o", str(binary)], check=True)
            subprocess.run([str(binary)], check=True)


if __name__ == "__main__":
    main()
