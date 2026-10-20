# Family: catalyst heating (CH) maps: when the ECU uses them, and what CHSTATE proves

Image: `testdata/local/CAR_current_43X001_SP95_STAGE1.bin` (read only). Addresses below are
file offsets (CPU address & 0x7FFFF). Bits are written as `FDxx.n`. iga bytes use 0x80 = 0 deg (0.375 deg/LSB),
so a value below 0x80 is a retard. Method: a sweep of every even
offset with `c167dis.decode` (MS43LAB tools); every site quoted here was re-read in a linear `dis.py` listing.
Tags: [obs] = read in the code or data; [inf] = inference.

## 0. Helpers the rules rely on
- `0A:BBA2` (0x2BBA2): `out = 0x80 + (in-0x80)*f/256`. A factor scales a retard toward 0 deg, and f=0 means no retard. [obs]
- `0D:1BFE` is a subtract clamped at 0; `0D:1184` is an add clamped at 0xFFFF (both used for ramps). [obs]
- Timer table: words at `E3C4+2i`, decremented by the loop at 0x6072A. When a word reaches 0, the loop calls the handler at `0x6E2DA+4i`.
  E3C6 (i=1) leads to `D828`, E3CE (i=5) to `D81E`, and E3D6 (i=9) to `D976` (handler pointers read at 0x6E2DE/0x6E2EE/0x6E2FE). [obs] Tick is 0.1 s
  (matches the 0.1 s table scaling). [inf]
- Logged state bits in F750 (0C:467E decompile, [obs]): bit1 = FD2C.9|FD42.13 and bit2 = FD2C.10|FD42.13 (ADX offset 0,
  mask 6: 2 = Idle, 4 = Part load, 6 = Full load); bit9 = FD9C.8 (ADX DRISTATE "AT in gear"); bit11 = CHSTATE.
  So FD2C.9 is idle, FD2C.10 is part load, and FD42.13 is full load. [inf, from the ADX labels]
- The DS2 frame does **not** carry any of 849C, 849D, E383, E386-E38F, FD52, FD9C.3, FDAA, E3C6/E3CE/E3D6 or 8216
  (none appear in 0C:467E). [obs] Vehicle speed 847F is carried. [obs]

## 1. The three CHSTATE bits
CHSTATE = FD9A.5 | FD9E.3 | FD9E.4. Confirmed: 0C:467E `((FD9A&0x20)>>5 | (FD9E&8)>>3 | (FD9E&0x10)>>4)<<11`, and
the same OR is at 0x446B2-0x446BA (`BMOV R6.11,FD9A.5; BOR R6.11,FD9E.3; BOR R6.11,FD9E.4`); the ADX gives offset 1, mask 0x08. [obs]

**FD9A.5 = CH phase 1 (cold-start ignition retard, plus the injection and VANOS sub-functions).** [obs/inf]
- Set: 0x5D7C2, in `0D:D776`. It is called unconditionally (0x2A292) from the start routine at 0x2A1EA, which sets engine
  state 1 (`BFLDL FD9A,#7,#1`) and latches the start coolant temperature tco_st (8450=8433). D776 sets E3CE (the hold timer) from
  `ip_t_iga_ch__tco_st__km_ctr` 0x7C310 if FD9C.3, else from `id_t_iga_ch_cat_var__tco_st` 0x79382. When E3CE is non-zero:
  BSET FD9A.5 + FD52.13 (hold), and E3C6 is set from `ip_t_ch_ti`/`id_t_ch_ti_cat_var`. When E3C6 is also non-zero, FD52.14 is set. [obs]
- When E3CE expires, `D81E` clears FD52.13 and FD52.14. 0x5DD12: FD42.6 also clears both. [obs]
- Ramp-out: 0x5DBFE. While FD9A.5 is set and FD52.13 is clear, E386 and E388 each lose 0x706B8 (=98) per call. [obs]
- Clear: 0x5DB32 when FD52.13 is clear and (849C==0x80, i.e. fully faded, or FD42.13 full load). Also cleared by `D75E` (0x5D75E,
  from the stop/reset routine at 0x29EBC) and by `FD9A&=0x240` (0x29B86). 849C is set to 0x80 at every clear. [obs]
