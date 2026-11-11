# Lists the records of an EMF file (one line each: offset, record name, the values that matter for drawing):
# fonts, pens, brushes, world transforms, paths and their points, text with its dx. Read-only.
# For finding out why a pasted picture looks wrong; the EMFs of clipboard dumps are private, so is the output.
# Usage: python tools\emf-records.py <file.emf> [--points]   (--points: every path point, not only the counts)
import struct
import sys

NAMES = {
    1: 'HEADER', 2: 'POLYBEZIER', 3: 'POLYGON', 4: 'POLYLINE', 5: 'POLYBEZIERTO', 6: 'POLYLINETO', 7: 'POLYPOLYLINE',
    8: 'POLYPOLYGON', 9: 'SETWINDOWEXTEX', 10: 'SETWINDOWORGEX', 11: 'SETVIEWPORTEXTEX', 12: 'SETVIEWPORTORGEX',
    13: 'SETBRUSHORGEX', 14: 'EOF', 17: 'SETMAPMODE', 18: 'SETBKMODE', 19: 'SETPOLYFILLMODE', 20: 'SETROP2',
    21: 'SETSTRETCHBLTMODE', 22: 'SETTEXTALIGN', 24: 'SETTEXTCOLOR', 25: 'SETBKCOLOR', 27: 'MOVETOEX', 28: 'SETMETARGN',
    29: 'EXCLUDECLIPRECT', 30: 'INTERSECTCLIPRECT', 33: 'SAVEDC', 34: 'RESTOREDC', 35: 'SETWORLDTRANSFORM',
    36: 'MODIFYWORLDTRANSFORM', 37: 'SELECTOBJECT', 38: 'CREATEPEN', 39: 'CREATEBRUSHINDIRECT', 40: 'DELETEOBJECT',
    43: 'RECTANGLE', 44: 'ROUNDRECT', 54: 'LINETO', 58: 'SETMITERLIMIT', 59: 'BEGINPATH', 60: 'ENDPATH',
    61: 'CLOSEFIGURE', 62: 'FILLPATH', 63: 'STROKEANDFILLPATH', 64: 'STROKEPATH', 65: 'FLATTENPATH', 66: 'WIDENPATH',
    67: 'SELECTCLIPPATH', 68: 'ABORTPATH', 70: 'GDICOMMENT', 75: 'EXTSELECTCLIPRGN', 76: 'BITBLT', 77: 'STRETCHBLT',
    81: 'STRETCHDIBITS', 82: 'EXTCREATEFONTINDIRECTW', 83: 'EXTTEXTOUTA', 84: 'EXTTEXTOUTW', 85: 'POLYBEZIER16',
    86: 'POLYGON16', 87: 'POLYLINE16', 88: 'POLYBEZIERTO16', 89: 'POLYLINETO16', 90: 'POLYPOLYLINE16',
    91: 'POLYPOLYGON16', 95: 'EXTCREATEPEN', 98: 'SETICMMODE', 114: 'SETLAYOUT', 115: 'TRANSPARENTBLT',
}
STOCK = {0x80000000 + i: n for i, n in enumerate(['WHITE_BRUSH', 'LTGRAY_BRUSH', 'GRAY_BRUSH', 'DKGRAY_BRUSH', 'BLACK_BRUSH',
                                                   'NULL_BRUSH', 'WHITE_PEN', 'BLACK_PEN', 'NULL_PEN', '', 'OEM_FIXED_FONT',
                                                   'ANSI_FIXED_FONT', 'ANSI_VAR_FONT', 'SYSTEM_FONT', 'DEVICE_DEFAULT_FONT',
                                                   'DEFAULT_PALETTE', 'SYSTEM_FIXED_FONT', 'DEFAULT_GUI_FONT', 'DC_BRUSH', 'DC_PEN'])}


def colour(v):
    return '#%02x%02x%02x' % (v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF)


