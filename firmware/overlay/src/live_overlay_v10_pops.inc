; V10's state (docs/live-overlay-v10.md): pool A logical 668..679, through DPP2, and FDF8 bits 7..13; FDF8.14 and
; FDF8.15 belong to the car file.
POPS_T          EQU     09F9Ch          ; word: 10 ms ticks since the pops window started, saturating
BLIP_ADD        EQU     09F9Eh          ; word: the blip's throttle addition now, 86A4 units
BLIP_LEFT       EQU     09FA0h          ; word: 10 ms ticks left in the blip
POPS_PH         EQU     09FA2h          ; byte: 10 ms ticks left in the current stab or gap
BLIP_SPK        EQU     09FA2h          ; byte: while a blip runs, the spark angle it wrote last, E2B5's encoding, FFh
                                        ; until its first (V10.2); shares POPS_PH, as no blip runs in a window and every
                                        ; window's start clears it
BLIP_CL        EQU     09FA3h          ; byte: 10 ms ticks since the clutch went in, saturating; 0 while it is released
BLIP_TGT        EQU     09FA4h          ; byte: the blip's target, 807F units (32 rpm)
BLIP_TAP        EQU     09FA5h          ; byte: 10 ms ticks since the pedal rose past the tap threshold
BLIP_PREV       EQU     09FA6h          ; byte: 807F one tick ago, while a blip rises (V10.1)
BLIP_RATE       EQU     09FA7h          ; byte: the rise per 10 ms tick x 16, filtered, while a blip rises (V10.1)
POPS_XSTOCK     EQU     09FA7h          ; byte: in a pops window, the car file's own target byte (V10.2); shares BLIP_RATE,
                                        ; as no blip runs in a window and every blip's start resets the rate
POPS_STABS      EQU     09FBFh          ; byte: the stabs begun in this pops window, up to 255, for the log (V10.1)
POPS_STAB       BIT     0FDF8h.7        ; a stab is on
BLIP_ON         BIT     0FDF8h.8        ; a blip is running
BLIP_USED       BIT     0FDF8h.9        ; this clutch press has had its blip, or the pedal is the driver's for it
BLIP_ARMED      BIT     0FDF8h.10       ; the pedal has been below the tap threshold since the clutch went in
BLIP_TAPPING    BIT     0FDF8h.11       ; the pedal rose past the threshold after the clutch went in
BLIP_HOLD       BIT     0FDF8h.12       ; the blip has reached its target band (the hold phase)
GUARD           BIT     0FDF8h.13       ; the torque guard is acting

; Pops on lift-off (docs/live-overlay-v8.md): settings group 138, its limits and the routines that use it.
; State: FDF8.3 a window is running, FDF8.4 this lift has had its window; the last four bytes of pool A hold
; the 10 ms ticks left and the lowest RPM byte (807F) seen in the window.

POPS_LEFT       EQU     09FBCh          ; word: 10 ms ticks left in the window
POPS_MIN        EQU     09FBEh          ; byte: the lowest 807F in the window (09FBFh: POPS_STABS, V10.1)

; ---- the limits of group 138: lowest and highest value of each byte; a switch above its highest reads as 0 ----
pops_limits:
        DB      0, 2            ; 0 switch: 0 off, 1 always on, 2 cruise control ready; or 80h..9Fh, a CAN bit (pops_conditions)
        DB      1, 50           ; 1 duration, 0.1 s
        DB      0, 255          ; 2 ignition angle, 0.375 X - 23.625 deg BTDC
        DB      50, 147         ; 3 AFR target, the real AFR x 10 in petrol AFR (V10.2): 50 = 5.0, 147 = 14.7
        DB      50, 219         ; 4 minimum RPM, 32 X
        DB      144, 255        ; 5 minimum coolant, 0.75 X - 48 deg C
        DB      0, 200          ; 6 throttle opening, 0.1 deg TPS (V9: up to 20.0)
        DB      0, 255          ; 7 minimum speed, km/h

