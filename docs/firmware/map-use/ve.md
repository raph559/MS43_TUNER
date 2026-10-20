# VE maps `ip_map_ve_1..8__map__n`: how MS43X001 selects and blends them

Image: `testdata/local/CAR_current_43X001_SP95_STAGE1.bin` (read only). Offsets below are file offsets
(CPU & 0x7FFFF). Tags: [obs] = seen in the disassembly or the bytes; [inf] = inference.

## 1. The rule

```
k = E31D            (high byte of word E31C = integer part of ACTIVEVE)
F = E31C & 0xFF     (low byte = fraction, /256)
i = k - 1 (byte); if i > 7 (unsigned) then i = 7
if i < 7:  VE = floor(ve[i]*(255-F)/256) + floor(ve[i+1]*F/256)   (16-bit add, saturates at 0xFFFF)
if i = 7:  VE = ve[7]                                              (full weight, F ignored)
```
In 1-based map names (`ve_1..ve_8`, ve_n = data at 0x7EE20 + 0x200*(n-1)):
- k = 1..7: `ve_k` has weight (255-F)/256 and `ve_{k+1}` has weight F/256.
- k = 8: only `ve_8`, weight 1.
- Edge cases [obs, from the code]: k = 0 wraps to i = 0xFF, which is clamped to 7, so only `ve_8` is used.
  k > 8 also gives only `ve_8`, and F is ignored.
- F = 0 on the blend path gives ve[i]*255/256. The weights add up to 255/256, not 1, so the blended value is
  about 0.4% low. The only exact full weight is the index-7 path [obs]. This is a firmware quirk (it uses
  `0xFF - F`, not `0x100 - F`).
- Each ve[n] read is a bilinear 16x16 interpolation. Rows (slow index) are on MAP `8FD6` (axis 0x77E60,
  603..15677 raw), columns on rpm `E0E4` (axis 0x77E82, 192..6496) [obs: axis searches at 0x3B9C6/0x3B9D6;
  in 0D:0168, cell = FB6B*FB6D + FB6C, where FB6B = MAP index and FB6D = number of rpm points].

Evidence, function 0B:B8D2 [obs]:
```
03B9DA  MOVBZ R4, E31Dh          ; k
03B9DE  SUBB  RL4, #1h
03B9E0  CMPB  RL4, #7h ; JMPR cc_ULE ; MOVB RL4,#7   ; unsigned clamp, so 0 -> FF -> 7
03B9E6  MOVBZ R6, RL4 ; SHL R6,#1 ; ADD R6,#4CE4h     ; pointer table 0x6CCE4 = 2E20..3C20 (page 1F)
03B9EE  CMPB  RL4, #7h ; JMPR cc_NC, 03BA42h          ; i >= 7 -> single read
03B9F2  MOV R12,[R6] ; MOV R13,#1Fh ; CALLS 0Dh,0168h ; ve[i]
03B9FE  MOVBZ R4, E31Ch          ; F = low byte
03BA02  MOV R13,#00FFh ; SUB R13,R4 ; MOV R4,#80h ; MUL R13,R4   ; (255-F)*128
03BA12  CALLS 0Dh,1900h          ; R4 = (R12*R13)>>15, saturates to FFFF if the product is >= 2^31
03BA16  MOV [-R0],R4
03BA18  MOV R12,[R6+#2] ; ... CALLS 0Dh,0168h          ; ve[i+1]
03BA26  MOVBZ R13,E31Ch ; MOV R4,#80h ; MUL R13,R4 ; CALLS 0Dh,1900h ; ve[i+1]*F/256
03BA38  MOV R12,R4 ; MOV R13,[R0+] ; CALLS 0Dh,1184h  ; ADD, saturates to FFFF on carry
03BA42  MOV R12,[R6] ; MOV R13,#1Fh ; CALLS 0Dh,0168h ; index-7 path: ve[7] only
```
Helpers [obs]: 0D:1900 = `MULU; R4=MDH<<1 | MDL.15` (truncating >>15). 0D:1184 = `ADD; JC -> FFFF`.
The products cannot overflow: 65535*32640 < 2^31. The pointer table 0x6CCE4 holds
`2E20 3020 3220 3420 3620 3820 3A20 3C20`, so page 0x1F gives 0x7EE20 + 0x200*i [obs bytes].
No other code reads the VE maps. The only reference to the 0x4CE4 table and to the VE axes 3E60/3E82 is in
this function [obs, xref].

## 2. The selector E31C: its writer and its range

The only writer is 0x34938 [obs, xref]: `CALLS 0Dh,0114h` axis search on 0x7537E (input `8C08`),
then `CALLS 0Dh,022Ah` u16 curve 0x7B690, then `MOV E31Ch,R4`.
- The car bytes are axis 0x7537E: count 8, values `8 21 28 48 75 102 128 181`. In XDF units (0.375 deg) that
  is 3.0 / 7.9 / 10.5 / 18.0 / 28.1 / 38.3 / 48.0 / 67.9 deg. The curve 0x7B690 is `256 512 ... 2048`
  [obs bytes]. The curve is calibration (`ip_nr_ip_ve__vo`), so in another bin E31C is still exactly what
  selects the maps, but the 1:1 VO-breakpoint meaning may not hold.
- 0D:0114 clamps at both ends (fraction 0 at the edges), and 0D:022A interpolates with round-half-up [obs].
  So on this car, E31C is always in 256..2048. k is 1..8, and k = 8 only when E31C = 2048 (VO at or
  above the last breakpoint) [obs + inf]. The k = 0 case can only happen before the first run of 0x34934
  (RAM start-up value) [inf].
