#!/usr/bin/env python3
"""Check a compiled plugin's real target format before packaging it."""
import struct
import sys
from pathlib import Path


def require(condition, message):
    if not condition:
        raise ValueError(message)


def mach(data, cpu, file_type, platform=None):
    require(data[:4] == b"\xcf\xfa\xed\xfe", "expected a little-endian 64-bit Mach-O")
    require(struct.unpack_from("<I", data, 4)[0] == cpu, "wrong Mach-O architecture")
    require(struct.unpack_from("<I", data, 12)[0] == file_type, "wrong Mach-O file type")
    if platform is not None:
        commands = struct.unpack_from("<I", data, 16)[0]
        offset = 32
        actual = None
        for _ in range(commands):
            command, size = struct.unpack_from("<II", data, offset)
            require(size >= 8 and offset + size <= len(data), "invalid Mach-O load command")
            if command == 0x32:
                actual = struct.unpack_from("<I", data, offset + 8)[0]
            elif command == 0x25:
                actual = 2
            offset += size
        require(actual == platform, "expected an iOS device object, not a simulator or host object")


def objects(data):
    require(data[:8] == b"!<arch>\n", "expected a static archive")
    offset = 8
    count = 0
    while offset < len(data):
        header = data[offset:offset + 60]
        require(len(header) == 60 and header[58:60] == b"`\n", "invalid archive member")
        size = int(header[48:58])
        name = header[:16].decode("ascii").strip()
        payload = data[offset + 60:offset + 60 + size]
        require(len(payload) == size, "truncated archive member")
        if name.startswith("#1/"):
            length = int(name[3:])
            name = payload[:length].rstrip(b"\0").decode("utf-8")
            payload = payload[length:]
        if not (name in ("/", "//", "/SYM64/") or name.startswith("__.SYMDEF")):
            count += 1
            yield payload
        offset += 60 + size + size % 2
    require(count > 0, "archive contains no objects")


def check(platform, data):
    if platform.startswith("macos-"):
        cpu = 0x0100000C if platform == "macos-arm64" else 0x01000007
        mach(data, cpu, 6)
    elif platform.startswith("android-"):
        require(data[:6] == b"\x7fELF\x02\x01", "expected a little-endian ELF64")
        kind, cpu = struct.unpack_from("<HH", data, 16)
        require(kind == 3, "expected an ELF shared object")
        require(cpu == (183 if platform == "android-arm64" else 62), "wrong Android architecture")
    elif platform == "windows-x86_64":
        require(data[:2] == b"MZ", "expected a Windows PE image")
        offset = struct.unpack_from("<I", data, 0x3C)[0]
        require(data[offset:offset + 4] == b"PE\0\0", "invalid Windows PE signature")
        require(struct.unpack_from("<H", data, offset + 4)[0] == 0x8664, "expected Windows x86_64")
        require(struct.unpack_from("<H", data, offset + 22)[0] & 0x2000, "expected a Windows DLL")
    elif platform in ("ios-arm64", "webgl"):
        for payload in objects(data):
            if platform == "ios-arm64":
                mach(payload, 0x0100000C, 1, platform=2)
            else:
                require(payload[:8] == b"\0asm\x01\0\0\0", "expected WebAssembly objects in the WebGL archive")
    else:
        raise ValueError("unknown platform: " + platform)


if __name__ == "__main__":
    try:
        check(sys.argv[1], Path(sys.argv[2]).read_bytes())
        print("Verified target format: " + sys.argv[1])
    except (ValueError, IndexError, OSError, struct.error) as error:
        sys.exit("Plugin target verification failed: " + str(error))
