# Map use: firmware evidence

These notes are the evidence behind `definitions/ms43x001_map_use.json`, the rules the Tune tab uses to say whether the ECU is using the map on screen. They come from read-only disassembly of the car bin (`testdata/local/CAR_current_43X001_SP95_STAGE1.bin`) with the tools of the sibling lab repository `../MS43LAB/tools/` (`dis.py`, `xref.py`, `c167dis.py`), on 2026-10-21. Tags: [obs] read in the code or data, [inf] inference.

| File | Maps |
|---|---|
| `ve.md` | `ip_map_ve_1..8__map__n` |
| `ignition.md` | ignition part load, idle, optimum and knock-adaptation maps |
| `loadstate.md` | the logged load state, VANOS setpoints, full-load AFR target, injection timing |
| `catheat.md` | catalyst-heating maps and what CHSTATE proves |

Where the rules file departs from a verdict in these notes:

- The E85 twins marked LEAVE OUT (read, weight 0) are in the rules with a weight taken from the calibration (cal 0x70A10..0x70A13 − 128) / 32, valid only while flex fuel is off (cal 0x70DF8 = 0). Otherwise they say "Selection unknown".
- The VANOS idle maps treat logged state 6 (full-load enrichment) as "Selection unknown", not as "not idle", like the idle ignition map.
- VANOS part load vs full load is left as "Possibly in use"; injection timing and the knock-adaptation maps are left out.
