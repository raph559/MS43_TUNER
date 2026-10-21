# Ignition family: map selection and blend weights (MS43X001, car image)

Source: `testdata/local/CAR_current_43X001_SP95_STAGE1.bin` (read only). Offsets are file offsets
(CPU address & 0x7FFFF; 0A:C2AE = file 0x2C2AE, 09:0000 = file 0x10000, 0D:xxxx = 0x5xxxx).
[obs] = read in the disassembly / bytes (offset cited). [inf] = inference.

## 1. The ignition routine 0A:C2AE (file 0x2C2AE) and how it picks a branch

- [obs] 0x2C2E4 `JNB FD48.0` else `JMPA 2D04C`: when FD48.0 is set the whole map step is skipped
  this run. FD48.0 toggles every run (`BXOR FD48.0`, 0x3F35C) when 807F > 0x92 (0x3F33C), so above
  that speed the maps are evaluated every other run and 81D3 keeps the last result. [inf] This
  does not change which map is in use.
- [obs] 0x2C2EC `MOV R4,FD9A; AND R4,#7`: =1 -> 0x2C2FE (start curve 0x747D0 vs E0E4; no family
  map), =2 -> 0x2C37C (idle maps), anything else -> 0x2C608 (part-load / overrun logic).
- [obs] FD9A bits 0-2 and FD2C bits 7-12 are written together by the state machine at
  0x29B6C-0x2AE10. Every `BSET FD2C.n` is followed within a few instructions by a matching
  `BFLDL FD9A,#07h,#v`: .8->1 (0x2A1F0/0x2A200, 0x2AB9C/0x2ABAC), .9->2 (0x2A508/0x2A516,
  0x2A90A/0x2A918, 0x2AAEA/0x2AAF8, 0x2ADE2/0x2ADF0), .10->3 (0x2A980, 0x2AA0C, 0x2AC6A, 0x2ADAA),
  .11->4 (0x2A6D0, 0x2AACC, 0x2ACF4), .12->5 (0x2A7D2/0x2A7DC); .7 with FD9A&7=0 (0x29B6C/0x29B82
  `AND R4,#0240h`). One exception: 0x2A44E falls back from part load (.10) to start (.8) when
  807F < cal 0x70BF2 without rewriting FD9A (it stays 3). In that case the part-load branch finds
  FD2C.10/.2/.4 clear and goes to 0x2C8F0, so no family map is read (see below).
  So: **FD9A&7 = 2 <=> FD2C.9 (idle)** and **FD2C.10 => FD9A&7 = 3 (part load)**.
- [obs] Part-load branch guard, 0x2C608-0x2C624:
  ```
  if !(FD2C.10 || FD2C.2 || (FD2C.4 && !FD30.3))  -> 0x2C8F0  (curves 0x74830/0x7483C/0x74848, not this family)
  else if (FD2C.4 && !FD2C.12)                     -> 0x2C69A  (pl maps NOT read; 81D3 keeps its last value)
  else                                             -> 0x2C628  (pl maps read and blended)
  ```
  FD2C.0/.1/.2/.4 and FD30.3 are cleared whenever idle or part load is entered (0x2A934-0x2A94C,
  0x2A9AA-0x2A9C2, 0x2AA66-0x2AA7E, 0x2AB52-0x2AB6A) and set only in overrun (0x2A588 .1, 0x2A594
  .4, 0x2A64A .0, 0x2A682 .2, 0x2A7B2 `BMOV FD2C.4,FD2C.1` on entering .12). [obs, but not every
  path into state 3 was traced] => in part load (FD2C.10) the pl maps are always read [inf, high].
  In overrun (.11 / .12) the result depends on .2/.4/FD30.3.
- The switch between idle and part load is **hard** (a different branch, no blend between _is and
  _pl). [obs] The only carry-over is an additive ramp: 8846 is ramped (0x2C5B4-0x2C5F4) and
  (8847 - 0x80) is added on the idle side (0x2C5F8). This is an offset, not a map weight.

