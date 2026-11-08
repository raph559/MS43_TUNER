; R2 = group ID (124..126, 138..142), R3 = byte within its 8-byte span. Return R3 pointer, R4 page.
; R5 is scratch; all callers preserve the original registers and PSW.
feature_address:
        MOV     R5, R3
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    NZ, fa_flash
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    Z, fa_find
        CMP     R2, #138
        JMPR    UGE, fa_bitmap_pops
        ; IDs 124..126 share bitmap byte 15. Avoid a full chain scan when absent.
        EXTP    #3, #1
        MOVBZ   R4, W_BITMAP+15
        MOV     R3, R2
        SUB     R3, #120
        SHR     R4, R3
        JNB     R4.0, fa_flash
        JMPR    UC, fa_find
fa_bitmap_pops:
        EXTP    #3, #1
        MOVBZ   R4, W_BITMAP+17
        MOV     R3, R2
        SUB     R3, #136
        SHR     R4, R3
        JNB     R4.0, fa_flash
fa_find:
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPR    Z, fa_flash
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_ON
        JMPR    NZ, fa_flash
        CALLA   UC, off_due
        CMP     R4, #0
        JMPR    NZ, fa_flash
        EXTP    #3, #1
        MOVB    RL4, [R3+2]
        MOVBZ   R4, RL4
        SHL     R4, #2
        MOV     R3, R4
        ADD     R3, R5
        CMP     R4, #704
        JMPR    ULT, fa_external
        ADD     R3, #(26E0h-704)
        MOV     R4, #3
        RET
fa_external:
        ADD     R3, #1D00h
        MOV     R4, #10h
        RET
fa_flash:
        CMP     R2, #138
        JMPR    ULT, fa_flash_x001
        MOV     R3, R2
        SUB     R3, #138
        SHL     R3, #3
        ADD     R3, #3F80h
        ADD     R3, R5
        MOV     R4, #33h
        RET
fa_flash_x001:
        MOV     R3, R2
        SUB     R3, #124
        SHL     R3, #3
        ADD     R3, #132Ch
        ADD     R3, R5
        MOV     R4, #1Ch
        RET

; R2 ID, R13 data pointer, R14 page. Preserve R2/R3/R6..R15; R4 reason or zero.
; No generic value-change limit: validate modes and protect the adjacent overboost byte.
feature_validate:
        CMP     R2, #138
        JMPA    Z, pops_validate
        JMPA    UGT, v10_validate
        CMP     R2, #126
        JMPR    UGT, fv_ok
        CMP     R2, #124
        JMPR    ULT, fv_ok
        EXTP    R14, #1
        MOVB    RL4, [R13]
        MOVB    RH4, RL4
        ANDB    RH4, #0E0h
        CMPB    RH4, #80h
        JMPR    Z, fv_trigger
        CMPB    RL4, #2
        JMPR    UGT, fv_range
        CMP     R2, #126
        JMPR    NZ, fv_ok
        CMPB    RL4, #1
        JMPR    UGT, fv_range
fv_trigger:
        CMP     R2, #126
        JMPR    NZ, fv_ok
        EXTP    R14, #1
        MOVB    RL4, [R13+7]
        EXTP    #1Ch, #1
        CMPB    RL4, 1343h
        JMPR    NZ, fv_range
fv_ok:
        MOV     R4, #0
        RET
fv_range:
        MOV     R4, #4
        RET

; Scalar inactive writes must supply one complete, valid group.
feature_write_validate:
        CMP     R2, #138
        JMPR    UGE, fwv_group
        CMP     R2, #126
        JMPR    UGT, fv_ok
        CMP     R2, #124
        JMPR    ULT, fv_ok
fwv_group:
        CMP     R6, #0
        JMPR    NZ, fv_range
        CMP     R7, #8
        JMPR    NZ, fv_range
        MOV     R13, #(REQ_ARG+3)
        MOV     R14, #3
        JMPA    UC, feature_validate
