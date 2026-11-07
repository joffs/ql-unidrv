#!/usr/bin/env python3
"""Compare two Brother QL raster streams semantically (e.g. QL-800 vs QL-810W capture of the same case).

usage: compare_models.py reference.prn candidate.prn [--quiet]

Unlike compare_prn.py (byte level), raster lines are decompressed first (M 02 PackBits, Z blank
lines), so a different compression setting shows up as an encoding difference, not as pixel
differences. Reports:
  - every non-raster command that differs (ESC i U J job-id block ignored), per page
  - raster encoding per page (g / G / w / Z counts, compressed byte total)
  - decompressed pixel differences per page (black plane 0; two-colour: 'w' planes 1 = black,
    2 = red, a Z line counts as blank in both)
Exit code 0 = same commands (except M) and same pixels.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from prn_dump import unpackbits  # noqa: E402

LINE = 90


def parse(b):
    """-> (lead zero count, pages); page = {'cmds': [bytes], 'planes': {plane: [line]}, 'enc': {...}, 'end': int}"""
    i = 0
    while i < len(b) and b[i] == 0:
        i += 1
    lead = i
    pages = []
    comp = 0
    two = False

    def new():
        return {'cmds': [], 'planes': {}, 'enc': {'g': 0, 'G': 0, 'w': 0, 'Z': 0, 'bytes': 0}, 'end': None}
    cur = new()
    while i < len(b):
        x = b[i]
        if x in (0x67, 0x47):
            ln = b[i + 2] if x == 0x67 else b[i + 1] | (b[i + 2] << 8)
            data = bytes(b[i + 3:i + 3 + ln])
            cur['planes'].setdefault(0, []).append(unpackbits(data, LINE) if comp == 2 else data.ljust(LINE, b'\0'))
            cur['enc']['g' if x == 0x67 else 'G'] += 1
            cur['enc']['bytes'] += ln
            i += 3 + ln
        elif x == 0x77:
            plane, ln = b[i + 1], b[i + 2]
            data = bytes(b[i + 3:i + 3 + ln])
            cur['planes'].setdefault(plane, []).append(unpackbits(data, LINE) if comp == 2 else data.ljust(LINE, b'\0'))
            cur['enc']['w'] += 1
            cur['enc']['bytes'] += ln
            i += 3 + ln
        elif x == 0x5A:
            # a blank line: in two-colour mode (ESC i K bit 0) it stands for both planes
            for plane in ((1, 2) if two else (0,)):
                cur['planes'].setdefault(plane, []).append(bytes(LINE))
            cur['enc']['Z'] += 1
            i += 1
        elif x in (0x0C, 0x1A):
            cur['end'] = x
            pages.append(cur)
            cur = new()
            i += 1
        elif x == 0x4D:
            comp = b[i + 1]
            cur['cmds'].append(bytes(b[i:i + 2]))
            i += 2
        elif x == 0x1B and b[i + 1] == 0x40:
            cur['cmds'].append(b'\x1b\x40')
            i += 2
        elif x == 0x1B and b[i + 1] == 0x69:
            c = b[i + 2]
            if c == 0x55:  # ESC i U <sub> <n0> <n1> ... : job id block, ignored
                cur['cmds'].append(b'\x1biU<job-id>')
                i += 6 + b[i + 5]
                continue
            n = {0x7A: 13, 0x64: 5, 0x6B: 6, 0x53: 3}.get(c, 4)
            if c == 0x4B:
                two = bool(b[i + 3] & 1)
            cur['cmds'].append(bytes(b[i:i + n]))
            i += n
        else:
            raise ValueError(f'unknown byte 0x{x:02x} at offset {i}')
    if cur['cmds'] or cur['planes']:
        pages.append(cur)
    return lead, pages


def popcount(bs):
    return sum(bin(v).count('1') for v in bs)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith('--')]
    if len(args) != 2:
        print(__doc__)
        return 2
    quiet = '--quiet' in sys.argv
    rl, rp = parse(open(args[0], 'rb').read())
    cl, cp = parse(open(args[1], 'rb').read())
    same = True
    notes = []
    if rl != cl:
        notes.append(f'leading zero bytes: reference {rl}, candidate {cl}')
        same = False
    if len(rp) != len(cp):
        notes.append(f'pages: reference {len(rp)}, candidate {len(cp)}')
        same = False
    for k in range(min(len(rp), len(cp))):
        a, c = rp[k], cp[k]
        ca = [x for x in a['cmds'] if x[:1] != b'M']
        cc = [x for x in c['cmds'] if x[:1] != b'M']
        if ca != cc:
            same = False
            for j in range(max(len(ca), len(cc))):
                xa = ca[j].hex(' ') if j < len(ca) else '-'
                xc = cc[j].hex(' ') if j < len(cc) else '-'
                if xa != xc:
                    notes.append(f'page {k + 1} command {j}: reference {xa} | candidate {xc}')
        ma = [x.hex(' ') for x in a['cmds'] if x[:1] == b'M']
        mc = [x.hex(' ') for x in c['cmds'] if x[:1] == b'M']
        if ma != mc:
            notes.append(f'page {k + 1} compression: reference {ma or "-"} | candidate {mc or "-"}')
        if a['end'] != c['end']:
            same = False
            notes.append(f'page {k + 1} terminator: reference {a["end"]} candidate {c["end"]}')
        if a['enc'] != c['enc']:
            notes.append(f'page {k + 1} encoding: reference {a["enc"]} | candidate {c["enc"]}')
        for plane in sorted(set(a['planes']) | set(c['planes'])):
            la, lc = a['planes'].get(plane, []), c['planes'].get(plane, [])
            n = min(len(la), len(lc))
            dl = [y for y in range(n) if la[y] != lc[y]]
            px = sum(popcount(bytes(p ^ q for p, q in zip(la[y], lc[y]))) for y in dl)
            if len(la) != len(lc) or dl:
                same = False
                notes.append(f'page {k + 1} plane {plane}: lines reference {len(la)} candidate {len(lc)}; '
                             f'{len(dl)} lines / {px} pixels differ' + (f' (first line {dl[0]})' if dl else ''))
    print(('SAME PIXELS+COMMANDS' if same else 'DIFFERENT') + f'  {os.path.basename(args[0])} vs {args[1]}')
    if not quiet or not same:
        for n in notes:
            print('  ' + n)
    return 0 if same else 1


if __name__ == '__main__':
    sys.exit(main())
