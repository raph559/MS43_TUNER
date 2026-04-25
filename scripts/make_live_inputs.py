"""Write definitions/ms43x001_live_inputs.json: which ECU variable feeds each table axis, and which logger channel
carries that variable. Used by the Tune tab's live cursor.

python -I scripts/make_live_inputs.py CATALOGUE.csv --bin BIN --functions TSV --tool PY --xdf XDF --out JSON

CATALOGUE.csv is the output of MS43LAB/tools/ms43_calmaps.py run on a copy of an MS43X001 bin with
--functions MS43LAB/ghidra/out_ms43x/functions.tsv. Every file is only read. The XDF only adds table names.
"""
import argparse, csv, datetime, hashlib, json, os, xml.etree.ElementTree as ET

# RAM variable -> logger channel of the ms43x ADX. offset/bits are the channel's place in the 0B B0 frame.
# A value read by the ECU = clamp(((channel raw & mask) >> shift) + add, 0, max); a negative shift shifts left.
FRAME = "frame 0x%02X (0B B0 frame built by 0C:467E)"
INPUTS = [
    ("E0E4", "N", 0x04, 16, 0x1FFF, 0, 0, 8191, False, "frame 0x04 = E0E4 & 0x1FFF, gear in bits 13-15 (0C:467E)"),
    ("807F", "N", 0x04, 16, 0x1FFF, 5, 0, 255, False, "0x128B0: 807F = E0E4 > 0x1FE0 ? 0xFF : E0E4 >> 5"),
    ("8080", "N", 0x04, 16, 0x1FFF, 5, 0, 255, False, "0x128CA: 8080 = 807F"),
    ("81AC", "N", 0x04, 16, 0xE000, 13, 0, 7, False,
     "frame 0x04 bits 13-15 = 81AC bits 0-2 (0C:467E); 81AC is 0, 80FA (<= 6, else 1) or the 5..0 of "
     "id_gear__n_vs_cru (0x58D18-0x58D7E), so all of it fits"),
    ("E33A", "MAFTI", 0x12, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x12 + " = E33A"),
    ("E33B", "MAFTI", 0x12, 16, 0xFFFF, 8, 0, 255, False, "high byte of E33A; no byte writes to E33B in the code"),
    ("E32C", "MAFIGA", 0x14, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x14 + " = E32C"),
    ("E32D", "MAFIGA", 0x14, 16, 0xFFFF, 8, 0, 255, False, "high byte of E32C; no byte writes to E32D in the code"),
    ("E328", "MAFIVVT", 0x16, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x16 + " = E328"),
    ("E329", "MAFIVVT", 0x16, 16, 0xFFFF, 8, 0, 255, False, "high byte of E328; no byte writes to E329 in the code"),
    ("E320", "MAFMMV", 0x18, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x18 + " = E320"),
    ("E330", "MAFMES", 0x1A, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x1A + " = E330"),
    ("E32E", "MAF", 0x1C, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x1C + " = E32E"),
    ("8FD6", "MAP", 0x08, 16, 0xFFFF, 0, 0, 65535, False, FRAME % 0x08 + " = 8FD6"),
    ("847F", "VS", 0x06, 8, 0xFF, 0, 0, 255, False, FRAME % 0x06 + " = 847F"),
    ("8432", "TIA", 0x0A, 8, 0xFF, 0, 0, 255, False, FRAME % 0x0A + " = 8432"),
    ("8433", "TCO", 0x0B, 8, 0xFF, 0, 0, 255, False, FRAME % 0x0B + " = 8433"),
    ("842F", "TOIL", 0x0C, 8, 0xFF, 0, 0, 255, False, FRAME % 0x0C + " = 842F"),
    ("8439", "TCOEX", 0x0D, 8, 0xFF, 0, 0, 255, False, FRAME % 0x0D + " = 8439"),
    ("E0FF", "PVS", 0x2A, 8, 0xFF, 0, 0, 255, False, FRAME % 0x2A + " = high byte of E0FE = E0FF"),
    ("E0FE", "PVS", 0x2A, 8, 0xFF, -8, 0x80, 65535, True, "the frame carries only the high byte of E0FE (0x2A)"),
    ("E15E", "TPS", 0x2B, 8, 0xFF, -8, 0x80, 65535, True, "the frame carries only the high byte of E15E (0x2B)"),
    ("84C8", "AMP", 0x07, 8, 0xFF, -8, 0x80, 65535, True, "the frame carries only the high byte of 84C8 (0x07)"),
]

