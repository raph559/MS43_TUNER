# Load-state family: VANOS setpoints, full-load AFR target, injection timing

Image: `testdata/local/CAR_current_43X001_SP95_STAGE1.bin` (MS43X001). Addresses are CPU seg:off
(file offset = CPU & 0x7FFFF, e.g. 0B:D3AA = file 0x3D3AA). Cals are read from this car bin.
[obs] = read in the disassembly/data cited; [inf] = interpretation. Scratch scripts were kept outside both repos.

## 1. The load-state variable

**FD2C bits 7..12 form a one-hot engine operating state** [obs]. The state machine sits in the
function at 0A:A3EC (with 0A:9B6A and 0A:A1EA). Each of its transitions clears five of the six bits
and sets one, and writes a code to FB6A:

| FD2C bit | FB6A | set at (examples) | frame byte 0 | meaning |
|---|---|---|---|---|
| .7  | 1    | 0A:9B6C, init 0B:04DA | not carried (FD2C low byte) | stopped/reset [inf] |
| .8  | 2    | 0A:A44E, 0A:AB9C, 0A:A1F0 | bit 0 (STSTATE) | start |
| .9  | 4    | 0A:A508, 0A:A90A, 0A:AAEA, 0A:ADE2 | bit 1 | idle (IS) |
| .10 | 8 [inf] | 0A:A980, 0A:AA0C, 0A:AC6A, 0A:ADAA | bit 2 | part load (PL) |
| .11 | 0x10 | 0A:A6D0, 0A:AACC, 0A:ACF4 | bit 3 (PUSTATE) | overrun |
| .12 | 0x20 | 0A:A7D2 | bit 4 (PUCSTATE) | overrun fuel cut |

- Every BSET/BCLR on FD2C.9/.10 in 08:0000–0D:FFFF is listed in the sweep [obs]. No site sets .9 and .10 together. The other clears are at 0A:9B70/72 and 0A:A1F2/F4; the hits at 0E:C302/0A are data, not code.
- **FD2D bits 1-2 (= FD2C.9/.10) are therefore never 11.** There is no "full-load" value inside FD2C [obs].
- **Transitions** [obs]:
  - into PL: from start, idle, overrun and fuel cut, when FD44.3 is clear or 8023 ∈ {2,5}. See 0A:AD94, 0A:AC54, 0A:A9F6.
  - into idle: from PL or start, when FD44.3 is set and rpm E0E4 < 88E0 (0A:A4E2–A508).
  - idle to overrun: FD44.3 is set and E0E4 ≥ 88E0 (0A:ACE2).
- **FD44.3 is the pedal-at-idle bit** [obs+inf]. It is set or cleared from the filtered pedal E34C
  against cal 0x70946 with hysteresis (0B:CECC–CEEC), and set at init (0B:D350).

**FD42.13 = full-load enrichment active** [obs]. It is written only in FUN 0B:D3AA (task, called
from 0B:F372). Code path:

```
if FD44.3 (pedal idle): skip to clear FD42.13                      ; 0B:D3B2
FD38.14 := (E33B > curve 0x75D7F[rpm])  (curve = 30 at all rpm)     ; 0B:D3BA-D3E0
R6 := blend(ron98 0x7D69C, e85 0x7D75C, f=8483) at (E33A load, E0E4 rpm) ; 0B:D3E2-D42A
if FDF8.0/.1/.2: R6 := max(R6, blend(cal pair 0x71332/33 | 0x7133A/3B | 0x71341/42, 8483)) ; D42C-D462
if R6 == 0x80: clear FD42.13 else set FD42.13                       ; 0B:D464-D476
FD44.2 := (E33B >= cal 0x70C3E = 30)                                 ; 0B:D478-D48E
clear FD42.13 if FD34.10, or rpm/32 (807F) < cal 0x70C12 (=15, 480 rpm),
  or (not yet latched FD9A.10 and 8C5E < cal 0x70712 and 807F < cal 0x70BF4 (=50, 1600 rpm)) ; D490-D4B8
if FD42.13: 91A0 := (R6-0x80)<<4 else 91A0 := 0                      ; D4BC-D4D0
91B9 := f(91B8, 0x1000-91A0)   (91B9 = logged AFR target, frame 0x42) ; D4D4-D4E4
```