## 2. Blend arithmetic

**Octane blend, helper 0D:1516** (file 0x51516) [obs]: R4 = R13*R12/R14, with R13 signed and the
rest unsigned. Rounding is half away from zero: +1 on the magnitude when the remainder is
>= 128 for R14 = 255 (`SUB R14,MDH; CMP R14,MDH; JMPR cc_UGT`).

Part load, 0x2C648-0x2C678 [obs]:
```
R9  = map(0x77315 ron98_pl)            ; 0x2C650
R13 = map(0x771D5 ron91_pl) - R9       ; 0x2C65E..0x2C668, signed 16-bit
81C8 = R9 + rnd(R13 * E305 / 255)      ; 0x2C66A MOVBZ R12,E305 ; R14=#FF ; CALLS 0D:1516 ; ADD R4,R9
```
=> **E305 = 0 -> 100 % RON98; E305 = 255 -> 100 % RON91.** Weight on RON91 = E305/255.
LIVE_OVERLAY.md writes this as `ron91 + (ron98-ron91)*factor`. That is the same thing only if
factor = 1 - E305/255. The code starts from ron98.

**Flex-fuel blend, helper 0D:1850** (file 0x51850) [obs]: blend(a=R12, b=R13, f=R14) =
b + trunc_toward_zero((a-b)*f / 32), clamped to 0..255 (`MUL`, `SHR #5`, then `ORB RH4`->FFh or
`JMPR cc_N`->0). f is signed (`MOVBS R14,8484h`). Neither f = 32 nor f < 0 is clamped, so a
calibration could extrapolate.

Part load, 0x2C67C-0x2C696 [obs]: `81D3 = blend(a = e85_pl 0x77494, b = 81C8, f = 8484)`.
Idle, 0x2C39C-0x2C3C4 [obs]: `81D3 = blend(a = e85_is 0x77470, b = ron98_is 0x775D4, f = 8484)`.
There is no ron91 idle map, and E305 is not used at idle.

Effective weights (f = (s8)8484, w = E305/255):
- **pl:** RON91 = w*(1-f/32), RON98 = (1-w)*(1-f/32), E85 = f/32.
- **idle:** RON98_is = 1-f/32, E85_is = f/32.

The result is integer: about +/-0.5 raw = +/-0.19 deg.

## 3. The drivers

**E305, octane factor** [obs]. It is the high byte of the 16-bit adaptation word E304.
- The word is updated with saturating add/sub helpers (0x35C50-0x35C7C, 0x36976-0x369BC).
- It is forced to FFFFh with FD3A.13 set when E305 > 8431 (0x369C0-0x369CE).
- It is zeroed by the adaptation reset (0x25934) and saved/restored with the adaptation block
  (0x24F5C / 0x2552A).

The ignition blend reads **only the byte E305** (0x2C66A, 0x1D7E2). The logger sends exactly that
byte (0x4488A `MOVB RL7,E305h` / `MOVB F790h,RL7` -> payload 0x40), so **no precision is lost**.
The low byte E304 only matters to other consumers (e.g. 0x43AA2 uses the word).

RONADFAC (ADX: offset 0x40, 0.0039215686*X) = E305/255 = **weight on RON91**: 0 = full RON98,
1.0 = full RON91. The "%" label is misleading: the value runs 0..1, so the app should multiply
by 100.

**8484, ignition flex factor** [obs, routine 0C:41EC, file 0x441EC]:
```
if (cal 0x70DF8 c_conf_ff == 0 || FDFA.2 sensor fault):     ; 0x441EC JMPR cc_Z / 0x44240
    8487 = 0 ; 8484 = cal 0x70A11 - 0x80                    ; 0x442D2..0x442EA (also 8483/8485/8486 from 0x70A10/12/13)
else:
    8482 = (E546 & 0x3FF) >> 2                              ; ethanol sensor ADC
    8487 = filter(curve 0x7BEE4 [axis 0x75250, input 8482], rate cal 0x70A14)   ; 0D:1604
    8484 = curve 0x7D664 ip_ff_fac_iga__ff [axis 0x777AA, input 8487] - 0x80    ; 0x44294..0x442A4
```
8484 is not forced to 0. It takes a calibration substitute.

