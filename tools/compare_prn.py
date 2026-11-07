#!/usr/bin/env python3
"""Compare two Brother QL raster streams (e.g. reference capture vs. ARM64 driver output).

usage: compare_prn.py reference.prn candidate.prn

Ignores Brother's "ESC i U J" job-id block (MAC address + spooler job id; "ESC i U w" is compared), and the candidate's job
header (invalidate + ESC @) when the reference has none (QL-570 and older). Reports per page:
header commands that differ, raster line count, and the number of differing pixels (packed lines, M 02,
are unpacked first).
Exit code 0 = byte-identical after normalisation.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from prn_dump import unpackbits  # noqa: E402


def parse(b):
    """-> (normalized bytes, pages); page = {'cmds': [bytes], 'lines': [bytes]}"""
    norm = bytearray()
    pages, cur = [], {'cmds': [], 'lines': []}
    packed = False
    width = None  # raster line bytes: 90, 162 on the QL-1050/1060N; from the first line
    i = 0
    while i < len(b) and b[i] == 0:
        i += 1
    norm += b[:i]
    lead = i
    while i < len(b):
        x = b[i]
        if x == 0x67 or x == 0x77:
            n = 3 + b[i + 2]
            data = bytes(b[i + 3:i + n])
            if width is None:
                width = len(unpackbits(data) if packed else data)
            cur['lines'].append(unpackbits(data, width) if packed else data)
        elif x == 0x5A:
            n = 1
            cur['lines'].append(bytes(width or 90))
        elif x in (0x0C, 0x1A):
            n = 1
            cur['end'] = x
            pages.append(cur)
            cur = {'cmds': [], 'lines': []}
        elif x == 0x4D:
            n = 2
            packed = b[i + 1] == 0x02
            cur['cmds'].append(bytes(b[i:i + n]))
        elif x == 0x1B and b[i + 1] == 0x40:
            n = 2
            cur['cmds'].append(bytes(b[i:i + n]))
        elif x == 0x1B and b[i + 1] == 0x69:
            c = b[i + 2]
            if c == 0x55 and b[i + 3] != 0x77:  # ESC i U J: job-id block, ignored
                i += 6 + b[i + 5]
                continue
            n = {0x7A: 13, 0x64: 5, 0x55: 132}.get(c, 4)  # ESC i U w 01 + 127 bytes (QL-1115NWB DT papers)
            cur['cmds'].append(bytes(b[i:i + n]))
        else:
            raise ValueError(f'unknown byte 0x{x:02x} at offset {i}')
        norm += b[i:i + n]
        i += n
    return bytes(norm), pages, lead


def popcount(bs):
    return sum(bin(v).count('1') for v in bs)


def main():
    if len(sys.argv) != 3:
        print(__doc__)
        return 2
    ref, cand = (open(p, 'rb').read() for p in sys.argv[1:3])
    # QL-570 and older captures start at the first page command (Brother's language monitor sends the
    # invalidate + ESC @): compare the candidate without its job header then.
    if ref[:1] != b'\0' and cand[:1] == b'\0':
        k = len(cand) - len(cand.lstrip(b'\0'))
        cand = cand[k + 2:] if cand[k:k + 2] == b'\x1b@' else cand[k:]
    rn, rp, rl = parse(ref)
    cn, cp, cl = parse(cand)
    if rn == cn:
        print(f'IDENTICAL  {len(rp)} page(s), {len(rn)} bytes (job-id block ignored)')
        return 0
    print(f'DIFFERENT  reference {len(rp)} page(s) / {len(rn)} bytes, candidate {len(cp)} page(s) / {len(cn)} bytes')
    if rl != cl:
        print(f'  leading zero bytes: reference {rl}, candidate {cl}')
    for k in range(max(len(rp), len(cp))):
        if k >= len(rp) or k >= len(cp):
            print(f'  page {k + 1}: only in {"candidate" if k >= len(rp) else "reference"}')
            continue
        a, b = rp[k], cp[k]
        if a['cmds'] != b['cmds']:
            for j in range(max(len(a['cmds']), len(b['cmds']))):
                ca = a['cmds'][j].hex(' ') if j < len(a['cmds']) else '-'
                cb = b['cmds'][j].hex(' ') if j < len(b['cmds']) else '-'
                if ca != cb:
                    print(f'  page {k + 1} command {j}: reference {ca} | candidate {cb}')
        if a.get('end') != b.get('end'):
            print(f'  page {k + 1} terminator: reference {a.get("end"):02x} candidate {b.get("end"):02x}')
        la, lb = a['lines'], b['lines']
        n = min(len(la), len(lb))
        diff_lines = [y for y in range(n) if la[y] != lb[y]]
        diff_px = sum(popcount(bytes(p ^ q for p, q in zip(la[y], lb[y]))) for y in diff_lines)
        ink_a = sum(popcount(l) for l in la)
        print(f'  page {k + 1}: lines reference {len(la)} candidate {len(lb)}; '
              f'{len(diff_lines)} lines / {diff_px} pixels differ (reference has {ink_a} black pixels)')
        if diff_lines:
            y = diff_lines[0]
            print(f'    first differing line {y}')
    return 1


if __name__ == '__main__':
    sys.exit(main())
