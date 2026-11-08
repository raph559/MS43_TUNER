; V9: the watched map. See docs/live-overlay-v9.md.
; The record is the end of pool A, logical 680..699 (041FA8..041FBB), reached through DPP2 (page 10h).

W_WOFF          EQU     09FA8h          ; word: the watched map's flash offset (R12 form, < 4000h)
W_WPAGE         EQU     09FAAh          ; word: its flash page; 0FFFFh = no watch
W_AOFF          EQU     09FACh          ; word: its live copy's offset
W_APAGE         EQU     09FAEh          ; word: its live copy's page; 0FFFFh = none
W_KIND          EQU     09FB0h          ; byte: 0 none yet, 1 map indices, 2 curve index, 3 map inputs, 4 curve input
W_COUNT         EQU     09FB1h          ; byte: reads of the watched map, wraps at 256
W_ROW           EQU     09FB2h          ; word: FB6B (zero-extended) or R13
W_ROWF          EQU     09FB4h          ; word: E390, or 0 for inputs
W_COL           EQU     09FB6h          ; word: FB6C (zero-extended) or R14; 0 for curves
W_COLF          EQU     09FB8h          ; word: E392, or 0 for inputs and curves
W_COLS          EQU     09FBAh          ; byte: FB6D, or 0 for inputs and curves
W_ROWIX         EQU     09FBBh          ; byte: the watched map's catalogue row, 0FFh = not in the catalogue
W_ON            BIT     0FDF8h.5        ; a watch is set: the hooks compare only while it is (no code uses FDF8.5/.6)
W_CP            BIT     0FDF8h.6        ; the watch has a live copy: the hooks compare W_AOFF/W_APAGE only then

; ---- sub 12: 6D 0C a2 a1 a0 sets the watch to the app address a2a1a0 (page * 4000h + offset); 0 clears it ----
; The reply echoes the address. An address of 80000h or more is refused with reason 4 and changes nothing.
sub_watch:
        MOVBZ   R6, REQ_SUB+1           ; a2
        MOVBZ   R7, REQ_SUB+2           ; a1
        MOVBZ   R8, REQ_SUB+3           ; a0
        CMP     R6, #8
        JMPA    UGE, bad_range
        MOVB    REP_DATA, RL6           ; the echo first: watch_set uses R6 and R7
        MOVB    REP_DATA+1, RL7
        MOV     R4, R8
        MOVB    REP_DATA+2, RL4
        MOV     R4, R6
        OR      R4, R7
        OR      R4, R8
        JMPR    NZ, sw_set
        CALLA   UC, watch_clear
        JMPR    UC, sw_reply
sw_set:
        MOV     R4, R6                  ; page = address >> 14
        SHL     R4, #2
        MOV     R5, R7
        SHR     R5, #6
        OR      R4, R5
        MOV     R5, R7                  ; offset = address & 3FFFh
        AND     R5, #3Fh
        SHL     R5, #8
        OR      R5, R8
        CALLA   UC, watch_set
sw_reply:
        MOVB    RL4, #0A0h
        MOVB    REP_STATUS, RL4
        MOVB    RL4, #7
        MOVB    REP_LEN, RL4
        JMPA    UC, h_exit

; ---- watch_set: R4 = page, R5 = offset. Clears the record and finds the map's catalogue row; uses R2, R3, R6, R7 ----
; The page is written last, so no hook matches a half-written address.
watch_set:
        BCLR    W_ON
        BCLR    W_CP
        MOV     R2, #0FFFFh
        MOV     W_WPAGE, R2
        MOV     W_APAGE, R2
        MOV     W_WOFF, R5
        MOV     R2, #0
        MOVB    W_KIND, RL2
        MOVB    W_COUNT, RL2
        MOV     R3, #(tables & 0FFFFh)
ws_find:
        EXTS    #0Ch, #2
        MOV     R6, [R3]
        MOV     R7, [R3+2]
        CMP     R6, R4
        JMPR    NZ, ws_next
        CMP     R7, R5
        JMPR    Z, ws_found
