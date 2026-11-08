"""Build the live-tuning overlay (V10.2) into a copy of your own MS43X001 bin.

    python -I build_overlay.py YOUR.bin [--out OUT.bin] [--asl-dir DIR] [--wsl DISTRO]

It writes code only: the overlay in MS43X001's free flash block, and the jumps to it at the hook sites
listed in write_list.json. Your calibration (everything from 0x70000) is checked to come out byte for
byte as it went in, apart from its two checksum words, so your maps, injector flow, limiter and every
other tune value stay yours.

Steps:
  1. Read your bin (512 KiB) and check that its program code is MS43X001's (SUPPORTED). Another
     program, or one already patched, is refused: the hooks would land on the wrong code.
  2. Assemble src/live_overlay_v10.asm with AS (asl and p2hex), natively if `asl` is on PATH or in
     --asl-dir, otherwise in WSL (--wsl, default Ubuntu-24.04). Any assembler warning but
     "page might not be addressable" (AS gives it on each EXTP/EXTS access, with correct bytes) refuses.
  3. Refuse any record outside the free block or the write list, and any hook site whose original bytes
     differ from MS43X001's. Every write-list site must be written.
  4. Apply the records, check the overlay header and the program CRC range, fix and verify the five
     checksums, and check that the calibration is unchanged.
  5. Write OUT (default: YOUR-live-overlay-v10_2.bin next to it). Your bin is only read.

Nothing here flashes anything. Keep your original bin, and flash the result only if you can flash back.
AS is Alfred Arnold's Macro Assembler: http://john.ccac.rwth-aachen.de:8000/as/
"""
import argparse
import hashlib
import json
import os
import shutil
import struct
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import ms43_checksums  # noqa: E402

SOURCE = os.path.join(HERE, "src", "live_overlay_v10.asm")
WRITE_LIST = os.path.join(HERE, "write_list.json")
SIZE = 0x80000
BLOCK = (0x4B544, 0x4FFE6)          # MS43X001's free flash, up to the programming signature
NEVER = [(0x00000, 0x08000), (0x4FFE6, 0x50000), (0x6FFF0, 0x70000), (0x7FFF0, 0x80000)]
CALIBRATION = (0x70000, 0x80000)
# The calibration's own checksum words, the only calibration bytes the build may change: fixing the five
# checksums recomputes them, and MS43X001 bins (the release included) can carry a calibration CRC that
# does not match, as MS4X Flasher corrects it when it flashes.
CALIBRATION_CHECKSUMS = [(0x72FFC, 4), (0x73FE0, 2)]
ALLOWED_WARNING = "page might not be addressable"

# Program identity: SHA-256 of the program code (file 0x10000..0x4FFE6 and 0x50000..0x70000),
# which leaves out the boot block, the free block's end and the whole calibration.
SUPPORTED = {
    "32141e6358bfbfa0cc58bf6bbab72516f04ea769afbbba67e572923e3bb10d35": "MS43X001 (Oct 2025 release, M54B25)",
}


def fail(message):
    sys.exit("build_overlay: " + message)


def identity(image):
    return hashlib.sha256(bytes(image[0x10000:0x4FFE6]) + bytes(image[0x50000:0x70000])).hexdigest()


def read_ihex(path):
    """Yield (address, bytes) from Intel HEX with extended linear/segment records."""
    upper = 0
    for line in open(path):
        line = line.strip()
        if not line.startswith(":"):
            continue
        raw = bytes.fromhex(line[1:])
        count, address, kind, data = raw[0], (raw[1] << 8) | raw[2], raw[3], raw[4:4 + raw[0]]
        if kind == 0:
            yield upper + address, data
        elif kind == 2:
            upper = ((data[0] << 8) | data[1]) << 4
        elif kind == 4:
            upper = ((data[0] << 8) | data[1]) << 16


def wsl_path(path):
    path = os.path.abspath(path).replace("\\", "/")
    return "/mnt/%s%s" % (path[0].lower(), path[2:])