; R3 = byte of group 138. Return R4 = the live or flashed setting, clamped to its limits.
; Preserves R2, R3 and R5..R15; changes PSW.
pops_setting:
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     [-R0], R5
        MOV     R2, #138
        CALLA   UC, feature_address
        EXTP    R4, #1
        MOVB    RL2, [R3]
        MOV     R3, [R0+2]
        CMP     R3, #0
        JMPR    NZ, ps_clamp
        MOVB    RL4, RL2
        ANDB    RL4, #0E0h
        CMPB    RL4, #80h
        JMPR    Z, ps_done              ; switch 80h..9Fh: a CAN bit, kept as it is
ps_clamp:
        SHL     R3, #1
        ADD     R3, #(pops_limits & 0FFFFh)
        EXTS    #0Ch, #1
        MOV     R4, [R3]
        CMPB    RL2, RL4
        JMPR    UGE, ps_low_ok
        MOVB    RL2, RL4
ps_low_ok:
        CMPB    RL2, RH4
        JMPR    ULE, ps_done
        MOVB    RL2, RH4
        MOV     R3, [R0+2]
        CMP     R3, #0
        JMPR    NZ, ps_done
        MOVB    RL2, #0
ps_done:
        MOVBZ   R4, RL2
        MOV     R5, [R0+]
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        RET

; R13 data pointer, R14 page of an 8-byte copy of group 138. R4 = 4 when any byte is outside its limits, else 0.
; Preserves R2, R3 and R6..R15; R5 is scratch, as for feature_validate.
pops_validate:
        MOV     [-R0], R6
        MOV     [-R0], R7
        MOV     R5, #0
pv_loop:
        MOV     R6, R13
        ADD     R6, R5
        EXTP    R14, #1
        MOVB    RL7, [R6]
        CMP     R5, #0
        JMPR    NZ, pv_range
        MOVB    RL4, RL7
        ANDB    RL4, #0E0h
        CMPB    RL4, #80h
        JMPR    Z, pv_next              ; switch 80h..9Fh: a CAN bit
pv_range:
        MOV     R6, R5
        SHL     R6, #1
        ADD     R6, #(pops_limits & 0FFFFh)
        EXTS    #0Ch, #1
        MOV     R4, [R6]
        CMPB    RL7, RL4
        JMPR    ULT, pv_bad
        CMPB    RL7, RH4
        JMPR    UGT, pv_bad
pv_next:
        ADD     R5, #1
        CMP     R5, #8
        JMPR    ULT, pv_loop
        MOV     R4, #0
        JMPR    UC, pv_done
pv_bad:
        MOV     R4, #4
pv_done:
        MOV     R7, [R0+]
        MOV     R6, [R0+]
        RET

; R5 = 1 when every condition for a window holds, else 0. Changes R2..R4 and PSW.
pops_conditions:
        MOV     R5, #0
        MOV     R3, #0
        CALLA   UC, pops_setting
        JB      R4.7, pc_can
        CMP     R4, #1
        JMPR    Z, pc_switched_on
        CMP     R4, #2
        JMPR    NZ, pc_done
        JNB     0FD18h.1, pc_done       ; cruise control main switch off
        JB      0FD18h.2, pc_done       ; cruise control holding a speed
        JMPR    UC, pc_switched_on
; Switch 80h..9Fh: bit 4 the message (0 the ASC's 153h, CAN object 4 data at EF47; 1 the A/C's 615h, object 3 at EF37),
; bit 3 the polarity (0: on while the bit is 1), bits 0..2 the bit of the frame's first byte.
pc_can:
        CALLA   UC, can_bit_matches     ; shared with the LC/NLS/RAL triggers
        CMP     R2, #0
        JMPR    Z, pc_done
