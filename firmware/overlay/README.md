# The live-tuning overlay (V10.2)

A small add-on to **MS43X001**, the MS4X project's custom firmware for the Siemens MS43. It lives in
MS43X001's free flash space and lets MS43 Tuner change maps and settings in the ECU's RAM over DS2 on
the K-line, with no reflash. Nothing you send survives a power cycle until you copy it into the bin
and flash it.

> [!WARNING]
> This is experimental firmware. It has been tested in an emulator that runs the real MS43X001 code,
> on scripted drives, and in one car. It has not been validated more widely. Keep your original bin,
> flash the overlay bin only if you can flash back, and use the Fun features only on a closed road or
> a track.

## What it adds

- **Live maps:** the ECU reads chosen maps (ignition, AFR target, VANOS and more) from RAM, where the
  app writes them, and can switch between them and the flashed ones at once.
- **Live settings:** the rev limiter, and MS43X001's own launch control, no-lift shift and rolling
  anti-lag settings.
- **Pops & bangs** on lift-off, with a real AFR target.
- **A downshift blip:** an automatic rev-match on braking downshifts, with a spark cap above the target
  engine speed.
- **Switch triggers:** pops and the blip can follow cruise control armed, ASC off or A/C on.
- **A map watch:** the app's live cursor can show which map the firmware read last.

Every feature is off until you turn it on in the app.

## What the build changes

[`build_overlay.py`](build_overlay.py) takes **your own MS43X001 bin** and writes a patched copy next to
it. It writes code only:

- the overlay itself, into MS43X001's erased flash block at `0x4B544`..`0x4FFE6`;
- a jump to it at each of the 216 hook sites in [`write_list.json`](write_list.json), each checked
  against MS43X001's original bytes first;
- the five checksums, fixed and verified.

Your calibration (`0x70000` onwards: maps, injector flow, limiter, every tune value) comes out byte for
byte as it went in, apart from its two checksum words; the build checks this and refuses otherwise. It
also refuses a bin whose program is not MS43X001's, a bin already patched, and any write outside the
block and the hook sites. The boot block and the validity tags are never written.

Supported program: MS43X001 (Oct 2025 release, M54B25). Other MS43X001 variants are refused until their
program is checked.

## Build

You need Python 3.10 or later, and AS, Alfred Arnold's Macro Assembler
([asl and p2hex](http://john.ccac.rwth-aachen.de:8000/as/)), for the C167.

```bash
python -I build_overlay.py YOUR.bin
```

- With `asl` on PATH it runs natively. Otherwise give its folder with `--asl-dir`; on Windows without a
  native AS, the build runs it in WSL (`--wsl`, default `Ubuntu-24.04`), and `--asl-dir` is a WSL path.
- `--out OUT.bin` names the result; the default is `YOUR-live-overlay-v10_2.bin`.
- The build prints the program identity of the result. For V10.2 it is
  `b8bafce5f10d2b26a8095d7a71be094a634145bfd7a431bcc36c536c05de7173` on any supported bin, since the
  identity covers the program code only.

Flash the result with MS4X Flasher, as any bin. Then open it in MS43 Tuner: the Live tuning and Fun tabs
find the overlay on the ECU and in the bin.

## The source

[`src/live_overlay_v10.asm`](src/live_overlay_v10.asm) is the entry point; it includes the `.inc` files
next to it. It is C167 assembly for AS, written with physical addresses and the firmware's DPP
assumptions. It is generated in the author's lab from the earlier overlay versions, and published here
as the full, buildable source.

## License

GPL-3.0, as a modification of MS43X001 (MS4X project,
[MS43X-Custom-Firmware](https://github.com/ms4x-net/MS43X-Custom-Firmware), GPL-3.0). MS43X001 is
built on Siemens's MS430069 software, which is not part of this repository. No bin is published. See
[NOTICE.md](../../NOTICE.md).