VE_SOURCE = ("hand-added [inf]: XDF ip_map_ve_%d__map__n on ldpm_ve_map (0.0082921489*X kPa, the ADX MAP scale) x "
             "ldpm_ve_n; read through 0D:0168 from computed pointers at 0x3B9F8/0x3BA20/0x3BA48")


def sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def xdf_names(path):
    """Table titles by absolute data address (first table in file order wins)."""
    root = ET.parse(path).getroot()
    base_el = root.find("XDFHEADER/BASEOFFSET")
    base = int(base_el.get("offset", "0")) if base_el is not None else 0
    subtract = base_el is not None and base_el.get("subtract", "0") == "1"
    names = {}
    for table in root.iter("XDFTABLE"):
        for axis in table.findall("XDFAXIS"):
            data = axis.find("EMBEDDEDDATA")
            if axis.get("id") == "z" and data is not None and data.get("mmedaddress"):
                address = int(data.get("mmedaddress"), 16)
                names.setdefault(address - base if subtract else address + base, table.findtext("title"))
    return names


def hexa(value):
    return "0x%05X" % value


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("catalogue")
    ap.add_argument("--bin", required=True)
    ap.add_argument("--functions", required=True)
    ap.add_argument("--tool", required=True)
    ap.add_argument("--xdf", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--date", default=datetime.date.today().isoformat())
    args = ap.parse_args()

    names = xdf_names(args.xdf)
    tables = []
    with open(args.catalogue, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            axes = [[hexa(int(row["row_axis"], 16)), 8 if row["row_axis_elem"] == "u8" else 16, row["row_input"]]]
            if row["col_axis"]:
                axes.append([hexa(int(row["col_axis"], 16)), 8 if row["col_axis_elem"] == "u8" else 16, row["col_input"]])
            data = int(row["data"], 16)
            tables.append({"data": hexa(data), "name": names.get(data), "interp": row["interp"] == "1",
                           "axes": axes, "source": "catalogue"})
    count = len(tables)
    known = {t["data"] for t in tables}
    for k in range(1, 9):
        data = 0x7EE20 + (k - 1) * 0x200
        if hexa(data) in known:
            raise SystemExit("VE map %d is already in the catalogue" % k)
        tables.append({"data": hexa(data), "name": names.get(data), "interp": True,
                       "axes": [["0x77E60", 16, "8FD6"], ["0x77E82", 16, "E0E4"]], "source": VE_SOURCE % k})

    provenance = {
        "catalogue": "ms43_calmaps.py on a copy of %s with --functions out_ms43x/functions.tsv: %d tables"
                     % (os.path.basename(args.bin), count),
        "catalogueTool": "MS43LAB/tools/ms43_calmaps.py sha256 %s" % sha256(args.tool),
        "bin": "%s sha256 %s" % (os.path.basename(args.bin), sha256(args.bin)),
        "functions": "MS43LAB/ghidra/out_ms43x/functions.tsv sha256 %s" % sha256(args.functions),
        "frame": "0B B0 logger frame from 0C:467E (MS43LAB/ghidra/out_ms43x/decompiled.c); channel offsets from "
                 "definitions/Siemens_MS43_MS43X001_Logging.adx",
        "names": "table names from %s" % os.path.basename(args.xdf),
        "generator": "python -I scripts/make_live_inputs.py CATALOGUE.csv --bin BIN --functions TSV --tool PY "
                     "--xdf XDF --out definitions/ms43x001_live_inputs.json",
        "date": args.date,
    }
    inputs = [{"ram": ram, "channel": ch, "offset": "0x%02X" % off, "bits": bits, "mask": "0x%X" % mask,
               "shift": shift, "add": add, "max": mx, "approximate": approx, "evidence": ev}
              for ram, ch, off, bits, mask, shift, add, mx, approx, ev in INPUTS]

    lines = ["{", '  "format": 1,', '  "softwareId": "43X001",',
             '  "provenance": ' + json.dumps(provenance, indent=4).replace("\n", "\n  ") + ",", '  "inputs": [']
    lines += ["    " + json.dumps(i) + ("," if n < len(inputs) - 1 else "") for n, i in enumerate(inputs)]
    lines += ["  ],", '  "tables": [']
    lines += ["    " + json.dumps(t) + ("," if n < len(tables) - 1 else "") for n, t in enumerate(tables)]
    lines += ["  ]", "}", ""]
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("%d tables (%d from the catalogue), %d inputs -> %s" % (len(tables), count, len(inputs), args.out))


if __name__ == "__main__":
    main()