pc_switched_on:
        MOV     R3, #4
        CALLA   UC, pops_setting
        MOVBZ   R2, 807Fh
        CMP     R2, R4
        JMPR    ULT, pc_done
        MOV     R3, #7
        CALLA   UC, pops_setting
        MOVBZ   R2, 847Fh
        CMP     R2, R4
        JMPR    ULT, pc_done
        MOV     R3, #5
        CALLA   UC, pops_setting
        MOVBZ   R2, 8433h
        CMP     R2, R4
        JMPR    ULT, pc_done
        JB      0FD2Eh.6, pc_done       ; clutch pressed: the clutch belongs to NLS
        JB      0FD2Eh.3, pc_done       ; braking: either brake switch
        JB      0FD2Eh.4, pc_done
        MOV     R2, 0FDF8h
        AND     R2, #7
        JMPR    NZ, pc_done             ; LC, NLS or RAL active
        MOV     R5, #1
pc_done:
        RET

; ---- the overrun-cut gate (0A:A7BC). The stock cut comes when FD2E.13 or FD30.2 is set; a pops window holds it off
; and stays in fuelled overrun. Exits are the stock ones, with every register and PSW as they came. ----
pops_gate:
        JB      0FD2Eh.13, pg_request
        JB      0FD30h.2, pg_request
        JMPS    0Ah, 0B0F2h
pg_request:
        JNB     BLIP_ON, pg_pops
        JMPS    0Ah, 0B0F2h             ; a blip holds the overrun cut off
pg_pops:
        PUSH    0FF10h                  ; PSW
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     [-R0], R4
        MOV     [-R0], R5
        CALLA   UC, pops_conditions
        JB      0FDF8h.3, pg_running
        JB      0FDF8h.4, pg_cut
        CMP     R5, #0
        JMPR    Z, pg_cut
        BSET    0FDF8h.3
        BSET    0FDF8h.4
        MOV     R3, #1
        CALLA   UC, pops_setting
        MOV     R3, R4
        SHL     R4, #3
        ADD     R4, R3
        ADD     R4, R3
        MOV     POPS_LEFT, R4
        CALLA   UC, stab_start
        MOVB    RL4, 807Fh
        MOVB    POPS_MIN, RL4
        JMPR    UC, pg_stay
pg_running:
        CMP     R5, #0
        JMPR    Z, pg_end
        MOV     R4, POPS_LEFT
        CMP     R4, #0
        JMPR    Z, pg_end
        MOVBZ   R4, POPS_MIN
        MOVBZ   R3, 807Fh
        MOV     R5, R4
        CALLA   UC, rise_allowance      ; + 160 rpm, or 139 byte 4 once stabbing
        CMP     R3, R5
        JMPR    UGE, pg_end             ; the engine is pushing, or the car is speeding up
        CMP     R3, R4
        JMPR    UGE, pg_stay
        MOVB    POPS_MIN, RL3
        JMPR    UC, pg_stay
pg_end:
        BCLR    0FDF8h.3
        BCLR    POPS_STAB
pg_cut:
        MOV     R5, [R0+]
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        POP     0FF10h                  ; PSW
        JMPS    0Ah, 0A7C8h
pg_stay:
        MOV     R5, [R0+]
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        POP     0FF10h                  ; PSW
        JMPS    0Ah, 0B0F2h

; ---- every 10 ms, from housekeeping (R3 and R4 are saved there): the lift is over when the engine leaves overrun.
; During a window the tick, not only the gate, ends it: stock paths can keep fuelled overrun without passing the
; gate, so the time limit and every stop are checked here too. While it runs, misfire detection stays suppressed,
; as LC, NLS and RAL do. ----
pops_tick:
        JB      0FD2Ch.11, pt_overrun
        JB      0FD2Ch.12, pt_overrun
        BCLR    0FDF8h.3
        BCLR    POPS_STAB
        JB      0FD2Eh.6, pt_rearm_done ; the clutch in: not a press that re-arms the pops
        BCLR    0FDF8h.4
pt_rearm_done:
        RET
pt_overrun:
        JB      0FD2Eh.3, pt_brake
        JNB     0FD2Eh.4, pt_no_brake
pt_brake:
        BSET    0FDF8h.4                ; braking uses up this lift's pops
        BCLR    0FDF8h.3
        BCLR    POPS_STAB
        RET