- `8C08` is written at 0x34874 as `(cal 0B0B + cal 0B0A)/2 + 0x93 - 8972 - 8AE0` (saturating subtractions)
  [obs]. Reading it as valve overlap from the VANOS cam positions comes from the XDF names [inf].

## 3. Is the VE result used? (load source 0x70DF3)

- **Car value: 0x70DF3 = 0x01** [obs bytes]. The dispatcher is 0B:B50A, called from 0C:430C [obs]:
  ```
  03B516 JB FD42h.6 -> 03B63A          ; fallback (throttle model)
  03B51A MOVB RL4,0DF3h ; 0 -> 03B528 ; 1 -> 03B568 ; else -> 03B63A
  ```
  **0B:B8D2 (the VE blend) is called on all three branches** (0x3B550, 0x3B632, 0x3B70E) [obs]. The blend
  inside it has no branch that skips it [obs]. So the VE maps are read on every run, in every mode.
- **E33A (the load) is written only by 0B:B8D2** (0x3BAC8; the other writes are only zeroing at
  0x29BAC/0x29E36) [obs, xref]. Fuelling reads E33A: `0x4AB2C MOV R12,E33Ah ; MOV R13,#01DDh (477)`
  [obs]. So the injected fuel always goes through VE x MAP(8FD6).
- Mode 0 (MAP sensor): `8FD6 = E312` (the sensor), and E33A comes straight from VE [obs 0x3B548].
- **Mode 1 (MAF, this car)**: 8FD6 is a manifold-filling model, not a sensor [obs 0x3B568-0x3B62E]:
  `E33C = E324(MAF flow, = E32E) - (E33A*rpm/16384)/2`. Then dp = f(IAT, E33C, time-constant map),
  `8FD6 += dp`, then B8D2 computes `E33A = VE(8FD6,rpm,E31C) x 8FD6 x rpm-term / (IAT+300)`.
  The fixed point of this loop is `E33A*rpm/32768 = E32E`, which is `E33A = E330` (the MAF load, computed at
  0x3B500 as `(E32E>>1)<<16 / rpm`) [inf from the observed formulas].
  **Consequence [inf]:** with the MAF source, a VE error does not change the steady-state fuelling. The model
  pressure absorbs it. The VE maps set (a) the modelled manifold pressure 8FD6, which is also an axis for
  other tables (for example the MAP fuel correction at 0x4AB42), and (b) how E33A tracks the MAF during
  transients.
- Fallback: `FD42.6 = FDB2.0 OR FDC0.4` (0x586FA-0x58706) [obs]. FDB2.0 is copied from FDA0.6 after the
  load-sensor plausibility diagnosis at 0x3BC9A (0A:B27C) [obs]. Reading it as a "MAF/MAP sensor fault" is
  [inf]. On that path B8D2 still runs, and 8FD6 follows the throttle-model load E334 [obs 0x3B63A-0x3B70E].
- Logger visibility: the frame carries 8FD6 (offset 0x08), E31C (0x10) and E33A (0x12) [obs]. The source
  byte 0x70DF3 is calibration, so it can be read from the bin. **FD42.6 (the fallback) is not in the frame**:
  the F750 flag word takes FD42 bit 13 only (decompiled.c lines 49-55) [obs]. A fallback cannot be seen.

## 4. Logger channel ACTIVEVE = E31C: confirmed

Frame builder 0C:467E [obs, disassembly]: `04476A MOV R8,E31Ch ; 04476E MOV F760h,R8`.
Also `F754 <- E0E4` (rpm), `F758 <- 8FD6`, `F762 <- E33A`. In the reply buffer, F74E = 0x4D (length) and
F74F = 0xA0 (status), so payload offset 0 = F750. This matches the ADX layout: N at 0x04, MAP at 0x08,
ACTIVEVE at 0x10, MAFTI at 0x12 [obs]. ACTIVEVE = 0.00390625*X over the 16-bit word E31C = k + F/256.
C167 words are little-endian, so payload byte 0x10 = F (fraction) and byte 0x11 = k.
The ADX's own `LUTVE1..8` channels (offset 0x11, `X&15`, one-hot lookup) look at k only. They mark `ve_k`
as active and ignore the share of `ve_{k+1}`. That is consistent with the rule above but coarser.

For the cursor: the VE row input is MAP channel (0x08) = 8FD6 (with the MAF source this is the model
pressure, not a sensor reading), and the column input is N (0x04, `&8191`) = E0E4. Both are in the frame [obs].

## 5. Verdict: INCLUDE WITH CAVEAT

The rule is fully observed. Weights for the viewed map n (1-based), from ACTIVEVE word W:
`k = W>>8, F = W&0xFF; if 1<=k<=7: w(ve_k) = (255-F)/256, w(ve_{k+1}) = F/256; else w(ve_8) = 1`.
All other maps have weight 0. The logger carries the exact selector (E31C) and both cursor axes.
Caveats to show the user:
1. With load source = MAF (0x70DF3 = 1, this car), the selected VE maps are computed and do feed the fuelling
   load E33A. Their effect on steady-state fuelling is cancelled by the manifold model [inf]. They shape the
   modelled MAP and the transients. "In use" is true, but "changing it changes cruise AFR" is not (with MAF).
2. A sensor-fault fallback (FD42.6) cannot be seen in the frame. The blend is the same in fallback, but the
   meaning of the load changes.
3. The blend weights add up to 255/256 (firmware quirk). Showing (256-F)/256 instead is off by under 0.4%.
