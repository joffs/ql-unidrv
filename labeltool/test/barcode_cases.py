#!/usr/bin/env python3
"""Barcode print test cases for QL Label Maker: writes one .qllabel per case and cases.json (expected content).

usage: barcode_cases.py <out folder>
Each label is 62 mm continuous tape (paper 259) with a length of its own (60 mm), one barcode on it (plus a QR on the "pair"
case). The expected text is what a decoder must read: Code 128 control tokens resolved, GS1-128 as AI+data with
GS (0x1D) after a variable-length element, the {date} field as today's date.
"""
import datetime
import json
import os
import sys

TODAY = datetime.date.today().strftime("%y%m%d")


def code(kind, data, x, y, w, h, rot=0, level="M", show=True, invert=False, tilt=0):
    o = {"type": "barcode", "symbology": kind, "x": x, "y": y, "w": w, "h": h, "rot": rot, "data": data,
         "showText": show, "level": level}
    if invert:
        o["invert"] = True
    if tilt:
        o["tilt"] = tilt
    return o


# Codes placed partly in the unprintable margin (x = 0, y = 0): printed cut off by design, as the editor's yellow hint
# says ("partly outside the printable area - it gets cut off", EditorRender.BarcodeHint); expected not to read.
CUT = {"code128-edge", "qr-edge"}

# name, objects, expected [(format, text)], dpi
CASES = [
    ("ean13-0", [code("ean13", "2000000000008", 8, 8, 40, 22)], [("EAN13", "2000000000008")], 300),
    ("ean13-90", [code("ean13", "2000000000008", 15, 4, 22, 40, rot=90)], [("EAN13", "2000000000008")], 300),
    ("ean13-180", [code("ean13", "2000000000008", 8, 8, 40, 22, rot=180)], [("EAN13", "2000000000008")], 300),
    ("ean13-270", [code("ean13", "2000000000008", 15, 4, 22, 40, rot=270)], [("EAN13", "2000000000008")], 300),
    ("ean13-12digits", [code("ean13", "200000000000", 8, 8, 40, 22)], [("EAN13", "2000000000008")], 300),
    ("ean13-small", [code("ean13", "2000000000008", 8, 8, 18, 10)], [("EAN13", "2000000000008")], 300),
    ("ean13-600dpi", [code("ean13", "2000000000008", 8, 8, 40, 22)], [("EAN13", "2000000000008")], 600),
    ("code128-text", [code("code128", "ABC-123 xyz", 5, 8, 50, 18)], [("Code128", "ABC-123 xyz")], 300),
    ("code128-digits", [code("code128", "1234567890", 5, 8, 40, 18)], [("Code128", "1234567890")], 300),
    ("code128-ctrl", [code("code128", "QL{tab}A{gs}B{x41}{cr}", 5, 8, 50, 18)], [("Code128", "QL\tA\x1dBA\r")], 300),
    ("code128-fnc1", [code("code128", "{fnc1}0102000000000008", 5, 8, 52, 18)], [("Code128", "0102000000000008")], 300),
    ("code128-90", [code("code128", "ROT-90", 15, 4, 18, 45, rot=90)], [("Code128", "ROT-90")], 300),
    ("code128-270", [code("code128", "ROT-270", 15, 4, 18, 45, rot=270)], [("Code128", "ROT-270")], 300),
    ("code128-small", [code("code128", "ABC-123", 5, 8, 18, 7)], [("Code128", "ABC-123")], 300),
    ("code128-edge", [code("code128", "EDGE-1", 0, 0, 30, 12)], [("Code128", "EDGE-1")], 300),
    ("code128-tilt30", [code("code128", "TILT-30", 5, 8, 40, 14, tilt=30)], [("Code128", "TILT-30")], 300),
    ("code128-600dpi", [code("code128", "HIRES-600", 5, 8, 45, 16)], [("Code128", "HIRES-600")], 600),
    ("gs1-128", [code("gs1-128", "(01)02000000000008(17){date:yyMMdd}(10)ABC123", 2, 8, 56, 18)],
     [("GS1-128", "0102000000000008" + "17" + TODAY + "10ABC123")], 300),
    ("gs1-128-var", [code("gs1-128", "(10)LOT7(21)SER42", 2, 8, 50, 18)], [("GS1-128", "10LOT7\x1d21SER42")], 300),
    ("gs1-128-90", [code("gs1-128", "(01)02000000000008(10)A1", 15, 2, 20, 56, rot=90)],
     [("GS1-128", "0102000000000008" + "10A1")], 300),
    ("qr-text", [code("qr", "Hallo Welt äöü ß 123", 10, 8, 24, 24)], [("QRCode", "Hallo Welt äöü ß 123")], 300),
    ("qr-url-H-180", [code("qr", "https://example.org/a?b=1", 10, 8, 24, 24, rot=180, level="H")],
     [("QRCode", "https://example.org/a?b=1")], 300),
    ("qr-90", [code("qr", "QR 90", 10, 8, 20, 20, rot=90)], [("QRCode", "QR 90")], 300),
    ("qr-invert", [code("qr", "INVERTED", 10, 8, 22, 22, invert=True)], [("QRCode", "INVERTED")], 300),
    ("qr-small", [code("qr", "SMALL QR 1234567890", 10, 8, 9, 9, level="L")], [("QRCode", "SMALL QR 1234567890")], 300),
    ("qr-long", [code("qr", "L" * 300, 5, 4, 50, 50, level="Q")], [("QRCode", "L" * 300)], 300),
    ("qr-600dpi", [code("qr", "QR 300x600", 10, 8, 22, 22)], [("QRCode", "QR 300x600")], 600),
    ("qr-edge", [code("qr", "EDGE QR", 0, 0, 16, 16)], [("QRCode", "EDGE QR")], 300),
    # at the printable area's corner (the driver: 2.96 mm feed margin along, 1.52 mm across): the quiet zone lies on the
    # white tape of the unprintable margin, the code itself is whole
    ("code128-margin", [code("code128", "MARGIN-1", 3, 1.6, 30, 12)], [("Code128", "MARGIN-1")], 300),
    ("qr-margin", [code("qr", "MARGIN QR", 3, 1.6, 16, 16)], [("QRCode", "MARGIN QR")], 300),
    ("pair-ean-qr", [code("ean13", "2000000000008", 3, 8, 30, 20), code("qr", "PAIR", 36, 8, 20, 20)],
     [("EAN13", "2000000000008"), ("QRCode", "PAIR")], 300),
]


def main():
    out = sys.argv[1]
    os.makedirs(out, exist_ok=True)
    meta = []
    for name, objs, expect, dpi in CASES:
        doc = {"format": "QL label", "version": 1,
               "label": {"form": "62mm", "paperId": 259, "view": "along", "length": "own", "length01": 600}, "objects": objs}
        with open(os.path.join(out, name + ".qllabel"), "w", encoding="utf-8") as f:
            json.dump(doc, f, ensure_ascii=False)
        meta.append({"name": name, "dpi": dpi, "expect": [{"format": k, "text": t} for k, t in expect], "cut": name in CUT})
    with open(os.path.join(out, "cases.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, ensure_ascii=False, indent=1)
    print(len(meta), "cases in", out)


if __name__ == "__main__":
    main()