- FD52.13 and FD52.14 are only ever set right after FD9A.5, and every path that clears FD9A.5 either clears them too or needs FD52.13 already clear.
  So **FD52.14 implies FD52.13, which implies FD9A.5**. [obs; the one exception, 0x29B86, sits in the same stop routine FUN_0a9b6a as the D75E call]
- Car calibration: `ip_t_iga_ch` is 40-150 s for every tco_st >= -12 C (0 only at -42 C). With FD9C.3=1, phase 1 therefore runs after
  almost every start, followed by the fade. `id_t_iga_ch_cat_var` is non-zero only for tco_st between about -10 and 32 C (800/600 s). [obs data; units inf]

**FD9E.3 = CH phase 2 (standstill/low-speed retard on `ip_iga_ch_2`).** [obs/inf]
- Set: 0x5D92A in `0D:D8D4`, called once from 0x2AFB0 (start/after-start code). Requires vs 847F <= 0x70DD4 (=19) and a
  non-zero E3D6 from `ip_t_ch_2__tco_st__km_ctr` 0x7BC22 (FD9C.3) / `id_t_ch_2_cat_var` 0x79362 (else). Also sets FD52.10. [obs]
  Car: ip_t_ch_2 is 20-55 s for tco_st >= -12 C. The cat_var variant is 0.1 s everywhere, so phase 2 is effectively off when FD9C.3=0. [obs data]
- When E3D6 expires, `D976` clears FD52.10. 0x5DD3C: vs > 19 kills the timer as well. Clear of the bit at 0x5DBB4: FD52.10 clear and 849D==0x80.
  Also `D8B0` (stop/reset) and `MOV FD9E,ZEROS` (0x29B94). 849D is set to 0x80 at every clear. [obs]

**FD9E.4 = CH phase 3 (idle/standstill retard on `ip_iga_ch_is__n`, ramped).** [obs/inf]
- Set: 0x5DDCE/0x5DE1A (idle, FD2C.9) or 0x5DEB6/0x5DEEE (part load FD2C.10 with vs 847F == 0). Needs FD9A.5 and FD9E.3
  both clear, FD9C.8 clear, FD52.11|FD54.1 (timers E3D8/E3DA), and tco 8433 < 0x70CF8/0x70CF9, or t_ast E29E < `ip_t_ch_dew_max`. [obs]
- Ramp-in (FD52.15): E382 drops toward the target, then holds. Ramp-out (FD54.0, started by e.g. vs != 0 or leaving idle/part load) brings E382 up
  to 0x8000, then 0x5DD08 BCLR FD9E.4. E383 (=E382 high byte) is the phase-3 iga, and 0x8000 means 0 deg. [obs]

**What CHSTATE proves:** CHSTATE=0 means FD9A.5, FD9E.3 and FD9E.4 are all clear. Then 849C=0x80, 849D=0x80 and E383 is neutral (every clear
path writes these), FD52.13 and FD52.14 are clear, and **none of the six maps is read or applied.** [obs]
CHSTATE=1 means at least one phase is active, including its fade-out tail. It cannot tell which phase, whether the hold timer is still
running, or which variant (FD9C.3) is selected. [obs]

## 2. Per map

### ip_iga_ch__n__maf 0x76D41 and ip_iga_ch_cat_var__n__maf 0x76DC1 (phase 1, off idle)
- Read at 0x5DAF4 (main map) or 0x5DB0C (cat_var) in `0D:DA6A`, only if FD9A.5 and not FD2C.9 (idle); **FD9C.3=1 selects the main map,
  FD9C.3=0 selects cat_var**. Exactly one of the two is read. [obs 0x5DA6C, 0x5DA70, 0x5DAD8]
- Effective value 849C = BBA2(BBA2(map, `id_fac_iga_ch_km` 0x75942 by km 86FE), E387). cat_var gets no km factor. E387 =
  `ip_iga_tco_ch_at` 0x775F8 (FD9C.0) or `_mt` 0x77678 (tco, t_ast). It is recomputed only while FD52.13 (0x5D82C) and then fades to 0
  (0x5DC06). [obs]
