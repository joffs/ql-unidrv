#!/usr/bin/env python3
"""Decode the raster of a Brother QL stream into images, and compare it with the bitmap that was printed.

usage: prn_raster.py capture.prn out_prefix                  -> out_prefix-p1.pbm (+ -p1-red.pbm), one per page
       prn_raster.py capture.prn out_prefix --png            -> also out_prefix-p1.png (black, red in red)
       prn_raster.py capture.prn out_prefix --expect sent.pbm
           compares page 1 with the bitmap the application drew (label editor spike, labeltool/test/editor_spike.ps1):
           finds where the bitmap sits in the head line and reports dot-exact / differing dots, offset, and
           black dots outside it. Exit code 0 = dot-exact and nothing outside.

Image layout: one image row per raster line in feed order (row 0 = the first line sent), one column per head dot
as seen on the printed side: the stream's lines are mirrored (last byte = first dot), so they are flipped here.
Uses compare_prn.parse (packed lines unpacked, Z = blank line, ESC i U J ignored). Two-colour pages ('w' lines,
black then red per line) give a black and a red image. No third-party modules (PBM P4 / PNG via zlib).
"""
import os
import struct
import sys
import zlib

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from compare_prn import parse  # noqa: E402


def lines_to_rows(lines):
    """raster lines (bytes, MSB first, mirrored) -> list of rows (lists of 0/1, printed-side order)"""
    rows = []
    for ln in lines:
        bits = []
        for v in ln:
            for k in range(7, -1, -1):
                bits.append((v >> k) & 1)
        bits.reverse()
        rows.append(bits)
    return rows


def decode(path):
    """-> list of pages, each (black rows, red rows or None); parse() keeps a two-colour page's 'w' lines in one
    list, black and red alternating"""
    b = open(path, 'rb').read()
    _, pages, _ = parse(b)
    red_stream = b'w\x01' in b or b'w\x02' in b
    out = []
    for p in pages:
        lines = p['lines']
        if red_stream and len(lines) % 2 == 0:
            out.append((lines_to_rows(lines[0::2]), lines_to_rows(lines[1::2])))
        else:
            out.append((lines_to_rows(lines), None))
    return out


def write_pbm(path, rows):
    h, w = len(rows), len(rows[0]) if rows else 0
    with open(path, 'wb') as f:
        f.write(b'P4\n%d %d\n' % (w, h))
        for r in rows:
            row = bytearray((w + 7) // 8)
            for x, v in enumerate(r):
                if v:
                    row[x >> 3] |= 0x80 >> (x & 7)
            f.write(row)


def read_pbm(path):
    data = open(path, 'rb').read()
    tokens, i = [], 0
    while len(tokens) < 3:
        while data[i:i + 1].isspace():
            i += 1
        if data[i:i + 1] == b'#':
            i = data.index(b'\n', i)
            continue
        j = i
        while not data[j:j + 1].isspace():
            j += 1
        tokens.append(data[i:j])
        i = j
    i += 1
    if tokens[0] != b'P4':
        raise ValueError('not a binary PBM: ' + path)
    w, h = int(tokens[1]), int(tokens[2])
    stride = (w + 7) // 8
    rows = []
    for y in range(h):
        row = data[i + y * stride:i + (y + 1) * stride]
        rows.append([(row[x >> 3] >> (7 - (x & 7))) & 1 for x in range(w)])
    return rows


def write_png(path, black, red=None):
    h, w = len(black), len(black[0]) if black else 0
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        for x in range(w):
            if black[y][x]:
                raw += b'\x00\x00\x00'
            elif red and red[y][x]:
                raw += b'\xff\x00\x00'
            else:
                raw += b'\xff\xff\xff'

    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n')
        f.write(chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)))
        f.write(chunk(b'IDAT', zlib.compress(bytes(raw), 9)))
        f.write(chunk(b'IEND', b''))


def bbox(rows):
    ys = [y for y, r in enumerate(rows) if any(r)]
    if not ys:
        return None
    xs = [x for r in rows for x, v in enumerate(r) if v]
    return min(xs), ys[0], max(xs), ys[-1]


def compare(printed, sent):
    """sent = the application's bitmap (its frame marks the printable area). Aligns the black bounding boxes,
    then counts differing dots inside the sent bitmap's area and black dots outside it."""
    pb, sb = bbox(printed), bbox(sent)
    if pb is None or sb is None:
        return False, ['empty page' if pb is None else 'empty bitmap']
    dx, dy = pb[0] - sb[0], pb[1] - sb[1]
    sh, sw = len(sent), len(sent[0])
    ph, pw = len(printed), len(printed[0])
    diff, outside, clipped = 0, 0, 0
    for y in range(sh):
        for x in range(sw):
            px, py = x + dx, y + dy
            if 0 <= px < pw and 0 <= py < ph:
                if printed[py][px] != sent[y][x]:
                    diff += 1
            elif sent[y][x]:
                clipped += 1
    for y in range(ph):
        for x in range(pw):
            if printed[y][x] and not (0 <= x - dx < sw and 0 <= y - dy < sh):
                outside += 1
    notes = ['printed raster %d lines x %d dots; bitmap %d x %d at head dot %d, line %d'
             % (ph, pw, sh, sw, dx, dy),
             'differing dots %d, clipped bitmap dots %d, black dots outside the bitmap %d' % (diff, clipped, outside)]
    return diff == 0 and outside == 0 and clipped == 0, notes


def main():
    args = sys.argv[1:]
    if len(args) < 2:
        print(__doc__)
        return 2
    prn, prefix = args[0], args[1]
    pages = decode(prn)
    for n, (black, red) in enumerate(pages, 1):
        write_pbm('%s-p%d.pbm' % (prefix, n), black)
        if red:
            write_pbm('%s-p%d-red.pbm' % (prefix, n), red)
        if '--png' in args:
            write_png('%s-p%d.png' % (prefix, n), black, red)
    print('%d page(s), page 1: %d lines' % (len(pages), len(pages[0][0]) if pages else 0))
    if '--expect' in args:
        sent = read_pbm(args[args.index('--expect') + 1])
        ok, notes = compare(pages[0][0], sent)
        print('DOT-EXACT' if ok else 'DIFFERENT')
        for s in notes:
            print('  ' + s)
        return 0 if ok else 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
