; V9: triggers for launch control, no-lift shift and rolling anti-lag. See docs/live-overlay-v9.md.
; An activation byte of 80h..9Fh is a trigger in pops' switch encoding: bit 4 the frame (0 the ASC's 153h, CAN object 4
; data at EF47; 1 the A/C's 615h, object 3 at EF37), bit 3 the polarity (0: on while the bit is 1), bits 0..2 the bit of
; the frame's first byte. A/C on is 97h (owner's car check, 2026-10-29). The trigger only makes the feature ready, and
; only gates engagement: once engaged, the feature runs and ends the stock way.

; ---- can_bit_matches: R4 = a value 80h..9Fh; R2 = 1 when its bit matches, else 0. Uses R3; preserves R4 ----
can_bit_matches:
        MOV     R3, #0EF47h
        JNB     R4.4, cb_read
        MOV     R3, #0EF37h
cb_read:
        MOVB    RL2, [R3]
        MOVBZ   R2, RL2
        MOV     R3, R4
        AND     R3, #7
        SHR     R2, R3
        AND     R2, #1
        JNB     R4.3, cb_done
        XOR     R2, #1
cb_done:
        RET

; ---- trigger_effective, at each feature's activation-byte read: RL2 = the stored byte, R3 = the feature (0 LC, 1 NLS,
; 2 RAL); returns the byte the stock code expects in RL2. Below 80h it is unchanged. 80h..9Fh reads as 1 while the
; feature is engaged (its FDF8 bit), else as its bit's state (RF6). A0h and above read as 0, as pops' switch does.
; Uses R3, R4, R5 and PSW, which the calling stub restores ----
trigger_effective:
        JNB     R2.7, te_done
        CMPB    RL2, #0A0h
        JMPR    UGE, te_off
        MOV     R5, #1
        SHL     R5, R3
        AND     R5, 0FDF8h
        JMPR    NZ, te_on
        MOVBZ   R4, RL2
        CALLA   UC, can_bit_matches
        RET
te_on:
        MOVB    RL2, #1
        RET
te_off:
        MOVB    RL2, #0
te_done:
        RET
