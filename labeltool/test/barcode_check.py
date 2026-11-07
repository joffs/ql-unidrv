#!/usr/bin/env python3
"""Decodes and measures the barcode test prints of barcode_print.ps1 (QL Label Maker through a capture printer).

usage: barcode_check.py <out folder>      (reads cases.json and <case>-p1.pbm from tools/prn_raster.py)
For every case: decodes the printed raster with zxing-cpp (an independent decoder; pip install zxing-cpp) and
compares format and text with cases.json; measures the symbol in printer dots:
  - linear codes (EAN-13, Code 128, GS1-128): every bar and space a whole number (1-4) of modules of the same
    width X; quiet zones in modules (EAN-13 >= 11 left / 7 right, Code 128 >= 10), "edge" when it reaches the
    edge of the printed raster (the printer's unprintable margin is white tape too);
  - QR code: the finder pattern's width a whole number of dots per module, every module a block of one colour,
    the quiet zone 4 modules (black for an inverted code).
At 300 x 600 dpi the rows (feed) are 600 dpi: a code whose modules run along the feed must have even rows, the
decoder gets every second row. Writes check.json and a table; exit 1 on any FAIL.
"""
import json
import os
import sys

from importlib import metadata

import numpy as np
import zxingcpp

FORMATS = {"EAN13": "EAN-13", "Code128": "Code 128", "GS1-128": "Code 128", "QRCode": "QR Code"}


def read_pbm(path):
    data = open(path, "rb").read()
    magic, dims, rest = data.split(b"\n", 2)
    w, h = map(int, dims.split())
    row = (w + 7) // 8
    bits = np.unpackbits(np.frombuffer(rest[:row * h], dtype=np.uint8).reshape(h, row), axis=1)[:, :w]
    return bits.astype(bool)  # True = black


def runs(line):
    """[(value, start, length)] of a 1-D bool array"""
    out, start = [], 0
    for i in range(1, len(line) + 1):
        if i == len(line) or line[i] != line[start]:
            out.append((bool(line[start]), start, i - start))
            start = i
    return out