ws_next:
        ADD     R3, #8
        ADD     R2, #1
        CMP     R2, #N_TABLES
        JMPR    ULT, ws_find
        MOV     R2, #0FFh
ws_found:
        MOVB    W_ROWIX, RL2
        MOV     W_WPAGE, R4
        BSET    W_ON
        CALLA   UC, watch_alias
        RET

; ---- watch_clear: no watch, no live copy, an empty record; uses R4 ----
watch_clear:
        BCLR    W_ON
        BCLR    W_CP
        MOV     R4, #0FFFFh
        MOV     W_WPAGE, R4
        MOV     W_APAGE, R4
        MOV     R4, #0
        MOVB    W_KIND, RL4
        MOVB    W_COUNT, RL4
        RET

; ---- watch_quiet, from housekeeping: the watch ends after 3 s without a 0x6D frame, but not at engine-off, since
; watching changes nothing the engine reads. Before the app's first frame since boot there is no watch. Uses R4 ----
watch_quiet:
        MOV     R4, W_WPAGE
        CMP     R4, #0FFFFh
        JMPR    Z, wq_none
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    NZ, wq_clear
        MOV     R4, F_TICKS
        EXTP    #3, #1
        SUB     R4, W_SEEN
        CMP     R4, #SILENCE
        JMPR    ULT, wq_done
wq_clear:
        CALLA   UC, watch_clear
wq_done:
        RET
wq_none:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off: a watch_set between the test and the clear
        MOV     R4, W_WPAGE
        CMP     R4, #0FFFFh
        JMPR    NZ, wq_kept
        BCLR    W_ON
wq_kept:
        POP     0FF10h
        RET

; ---- the readers (Task 2): each JMPS 0Ch, watch_rdN replaces its entry's MOVBZ Rx, FB6Bh. Rx is free until that
; instruction, which runs last; no incoming flag is read (every flag test inside comes after a MOV or MULU). ----
watch_rd0:                             ; 0D:022A, curve
        JNB     W_ON, wr0_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr0_page
        JB      W_CP, wr0_copy
wr0_stock:
        MOVBZ   R2, 0FB6Bh
        JMPS    0Dh, 022Eh
wr0_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr0_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr0_stock
        JMPR    UC, wr0_hit
wr0_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr0_copy
wr0_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr0_rec:
        MOVBZ   R2, 0FB6Bh
        MOV     W_ROW, R2
        MOV     R2, 0E390h
        MOV     W_ROWF, R2
        MOV     R2, #0
        MOV     W_COL, R2
        MOV     W_COLF, R2
        MOVB    W_COLS, RL2
        MOVB    RL2, #2
        MOVB    W_KIND, RL2
        MOVB    RL2, W_COUNT
        ADDB    RL2, #1
        MOVB    W_COUNT, RL2
wr0_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr0_stock

watch_rd1:                             ; 0D:0262, curve
        JNB     W_ON, wr1_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr1_page
        JB      W_CP, wr1_copy
wr1_stock:
        MOVBZ   R2, 0FB6Bh
        JMPS    0Dh, 0266h
wr1_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr1_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr1_stock
        JMPR    UC, wr1_hit
wr1_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr1_copy
wr1_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr1_rec:
        MOVBZ   R2, 0FB6Bh
        MOV     W_ROW, R2
        MOV     R2, 0E390h
        MOV     W_ROWF, R2
        MOV     R2, #0
        MOV     W_COL, R2
        MOV     W_COLF, R2
        MOVB    W_COLS, RL2
        MOVB    RL2, #2
        MOVB    W_KIND, RL2
        MOVB    RL2, W_COUNT
        ADDB    RL2, #1
        MOVB    W_COUNT, RL2
wr1_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr1_stock

watch_rd2:                             ; 0D:0454, curve
        JNB     W_ON, wr2_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr2_page
        JB      W_CP, wr2_copy
wr2_stock:
        MOVBZ   R2, 0FB6Bh
        JMPS    0Dh, 0458h
wr2_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr2_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr2_stock
        JMPR    UC, wr2_hit