Car values [obs, bytes]:
- 0x70DF8 = 00 (flex fuel off).
- 0x70A10..13 = 80 80 80 80, so 8484 = 0.
- Curve 0x7D664 is flat 0x80 (and so are 0x7D654/74/84), so 8484 would also be 0 with the
  sensor enabled.
- **On this calibration 8484 = 0 always, so E85 weight = 0 % and the gasoline result passes
  through unchanged** (blend with f = 0 returns b exactly).

8484 is **not logged**. FFBLEND (payload 0x41) is 8487 (0x44892), which is 0 while flex fuel is
off. In general, 8484 can be derived from 8487 only through the calibration curve 0x7D664 (plus
the substitute 0x70A11 when the sensor is off or faulty). The library interpolation rounding
(0D:0060/0262) was not re-verified here.

**State bits in the frame** [obs, 0x4468E-0x446D2]. Payload byte 0 = low byte of F750 =
FD2D (FD2C bits 8..15) with overrides:

| bit | source |
|---|---|
| b0 | FD2C.8 (start) |
| b1 | FD2C.9 OR FD42.13 |
| b2 | FD2C.10 OR FD42.13 |
| b3 | FD2C.11 (overrun) |
| b4 | FD2C.12 (overrun cut) |
| b5..7 | FD2E.3/.4/.6 |

The ADX matches: ISSTATE = (b&6)==2, PLSTATE = 4, FLSTATE = 6, PUSTATE = b3, PUCSTATE = b4.

FD42.13 is set when the flex-blended AFR target (0x7D69C/0x7D75C, 0x3D40A-0x3D426) is not neutral
0x80 and 807F >= cal 0x70C12 = 15 (0x3D464-0x3D4C8). [inf] It is the full-load enrichment flag.

**Not logged:**
- FD9A&7 (b2 of payload 1 is FD9A.4, b3 is FD9A.5|FD9E.3|.4; 0x446AE/0x446B2)
- FD2C.0-.4
- FD30.3
- FD48.0
- FD18.15
- 8484

## 4. App rule (from the logged frame)

Let s = payload[0]:
1. b0 set -> start: **no family map read** (start curve, or 0x2C8F0 in the 0x2A44E fallback).
2. b3 or b4 set -> overrun: pl maps are read, skipped (stale 81D3) or replaced by 0x2C8F0
   depending on unlogged FD2C.2/.4/FD30.3. **Undetermined**: show "overrun - unknown".
3. (s & 6) == 2 -> idle: ron98_is 100*(1-f/32) %, e85_is 100*f/32 %; car: **100 % / 0 %**. pl, aj
   not read by this routine.
4. (s & 6) == 4 -> part load: RON91 = RONADFAC*(1-f/32), RON98 = (1-RONADFAC)*(1-f/32),
   E85 = f/32; car: **RON98 (1-E305/255), RON91 E305/255, E85 0 % (read)**. Idle maps not read.
5. (s & 6) == 6 -> FD42.13 set: idle and part load **cannot be told apart** from the frame (the OR
   destroys FD2C.9/.10). [inf] Usually part load (full-load enrichment at closed throttle is
   unlikely), but not proven: show "idle or part load".
6. s & 0x1F == 0 -> stopped (FD2C.7): no family map read.

Cursor inputs: the pl, is and optm maps use E0E4 (payload 4-5, low 13 bits) and E32C
(payload 0x14). The aj maps use 807F (not logged directly) and E33A (payload 0x12), because cal
0x70B16 = 0 selects E33A over E336 (0x1D798).

## 5. The other maps