So "full load" in the frame means "the AFR-target map, after blending and floors, is not 0x80, and the
gates pass". It is **not** a load threshold. The DIFF's "load above curve 0x75D7F / ≥ 0x70C3E"
describes two other bits, FD38.14 and FD44.2. Neither is in the frame [obs].

**Logger frame byte 0 (0C:468E–4696)** [obs]: `MOVBZ R6,FD2Dh; BOR R6.1,FD42h.13; BOR R6.2,FD42h.13`.
- `(b0 & 6) == 2`: FD2C.9 is set and FD42.13 is clear. This is exactly the idle bit the selectors test.
- `== 4`: FD2C.10 is set and FD42.13 is clear (part load).
- `== 6`: FD42.13 is set. The real FD2C.9/.10 cannot be read in this case. In practice it is PL: FD42.13 needs FD44.3 clear, and that state sends idle, start and overrun to PL on the next state-machine pass [obs logic]. A one-pass overlap with idle is possible [inf].
- `== 0`: neither idle nor PL, and no enrichment. Use bit 0 (start), bit 3 (overrun) or bit 4 (fuel cut). With all clear, it is the FD2C.7 stopped/reset state [inf].
- **The selectors below test FD2C.8/.9 directly, plus FD38.14 for VANOS.** None of them tests FD2C.10 or FD42.13. FLSTATE (6) is meaningful only for the AFR-target map.

## 2. Flex-fuel blend factors (e85 twins)

- 0C:41EC: `MOVB RL4,0DF8h; JMPR cc_Z,0C42D2` [obs]. Cal **0x70DF8 = 0 on the car**, so flex fuel is disabled.
- With flex disabled, or in limp (FDFA.2), the factors are fixed cals [obs, 0C:42D2–4302]:
  - 8487 (ethanol content) := 0;
  - 8483..8486 := cal 0x70A10..0x70A13 − 0x80. These cals are 128,128,128,128, so **every factor is 0**.
- With flex enabled, factor = curve(8487) − 0x80. The curves are 0x7D654 (fuel 8483), 0x7D664 (ignition), 0x7D674 (boost) and 0x7D684 (cam 8486), on axis 0x777AA (0C:4244–42CC) [obs].
- Blend helper 0D:1850: `result = R13 + (R12−R13)·R14/32`, clamped 0..255 [obs]. R13 is the ron98/base map, R12 the e85 twin.
- **So f=0 returns the ron98 value exactly; the twin is read but has zero weight** [obs].
- The factors are "forced to 0" by cal, not by a code literal [obs].
- Frame visibility [obs, decompiled.c]: 8483–8486 are not in the frame. 8487 is at frame 0x41 (FFBLEND); FDFA.2 is F752 bit 9.

## 3. Per-map rules

### VANOS setpoints (selector 0B:39A4–0B:3C28, inside the VANOS controller starting 0B:2C92)

- The selector runs only in VANOS mode 820D == 2 (0B:2EEE). Even then, checks at 0B:372E–374C (842F, FBD5) and 0B:38A4–38B4 (curve 0x74A0A over rpm/32 vs 842F) can drop the mode to 0 or 1 and skip it. Faults (FD16.2, FDB2.x, FDBA.x, FDAC.10 at 0B:2CC2) lead to mode 3 [obs].
- 820D bits 0-1 are carried as frame offset 0x02 bits 0-1: VANOSACTIVE == 2 [obs: decompiled.c `DAT_04020d & 0xff03`; ADX].
- **Hard switch, no ramp between maps** [obs]:
  - `JB FD2C.9` → IS maps (0B:39A4);
  - otherwise `JNB FD38.14` → PL maps (0B:3ACC → 0B:3B2C);
  - otherwise FL curves.
  - Setpoints go to 8215 (intake) and 8213 (exhaust). Adders and rate logic follow (0B:3A24–3AB4 idle adder; 0B:3C2A–3CBC FD52.13 adder) [obs]. The base value is still the selected map.
- IS (FD2C.9): intake = blend(0x7636C, 0x76184, 8486); exhaust = blend(0x76278, 0x76090, 8486). Map reads at 0B:39D4/39E4/3A00/3A10.
- FL (not idle, FD38.14): intake = blend(curve 0x7635C, 0x76174, 8486); exhaust = blend(0x76268, 0x76080, 8486). These are rpm-only curves (0B:3AD8–3B20).
- PL (not idle, !FD38.14): intake 0x76390 / 0x761A8; exhaust 0x7629C / 0x760B4 (0B:3B40–3C26).
  - If FD9C.3 = 0, the PL result is pulled toward a constant [obs, 0B:3B9A–3C26]. Intake becomes `0x70B09(160) − (160 − v)·0x70B53(218)/256`; exhaust uses 0x70B08 (120).
  - So about 85% of the map's deviation still applies.
  - FD9C.3 is set by the lambda-sensor readiness logic at 0A:317C–32B6 [inf: "sensors ready"]. It is not in the frame (the frame carries FD9C.7/.8 only).