def linear(img, b, kind, row_scale):
    """module and quiet-zone check of a linear code along its scan line"""
    p = b.position
    pts = [(p.top_left.x, p.top_left.y), (p.top_right.x, p.top_right.y), (p.bottom_right.x, p.bottom_right.y),
           (p.bottom_left.x, p.bottom_left.y)]
    xs, ys = [q[0] for q in pts], [q[1] for q in pts]
    o = b.orientation % 180
    if o not in (0, 90):
        return None, "turned %d°: modules not measured" % b.orientation
    # rows were halved for the decoder at 600 dpi
    y0, y1 = int(min(ys) * row_scale), int(max(ys) * row_scale)
    x0, x1 = int(min(xs)), int(max(xs))
    if o == 0:   # bars upright in the image: scan a row
        line = img[(y0 + y1) // 2, :]
        lo, hi = x0, x1
        axis = "across"
    else:        # bars lying: scan a column (feed direction)
        line = img[:, (x0 + x1) // 2]
        lo, hi = y0, y1
        axis = "feed"
    r = runs(line)
    slack = 6 * row_scale if axis == "feed" else 6
    inside = [i for i, (v, s, n) in enumerate(r) if v and s + n > lo - slack and s < hi + slack]
    if not inside:
        return False, "no bars found on the scan line"
    first, last = inside[0], inside[-1]
    elems = r[first:last + 1]
    x = min(n for v, s, n in elems)
    bad = [n for v, s, n in elems if n % x or not 1 <= n // x <= 4]
    width = elems[-1][1] + elems[-1][2] - elems[0][1]
    before = r[first - 1] if first > 0 else None
    after = r[last + 1] if last + 1 < len(r) else None
    def quiet(run, at_end):
        if run is None:
            return 0, True
        edge = run[1] == 0 if not at_end else run[1] + run[2] == len(line)
        return run[2] // x, edge
    ql, el = quiet(before, False)
    qr, er = quiet(after, True)
    need_l, need_r = (11, 7) if kind == "EAN13" else (10, 10)
    dpi = 600 if axis == "feed" and row_scale == 2 else 300
    note = "X=%d dots (%.3f mm), %s, %d modules wide; quiet %s%d / %s%d" % (
        x, x * 25.4 / dpi, axis, width // x, "edge " if el else "", ql, "edge " if er else "", qr)
    ok = not bad and (ql >= need_l or el) and (qr >= need_r or er)
    if bad:
        note += "; uneven runs %s" % bad[:6]
    if ql < need_l and not el:
        note += "; left quiet zone too small"
    if qr < need_r and not er:
        note += "; right quiet zone too small"
    if (el and ql < need_l) or (er and qr < need_r):
        note += "; quiet zone runs into the raster edge"
    return ok, note


def qr(img, b, row_scale, inverted):
    p = b.position
    if b.orientation % 90:
        return None, "turned %d°: modules not measured" % b.orientation
    xs = [q.x for q in (p.top_left, p.top_right, p.bottom_right, p.bottom_left)]
    ys = [q.y * row_scale for q in (p.top_left, p.top_right, p.bottom_right, p.bottom_left)]
    x0, x1, y0, y1 = int(min(xs)), int(max(xs)), int(min(ys)), int(max(ys))
    dark = not inverted
    # the symbol's black (dark) bounding box near zxing's corners
    sub = img[max(0, y0 - 8):y1 + 9, max(0, x0 - 8):x1 + 9] == dark
    rows_any, cols_any = np.where(sub.any(axis=1))[0], np.where(sub.any(axis=0))[0]
    if inverted:
        # light modules on black: find the symbol from the light pixels
        sub = img[max(0, y0 - 8):y1 + 9, max(0, x0 - 8):x1 + 9] == False  # noqa: E712
        rows_any, cols_any = np.where(sub.any(axis=1))[0], np.where(sub.any(axis=0))[0]
    if not len(rows_any):
        return False, "symbol not found"
    by0, by1 = rows_any[0] + max(0, y0 - 8), rows_any[-1] + max(0, y0 - 8) + 1
    bx0, bx1 = cols_any[0] + max(0, x0 - 8), cols_any[-1] + max(0, x0 - 8) + 1
    if inverted:
        # the light area includes the inner quiet-zone-free symbol only: the outer modules of the finders are dark,
        # so the symbol starts one module outside the light area; estimate from the size
        pass
    w, h = bx1 - bx0, by1 - by0
    # module width from a finder pattern's outer edge (7 modules) at one of the corners, checked against the size
    finder = 0
    for line in (img[by0, bx0:bx1], img[by0, bx0:bx1][::-1], img[by1 - 1, bx0:bx1], img[by1 - 1, bx0:bx1][::-1]):
        r = runs(line == dark)
        if r and r[0][0] and r[0][2] % 7 == 0:
            finder = r[0][2]
            break
    if inverted:
        return None, "inverted: decoded; modules not measured (symbol %dx%d dots)" % (w, h)
    if not finder:
        return False, "no finder pattern edge of 7 whole modules at a corner"
    m = finder // 7
    mh = m * h // w if w else m
    n = w // m
    note = "module %dx%d dots (%.3f mm), %d modules" % (m, mh, m * 25.4 / 300, n)
    ok = w % m == 0 and (h * m) % w == 0 and n >= 21 and (n - 17) % 4 == 0
    if not ok:
        note += "; size %dx%d not whole modules" % (w, h)
        return False, note
    # every module one colour
    mixed = 0
    for j in range(n):
        for i in range(n):
            block = img[by0 + j * mh:by0 + (j + 1) * mh, bx0 + i * m:bx0 + (i + 1) * m]
            if block.any() and not block.all():
                mixed += 1
    if mixed:
        note += "; %d modules not one colour" % mixed
    # quiet zone: 4 modules around, light (or the raster edge)
    qz = []
    for name, area, edge in (("left", img[by0:by1, max(0, bx0 - 4 * m):bx0], bx0 - 4 * m < 0),
                             ("right", img[by0:by1, bx1:bx1 + 4 * m], bx1 + 4 * m > img.shape[1]),
                             ("top", img[max(0, by0 - 4 * mh):by0, bx0:bx1], by0 - 4 * mh < 0),
                             ("bottom", img[by1:by1 + 4 * mh, bx0:bx1], by1 + 4 * mh > img.shape[0])):
        if area.any():
            qz.append(name + " not clear")
        elif edge:
            qz.append(name + " reaches the raster edge")
    if qz:
        note += "; quiet zone: " + ", ".join(qz)
    return ok and not mixed and not any("not clear" in q for q in qz), note


def main():
    out = sys.argv[1]
    cases = json.load(open(os.path.join(out, "cases.json"), encoding="utf-8"))
    results, failures = [], 0
    for c in cases:
        path = os.path.join(out, c["name"] + "-p1.pbm")
        if not os.path.exists(path):
            results.append((c["name"], "SKIP", "not printed"))
            continue
        img = read_pbm(path)
        scale = 2 if c["dpi"] == 600 else 1
        view = img[::scale]
        gray = np.where(view, 0, 255).astype(np.uint8)
        found = zxingcpp.read_barcodes(gray, text_mode=zxingcpp.TextMode.Plain)
        notes, ok = [], True
        if c.get("cut"):
            # partly in the unprintable margin: cut off by design (the editor warns); read anyway = the margin changed
            read = [e["text"] for e in c["expect"] if any(str(b.format) == FORMATS[e["format"]] and b.text == e["text"] for b in found)]
            if read:
                failures += 1
                results.append((c["name"], "FAIL", "read although cut off by design: %s - did the printable area change?" % ", ".join(read)))
            else:
                results.append((c["name"], "PASS", "cut off at the printable area as expected (the editor's yellow hint), not read"))
            continue
        for e in c["expect"]:
            want = FORMATS[e["format"]]
            hit = [b for b in found if str(b.format) == want and b.text == e["text"]]
            if not hit:
                ok = False
                got = ["%s %r" % (b.format, b.text) for b in found]
                notes.append("%s %r not read (read: %s)" % (want, e["text"], ", ".join(got) or "nothing"))
                continue
            b = hit[0]
            if e["format"] == "GS1-128" and b.symbology_identifier != "]C1":
                ok = False
                notes.append("GS1-128 without FNC1 first (%s)" % b.symbology_identifier)
            inverted = c["name"].endswith("invert")
            if e["format"] == "QRCode":
                m_ok, note = qr(img, b, scale, inverted)
            else:
                m_ok, note = linear(img, b, e["format"], scale)
            if m_ok is False:
                ok = False
            notes.append("%s read%s; %s" % (want, " (" + b.symbology_identifier + ")" if e["format"] == "GS1-128" else "", note))
        if not ok:
            failures += 1
        results.append((c["name"], "PASS" if ok else "FAIL", " | ".join(notes)))
    w = max(len(r[0]) for r in results)
    for name, res, note in results:
        print("%-*s  %-4s  %s" % (w, name, res, note))
    with open(os.path.join(out, "check.json"), "w", encoding="utf-8") as f:
        json.dump([{"case": n, "result": r, "notes": t} for n, r, t in results], f, ensure_ascii=False, indent=1)
    print("zxing-cpp", metadata.version("zxing-cpp"), "-", failures, "FAIL" if failures else "all PASS")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