wr2_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr2_copy
wr2_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr2_rec:
        MOVBZ   R2, 0FB6Bh
        MOV     W_ROW, R2
        MOV     R2, 0E390h
        MOV     W_ROWF, R2
        MOV     R2, #0
        MOV     W_COL, R2
        MOV     W_COLF, R2
        MOVB    W_COLS, RL2
        MOVB    RL2, #2
        MOVB    W_KIND, RL2
        MOVB    RL2, W_COUNT
        ADDB    RL2, #1
        MOVB    W_COUNT, RL2
wr2_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr2_stock

watch_rd3:                             ; 0D:042E, curve
        JNB     W_ON, wr3_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr3_page
        JB      W_CP, wr3_copy
wr3_stock:
        MOVBZ   R2, 0FB6Bh
        JMPS    0Dh, 0432h
wr3_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr3_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr3_stock
        JMPR    UC, wr3_hit
wr3_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr3_copy
wr3_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr3_rec:
        MOVBZ   R2, 0FB6Bh
        MOV     W_ROW, R2
        MOV     R2, 0E390h
        MOV     W_ROWF, R2
        MOV     R2, #0
        MOV     W_COL, R2
        MOV     W_COLF, R2
        MOVB    W_COLS, RL2
        MOVB    RL2, #2
        MOVB    W_KIND, RL2
        MOVB    RL2, W_COUNT
        ADDB    RL2, #1
        MOVB    W_COUNT, RL2
wr3_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr3_stock

watch_rd4:                             ; 0D:0168, map
        JNB     W_ON, wr4_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr4_page
        JB      W_CP, wr4_copy
wr4_stock:
        MOVBZ   R3, 0FB6Bh
        JMPS    0Dh, 016Ch
wr4_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr4_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr4_stock
        JMPR    UC, wr4_hit
wr4_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr4_copy
wr4_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr4_rec:
        MOVBZ   R3, 0FB6Bh
        MOV     W_ROW, R3
        MOV     R3, 0E390h
        MOV     W_ROWF, R3
        MOVBZ   R3, 0FB6Ch
        MOV     W_COL, R3
        MOV     R3, 0E392h
        MOV     W_COLF, R3
        MOVB    RL3, 0FB6Dh
        MOVB    W_COLS, RL3
        MOVB    RL3, #1
        MOVB    W_KIND, RL3
        MOVB    RL3, W_COUNT
        ADDB    RL3, #1
        MOVB    W_COUNT, RL3
wr4_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr4_stock

watch_rd5:                             ; 0D:029A, map
        JNB     W_ON, wr5_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr5_page
        JB      W_CP, wr5_copy
wr5_stock:
        MOVBZ   R3, 0FB6Bh
        JMPS    0Dh, 029Eh
wr5_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr5_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr5_stock
        JMPR    UC, wr5_hit
wr5_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr5_copy
wr5_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr5_rec:
        MOVBZ   R3, 0FB6Bh
        MOV     W_ROW, R3
        MOV     R3, 0E390h
        MOV     W_ROWF, R3
        MOVBZ   R3, 0FB6Ch
        MOV     W_COL, R3
        MOV     R3, 0E392h
        MOV     W_COLF, R3
        MOVB    RL3, 0FB6Dh
        MOVB    W_COLS, RL3
        MOVB    RL3, #1
        MOVB    W_KIND, RL3
        MOVB    RL3, W_COUNT
        ADDB    RL3, #1
        MOVB    W_COUNT, RL3
wr5_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr5_stock

watch_rd6:                             ; 0D:03D2, map
        JNB     W_ON, wr6_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr6_page
        JB      W_CP, wr6_copy
wr6_stock:
        MOVBZ   R3, 0FB6Bh
        JMPS    0Dh, 03D6h
wr6_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr6_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr6_stock
        JMPR    UC, wr6_hit
