#!/usr/bin/env python3
"""Print with data, end to end: compares a capture of the dialog's print job with the engine's pages.

usage: printdata_prn_check.py <folder> [<folder> ...]
Each folder (written by printdata_ui.cs) holds capture.prn and expected-p1.pbm ... expected-pN.pbm. Checks: the
capture has N pages, and page k is dot for dot expected-pk.pbm (tools/prn_raster.py's compare: the bitmaps aligned at
their black bounding boxes, no differing, clipped or extra dots). Prints CHECK lines; exit code 1 on a FAIL.
"""
import glob
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, '..', '..', 'tools'))
from prn_raster import decode, read_pbm, compare  # noqa: E402


def check(folder):
    name = os.path.basename(folder.rstrip('\\/'))
    ok = True
    prn = os.path.join(folder, 'capture.prn')
    expected = sorted(glob.glob(os.path.join(folder, 'expected-p*.pbm')), key=lambda p: int(p.rsplit('-p', 1)[1][:-4]))
    if not os.path.exists(prn):
        print('CHECK %s: capture.prn present FAIL' % name)
        return False
    try:
        pages = decode(prn)
    except ValueError as e:   # not a stream compare_prn reads (e.g. a second job header inside the job)
        print('CHECK %s: the capture is one clean job (%s) FAIL' % (name, e))
        return False
    same = len(pages) == len(expected)
    print('CHECK %s: %d pages in the capture, %d expected %s' % (name, len(pages), len(expected), 'PASS' if same else 'FAIL'))
    ok &= same
    lengths = set()
    for k, (page, path) in enumerate(zip(pages, expected), 1):
        exact, notes = compare(page[0], read_pbm(path))
        lengths.add(len(page[0]))
        print('CHECK %s: page %d dot for dot as the engine drew it %s' % (name, k, 'PASS' if exact else 'FAIL'))
        if not exact:
            for s in notes:
                print('  ' + s)
        ok &= exact
    print('INFO %s: page lengths %s raster lines' % (name, ', '.join(str(n) for n in sorted(lengths))))
    return ok


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    ok = True
    for folder in sys.argv[1:]:
        ok &= check(folder)
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