- **FD38.14 is not FLSTATE** [obs]:
  - It is `E33B > 30`, i.e. load E33A ≥ 0x1F00. E33A is carried at frame 0x12.
  - It is recomputed only when FD44.3 is clear (0B:D3B2). While the pedal is at idle it keeps its last value. That covers overrun and PL with the pedal released.

Verdicts:
- **ip_cam_sp_ron98_in_is 0x7636C, _ex_is 0x76278: INCLUDE.** Active iff `(b0&6)==2` and VANOSACTIVE (`frame[0x02]&3==2`).
  - Caveat: if b0 shows 6, idle cannot be excluded for one pass [inf]. Treat 6 as "not idle".
- **ip_cam_sp_ron98_in_pl 0x76390, _ex_pl 0x7629C, _in_fl 0x7635C, _ex_fl 0x76268: INCLUDE WITH CAVEAT.** Do NOT use PLSTATE/FLSTATE. The rule is:
  - VANOSACTIVE, and not idle `(b0&6)!=2`;
  - FL if `frame[0x13]` (E33B) > value of curve 0x75D7F at rpm (30 on this car), else PL.
  - Caveat 1: when the pedal is released, FD38.14 is stale. The app cannot see FD44.3, so with b0 bit 3/4 set (overrun) say "last PL/FL choice held".
  - Caveat 2: PL is scaled toward 160/120 when FD9C.3 = 0, which is not observable [inf: only early after start].
- **e85 twins 0x76184, 0x761A8, 0x76174, 0x76090, 0x760B4, 0x76080: LEAVE OUT** (read, zero weight).
  - 8486 = 0x70A13 − 0x80 = 0 while 0x70DF8 = 0 [obs].
  - If a future bin enables flex fuel, the weight is curve 0x7D684(8487) − 0x80, and 8487 is at frame 0x41.

### Full-load AFR target (FUN 0B:D3AA, reads at 0B:D40A / 0B:D41A)

- **The map is read every pass with the pedal off idle.** Its value takes effect only when FD42.13 = 1 [obs]: 91A0 = 0 otherwise, and 91A0 feeds fuel at 0C:AFA2/AFB6.
- **The only conditions are those in §1.** There is no separate load threshold.
  - On this car, map rows 0-4 (load axis 0x757C6 at or below 0x1A72) are all 128, so they never enrich.
  - Rows 5-11 are 131..184 [obs data]. Enrichment therefore starts once interpolation between rows 4 and 5 rises above 128, at E33A > 0x1A72.
- FDF8 floors [obs]:
  - FDF8.0 is launch control (0B:FD1C), enabled on this car: 0x7132C = 1, whereas the DIFF says 0.
  - FDF8.1 is no-lift shift (0B:FD82), enabled: 0x71334 = 1.
  - FDF8.2 is disabled (0x7133C = 0).
  - With f=0 the floors are 130 (FDF8.0/.2) and 115 (FDF8.1).
  - **During launch control the target is max(map, 130).** A cell of 128 then enriches from the cal, not the map. FDF8 is not in the frame [obs].
  - Launch condition: 847F (frame 0x06) == 0, FD2E.6 (frame F750 bit 7), and pedal E0FF (frame 0x2A) ≥ 0x7132E. 
- **ip_af_target_ron98__maf__n 0x7D69C: INCLUDE WITH CAVEAT.** Active iff `(b0&6)==6` (frame 6 ⇔ FD42.13, because .9/.10 are never both set [obs]).
  - Applied value = frame 0x42 (91B9) for cross-checking.
  - Caveat: at standstill with WOT (launch), a cell of 128 or 129 is overridden by the cal floor 130. Say "launch floor" when 847F==0 and the pedal is at or above 0x7132E.
- **ip_af_target_e85__maf__n 0x7D75C: LEAVE OUT** (read, weight 8483 = 0x70A10 − 0x80 = 0 [obs]).

### Injection timing (0C:B0A0–0C:B196)

