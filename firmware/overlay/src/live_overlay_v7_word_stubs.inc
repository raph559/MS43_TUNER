; V7 u16 call-site adapters. The original interpolation and axis code stays in flash.
word_map127:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map127_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map127_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+15
        JNB R4.7, word_map127_flash
word_map127_find:
        CMP R12, #03674h
        JMPR NZ, word_map127_flash
        CMP R13, #01Eh
        JMPR NZ, word_map127_flash
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #127
        CALLA UC, word_address
        MOV R12, R3
        MOV R13, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map127_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map128:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map128_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map128_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.0, word_map128_flash
word_map128_find:
        CMP R12, #022E0h
        JMPR NZ, word_map128_flash
        CMP R13, #01Eh
        JMPR NZ, word_map128_flash
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #128
        CALLA UC, word_address
        MOV R12, R3
        MOV R13, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map128_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map129:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map129_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map129_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.1, word_map129_flash
word_map129_find:
        CMP R12, #0155Eh
        JMPR NZ, word_map129_flash
        CMP R13, #01Eh
        JMPR NZ, word_map129_flash
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #129
        CALLA UC, word_address
        MOV R12, R3
        MOV R13, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map129_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map130:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map130_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map130_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.2, word_map130_flash
word_map130_find:
        CMP R12, #03680h
        JMPR NZ, word_map130_flash
        CMP R13, #01Eh
        JMPR NZ, word_map130_flash
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #130
        CALLA UC, word_address
        MOV R12, R3
        MOV R13, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map130_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0022Ah
word_map131:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map131_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map131_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.3, word_map131_flash
word_map131_find:
        CMP R12, #04854h
        JMPR NZ, word_map131_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #131
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 004B0h
word_map131_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0049Ah
word_map132:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map132_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map132_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.4, word_map132_flash
word_map132_find:
        CMP R12, #04860h
        JMPR NZ, word_map132_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #132
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 004B0h
word_map132_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0049Ah
word_map133:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map133_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map133_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.5, word_map133_flash
word_map133_find:
        CMP R12, #0486Ch
        JMPR NZ, word_map133_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #133
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 004B0h
word_map133_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0049Ah
word_map134:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map134_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map134_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.6, word_map134_flash
word_map134_find:
        CMP R12, #04878h
        JMPR NZ, word_map134_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #134
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 004B0h
word_map134_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0049Ah
word_map135:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map135_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map135_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+16
        JNB R4.7, word_map135_flash
word_map135_find:
        CMP R12, #04884h
        JMPR NZ, word_map135_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #135
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 004B0h
word_map135_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 0049Ah
word_map136:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map136_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map136_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+17
        JNB R4.0, word_map136_flash
word_map136_find:
        CMP R12, #024DCh
        JMPR NZ, word_map136_flash
        CMP R13, #01Fh
        JMPR NZ, word_map136_flash
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #136
        CALLA UC, word_address
        MOV R12, R3
        MOV R13, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 00168h
word_map136_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 00168h
word_map137:
        PUSH 0FF10h
        MOV [-R0], R4
        EXTP #3, #1
        MOV R4, W_MAGIC
        CMP R4, #MAGIC
        JMPR NZ, word_map137_flash
        EXTP #3, #1
        MOVBZ R4, W_DIR+35
        CMP R4, #255
        JMPR Z, word_map137_find
        EXTP #3, #1
        MOVB RL4, W_BITMAP+17
        JNB R4.1, word_map137_flash
word_map137_find:
        CMP R12, #04450h
        JMPR NZ, word_map137_flash
        POP 0FF10h
        MOV R10, [R12]
        MOV R2, [R12+4]
        MOV R3, [R12+8]
        MOV R5, [R12+6]
        MOV R11, [R12+10]
        MOV R12, [R12+2]
        PUSH 0FF10h
        MOV [-R0], R2
        MOV [-R0], R3
        MOV [-R0], R5
        MOV R2, #137
        CALLA UC, word_address
        MOV R10, R3
        MOV R12, R4
        MOV R5, [R0+]
        MOV R3, [R0+]
        MOV R2, [R0+]
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 009BCh
word_map137_flash:
        MOV R4, [R0+]
        POP 0FF10h
        JMPS 0Dh, 009A6h

; Caller checked runtime magic and, when present, the allocation bitmap.
; R2 is a catalogue ID. Return data offset R3 and page R4; preserve R2.
word_address:
        CALLA UC, entry_addr
        CMP R3, #0
        JMPR Z, wa_flash
        EXTP #3, #1
        MOVB RL4, [R3]
        CMPB RL4, #ST_ON
        JMPR NZ, wa_flash
        CALLA UC, off_due
        CMP R4, #0
        JMPR NZ, wa_flash
        EXTP #3, #1
        MOVB RL4, [R3+2]
        MOVBZ R3, RL4
        SHL R3, #2
        CMP R3, #704
        JMPR ULT, wa_external
        ADD R3, #(26E0h-704)
        MOV R4, #3
        RET
wa_external:
        ADD R3, #1D00h
        MOV R4, #10h
        RET
wa_flash:
        MOV R3, R2
        SHL R3, #3
        ADD R3, #(tables & 0FFFFh)
        EXTS #0Ch, #2
        MOV R4, [R3]
        MOV R3, [R3+2]
        RET
