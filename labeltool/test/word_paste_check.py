#!/usr/bin/env python3
"""Checks word-paste.ps1's rendered PDF page: the pasted object's box (bbox in pixels at 600 dpi and mm), the QR code
decodes (zxing-cpp) to https://example.org; the PDF's fonts and images are only reported.
usage: word_paste_check.py <page.raw BGR24> <width> <height> <stride> <pdf>"""
import re
import sys
import zlib

import numpy as np
import zxingcpp

raw, w, h, stride, pdf = sys.argv[1], int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
a = np.fromfile(raw, dtype=np.uint8).reshape(h, stride)[:, : w * 3].reshape(h, w, 3)
gray = a.mean(axis=2).astype(np.uint8)
ink = gray < 200
ys, xs = np.nonzero(ink)
fails = 0
if len(xs) == 0:
    print("FAIL nothing on the page")
    sys.exit(1)
x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
print(f"bbox {x0} {y0} {x1} {y1}")
print(f"ink on the page {(x1 - x0 + 1) * 25.4 / 600:.1f} x {(y1 - y0 + 1) * 25.4 / 600:.1f} mm (600 dpi)")
codes = zxingcpp.read_barcodes(np.ascontiguousarray(gray))
qr = [c for c in codes if "QR" in str(c.format)]
ok = any(c.text == "https://example.org" for c in qr)
print(("OK   " if ok else "FAIL ") + "QR decodes: " + (", ".join(repr(c.text) for c in qr) or "none"))
fails += not ok
# Text as text: a font in the PDF (Word embeds the EMF's text as PDF text; a pixel picture has none)
data = open(pdf, "rb").read()
fonts = sorted(set(m.decode("latin-1") for m in re.findall(rb"/BaseFont\s*/([A-Za-z0-9+\-,_]+)", data)))
images = len(re.findall(rb"/Subtype\s*/Image", data))
print(f"pdf: fonts {fonts or 'none'}, {images} image(s)")
# Reported only: Word may draw the EMF's text as outlines (no font in the PDF); checked by eye, that is fine
sys.exit(1 if fails else 0)
