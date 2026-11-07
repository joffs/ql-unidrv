#!/usr/bin/env python3
"""Rewrites a QL-810W/820NWB two-colour capture as the QL-800 stream Brother's QL-800 driver makes: no ESC i U J, M 00, every line 'w 01' + 'w 02' with 90 bytes, no Z.
For a raw test on the real QL-800 before the driver supports two-colour.

usage: twocolor_ql800.py in.prn out.prn
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from compare_models import parse, LINE  # noqa: E402


def main():
    src, dst = sys.argv[1], sys.argv[2]
    b = open(src, 'rb').read()
    lead, pages = parse(b)
    out = bytearray(bytes(400))
    for n, p in enumerate(pages):
        for c in p['cmds']:
            if c.startswith(b'\x1biU'):
                continue
            out += b'M\x00' if c[:1] == b'M' else c
        black, red = p['planes'].get(1, []), p['planes'].get(2, [])
        if len(black) != len(red) or not black:
            raise SystemExit(f'page {n}: planes {len(black)} / {len(red)} lines')
        for k, r in zip(black, red):
            out += b'w\x01' + bytes([LINE]) + k + b'w\x02' + bytes([LINE]) + r
        out.append(p['end'])
    open(dst, 'wb').write(out)
    print(f'{dst}: {len(out)} bytes, {len(pages)} page(s), {sum(len(p["planes"].get(1, [])) for p in pages)} lines')


if __name__ == '__main__':
    main()