wr6_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr6_copy
wr6_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr6_rec:
        MOVBZ   R3, 0FB6Bh
        MOV     W_ROW, R3
        MOV     R3, 0E390h
        MOV     W_ROWF, R3
        MOVBZ   R3, 0FB6Ch
        MOV     W_COL, R3
        MOV     R3, 0E392h
        MOV     W_COLF, R3
        MOVB    RL3, 0FB6Dh
        MOVB    W_COLS, RL3
        MOVB    RL3, #1
        MOVB    W_KIND, RL3
        MOVB    RL3, W_COUNT
        ADDB    RL3, #1
        MOVB    W_COUNT, RL3
wr6_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr6_stock

watch_rd7:                             ; 0D:043A, map
        JNB     W_ON, wr7_stock         ; no watch
        CMP     R12, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wr7_page
        JB      W_CP, wr7_copy
wr7_stock:
        MOVBZ   R3, 0FB6Bh
        JMPS    0Dh, 043Eh
wr7_copy:
        CMP     R12, W_AOFF
        JMPR    NZ, wr7_stock
        CMP     R13, W_APAGE
        JMPR    NZ, wr7_stock
        JMPR    UC, wr7_hit
wr7_page:
        CMP     R13, W_WPAGE
        JMPR    NZ, wr7_copy
wr7_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wr7_rec:
        MOVBZ   R3, 0FB6Bh
        MOV     W_ROW, R3
        MOV     R3, 0E390h
        MOV     W_ROWF, R3
        MOVBZ   R3, 0FB6Ch
        MOV     W_COL, R3
        MOV     R3, 0E392h
        MOV     W_COLF, R3
        MOVB    RL3, 0FB6Dh
        MOVB    W_COLS, RL3
        MOVB    RL3, #1
        MOVB    W_KIND, RL3
        MOVB    RL3, W_COUNT
        ADDB    RL3, #1
        MOVB    W_COUNT, RL3
wr7_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wr7_stock

; ---- the descriptor lookups (Task 3): JMPS 0Ch, watch_dsN replaces the MOV DPP0 after the descriptor's unpacking. There
; R12 (page) : R10 (offset) is the data address, R13 the row input and R14 the column input. The displaced MOV DPP0 runs
; last; the next instructions set the flags before any test. ----
watch_ds0:                             ; 0D:04B0, map
        JNB     W_ON, wd0_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd0_page
        JB      W_CP, wd0_copy
wd0_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 04B4h
wd0_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd0_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd0_stock
        JMPR    UC, wd0_hit
wd0_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd0_copy
wd0_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd0_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd0_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd0_stock

watch_ds1:                             ; 0D:05C8, map
        JNB     W_ON, wd1_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd1_page
        JB      W_CP, wd1_copy
wd1_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 05CCh
wd1_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd1_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd1_stock
        JMPR    UC, wd1_hit
wd1_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd1_copy
wd1_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd1_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd1_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd1_stock

watch_ds2:                             ; 0D:0776, map
        JNB     W_ON, wd2_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd2_page
        JB      W_CP, wd2_copy
wd2_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 077Ah
wd2_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd2_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd2_stock
        JMPR    UC, wd2_hit
wd2_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd2_copy
wd2_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd2_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd2_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd2_stock

watch_ds3:                             ; 0D:089E, map
        JNB     W_ON, wd3_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd3_page
        JB      W_CP, wd3_copy
wd3_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 08A2h
wd3_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd3_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd3_stock
        JMPR    UC, wd3_hit
wd3_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd3_copy
wd3_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd3_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd3_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd3_stock

watch_ds4:                             ; 0D:09BC, map
        JNB     W_ON, wd4_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd4_page
        JB      W_CP, wd4_copy
wd4_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 09C0h
wd4_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd4_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd4_stock
        JMPR    UC, wd4_hit
wd4_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd4_copy
wd4_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd4_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd4_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd4_stock

watch_ds5:                             ; 0D:0B60, map
        JNB     W_ON, wd5_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd5_page
        JB      W_CP, wd5_copy
wd5_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0B64h
wd5_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd5_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd5_stock
        JMPR    UC, wd5_hit
wd5_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd5_copy
wd5_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd5_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd5_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd5_stock

watch_ds6:                             ; 0D:0C88, map
        JNB     W_ON, wd6_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd6_page
        JB      W_CP, wd6_copy
