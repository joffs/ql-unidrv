# Rough native call stacks from a Windows minidump (ARM64 or x64) without a debugger or symbols: scans each thread's
# stack for return addresses into loaded modules (the instruction before is a call) and names them module!export+off.
# Managed (JIT) frames show as "?" addresses; NGEN images (*.ni.dll) show by module only. Reads only, changes nothing.
#   python -I tools\dumpstacks.py <file.dmp> [thread id ...]
# Written for the QL Printer Status hang (2026-11-04).
import struct, sys

def main(path, only):
    f = open(path, 'rb')
    def rd(off, n):
        f.seek(off); return f.read(n)
    sig, ver, nstreams, dirrva = struct.unpack('<4sIII', rd(0, 16))
    assert sig == b'MDMP'
    streams = {}
    for i in range(nstreams):
        t, size, rva = struct.unpack('<III', rd(dirrva + 12 * i, 12))
        streams[t] = (size, rva)
    arch = None
    if 7 in streams:  # SystemInfoStream
        arch = struct.unpack('<H', rd(streams[7][1], 2))[0]   # 9 = x64, 12 = ARM64
    # memory (Memory64ListStream)
    ranges = []
    n, base = struct.unpack('<QQ', rd(streams[9][1], 16))
    off = base
    for i in range(n):
        start, size = struct.unpack('<QQ', rd(streams[9][1] + 16 + 16 * i, 16))
        ranges.append((start, size, off)); off += size
    ranges.sort()
    import bisect
    starts = [r[0] for r in ranges]
    def mem(addr, n):
        i = bisect.bisect_right(starts, addr) - 1
        if i < 0: return None
        s, size, o = ranges[i]
        if addr + n > s + size: return None
        return rd(o + addr - s, n)
    def u64(addr):
        b = mem(addr, 8); return struct.unpack('<Q', b)[0] if b else None
    def u32(addr):
        b = mem(addr, 4); return struct.unpack('<I', b)[0] if b else None
    # modules
    mods = []
    cnt = struct.unpack('<I', rd(streams[4][1], 4))[0]
    for i in range(cnt):
        e = streams[4][1] + 4 + 108 * i
        b, size, _, _, namerva = struct.unpack('<QIIII', rd(e, 24))
        ln = struct.unpack('<I', rd(namerva, 4))[0]
        name = rd(namerva + 4, ln).decode('utf-16le').split('\\')[-1]
        mods.append((b, size, name))
    mods.sort()
    exports = {}
    def exp(m):
        if m in exports: return exports[m]
        b, size, name = m
        lst = []
        try:
            pe = u32(b + 0x3c); magic = struct.unpack('<H', mem(b + pe + 24, 2))[0]
            dd = b + pe + 24 + (112 if magic == 0x20b else 96)
            erva, esize = struct.unpack('<II', mem(dd, 8))
            if erva:
                e = b + erva
                nfun, nnames, afun, anames, aord = struct.unpack('<IIIII', mem(e + 20, 20))
                for k in range(nnames):
                    nrva = u32(b + anames + 4 * k); ordi = struct.unpack('<H', mem(b + aord + 2 * k, 2))[0]
                    frva = u32(b + afun + 4 * ordi)
                    s = mem(b + nrva, 80) or b''
                    lst.append((frva, s.split(b'\0')[0].decode('ascii', 'replace')))
        except Exception:
            pass
        lst.sort(); exports[m] = lst; return lst
    def name(a):
        i = bisect.bisect_right([m[0] for m in mods], a) - 1
        if i < 0: return None
        m = mods[i]
        if a >= m[0] + m[1]: return None
        rva = a - m[0]; best = None
        for frva, n in exp(m):
            if frva <= rva: best = (frva, n)
            else: break
        return m[2] + ('!' + best[1] + '+0x%x' % (rva - best[0]) if best else '+0x%x' % rva)
    def is_call(ret):
        if arch == 12:
            ins = u32(ret - 4)
            return ins is not None and ((ins & 0xFC000000) == 0x94000000 or (ins & 0xFFFFFC1F) == 0xD63F0000)
        b = mem(ret - 6, 6)
        return b is not None and (b[1] == 0xE8 or b[0] == 0xFF and (b[1] & 0x38) == 0x10 or b[4] == 0xFF and (b[5] & 0x38) == 0x10 or b[3] == 0xFF)
    # threads
    cnt = struct.unpack('<I', rd(streams[3][1], 4))[0]
    for i in range(cnt):
        e = streams[3][1] + 4 + 48 * i
        tid, _, _, _, teb, stk, _, _, csize, crva = struct.unpack('<IIIIQQIIII', rd(e, 48))
        if only and tid not in only: continue
        ctx = rd(crva, csize)
        if arch == 12: pc, sp = struct.unpack('<Q', ctx[264:272])[0], struct.unpack('<Q', ctx[256:264])[0]
        else: sp, pc = struct.unpack('<Q', ctx[0x98:0xa0])[0], struct.unpack('<Q', ctx[0xf8:0x100])[0]
        top = u64(teb + 8) or sp + 0x10000
        print('=== thread %d (0x%x)  pc %s' % (tid, tid, name(pc) or hex(pc)))
        a = sp; shown = 0
        while a < top and shown < 60:
            v = u64(a)
            if v and is_call(v):
                n = name(v)
                if n: print('   ', n); shown += 1
            a += 8

if __name__ == '__main__':
    main(sys.argv[1], set(int(x) for x in sys.argv[2:]))