- **These are not e85 twins.** tco_1 and tco_2 are coolant-temperature twins [obs]. Selection, a hard switch [obs]:

```
if FD7C.2:          SOI := cal 0x70C4D (=30)          ; 0C:B0A0 -> 0C:B198  (fault fallback [inf])
elif FD2C.8 start:  start map (R12=4FD4) via 0D:0EAC   ; 0C:B0A8
elif FD2C.9 idle:   SOI := blend(R12=0x78055 tco_1_is, R13=0x780C9 tco_2_is, f=820A) ; 0C:B0FC-B148
else (PL, overrun, fuel cut, stopped): blend(R12=0x78079 tco_1_pl, R13=0x780ED tco_2_pl, f=820B) ; 0C:B14C-B194
```

- 820A = map 0x76866 − 0x80; 820B = map 0x76A98 − 0x80 (0C:3C44–3C68) [obs]. Both are on axes 0x7433D (8433 = coolant TCO, frame 0x0B) and 0x74346 (8450 = coolant at start tco_st, **not in the frame**).
- On this car, both maps are 160 in the cold region and 128 (f = 0) in the whole last row and last column. That last breakpoint is raw 0xB1, about 84 °C [obs data].
- **So once TCO ≥ 0xB1 (or tco_st ≥ 0xB1), f = 0:** tco_2 maps are used 100% and tco_1 maps have zero weight. Below that, the weight depends on tco_st, which cannot be observed. The orientation of the map does not matter for this endpoint.
- The column axis input 91B4 = (8DAA/2 + 8F18/2) is combined with E47C through 0D:1184 (0C:B07A–B09C) [obs]. 8DAA/8F18 are at frame 0x26/0x28; E47C is not in the frame. The cursor column is therefore approximate. Flag this for whoever handles axes.
- **ip_soi_tco_2_is 0x780C9: INCLUDE WITH CAVEAT.** Active iff `(b0&6)==2` and not start. Full weight when `frame[0x0B]` ≥ 0xB1; otherwise mixed with tco_1 in an unknown ratio. Ignores the FD7C.2 fallback, which is not in the frame [inf: a DTC state].
- **ip_soi_tco_2_pl 0x780ED: INCLUDE WITH CAVEAT.** Active iff not start (b0 bit 0 clear) and `(b0&6)!=2`.
  - **PLSTATE is wrong as the gate here.** The PL map also runs in overrun, fuel cut (no injection there) and full load.
  - Same TCO caveat as the idle map.
- **ip_soi_tco_1_is 0x78055, ip_soi_tco_1_pl 0x78079: INCLUDE WITH CAVEAT, or LEAVE OUT for a warm engine.** Same state rule, but:
  - weight 0 when `frame[0x0B]` ≥ 0xB1 (show "not used, engine warm");
  - below that the weight is unobservable (tco_st). Show "partly used" without a ratio.

## 4. Summary of what the frame can and cannot tell

| map(s) | gate actually tested | frame equivalent | verdict |
|---|---|---|---|
| cam IS ron98 | FD2C.9 (+ 820D==2) | b0&6==2, frame[2]&3==2 | INCLUDE |
| cam PL/FL ron98 | !FD2C.9, FD38.14 (load>curve, stale while pedal idle) | b0&6!=2 + E33B>30; not FL/PLSTATE | INCLUDE WITH CAVEAT |
| cam e85 twins | weight 8486 = 0 | none | LEAVE OUT |
| AFR ron98 | FD42.13 (+FDF8 floor) | b0&6==6; 91B9 at 0x42 | INCLUDE WITH CAVEAT |
| AFR e85 | weight 8483 = 0 | none | LEAVE OUT |
| SOI tco_2 IS / PL | FD2C.9 / !FD2C.9&!FD2C.8 (+FD7C.2) | b0&6==2 / b0 bit0==0 && b0&6!=2 | INCLUDE WITH CAVEAT |
| SOI tco_1 IS / PL | same, weight 820A/820B(TCO, tco_st) | weight 0 iff frame[0x0B]≥0xB1 | CAVEAT / LEAVE OUT warm |

Open:
- [inf] meanings of FD34.10, FD9C.3, FD7C.2, 8023 ∈ {2,5}, 8C5E and 842F.
- The SOI start map (R12=4FD4 via 0D:0EAC) was not resolved; it is not in this family.
- The frame offsets come from decompiled.c and the ADX. They were not checked against a live capture.
