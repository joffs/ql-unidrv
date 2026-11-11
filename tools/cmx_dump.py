"""Lists the records of a CMX file (Corel Presentation Exchange, RIFF "CMX1", as CorelDRAW puts it on the clipboard
under "Corel 32-bit Presentation Exchange Data"): the RIFF chunks, then the page's instructions with their tags.
Read-only; changes nothing.

Usage: python tools/cmx_dump.py <file.bin> [--hex N]   (N: bytes of hex per tag, default 48)

The page chunk holds instructions: uint16 size (header included), int16 code; or the long form 0xFFFF, int32 size,
int16 code. Newer instructions carry tags: a tag byte, uint16 size (header included), data; tag 0xFF ends the list.
"""
import struct
import sys

CODES = {
    2: "Comment", 9: "BeginPage", 10: "EndPage", 11: "BeginLayer", 12: "EndLayer", 13: "BeginGroup", 14: "EndGroup",
    17: "BeginProcedure", 18: "EndSection", 20: "BeginTextStream", 21: "EndTextStream", 22: "BeginEmbedded",
    23: "EndEmbedded", 65: "DrawChars", 66: "Ellipse", 67: "PolyCurve", 68: "Rectangle", 69: "DrawImage",
    70: "BeginTextObject", 71: "EndTextObject", 72: "BeginTextGroup", 73: "EndTextGroup", 85: "SetCharStyle",
    86: "SimpleWideText", 88: "AddClippingRegion", 89: "RemoveLastClippingRegion", 90: "ClearClipping",
    93: "SetGlobalTransform", 94: "AddGlobalTransform", 95: "RestoreLastGlobalTransform", 96: "PushMappingMode",
    97: "PopMappingMode", 98: "TextFrame", 99: "BeginParagraph", 100: "EndParagraph", 101: "CharInfo",
    102: "Characters", 103: "PushTint", 104: "PopTint", 111: "JumpAbsolute",
}


def chunks(d, off, end, depth, out):
    while off + 8 <= end:
        cid = d[off:off + 4]
        n = struct.unpack_from("<I", d, off + 4)[0]
        if cid in (b"RIFF", b"LIST"):
            out.append(("  " * depth) + "%s %s @%d (%d bytes)" % (cid.decode("latin1"), d[off + 8:off + 12].decode("latin1"), off, n))
            chunks(d, off + 12, off + 8 + n, depth + 1, out)
        else:
            out.append(("  " * depth) + "%s @%d (%d bytes)" % (cid.decode("latin1"), off, n))
            yield_chunk[cid.decode("latin1")] = (off + 8, n)
        off += 8 + n + (n & 1)


yield_chunk = {}


def hexs(b, n):
    s = " ".join("%02x" % x for x in b[:n])
    return s + (" ..." if len(b) > n else "")


def utf16_runs(b):
    """Printable UTF-16 strings of 2+ characters in b."""
    found, cur = [], ""
    for i in range(0, len(b) - 1, 2):
        c = struct.unpack_from("<H", b, i)[0]
        if 32 <= c < 0xD800 and c not in (0x7F,):
            cur += chr(c)
        elif c == 13 and cur:
            cur += "\\r"
        else:
            if len(cur) >= 2:
                found.append(cur)
            cur = ""
    if len(cur) >= 2:
        found.append(cur)
    return found


def tags(body):
    """The tag list of an instruction body, or None when it isn't one."""
    out, i = [], 0
    while i < len(body):
        t = body[i]
        if t == 0xFF:
            return out if i == len(body) - 1 or all(x == 0 for x in body[i + 1:]) else None
        if i + 3 > len(body):
            return None
        n = struct.unpack_from("<H", body, i + 1)[0]
        if n < 3 or i + n > len(body):
            return None
        out.append((t, body[i + 3:i + n]))
        i += n
    return None


def main():
    path = sys.argv[1]
    nhex = int(sys.argv[sys.argv.index("--hex") + 1]) if "--hex" in sys.argv else 48
    d = open(path, "rb").read()
    out = []
    chunks(d, 0, len(d), 0, out)
    print("\n".join(out))
    if "page" not in yield_chunk:
        return
    off, n = yield_chunk["page"]
    end = off + n
    print("\npage instructions:")
    depth = 0
    while off + 4 <= end:
        size, code = struct.unpack_from("<Hh", d, off)
        head = 4
        if size == 0xFFFF:   # long form: 0xFFFF, int32 size, int16 code
            size, code = struct.unpack_from("<ih", d, off + 2)
            head = 8
        if size < head:
            print("@%d: size %d, stop" % (off, size))
            break
        body = d[off + head:off + size]
        name = CODES.get(abs(code), "?%d" % code)
        if name.startswith("End"):
            depth = max(0, depth - 1)
        print("%s@%d %s (%d) size %d" % ("  " * depth, off, name, code, size))
        tl = tags(body)
        if tl is not None:
            for t, data in tl:
                s = utf16_runs(data)
                print("%s  tag %d (%d): %s%s" % ("  " * depth, t, len(data), hexs(data, nhex), ("  " + repr(s)) if s else ""))
        elif body:
            print("%s  %s" % ("  " * depth, hexs(body, nhex)))
        if name.startswith("Begin"):
            depth += 1
        off += size


if __name__ == "__main__":
    main()
