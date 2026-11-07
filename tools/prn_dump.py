#!/usr/bin/env python3
"""Decode a Brother QL raster command stream (.prn) into a readable command list.

usage: prn_dump.py file.prn [--lines] [--png out.png]
  --lines   print every raster line command (default: summarize runs)
  --png     render the raster as a PNG (head order, one line per row, feed downwards; red plane in red)
"""
import sys


def u16(b, o):
    return b[o] | (b[o + 1] << 8)


def u32(b, o):
    return b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)


def unpackbits(data, outlen=None):
    """TIFF PackBits as used by QL 'M 02' compression. outlen None: the decoded length as is."""
    out = bytearray()
    i = 0
    while i < len(data):
        n = data[i]
        i += 1
        if n < 128:
            out += data[i:i + n + 1]
            i += n + 1
        elif n > 128:
            out += bytes([data[i]]) * (257 - n)
            i += 1
    if outlen is None:
        return bytes(out)
    if len(out) < outlen:
        out += bytes(outlen - len(out))
    return bytes(out[:outlen])


ZFLAGS = {0x02: 'KIND', 0x04: 'WIDTH', 0x08: 'LENGTH', 0x40: 'QUALITY', 0x80: 'RECOVER'}


def decode(b, show_lines=False, red_pages=None):
    """red_pages: if a list, filled with one list per page holding each line's red plane (w 02) or None."""
    pages = []
    cur = []
    cur_red = []
    i = 0
    n = len(b)
    compress = 0
    head_bytes = 90  # raster line width: 90 bytes (720-dot head), 162 on the QL-1050/1060N; set from the first line
    head_known = False
    out = []
    zero_run = 0
    while i < n and b[i] == 0:
        i += 1
    if i:
        out.append(f'{0:06x}  invalidate: {i} x 00')
    run_kind, run_count, run_start = None, 0, 0

    def flush():
        nonlocal run_kind, run_count
        if run_kind and not show_lines:
            out.append(f'{run_start:06x}  {run_kind} x {run_count}')
        run_kind, run_count = None, 0

    while i < n:
        c = b[i]
        o = i
        if c == 0x1B:
            cmd = b[i + 1:i + 3]
            if b[i + 1] == 0x40:
                flush(); out.append(f'{o:06x}  ESC @  (initialize)'); i += 2; continue
            if cmd == b'ia':
                flush(); out.append(f'{o:06x}  ESC i a {b[i+3]:02x}  (switch mode: {"raster" if b[i+3]==1 else b[i+3]})'); i += 4; continue
            if cmd == b'i!':
                flush(); out.append(f'{o:06x}  ESC i ! {b[i+3]:02x}  (auto status notify {"off" if b[i+3] else "on"})'); i += 4; continue
            if cmd == b'iU':
                sub = b[i + 3]
                if sub == 0x4A:  # 'J'
                    blk = b[i + 4:i + 18]
                    flush(); out.append(f'{o:06x}  ESC i U J {blk.hex(" ")}  (job id: {blk[0]:02x} {blk[1]:02x}, mac {blk[2:8].hex(":")}, jobid {u32(blk,10)})'); i += 18; continue
                if sub == 0x77:  # 'w'
                    blk = b[i + 5:i + 5 + 127]
                    flush(); out.append(f'{o:06x}  ESC i U w {b[i+4]:02x} + 127 bytes:'); out.append('        ' + blk.hex(' ')); i += 5 + 127; continue
                flush(); out.append(f'{o:06x}  ESC i U {sub:02x} ?? (unknown)'); i += 4; continue
            if cmd == b'iz':
                p = b[i + 3:i + 13]
                fl = '|'.join(v for k, v in ZFLAGS.items() if p[0] & k)
                flush(); out.append(f'{o:06x}  ESC i z {p.hex(" ")}  (flags {p[0]:02x}={fl}, kind {p[1]:02x}, width {p[2]}mm, length {p[3]}mm, lines {u32(p,4)}, page {p[8]}, n10 {p[9]})')
                i += 13; continue
            if cmd == b'iM':
                v = b[i + 3]; flush(); out.append(f'{o:06x}  ESC i M {v:02x}  (autocut={bool(v&0x40)} mirror={bool(v&0x80)} other={v&0x3f:02x})'); i += 4; continue
            if cmd == b'iA':
                flush(); out.append(f'{o:06x}  ESC i A {b[i+3]:02x}  (cut every {b[i+3]})'); i += 4; continue
            if cmd == b'iK':
                v = b[i + 3]; flush(); out.append(f'{o:06x}  ESC i K {v:02x}  (2color={bool(v&1)} cut_at_end={bool(v&8)} highres={bool(v&0x40)} other={v&0xb6:02x})'); i += 4; continue
            if cmd == b'id':
                flush(); out.append(f'{o:06x}  ESC i d {b[i+3]:02x} {b[i+4]:02x}  (margin {u16(b,i+3)} dots)'); i += 5; continue
            if cmd == b'ik':
                flush(); out.append(f'{o:06x}  ESC i k {b[i+3:i+6].hex(" ")}'); i += 6; continue
            if cmd == b'iS':
                flush(); out.append(f'{o:06x}  ESC i S  (status request)'); i += 3; continue
            flush(); out.append(f'{o:06x}  ESC {b[i+1:i+4].hex(" ")} ?? unknown'); i += 2; continue
        if c == 0x4D:  # M
            compress = b[i + 1]; flush(); out.append(f'{o:06x}  M {compress:02x}  (compression {"packbits" if compress==2 else "none"})'); i += 2; continue
        if c in (0x67, 0x47):  # g / G
            ln = b[i + 2] if c == 0x67 else u16(b, i + 1)
            hdr = 3
            data = b[i + hdr:i + hdr + ln]
            if not head_known:
                head_bytes, head_known = len(unpackbits(data) if compress == 2 else data), True
            raw = unpackbits(data, head_bytes) if compress == 2 else data
            cur.append(raw)
            cur_red.append(None)
            kind = f'g {b[i+1]:02x} nn (raster, {ln} bytes)'
            if show_lines:
                out.append(f'{o:06x}  {kind}: {data.hex()}')
            else:
                if kind != run_kind:
                    flush(); run_kind, run_start = kind, o
                run_count += 1
            i += hdr + ln; continue
        if c == 0x77:  # w (two colour)
            ln = b[i + 2]
            data = b[i + 3:i + 3 + ln]
            kind = f'w {b[i+1]:02x} nn (2-colour plane {b[i+1]})'
            if show_lines:
                out.append(f'{o:06x}  {kind}: {data.hex()}')
            else:
                if kind != run_kind:
                    flush(); run_kind, run_start = kind, o
                run_count += 1
            if b[i + 1] == 1:
                cur.append(unpackbits(data, head_bytes) if compress == 2 else data)
                cur_red.append(None)
            elif b[i + 1] == 2 and cur_red:
                cur_red[-1] = unpackbits(data, head_bytes) if compress == 2 else data
            i += 3 + ln; continue
        if c == 0x5A:  # Z
            cur.append(bytes(head_bytes))
            cur_red.append(None)
            kind = 'Z (blank line)'
            if kind != run_kind:
                flush(); run_kind, run_start = kind, o
            run_count += 1
            i += 1; continue
        if c in (0x0C, 0x1A):
            flush()
            out.append(f'{o:06x}  FF  (print, more pages follow)' if c == 0x0C else f'{o:06x}  1A  (print, last page)')
            pages.append(cur)
            if red_pages is not None:
                red_pages.append(cur_red)
            cur, cur_red = [], []
            i += 1; continue
        flush(); out.append(f'{o:06x}  ?? {c:02x}'); i += 1
    flush()
    if cur:
        pages.append(cur)
        if red_pages is not None:
            red_pages.append(cur_red)
    return out, pages


