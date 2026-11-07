"""Zeroes the capturing PC's MAC address in Brother captures: the 6 bytes after ESC i U J 00 0C, which Brother's
network-capable drivers write into every job (the export scan refuses captures that still have one; replay_test
ignores ESC i U J). Changes .prn files in place; files without a MAC stay untouched.

Run:  python -I tools/zero-capture-mac.py [folder]      (default: reference/captures, recursively)
"""
import os
import sys

MARK = b"\x1biUJ\x00\x0c"
ZERO = bytes(6)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.dirname(
        os.path.abspath(__file__))), "reference", "captures")
    files = fields = 0
    for folder, _, names in os.walk(root):
        for name in sorted(names):
            if not name.lower().endswith(".prn"):
                continue
            path = os.path.join(folder, name)
            b = bytearray(open(path, "rb").read())
            n = 0
            i = b.find(MARK)
            while i >= 0:
                at = i + len(MARK)
                if b[at:at + 6] != ZERO:
                    b[at:at + 6] = ZERO
                    n += 1
                i = b.find(MARK, at)
            if n:
                with open(path, "wb") as f:
                    f.write(b)
                files += 1
                fields += n
    print(f"zeroed {fields} MAC fields in {files} files")


if __name__ == "__main__":
    main()
