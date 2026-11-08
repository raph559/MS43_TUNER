"""Verify (and optionally fix) MS43 430069 checksums.

    python ms43_checksums.py BIN            verify
    python ms43_checksums.py BIN --fix OUT  write a corrected copy

Algorithms recovered from the boot block (file 0x1460-0x1772, CRC loop at
0x1742) and the monitor checksum routine (0x5FCxx):

* CRC-16, reflected polynomial 0xA001, no final XOR, processed byte-wise
  over a list of inclusive [start, end] ranges. Seed is the little-endian
  word at a pointer stored in the descriptor (boot: word at 0x3FE6).
* 32-bit "monitor" sums (routine 0x5FCxx, adder at 0x5FD7C): u32 seed plus
  every little-endian u16 in two [start, end) ranges. Descriptor block at
  file 0x6FDAE: +0 u32 program sum, +4 u32 program seed, +10 u32 cal seed,
  +16 2x(start,end) program ranges, +32 2x(start,end) cal ranges. The cal
  sum is stored at 0x72FFC (inside the calibration CRC range, so fix sums
  before CRCs). Cal byte 0x70E07 == 0xA5 makes the ECU ignore a cal-sum
  mismatch; cal word 0x73FFE == 0xFFFF skips the cal CRC.
* The range list sits right after each stored CRC:
      +0 u16 crc, +2 u8 count, +4 count x (u32 start, u32 end)
  Addresses are CPU linear addresses; the 512 KiB flash is mirrored, so
  file offset = address & 0x7FFFF.
"""
import struct
import sys

MASK = 0x7FFFF


def crc16(data, crc):
    for b in data:
        crc ^= b
        for _ in range(8):
            crc = (crc >> 1) ^ 0xA001 if crc & 1 else crc >> 1
    return crc


def ranges(d, off):
    stored = struct.unpack_from("<H", d, off)[0]
    n = d[off + 2]
    rs = [struct.unpack_from("<II", d, off + 4 + 8 * i) for i in range(n)]
    return stored, rs


def crc_over(d, rs, seed):
    c = seed
    for s, e in rs:
        c = crc16(d[s & MASK:(e & MASK) + 1], c)
    return c


def u16(d, o):
    return struct.unpack_from("<H", d, o)[0]


def u32(d, o):
    return struct.unpack_from("<I", d, o)[0]


# (name, descriptor offset, seed offset)
#   boot seed: EXTP #0 / MOV R2,3FE6h  -> word at 0x3FE6
#   program seed: 32-bit pointer at DPP1:6D42 (file 0x6ED42)
#   calibration seed: 32-bit pointer at DPP1:6D5A (file 0x6ED5A)
def blocks(d):
    yield "boot", 0x3C24, 0x3FE6
    yield "program", 0x6FDE0, u32(d, 0x6ED42) & MASK
    yield "calibration", 0x73FE0, u32(d, 0x6ED5A) & MASK


def monitor_sums(d):
    """Yield (name, stored_offset, stored, calc)."""
    for name, store, seed, tab in (("program", 0x6FDAE, 0x6FDB2, 0x6FDBE),
                                   ("calibration", 0x72FFC, 0x6FDB8, 0x6FDCE)):
        s = u32(d, seed)
        for i in range(2):
            a, b = (x & MASK for x in struct.unpack_from("<II", d, tab + 8 * i))
            s += sum(struct.unpack_from("<%dH" % ((b - a) // 2), d, a))
        yield name, store, u32(d, store), s & 0xFFFFFFFF


def fix(d):
    for name, store, stored, calc in monitor_sums(d):
        struct.pack_into("<I", d, store, calc)
    for name, desc, seedoff in blocks(d):
        stored, rs = ranges(d, desc)
        if name == "boot":
            rs = [struct.unpack_from("<II", d, desc + 4)]
        struct.pack_into("<H", d, desc, crc_over(d, rs, u16(d, seedoff)))


def check(d):
    ok = True
    for name, store, stored, calc in monitor_sums(d):
        flag = "OK " if calc == stored else "BAD"
        ok &= calc == stored
        print("%s SUM32 %-11s stored %08X calc %08X @%05X"
              % (flag, name, stored, calc, store))
    for name, desc, seedoff in blocks(d):
        stored, rs = ranges(d, desc)
        if name == "boot":
            # boot descriptor uses one (start, end) pair at +4
            rs = [struct.unpack_from("<II", d, desc + 4)]
        seed = u16(d, seedoff)
        calc = crc_over(d, rs, seed)
        rtxt = ", ".join("%06X-%06X" % r for r in rs)
        flag = "OK " if calc == stored else "BAD"
        ok &= calc == stored
        print("%s CRC16 %-11s stored %04X calc %04X seed %04X@%05X  [%s]"
              % (flag, name, stored, calc, seed, seedoff, rtxt))
    return ok


if __name__ == "__main__":
    d = bytearray(open(sys.argv[1], "rb").read())
    if len(sys.argv) == 4 and sys.argv[2] == "--fix":
        fix(d)
        open(sys.argv[3], "wb").write(d)
        print("wrote", sys.argv[3])
    sys.exit(0 if check(d) else 1)