wd6_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0C8Ch
wd6_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd6_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd6_stock
        JMPR    UC, wd6_hit
wd6_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd6_copy
wd6_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd6_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd6_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd6_stock

watch_ds7:                             ; 0D:0EC2, map
        JNB     W_ON, wd7_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd7_page
        JB      W_CP, wd7_copy
wd7_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0EC6h
wd7_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd7_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd7_stock
        JMPR    UC, wd7_hit
wd7_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd7_copy
wd7_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd7_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd7_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd7_stock

watch_ds8:                             ; 0D:1092, map
        JNB     W_ON, wd8_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd8_page
        JB      W_CP, wd8_copy
wd8_stock:
        MOV     0FE00h, R11             ; MOV DPP0, R11
        JMPS    0Dh, 1096h
wd8_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd8_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd8_stock
        JMPR    UC, wd8_hit
wd8_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd8_copy
wd8_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd8_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R14
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #3
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd8_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd8_stock

watch_ds9:                             ; 0D:06E4, curve
        JNB     W_ON, wd9_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd9_page
        JB      W_CP, wd9_copy
wd9_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 06E8h
wd9_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd9_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd9_stock
        JMPR    UC, wd9_hit
wd9_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd9_copy
wd9_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd9_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd9_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd9_stock

watch_ds10:                             ; 0D:0AD6, curve
        JNB     W_ON, wd10_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd10_page
        JB      W_CP, wd10_copy
wd10_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0ADAh
wd10_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd10_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd10_stock
        JMPR    UC, wd10_hit
wd10_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd10_copy
wd10_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd10_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd10_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd10_stock

watch_ds11:                             ; 0D:0DA4, curve
        JNB     W_ON, wd11_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd11_page
        JB      W_CP, wd11_copy
wd11_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0DA8h
wd11_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd11_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd11_stock
        JMPR    UC, wd11_hit
wd11_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd11_copy
wd11_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd11_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd11_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd11_stock

watch_ds12:                             ; 0D:0E30, curve
        JNB     W_ON, wd12_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd12_page
        JB      W_CP, wd12_copy
wd12_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0E34h
wd12_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd12_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd12_stock
        JMPR    UC, wd12_hit
wd12_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd12_copy
wd12_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd12_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd12_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd12_stock

watch_ds13:                             ; 0D:0FE2, curve
        JNB     W_ON, wd13_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd13_page
        JB      W_CP, wd13_copy
wd13_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 0FE6h
wd13_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd13_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd13_stock
        JMPR    UC, wd13_hit
wd13_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd13_copy
wd13_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd13_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd13_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd13_stock

watch_ds14:                             ; 0D:1018, curve
        JNB     W_ON, wd14_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd14_page
        JB      W_CP, wd14_copy
wd14_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 101Ch
wd14_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd14_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd14_stock
        JMPR    UC, wd14_hit
wd14_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd14_copy
wd14_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd14_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd14_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd14_stock

watch_ds15:                             ; 0D:1052, curve
        JNB     W_ON, wd15_stock         ; no watch
        CMP     R10, W_WOFF           ; another map's read falls through to the stock instruction
        JMPR    Z, wd15_page
        JB      W_CP, wd15_copy
wd15_stock:
        MOV     0FE00h, R5              ; MOV DPP0, R5
        JMPS    0Dh, 1056h
wd15_copy:
        CMP     R10, W_AOFF
        JMPR    NZ, wd15_stock
        CMP     R12, W_APAGE
        JMPR    NZ, wd15_stock
        JMPR    UC, wd15_hit
wd15_page:
        CMP     R12, W_WPAGE
        JMPR    NZ, wd15_copy
wd15_hit:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off for the record, as at 0C:A314
wd15_rec:
        PUSH    R4
        MOV     W_ROW, R13
        MOV     R4, #0
        MOV     W_ROWF, R4
        MOV     W_COL, R4
        MOV     W_COLF, R4
        MOVB    W_COLS, RL4
        MOVB    RL4, #4
        MOVB    W_KIND, RL4
        MOVB    RL4, W_COUNT
        ADDB    RL4, #1
        MOVB    W_COUNT, RL4
        POP     R4