- Applied (0x2CB42): R6 += 849C-0x80 only if FD9A.5, not full load (FD42.13), and 849C < 0x80. During hold the map is read at full load but
  not applied. [obs]
- Fade: hard switch on FD9A.5, then two unlogged weights (the km factor, and E387, which also carries the post-hold ramp). During the
  ramp-out the map is still read but its weight falls to 0. [obs]
- FD9C.3: written by fault/coding-type code (0x1D9E4, 0x2317C, 0x232B6 clears; bits 0-5 restored from F727 at 0x2568A). Meaning
  not identified, and it is not logged. [obs/inf]
- **Verdict (both): INCLUDE WITH CAVEAT.** Not in use when CHSTATE=0, at idle, or at full load. When CHSTATE=1 at part load, it
  *may* be in use: the logger cannot tell whether phase 1 is the active bit (it could be phase 2, or phase 3 at standstill), and cannot tell which of
  the two maps FD9C.3 selects. The phase-2 window needs FD9C.3=1, so seeing phase 2 hints at the main map, but phase 2 itself is not
  observable. [inf] The app could say "possibly in use (phase 1, main or cat_var variant)" for both maps. Never "in use".

### ip_iga_ch_2__n__pvs_av 0x76D81 (phase 2)
- Read at 0x5DB5C only if FD9E.3 (any load). 849D = BBA2(BBA2(BBA2(map, `id_fac_iga_ch_2_km` 0x7593A), E38F), E38B). [obs]
- E38E (E38F) ramps up by 0x706B6 toward E38C = `ip_fac_iga_ch_2__tco__t_ast` 0x767CE while FD52.10 is set and FD52.7 is clear. Otherwise it ramps down by
  0x706BA. FD52.7 is set when vs > 2 (0x70DD5) or after more than 16 ticks of part load (E384 > 0x708D6). [obs 0x5DC26-0x5DC62, 0x5DD38-0x5DD72]
  E38A (E38B) decays from 0xFFFF to 0x70B6A (=205/256) while FD52.6 (vs > 2 or FD9C.8). [obs 0x5DC66]
- Part-load latch: when FD2C.10 is set, 849D is only updated if the new value is less of a retard and the pedal is not falling (849E vs E0FF). [obs 0x5DB7E]
- Applied: off idle, R6 += 849D-0x80 if FD9E.3 and 849D < 0x80 (0x2CB5E). At idle, R6 += min(849C+849D-0x80, E383, 8849, 883F)-0x80
  (0x2CAD0-0x2CB36), so at idle it only wins if it is the largest retard. [obs]
- **Verdict: INCLUDE WITH CAVEAT.** Not in use when CHSTATE=0. When CHSTATE=1 it may come from FD9A.5 or FD9E.4 alone, so it cannot be
  proven. Weights not logged (E38F/E38B ramps, km factor). On this car the phase is effectively off when FD9C.3=0 (0.1 s timer).

### ip_iga_ch_is__n 0x76E01 (phase 1 at idle, and phase 3)
- Read at 0x5DA9C when FD9A.5 and FD2C.9 (idle). 849C = BBA2(BBA2(map, `id_fac_iga_ch_is_km` 0x7593E), E389), where
  E389 = `ip_iga_tco_ch_is__tco__t_ast` 0x77638, held during the hold and then faded. [obs]
- Read at 0x5DBE6 when FD9E.4 and neither ramp is running, and at 0x5DCB0 during ramp-in (FD52.15). E383 = BBA2(map, 0x70B6B=83/256 = 0.32) is the
  target, and E382 ramps toward it. It is **not read during ramp-out** (FD54.0); E383 just rises back to 0x80. [obs]
- Applied: at idle through the min() above. Off idle (phase 3 at vs=0 part load), R6 += E383-0x80 if FD9E.4 and E383 < 0x80. [obs]
- **Verdict: INCLUDE WITH CAVEAT.** Not in use when CHSTATE=0. Also not in use off idle with vs > 0 (phase 1 off idle uses the
  n/maf maps, and phase 3 ramps out when vs != 0). [obs] When CHSTATE=1 at idle, it is read if the active bit is FD9A.5 or FD9E.4, but not
  if CHSTATE comes from FD9E.3 alone. Its weight (km factor, E389 fade, the 0.32 constant and the ramp) and the idle min() are unlogged.