pt_no_brake:
        JNB     0FDF8h.3, pt_done
        MOV     R4, POPS_LEFT
        CMP     R4, #0
        JMPR    Z, pt_end
        SUB     R4, #1
        MOV     POPS_LEFT, R4
        JMPR    Z, pt_end               ; the time is up
        MOV     [-R0], R2
        MOV     [-R0], R5
        CALLA   UC, pops_conditions     ; R5 = 1 while every condition holds
        MOV     R2, R5
        MOVBZ   R4, POPS_MIN
        MOVBZ   R3, 807Fh
        MOV     R5, R4
        CALLA   UC, rise_allowance      ; + 160 rpm, or 139 byte 4 once stabbing
        CMP     R3, R5
        JMPR    ULT, pt_no_rise
        MOV     R2, #0                  ; the engine is pushing, or the car is speeding up
pt_no_rise:
        CMP     R3, R4
        JMPR    UGE, pt_kept
        MOVB    POPS_MIN, RL3
pt_kept:
        CMP     R2, #0
        JMPR    Z, pt_stop
        CALLA   UC, stab_tick
        MOV     R5, [R0+]
        MOV     R2, [R0+]
        MOV     R4, #100
        MOV     0E3B0h, R4
        BSET    0FD2Ah.12
pt_done:
        RET
pt_stop:
        MOV     R5, [R0+]
        MOV     R2, [R0+]
pt_end:
        BCLR    0FDF8h.3
        BCLR    POPS_STAB
        RET

; ---- the pedal-released skip (0B:D3B2, JNB FD44.3): with the pedal released the stock code skips the AFR target
; and full-load enrichment path. During a window it runs, so the rich target and lambda-loop-off take effect. ----
pops_target:
        JNB     0FD44h.3, pg_target
        JNB     0FDF8h.3, pg_skip
        JNB     0FD2Ch.11, pg_skip
pg_target:
        JMPS    0Bh, 0D3BAh
pg_skip:
        JMPS    0Bh, 0D476h

; ---- the spark call (0A:D424, CALLS 0Bh, 5C84h): during a window the angle is the setting, written where the
; per-cylinder angles are taken from (E2B5, the encoding of ip_iga_puc). Knock retard is not applied in overrun. ----
pops_spark:
        JB      BLIP_ON, psk_blip
        JNB     0FDF8h.3, psk_stock
        JNB     0FD2Ch.11, psk_stock
        PUSH    0FF10h                  ; PSW
        MOV     [-R0], R3
        MOV     [-R0], R4
        MOV     R3, #2
        CALLA   UC, pops_setting
        MOVB    0E2B5h, RL4
        MOV     R4, #0
        MOVB    0E2B4h, RL4
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        POP     0FF10h                  ; PSW
psk_stock:
        JMPS    0Bh, 05C84h
