; Six manual gear limits, hysteresis, then flags: requested mode bit 0,
; confirmed copy bit 5, currently applied mode bit 6, enabled bit 7.
hard_init:
        MOV     R12, #1D47h
        MOV     R13, #W_HARD
        MOV     R10, #6
hi_loop:
        EXTP    #1Dh, #1
        MOVB    RL4, [R12]
        EXTP    #10h, #1
        MOVB    [R13], RL4
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, hi_loop
        MOVB    RL4, 0BFFh
        EXTP    #10h, #1
        MOVB    W_HARD+6, RL4
        MOVBZ   R4, 131Eh
        AND     R4, #1
        MOV     R5, R4
        SHL     R5, #6
        OR      R4, R5
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
        RET

; R4 = 1 only when the requested limiter override may supply parameters.
hard_active:
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    NZ, ha_off
        CALLA   UC, off_due
        CMP     R4, #0
        JMPR    NZ, ha_off
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        AND     R4, #80h
        RET
ha_off:
        MOV     R4, #0
        RET

; Preserve the original discrete gear lookup and all LC/NLS branches around it.
hard_gears:
        MOV     [-R0], R4
        CALLA   UC, hard_active
        CMP     R4, #0
        JMPR    Z, hg_flash
        MOV     R12, #(W_HARD & 3FFFh)
        MOV     R13, #10h
hg_flash:
        MOV     R4, [R0+]
        JMPS    0Dh, 042Eh

hard_hysteresis:
        MOV     [-R0], R4
        CALLA   UC, hard_active
        CMP     R4, #0
        JMPR    Z, hh_flash
        EXTP    #10h, #1
        MOVBZ   R13, W_HARD+6
        JMPR    UC, hh_done
hh_flash:
        MOVBZ   R13, 0BFFh
hh_done:
        MOV     R4, [R0+]
        MOV     R13, R13
        RETS

; The original ETC-fault branch precedes this hook and still forces fuel cut.
hard_mode:
        MOV     [-R0], R3
        EXTP    #3, #1
        MOV     R3, W_MAGIC
        CMP     R3, #MAGIC
        JMPR    NZ, hm_flash
        EXTP    #10h, #1
        MOVBZ   R3, W_HARD+7
        SHR     R3, #6
        AND     R3, #1
        MOVB    RL4, RL3
        MOV     R3, [R0+]
        MOVB    RL4, RL4
        RETS
hm_flash:
        MOV     R3, [R0+]
        MOVB    RL4, 131Eh
        RETS

; Called after the stock task has refreshed its effective limiter thresholds.
; A cut mode never changes while either mode's cut state remains latched.
hard_refresh:
        MOV     [-R0], R3
        MOV     [-R0], R4
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    NZ, hr_done
        JB      0FD40h.8, hr_done
        JB      0FD9Ah.15, hr_done
        JB      0FD50h.0, hr_done
        JB      0FDACh.7, hr_done
        JB      0FDACh.8, hr_done
        JB      0FDACh.9, hr_done
        CALLA   UC, hard_active
        CMP     R4, #0
        JMPR    Z, hr_flash
        EXTP    #10h, #1
        MOVBZ   R3, W_HARD+7
        AND     R3, #1
        JMPR    UC, hr_latch
hr_flash:
        MOVBZ   R3, 131Eh
        AND     R3, #1
hr_latch:
        SHL     R3, #6
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        AND     R4, #0BFh
        OR      R4, R3
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
hr_done:
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        MOVB    RL6, 81ACh
        RETS

; Special patch ID 255. Offset must be zero; flags supplied by the client are
; only requested mode and enable. The firmware owns confirmed/current flags.
sub_hard:
        CMP     R4, #0
        JMPA    NZ, bad_range
        EXTP    #10h, #2
        MOV     R4, W_PATCH+10
        MOVBZ   R5, W_PATCH+19
        CMP     R4, #0
        JMPA    NZ, bad_range
        MOV     R4, R5
        AND     R4, #7Eh
        JMPA    NZ, bad_range
        JNB     R5.7, sh_validate
        CALLA   UC, running
        CMP     R4, #0
        JMPA    Z, refuse_stopped
sh_validate:
        EXTP    #10h, #1
        MOVBZ   R5, W_PATCH+18
        MOV     R12, #(W_PATCH+12)
        MOV     R10, #6
sh_limit:
        EXTP    #10h, #1
        MOVB    RL4, [R12]
        MOVBZ   R4, RL4
        CMP     R4, R5
        JMPA    ULE, bad_range
        ADD     R12, #1
        SUB     R10, #1
        JMPR    NZ, sh_limit
        MOV     R12, #(W_PATCH+4)
        CALLA   UC, patch_dword
        MOV     R4, R6
        AND     R4, R7
        CMP     R4, #0FFFFh
        JMPR    NZ, sh_revision
        MOV     R4, #13
        JMPA    UC, refuse_b1
sh_revision:
        CALLA   UC, check_revision
        CMP     R4, #0
        JMPR    Z, sh_new
        ADD     R6, #1
        ADDC    R7, #0
        CALLA   UC, check_revision
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, hard_matches
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        JMPA    UC, ack_same
sh_new:
        CALLA   UC, revision_available
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        AND     R4, #40h
        OR      R4, #20h
        EXTP    #10h, #1
        MOVBZ   R5, W_PATCH+19
        OR      R4, R5
        EXTP    #10h, #1
        MOVB    W_PATCH+19, RL4
        EXTP    #10h, #4
        MOV     R4, W_PATCH+12
        MOV     R5, W_PATCH+14
        MOV     R6, W_PATCH+16
        MOV     R7, W_PATCH+18
hard_commit:
        EXTP    #10h, #4
        MOV     W_HARD, R4
        MOV     W_HARD+2, R5
        MOV     W_HARD+4, R6
        MOV     W_HARD+6, R7
        CALLA   UC, hard_matches
        CMP     R4, #0
        JMPA    Z, ack
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        AND     R4, #41h
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
        CALLA   UC, advance_revision
        MOV     R4, #9
        JMPA    UC, refuse_b1

hard_matches:
        MOV     R12, #(W_PATCH+12)
        MOV     R13, #W_HARD
        MOV     R10, #8
hm_loop:
        EXTP    #10h, #2
        MOVB    RL4, [R12]
        MOVB    RL5, [R13]
        MOVBZ   R4, RL4
        MOVBZ   R5, RL5
        CMP     R10, #1
        JMPR    NZ, hm_compare
        AND     R4, #81h
        AND     R5, #81h
hm_compare:
        CMP     R4, R5
        JMPA    NZ, pc_bad
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, hm_loop
        MOV     R4, #0
        RET