def write_png(pages, path, red_pages=None):
    """PNG preview: one raster line per image row, bit 7 of byte 0 at left (head order: the printed label is
    this picture mirrored left-right). Pages are separated by a blank row. 1-bit; if the stream has red planes
    (two-colour, w 02), an RGB picture with the red dots in red (black wins where both are set)."""
    import struct
    import zlib
    rows, reds = [], []
    for k, p in enumerate(pages):
        rows += p + [bytes(len(p[0]) if p else 90)]
        rp = red_pages[k] if red_pages and k < len(red_pages) else []
        reds += list(rp) + [None] * (len(p) - len(rp)) + [None]
    if not rows:
        return
    wb = max(len(r) for r in rows)
    two_colour = any(r for r in reds)
    if two_colour:
        lines = []
        for r, red in zip(rows, reds):
            r, red = r.ljust(wb, b'\0'), (red or b'').ljust(wb, b'\0')
            px = bytearray(b'\0')
            for x in range(wb * 8):
                bit = 0x80 >> (x & 7)
                px += b'\x00\x00\x00' if r[x >> 3] & bit else (b'\xdc\x00\x00' if red[x >> 3] & bit else b'\xff\xff\xff')
            lines.append(bytes(px))
        raw, depth, colour = b''.join(lines), 8, 2
    else:
        raw, depth, colour = b''.join(b'\0' + bytes(~x & 0xFF for x in r.ljust(wb, b'\0')) for r in rows), 1, 0

    def chunk(t, d):
        return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', wb * 8, len(rows), depth, colour, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(raw)) + chunk(b'IEND', b'')
    open(path, 'wb').write(png)


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__); return 2
    path = args[0]
    b = open(path, 'rb').read()
    red_pages = []
    out, pages = decode(b, '--lines' in args, red_pages)
    print(f'{path}: {len(b)} bytes, {len(pages)} page(s), lines/page: {[len(p) for p in pages]}')
    print('\n'.join(out))
    if '--png' in args:
        write_png(pages, args[args.index('--png') + 1], red_pages)


if __name__ == '__main__':
    sys.exit(main())