wd15_end:
        POP     0FF10h                  ; PSW
        JMPR    UC, wd15_stock

; ---- watch_alias (Task 4): W_APAGE:W_AOFF is the watched map's live copy, in the readers' form, while its catalogue row
; holds an allocation; else W_APAGE = 0FFFFh. From housekeeping every 10 ms and from watch_set. Uses R3, R4 ----
watch_alias:
        MOV     [-R0], R2
        MOV     [-R0], R5
        MOV     R5, #0FFFFh             ; the page: none
        MOV     R2, W_WPAGE
        CMP     R2, #0FFFFh
        JMPR    Z, wa_store
        EXTP    #3, #1
        MOV     R2, W_MAGIC
        CMP     R2, #MAGIC
        JMPR    NZ, wa_store
        MOVBZ   R2, W_ROWIX
        CMP     R2, #0FFh
        JMPR    Z, wa_store
        CALLA   UC, entry_addr          ; R3 = its directory record, or 0
        CMP     R3, #0
        JMPR    Z, wa_store
        EXTP    #3, #1
        MOVB    RL4, [R3+2]             ; the logical offset / 4
        MOVBZ   R4, RL4
        SHL     R4, #2
        MOV     R5, #10h
        CMP     R4, #704
        JMPR    ULT, wa_pool_a
        SUB     R4, #704
        ADD     R4, #26E0h
        MOV     R5, #3
        JMPR    UC, wa_store
wa_pool_a:
        ADD     R4, #1D00h
wa_store:
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off: no hook sees a page with another map's offset
        MOV     R2, #0FFFFh
        MOV     W_APAGE, R2
        MOV     W_AOFF, R4
        MOV     W_APAGE, R5
        BCLR    W_CP
        CMP     R5, #0FFFFh
        JMPR    Z, wa_none
        BSET    W_CP
wa_none:
        POP     0FF10h
        MOV     R5, [R0+]
        MOV     R2, [R0+]
        RET

; ---- watch_unalias (Task 4): no live copy for the watch, at once. Load, Free, Revert and every switch-off call it before
; they change an allocation (RF1); housekeeping finds the copy again within 10 ms. Preserves every register ----
watch_unalias:
        BCLR    W_CP
        MOV     [-R0], R4
        MOV     R4, #0FFFFh
        MOV     W_APAGE, R4
        MOV     R4, [R0+]
        RET

; ---- sub 11 (Task 5): 6D 0B logs. The reply is the stock 0B B0 payload (F750..F798, unchanged), then the record
; big-endian at F799..F7A3: KIND COUNT ROWh ROWl ROWFh ROWFl COLh COLl COLFh COLFl COLS; 88 bytes with the frame ----
sub_log:
        CALLS   0Ch, 467Eh              ; the stock logging reply; changes R4..R9 and PSW, which h_exit restores
        MOVB    RL4, #58h
        MOVB    REP_LEN, RL4
        MOV     R6, #0F799h
        PUSH    0FF10h                  ; PSW
        BCLR    0FF10h.11               ; IEN off: a reply never carries half of two reads (RF2)
wl_rec:
        MOVB    RL4, W_KIND
        MOVB    [R6], RL4
        MOVB    RL4, W_COUNT
        MOVB    [R6+1], RL4
        MOV     R4, W_ROW
        MOVB    [R6+2], RH4
        MOVB    [R6+3], RL4
        MOV     R4, W_ROWF
        MOVB    [R6+4], RH4
        MOVB    [R6+5], RL4
        MOV     R4, W_COL
        MOVB    [R6+6], RH4
        MOVB    [R6+7], RL4
        MOV     R4, W_COLF
        MOVB    [R6+8], RH4
        MOVB    [R6+9], RL4
        MOVB    RL4, W_COLS
        MOVB    [R6+10], RL4
wl_end:
        POP     0FF10h                  ; PSW
        JMPA    UC, h_exit