def assemble(asl_dir, distro):
    base = os.path.splitext(SOURCE)[0]
    asl = shutil.which("asl", path=asl_dir) if asl_dir else shutil.which("asl")
    if asl:
        p2hex = shutil.which("p2hex", path=os.path.dirname(asl))
        if not p2hex:
            fail("found %s but no p2hex next to it" % asl)
        steps = [[asl, "-q", "-L", "-olist", base + ".lst", SOURCE],
                 [p2hex, "-q", "-F", "Intel32", "-l", "32", base + ".p", base + ".hex"]]
        for step in steps:
            result = subprocess.run(step, cwd=os.path.dirname(SOURCE), capture_output=True, text=True)
            if result.returncode:
                break
    else:
        tools = (asl_dir.rstrip("/") + "/") if asl_dir else ""
        command = ("set -e; cd \"$(dirname '{a}')\"; {t}asl -q -L -olist '{b}.lst' '{a}'; "
                   "{t}p2hex -q -F Intel32 -l 32 '{b}.p' '{b}.hex'").format(a=wsl_path(SOURCE), b=wsl_path(base), t=tools)
        try:
            result = subprocess.run(["wsl.exe", "-d", distro, "--exec", "bash", "-lc", command],
                                    capture_output=True, text=True)
        except FileNotFoundError:
            fail("no asl on PATH or in --asl-dir, and no WSL to run it in")
    if result.returncode:
        listing = base + ".lst"
        errors = [line for line in open(listing, errors="replace") if "error" in line] if os.path.exists(listing) else []
        fail("assembly failed:\n" + result.stdout + result.stderr + "".join(errors))
    bad = [line.strip() for line in open(base + ".lst", errors="replace")
           if (": warning:" in line and ALLOWED_WARNING not in line) or ": error:" in line]
    if bad:
        fail("the assembler said more than '%s':\n  %s" % (ALLOWED_WARNING, "\n  ".join(bad)))
    return base + ".hex"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("bin", help="your MS43X001 bin (only read)")
    parser.add_argument("--out", help="the bin to write (default: next to yours, -live-overlay-v10_2.bin)")
    parser.add_argument("--asl-dir", help="the folder with asl and p2hex (native, or a WSL path)")
    parser.add_argument("--wsl", default="Ubuntu-24.04", help="the WSL distribution to run AS in, when it is not native")
    args = parser.parse_args()

    source = open(args.bin, "rb").read()
    if len(source) != SIZE:
        fail("%s is %d bytes, not a 512 KiB MS43 bin" % (args.bin, len(source)))
    program = identity(source)
    if program not in SUPPORTED:
        fail("this bin's program is not one this overlay is built for (program identity %s).\n"
             "It needs an unpatched MS43X001 program; the calibration may be anything." % program)
    out = args.out or os.path.splitext(args.bin)[0] + "-live-overlay-v10_2.bin"
    if os.path.abspath(out) == os.path.abspath(args.bin):
        fail("--out must not be your bin")
    print("program: %s" % SUPPORTED[program])

    sites = {int(row["offset"], 16): row for row in json.load(open(WRITE_LIST))}
    records = [(address & 0x7FFFF, bytes(data)) for address, data in read_ihex(assemble(args.asl_dir, args.wsl))]
    image = bytearray(source)
    written = set()
    for offset, data in records:
        end = offset + len(data)
        if any(offset < stop and end > start for start, stop in NEVER):
            fail("a record writes file 0x%05X-0x%05X: the boot block, a tag or the signature" % (offset, end - 1))
        if BLOCK[0] <= offset and end <= BLOCK[1]:
            if any(b != 0xFF for b in source[offset:end]):
                fail("the free block is not erased at 0x%05X-0x%05X" % (offset, end - 1))
        else:
            row = sites.get(offset)
            if row is None or len(data) != len(bytes.fromhex(row["original"])):
                fail("a record writes file 0x%05X-0x%05X, outside the write list" % (offset, end - 1))
            if source[offset:end] != bytes.fromhex(row["original"]):
                fail("unexpected original bytes at 0x%05X (%s)" % (offset, row["name"]))
            written.add(offset)
        image[offset:end] = data
    missing = sorted(set(sites) - written)
    if missing:
        fail("write-list sites not written: " + ", ".join("0x%05X" % site for site in missing))

    block_end = max(offset + len(data) for offset, data in records if BLOCK[0] <= offset < BLOCK[1])
    header = image[BLOCK[0]:BLOCK[0] + 16]
    if header[:4] != b"LTOV":
        fail("no LTOV header at 0x%05X" % BLOCK[0])
    version, code_start, code_end, data_end = struct.unpack_from("<HHHH", header, 4)
    if 0x40000 + data_end != block_end:
        fail("the header's data end 0C:%04X is not the block's end 0x%05X" % (data_end, block_end))
    crc_end = struct.unpack_from("<I", image, 0x6FE00)[0]
    if crc_end != 0x0C0000 + data_end - 1:
        fail("the program CRC range ends at %06X, not at the overlay's last byte" % crc_end)

    ms43_checksums.fix(image)
    print("checksums:")
    if not ms43_checksums.check(image):
        fail("the checksums do not verify")
    changed = [offset for offset in range(*CALIBRATION) if image[offset] != source[offset]
               and not any(start <= offset < start + size for start, size in CALIBRATION_CHECKSUMS)]
    if changed:
        fail("the calibration changed at 0x%05X: refusing" % changed[0])

    open(out, "wb").write(image)
    print("overlay protocol %d: code 0C:%04X-0C:%04X, data to 0C:%04X (%d bytes of %d free); %d hook sites"
          % (version, code_start, code_end, data_end, data_end - (BLOCK[0] - 0x40000), BLOCK[1] - BLOCK[0], len(sites)))
    print("calibration unchanged (apart from its checksum words)")
    print("wrote %s" % out)
    print("program identity %s" % identity(image))
    return 0


if __name__ == "__main__":
    sys.exit(main())
