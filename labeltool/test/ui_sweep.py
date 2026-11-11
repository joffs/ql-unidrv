#!/usr/bin/env python3
"""Second half of ui_sweep.ps1: decodes the barcode size sweep, checks sample contents, writes the report.

usage: ui_sweep.py <out\\ui-sweep> <repository root> <checks: clip,drag,sizes,contents>
  sizes:    every rendered size (sizes-<lang>\\sizes.tsv + PNGs from ui_sweep.cs) decoded with zxing-cpp. A finding
            when the editor shows neither a problem line nor a warning but the code doesn't read, when the text under the bars is
            missing without a problem line saying so, or when a size reads although the editor refuses it.
  contents: the published sample/help/test files must not carry real GTINs: every 13/14-digit number with a valid
            check digit must start with 0 (GTIN-14 indicator / all-zero samples) or 2 (restricted circulation).
Report: report.txt (FIND lines of ui_sweep.cs and these, counted); exit 1 when there are findings.
"""
import csv
import glob
import os
import re
import sys

FINDS = []


def find(check, where, what):
    FINDS.append("FIND %s %s: %s" % (check, where, what))


def gtin_ok(digits):
    body, check = digits[:-1], int(digits[-1])
    s = sum(int(d) * (3 if i % 2 == 0 else 1) for i, d in enumerate(reversed(body)))
    return (10 - s % 10) % 10 == check


def sizes_check(out):
    import zxingcpp
    for tsv in glob.glob(os.path.join(out, "sizes-*", "sizes.tsv")):
        folder = os.path.dirname(tsv)
        rows = list(csv.DictReader(open(tsv, encoding="utf-8"), delimiter="\t"))
        for r in rows:
            img = read_pgm(os.path.join(folder, r["image"]))
            found = zxingcpp.read_barcodes(img)
            reads = len(found) > 0
            where = "%s %s mm" % (r["kind"], r["width_mm"])
            problem = r["problem"].strip()
            hint = r.get("hint", "").strip()
            if not problem and not hint and not reads:
                find("sizes", where, "no problem line, but the code doesn't read")
            if problem and reads and "Text" not in problem and "text" not in problem:
                find("sizes", where, "reads, but the editor refuses it: " + problem)
            if not r["kind"].startswith("qr") and not problem and "text" not in hint.lower() and int(r["text_dots"]) == 0:
                find("sizes", where, "no text under the bars and no problem line saying so")


def read_pgm(path):
    """P5 grey image (ui_sweep.cs SavePgm) -> numpy array"""
    import numpy as np
    data = open(path, "rb").read()
    magic, dims, maxval, rest = data.split(b"\n", 3)
    w, h = map(int, dims.split())
    return np.frombuffer(rest[:w * h], dtype=np.uint8).reshape(h, w)


def contents(root):
    pats = ["labeltool/help/**/*.qllabel", "labeltool/help/**/*.html", "labeltool/src/**/*.cs", "labeltool/test/*",
            "labels/*.json", "docs/*.md", "tools/testkit/**/*"]
    seen = set()
    for p in pats:
        for f in glob.glob(os.path.join(root, p), recursive=True):
            if not os.path.isfile(f) or f in seen:
                continue
            seen.add(f)
            try:
                text = open(f, encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            for m in re.finditer(r"(?<!\d)(\d{13,14})(?!\d)", text):
                d = m.group(1)
                if d[0] in "02" or not gtin_ok(d):
                    continue
                rel = os.path.relpath(f, root).replace("\\", "/")
                line = text.count("\n", 0, m.start()) + 1
                find("contents", "%s:%d" % (rel, line), "real-looking GTIN %s (use a 0... or 2... test number)" % d)


def main():
    out, root, checks = sys.argv[1], sys.argv[2], sys.argv[3].split(",")
    if "sizes" in checks:
        sizes_check(out)
    if "contents" in checks:
        contents(root)
    log = os.path.join(out, "sweep.log")
    sweep = [l.rstrip() for l in open(log, encoding="utf-8", errors="replace")] if os.path.exists(log) else []
    finds = [l for l in sweep if l.startswith("FIND")] + FINDS
    infos = [l for l in sweep if l.startswith("INFO") or l.startswith("DONE") or "to a test process" in l]
    with open(os.path.join(out, "report.txt"), "w", encoding="utf-8-sig") as f:   # BOM: Notepad and PowerShell 5 read it as UTF-8
        f.write("QL Label Maker UI sweep: %d finding(s)\n\n" % len(finds))
        f.write("\n".join(finds) + "\n\n" + "\n".join(infos) + "\n")
    for l in FINDS:
        print(l)
    print("UI sweep: %d finding(s), report %s" % (len(finds), os.path.join(out, "report.txt")))
    return 1 if finds else 0


if __name__ == "__main__":
    sys.exit(main())