### ip_ti_ch__n__maf 0x78773 (cat-heating enrichment)
- Read at 0x4AD18 only if FD52.14 (injection timer E3C6 running), not FDAA.5, and not (FDAA.4 and idle); those cases select
  `ip_ti_sawup__n__maf` instead (0x4AC7C). [obs] Result: 91A2 = f(map, (8464 or 8465 at idle) * 8462)*16 + 9196, then added into
  91B0 (0x4AE84). 8464/8465 = `ip_ti_tco_ch(_is)__tco`, 9196 = `ip_ti_fac_ch_at/mt__tco__t_ast`+0x800 (0x43866-0x438D0). [obs]
- No fade: hard on/off on FD52.14. That bit is set only at a phase-1 start where the t_ch_ti timer is non-zero, and it ends when the timer expires (D828), when E3CE
  expires (D81E), or on FD42.6 (0x5DD18). [obs] Car timer: 47.5 s (or 65/55 s cat_var) only for tco_st of about -10 to 32 C. [obs data]
- FD52.14 implies FD9A.5, which implies CHSTATE=1. [obs]
- **Verdict: INCLUDE WITH CAVEAT.** Not in use when CHSTATE=0. CHSTATE=1 does not prove use: the window is only the first ~50-65 s after
  a cold-ish start, while CHSTATE can stay 1 for minutes. FD52.14 and FDAA are not logged. [obs]

### ip_fac_ivvt_ch__n__maf_ivvt 0x768B4 (VANOS during CH)
- Read at 0x33C3E only if FD52.13 (phase-1 hold) and FD9C.8 (logged as DRISTATE). 8216 = 820C*map/256, where 820C =
  `ip_cam_sp_add_cat_pl_ex__tco_st__km_ctr` 0x75FD6 - 0x80, latched at start (0x2AF4A-0x2AF5A). It is added to cam setpoint 8213
  (0x33C56) and FD3A.10 is set. [obs] (8213 is the exhaust-cam setpoint, judging by the "_ex" in the name.) [inf]
- If FD9C.8=0, FD3A.10 is cleared and nothing is added: a hard off. After the hold ends with FD3A.10 set, 8216 is stepped
  toward 0 (0x33C6E-0x33CBC) and **the map is not read** during that fade. [obs]
- FD52.13 implies FD9A.5, which implies CHSTATE=1. Car: 820C is 0 deg in the first and last tco_st rows, -3 to -8 deg in rows 2-5, so the map has no effect when 820C=0. [obs data]
- **Verdict: INCLUDE WITH CAVEAT.** Not in use when CHSTATE=0 or DRISTATE=0. With both at 1 it is possibly in use: the hold (FD52.13) cannot be
  told apart from the phase-1 fade or from phases 2/3. On a manual car FD9C.8 presumably never sets, so the map is never used. [inf]

## 3. Summary for the app
| map | proven "not in use" when | "in use" provable? |
|---|---|---|
| ip_iga_ch__n__maf | CHSTATE=0, or Idle, or Full load | no (phase/FD9C.3/weights unlogged) |
| ip_iga_ch_cat_var__n__maf | same | no (and needs FD9C.3=0) |
| ip_iga_ch_2__n__pvs_av | CHSTATE=0 | no |
| ip_iga_ch_is__n | CHSTATE=0, or (not Idle and vs > 0) | no |
| ip_ti_ch__n__maf | CHSTATE=0 | no (≤ ~65 s window, FD52.14 unlogged) |
| ip_fac_ivvt_ch__n__maf_ivvt | CHSTATE=0 or DRISTATE=0 | no |

Show "not in use" when the left-hand rule holds, and "possibly in use (cat heating active)" otherwise. A definite "in use" would
need RAM the frame lacks: FD52 (bits 13/14/10), FD9C.3, 849C/849D/E383. A DS2 memory read of those addresses would settle it,
if the app ever adds one. [inf]
Open (not verified): the call rate of the ramps (0x5DBFC, called via 0x3F546), and the timer tick; fade durations are therefore not quantified.