**ip_iga_aj_ron91 0x76CDD / aj_ron98 0x76D0D** [obs, 0x1D758-0x1D7F8]. Same octane arithmetic:
`80DB = aj98 + rnd((aj91-aj98)*E305/255) + 0x80`. The result seeds or limits the decaying
intervention 80DC (0x1D800-0x1D840, consumed at 0x2D376/0x2D384 only while FD18.15). The maps are
read only while FD18.15 is set (`JB FD18.15` at 0x1D758, else skip to 0x1D86A). FD18.15 is set
and cleared by event logic at 0x1D4AA-0x1D8C4; the same routine reads
id_t_dly_iga_aj_*__gear (0x1D66A/0x1D69C). [inf] "aj" = anti-jerk, a transient retard.
**FD18.15 is not in the frame** (payload 1 b4 = FD18.1).

**ip_iga_optm_ron98 0x770A5 / optm_e85 0x76FE5** [obs, 0x10258-0x102A2]:
- `8022 = optm98; 8025 = blend(a = optm_e85, b = 8022, f = 8484)`; then 8025 is used with 81C1
  (0D:1BC4) and map 0x7402A -> E02A (torque model).
- The reads sit in routine 09:0000 (file 0x10000). No return or skip branch was found between its
  entry and 0x10290; every branch rejoins at 0x10218.
- 09:0000 is called by `CALLS 09h,0000h` at 0x55400, with no guard immediately before it.
- No ron91 optm map and no E305 term. Weights: optm98 = 1-f/32, optm_e85 = f/32; car 100 % / 0 %.
- [inf] This is the MBT reference for the torque model, not the fired spark.

## 6. Verdicts

| Map | Verdict | Why |
|---|---|---|
| ip_iga_ron98_pl 0x77315 | INCLUDE WITH CAVEAT | Rule [obs]. Drivers logged (state bits, E305). Weight (1-E305/255)*(1-f/32), f = 0 on car. Caveats: "idle or part load" when b1 = b2 = 1; unknown in overrun. |
| ip_iga_ron91_pl 0x771D5 | INCLUDE WITH CAVEAT | Same rule, weight E305/255*(1-f/32). |
| ip_iga_e85_pl 0x77494 | INCLUDE WITH CAVEAT | Read whenever the pl blend runs, weight f/32 = **0 %** on this calibration (0x70DF8 = 0, 0x70A11 = 0x80, curve 0x7D664 flat). 8484 not logged; constant from calibration. Show "read, 0 %". |
| ip_iga_ron98_is 0x775D4 | INCLUDE WITH CAVEAT | Read iff FD9A&7 = 2 <=> FD2C.9; weight 1-f/32 = 100 %. Same b1/b2 ambiguity when FD42.13 is set. |
| ip_iga_e85_is 0x77470 | INCLUDE WITH CAVEAT | Read at idle with weight f/32 = **0 %** on this car. |
| ip_iga_optm_ron98 0x770A5 | INCLUDE WITH CAVEAT | Read on every run of 09:0000 (torque model, not the spark angle); weight 100 % on car. No state dependency found. |
| ip_iga_optm_e85 0x76FE5 | INCLUDE WITH CAVEAT | Read every run, weight **0 %** on car. |
| ip_iga_aj_ron91 0x76CDD | LEAVE OUT | The blend (E305) is known, but whether the map is read depends on FD18.15 (event-driven, not logged). Showing a weight would claim usage we cannot see. |
| ip_iga_aj_ron98 0x76D0D | LEAVE OUT | Same reason. |

Open:
- Every path into state 3 clearing FD2C.4 (the 0x2AC64 and 0x2ADA4 entries come from idle or
  start, whose entries clear it).
- What FD2C.0-.4 mean in overrun.
- Whether idle with FD42.13 set occurs on the car (a log with b1 = b2 = 1 at idle rpm would
  answer it).
- The semantics of 807F (assumed rpm/32 from axis 0x7448A).
