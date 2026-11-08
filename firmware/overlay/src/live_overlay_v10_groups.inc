; V10's settings groups (docs/live-overlay-v10.md): 139 pops stabs, 140 the downshift blip, 141 its gears and tap,
; 142 its gear ratios. Each is 8 bytes, as every group is: feature_write_validate takes one whole group, and
; feature_address maps a byte within an 8-byte span. Their flashed defaults follow pops' at 0C:FF88.

; ---- the limits: lowest and highest value of each byte, 16 bytes per group from 139; a switch above its highest
; reads as 0 ----
v10_limits:
        DB      0, 50           ; 139/0 load time, 0.1 s
        DB      0, 150          ; 139/1 stab size on top of the opening, 0.2 deg TPS
        DB      5, 30           ; 139/2 stab length, 10 ms
        DB      10, 100         ; 139/3 gap, 10 ms
        DB      5, 31           ; 139/4 rise allowed once stabbing, 32 rpm (V10.1; 5 is V8's 160 rpm)
        DB      0, 0            ; 139/5..7 reserved
        DB      0, 0
        DB      0, 0
        DB      0, 2            ; 140/0 switch: 0 off, 1 always on, 2 cruise ready; or 80h..9Fh, a CAN bit
        DB      1, 3            ; 140/1 signs: 1 while braking, 2 on a tap, 3 either
        DB      10, 255         ; 140/2 minimum speed, km/h (847F)
        DB      63, 187         ; 140/3 maximum rpm, 32 X (807F units)
        DB      3, 30           ; 140/4 maximum time, 0.1 s
        DB      0, 150          ; 140/5 blip opening, 0.2 deg TPS
        DB      0, 150          ; 140/6 hold opening, 0.2 deg TPS, at most byte 5
        DB      63, 143         ; 140/7 spark angle, 0.375 X - 23.625 deg BTDC
        DB      0, 1            ; 141/0 5th to 4th
        DB      0, 1            ; 141/1 4th to 3rd
        DB      0, 1            ; 141/2 3rd to 2nd
        DB      0, 1            ; 141/3 2nd to 1st, on a tap only
        DB      11, 85          ; 141/4 tap threshold, E0FF units (0.46875 deg PVS)
        DB      10, 60          ; 141/5 tap length, 10 ms
        DB      0, 30           ; 141/6 blip lead, 10 ms (V10.1; 0 ends the rise on the rpm itself, as V10)
        DB      0, 143          ; 141/7 cap angle above the blip's target, 0.375 X - 23.625 deg BTDC (V10.2; 0 = -23.625)
        DB      180, 255        ; 142/0 rpm per km/h of 1st, 0.5 per step
        DB      100, 160        ; 142/1 of 2nd
        DB      70, 110         ; 142/2 of 3rd
        DB      50, 85          ; 142/3 of 4th
        DB      0, 0            ; 142/4..7 reserved
        DB      0, 0
        DB      0, 0
        DB      0, 0

; R2 = group 139..142, R3 = byte. Return R4 = the live or flashed setting, clamped to its limits; 140's switch keeps
; 80h..9Fh and reads anything else above 2 as 0, as pops_setting does. Preserves R2, R3 and R5..R15; changes PSW.
v10_setting:
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     [-R0], R5
        MOV     [-R0], R6
        CALLA   UC, feature_address     ; R3 pointer, R4 page (R5 scratch)
        EXTP    R4, #1
        MOVB    RL4, [R3]
        MOV     R2, [R0+6]              ; the group and the byte again
        MOV     R3, [R0+4]
        CMP     R2, #140
        JMPR    NZ, vs_clamp
        CMP     R3, #0
        JMPR    NZ, vs_clamp
        MOVB    RH4, RL4
        ANDB    RH4, #0E0h
        CMPB    RH4, #80h
        JMPR    Z, vs_done              ; switch 80h..9Fh: a CAN bit, kept as it is
vs_clamp:
        MOV     R5, R2
        SUB     R5, #139
        SHL     R5, #4
        ADD     R5, R3
        ADD     R5, R3
        ADD     R5, #(v10_limits & 0FFFFh)
        EXTS    #0Ch, #1
        MOV     R6, [R5]
        CMPB    RL4, RL6
        JMPR    UGE, vs_low_ok
        MOVB    RL4, RL6
vs_low_ok:
        CMPB    RL4, RH6
        JMPR    ULE, vs_done
        MOVB    RL4, RH6
        CMP     R2, #140
        JMPR    NZ, vs_done
        CMP     R3, #0
        JMPR    NZ, vs_done
        MOVB    RL4, #0                 ; a switch above its highest reads as off
vs_done:
        MOVBZ   R4, RL4
        MOV     R6, [R0+]
        MOV     R5, [R0+]
        MOV     R3, [R0+]
        MOV     R2, [R0+]
        RET

; R2 = group 139..142, R13 data pointer, R14 page of an 8-byte copy. R4 = 4 when any byte is outside its limits, or
; 140's hold opening (byte 6) is above its blip opening (byte 5), else 0. Preserves R2, R3 and R6..R15; R5 is
; scratch, as for feature_validate.
v10_validate:
        MOV     [-R0], R6
        MOV     [-R0], R7
        MOV     R5, #0
vv_loop:
        MOV     R6, R13
        ADD     R6, R5
        EXTP    R14, #1
        MOVB    RL7, [R6]
        CMP     R2, #140
        JMPR    NZ, vv_range
        CMP     R5, #0
        JMPR    NZ, vv_range
        MOVB    RL4, RL7
        ANDB    RL4, #0E0h
        CMPB    RL4, #80h
        JMPR    Z, vv_next              ; switch 80h..9Fh: a CAN bit
vv_range:
        MOV     R6, R2
        SUB     R6, #139
        SHL     R6, #4
        ADD     R6, R5
        ADD     R6, R5
        ADD     R6, #(v10_limits & 0FFFFh)
        EXTS    #0Ch, #1
        MOV     R4, [R6]                ; RL4 lowest, RH4 highest
        CMPB    RL7, RL4
        JMPR    ULT, vv_bad
        CMPB    RL7, RH4
        JMPR    UGT, vv_bad
vv_next:
        ADD     R5, #1
        CMP     R5, #8
        JMPR    ULT, vv_loop
        CMP     R2, #140
        JMPR    NZ, vv_ok
        MOV     R6, R13
        ADD     R6, #5
        EXTP    R14, #2
        MOVB    RL7, [R6]
        MOVB    RH7, [R6+1]
        CMPB    RH7, RL7
        JMPR    UGT, vv_bad             ; the hold opening is above the blip opening
vv_ok:
        MOV     R4, #0
        JMPR    UC, vv_done
vv_bad:
        MOV     R4, #4
vv_done:
        MOV     R7, [R0+]
        MOV     R6, [R0+]
        RET