psk_blip:
        JNB     0FD2Ch.11, psk_stock
        PUSH    0FF10h                  ; PSW
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     [-R0], R4
        MOV     [-R0], R5
        MOV     R2, #140
        MOV     R3, #7                  ; the blip's own spark angle
        CALLA   UC, v10_setting
        MOVB    RL5, 807Fh
        CMPB    RL5, BLIP_TGT
        JMPR    ULE, psk_angle          ; at or below the target
        MOV     R5, R4
        MOV     R2, #141                ; V10.2: above it, the cap angle (141 byte 7)
        CALLA   UC, v10_setting
        CMP     R4, R5
        JMPR    ULE, psk_angle
        MOV     R4, R5                  ; a cap above the blip angle is off (V10.2's review)
psk_angle:
        MOVBZ   R3, 807Fh               ; V10.2: the most advance now, the last angle + 807F / 4
        SHR     R3, #2
        MOVBZ   R5, BLIP_SPK
        ADD     R3, R5
        CMP     R4, R3
        JMPR    ULE, psk_write
        MOV     R4, R3
psk_write:
        MOVB    BLIP_SPK, RL4
        MOVB    0E2B5h, RL4
psk_written:
        MOV     R4, #0
        MOVB    0E2B4h, RL4
        MOV     R5, [R0+]
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        POP     0FF10h                  ; PSW
        JMPS    0Bh, 05C84h

; ---- the AFR branch (0B:D434, JB FDF8.2): RAL first, as stock; during a window the rich target goes through the
; shared feature path, which takes the richer of it and the stock target and switches the lambda loop off. ----
pops_afr:
        JB      0FDF8h.2, pa_ral
        JNB     0FDF8h.3, pa_none
        JNB     0FD2Ch.11, pa_none
        PUSH    0FF10h                  ; PSW
        MOV     [-R0], R3
        MOV     [-R0], R4
        MOV     R3, #3
        CALLA   UC, pops_setting
        MOV     R12, R6                 ; V10.2: at 14.7 the car file's own target, as stock
        CMP     R4, #147
        JMPR    UGE, pa_pass
        MOVB    POPS_XSTOCK, RL6        ; the car file's byte, for pops_fuel's richer-of rule
        MOV     R12, #255               ; forces the shared rich path: the lambda loop off, 91A0 = 2032
pa_pass:
        MOV     R13, R12                ; the same value for both fuels, so the blend returns it
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        POP     0FF10h                  ; PSW
        JMPS    0Bh, 0D456h
pa_ral:
        JMPS    0Bh, 0D44Eh
pa_none:
        JMPS    0Bh, 0D464h

; ---- the throttle store (09:B138, MOV 86A4h, R4): during a window the opening is added to the pedal-map setpoint,
; saturating; the store then sets E, Z and N as the stock one does, with C and V as they came. ----
pops_throttle:
        JB      BLIP_ON, pth_blip
        JNB     0FDF8h.3, pth_store
        JNB     0FD2Ch.11, pth_store
        PUSH    0FF10h                  ; PSW
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     R2, R4
        MOV     R3, #6
        CALLA   UC, pops_setting
        MOV     [-R0], R5
        MOV     R5, R4
        SHL     R5, #5
        MOV     R4, R5               ; 32 X
        SHR     R5, #1
        ADD     R4, R5               ; + 16 X
        SHR     R5, #2
        ADD     R4, R5               ; + 4 X
        SHR     R5, #1
        ADD     R4, R5               ; + 2 X
        SHR     R5, #2
        ADD     R4, R5               ; + X / 2
        SHR     R5, #2
        ADD     R4, R5               ; + X / 8: 54.625 X, 10925 at 20.0 deg TPS
        MOV     R5, [R0+]
        ADD     R4, R2
        JMPR    NC, pth_sum
        MOV     R4, #0FFFFh
pth_sum:
        JNB     POPS_STAB, pth_done
        JB      GUARD, pth_done
        MOV     [-R0], R4               ; the stock setpoint plus the opening
        MOV     R2, #139
        MOV     R3, #1
        CALLA   UC, v10_setting         ; the stab size
        CALLA   UC, opening_units
        MOV     R2, R4
        MOV     R4, [R0+]
        ADD     R4, R2
        JMPR    NC, pth_done
        MOV     R4, #0FFFFh
pth_done:
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        POP     0FF10h                  ; PSW
pth_store:
        MOV     86A4h, R4
        RETS
pth_blip:
        JB      GUARD, pth_store
        PUSH    0FF10h                  ; PSW
        ADD     R4, BLIP_ADD
        JMPR    NC, pth_blip_sum
        MOV     R4, #0FFFFh
pth_blip_sum:
        POP     0FF10h                  ; PSW
        MOV     86A4h, R4
        RETS


; ---- V10.2, the AFR display join (0B:D4D4, MOVBZ R12, 91B8h): both branches of the shared rich path meet here, 91A0
; set (0B:D4BC, L = (X - 128) x 16, with FD9A.10) or cleared. When pops_afr forced the rich path in this pass (a window,
; the pops AFR below 14.7, so 91A0 holds the forced 2032), the enrichment becomes the real AFR's, (147 - A) x 4096 / A,
; or the car file's own made real when that is richer, and 91B9, the logged target, the real mixture: 91B8 x 4096 /
; (4096 + 91A0). Every other pass with a rich target makes it real (every AFR real): 91B9 first, from L, as the stock
; display, so its numbers stay as today, then 91A0 = afr_real(L). MULU and DIVLU as the stock 0D:194C uses them at
; 0B:D4E0 in the same pass; R4, R5, R12, R13 and MD are scratch there, and R5 and R12 are left as the stock code leaves
; them. Without a rich target the displaced instruction runs. ----
pops_fuel:
        JNB     0FD9Ah.10, pf_plain     ; no rich target in this pass
        JNB     0FDF8h.3, pf_every
        JNB     0FD2Ch.11, pf_every
        JB      0FDF8h.2, pf_every
        MOV     R12, 091A0h
        CMP     R12, #2032
        JMPR    NZ, pf_every            ; not forced by pops_afr in this pass
        MOV     [-R0], R3
        MOV     R3, #3
        CALLA   UC, pops_setting        ; A, the real AFR x 10, 50..147
        MOV     R3, [R0+]
        CMP     R4, #147
        JMPR    UGE, pf_every
        MOV     R12, #147
        SUB     R12, R4
        MOV     R13, #4096
        MULU    R12, R13                ; MD = (147 - A) x 4096
        DIVLU   R4                      ; / A: at most 7,946 at 5.0
        MOV     R12, 0FE0Eh             ; MDL
        MOVBZ   R13, POPS_XSTOCK
        CMP     R13, #128
        JMPR    ULE, pf_store           ; the car file's byte at lambda 1 or leaner adds nothing
        SUB     R13, #128
        SHL     R13, #4                 ; (X - 128) x 16, as the shared path stores it
        CALLA   UC, afr_real            ; made real, as every target
        CMP     R13, R12
        JMPR    ULE, pf_store
        MOV     R12, R13                ; the car file's own target is the richer
pf_store:
        MOV     091A0h, R12
        MOVBZ   R13, 091B8h
        MOV     R4, #4096
        MULU    R13, R4                 ; MD = 91B8 x 4096
        ADD     R12, #4096
        DIVLU   R12                     ; / (4096 + 91A0): the real mixture
        MOV     R4, 0FE0Eh              ; MDL
        CMP     R4, #255
        JMPR    ULE, pf_shown
        MOV     R4, #255
pf_shown:
        MOVB    091B9h, RL4
pf_done:
        MOV     R5, #1000h
        MOVBZ   R12, 091B8h
        JMPS    0Bh, 0D4E8h
pf_every:                               ; every AFR real
        MOVBZ   R12, 091B8h
        MOV     R13, #1000h
        SUB     R13, 091A0h
        CALLS   0Dh, 0194Ch             ; the stock display from L (0B:D4D8..D4E0)
        MOVB    091B9h, RL4
        MOV     R13, 091A0h
        CALLA   UC, afr_real
        MOV     091A0h, R13
        JMPR    UC, pf_done
pf_plain:
        MOVBZ   R12, 091B8h
        JMPS    0Bh, 0D4D8h

; V10.2: R13 = L, an enrichment as the shared rich path stores it ((X - 128) x 16, -2048..2032). Return R13 = the real
; AFR's, L x 4096 / (4096 - L), signed and truncated toward zero: the byte the XDF shows as AFR 14.7 x (384 - X) / 256
; then gives that AFR. Changes R4, MD and PSW.
afr_real:
        MOV     R4, R13
        JB      R13.15, ar_lean
        MOV     R13, #4096
        MULU    R4, R13                 ; MD = L x 4096
        SUB     R13, R4                 ; 4096 - L, 2064..4096
        DIVLU   R13
        MOV     R13, 0FE0Eh             ; MDL, at most 4,032
        RET
ar_lean:
        NEG     R4                      ; -L
        MOV     R13, #4096
        MULU    R4, R13                 ; MD = -L x 4096
        ADD     R13, R4                 ; 4096 - L, 4112..6144
        DIVLU   R13
        MOV     R13, 0FE0Eh             ; MDL, at most 1,365
        NEG     R13
        RET