def main():
    d = open(sys.argv[1], 'rb').read()
    points = '--points' in sys.argv
    o = 0
    while o + 8 <= len(d):
        t, n = struct.unpack_from('<II', d, o)
        name = NAMES.get(t, str(t))
        info = ''
        if t == 1:
            bounds = struct.unpack_from('<4i', d, o + 8)
            frame = struct.unpack_from('<4i', d, o + 24)
            dev = struct.unpack_from('<2i', d, o + 72)
            mm = struct.unpack_from('<2i', d, o + 80)
            info = 'bounds %s frame(0.01mm) %s device px %s mm %s' % (bounds, frame, dev, mm)
        elif t in (9, 10, 11, 12, 27):
            info = str(struct.unpack_from('<2i', d, o + 8))
        elif t in (17, 18, 19, 20, 22, 34, 98):
            info = str(struct.unpack_from('<i', d, o + 8)[0])
        elif t in (24, 25):
            info = colour(struct.unpack_from('<I', d, o + 8)[0])
        elif t in (35, 36):
            m = struct.unpack_from('<6f', d, o + 8)
            info = '[%s]' % ', '.join('%.4g' % v for v in m) + (' mode %d' % struct.unpack_from('<I', d, o + 32)[0] if t == 36 else '')
        elif t in (37, 40):
            h = struct.unpack_from('<I', d, o + 8)[0]
            info = STOCK.get(h, str(h))
        elif t == 38:
            ih, style, w, _, c = struct.unpack_from('<IIiiI', d, o + 8)
            info = 'obj %d style 0x%x width %d %s' % (ih, style, w, colour(c))
        elif t == 39:
            ih, style, c, hatch = struct.unpack_from('<IIII', d, o + 8)
            info = 'obj %d style %d %s' % (ih, style, colour(c))
        elif t == 95:
            ih, offBmi, cbBmi, offBits, cbBits, style, width, bstyle, c, hatch, nEntries = struct.unpack_from('<11I', d, o + 8)
            info = 'obj %d style 0x%x (type %s, end 0x%x, join 0x%x) width %d brush %d %s dashes %d' % (
                ih, style, 'geometric' if style & 0x10000 else 'cosmetic', style & 0xF00, style & 0xF000, width, bstyle, colour(c), nEntries)
            if nEntries:
                info += ' ' + str(struct.unpack_from('<%dI' % nEntries, d, o + 52))
        elif t == 82:
            ih = struct.unpack_from('<I', d, o + 8)[0]
            lf = struct.unpack_from('<5i8B', d, o + 12)
            face = d[o + 40:o + 104].decode('utf-16le').split('\0')[0]
            info = 'obj %d height %d width %d esc %d orient %d weight %d italic %d "%s"' % (ih, lf[0], lf[1], lf[2], lf[3], lf[4], lf[5], face)
        elif t == 84:
            gm, sx, sy = struct.unpack_from('<Iff', d, o + 24)
            rx, ry, nch, offs, opts = struct.unpack_from('<iiIII', d, o + 36)
            offdx = struct.unpack_from('<I', d, o + 72)[0]
            s = d[o + offs:o + offs + 2 * nch].decode('utf-16le', 'replace')
            dx = struct.unpack_from('<%di' % nch, d, o + offdx) if offdx else ()
            info = 'mode %d at (%d,%d) opts 0x%x %r dx %s' % (gm, rx, ry, opts, s, list(dx))
        elif t in (85, 86, 87, 88, 89):
            cnt = struct.unpack_from('<I', d, o + 24)[0]
            pts = struct.unpack_from('<%dh' % (2 * cnt), d, o + 28)
            info = '%d points' % cnt + (': ' + ' '.join('(%d,%d)' % (pts[i], pts[i + 1]) for i in range(0, len(pts), 2)) if points else '')
        elif t in (2, 3, 4, 5, 6):
            cnt = struct.unpack_from('<I', d, o + 24)[0]
            info = '%d points' % cnt
        elif t in (90, 91):
            npolys, cnt = struct.unpack_from('<II', d, o + 24)
            info = '%d polys, %d points' % (npolys, cnt)
        elif t == 70:
            size = struct.unpack_from('<I', d, o + 8)[0]
            info = '%d bytes %r' % (size, d[o + 12:o + 16])
        elif t == 75:
            cb, mode = struct.unpack_from('<II', d, o + 8)
            info = 'mode %d, %d bytes' % (mode, cb)
        elif t in (29, 30):
            info = str(struct.unpack_from('<4i', d, o + 8))
        elif t == 58:
            info = str(struct.unpack_from('<f', d, o + 8)[0])
        print('%6d %-22s %s' % (o, name, info))
        if t == 14 or n < 8:
            break
        o += n


if __name__ == '__main__':
    main()
