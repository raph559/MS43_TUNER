; MS43X001 live overlay protocol 10 (V10.2). Build it with ../build_overlay.py on your own MS43X001 bin.
; Code only: it writes the overlay's free flash block and its hook sites, never the calibration.
; Copyright (C) 2026 raph559. GPL-3.0, as a modification of MS43X001 (MS4X project, GPL-3.0).
; Control: 00E6B0..00E6DF; idle/intake: 00E6E0..00E7C3; revision: 00E7C4..00E7C7.
; Main/AFR/exhaust: 041D00..041FBF; private request: 041FC0..041FD7; limiter: 041FD8..041FDF.
; The disabled service monitor must remain disabled. No flash programming occurs at runtime.

        CPU     80C167
        ASSUME  DPP0:1Ch, DPP1:3Bh, DPP2:10h, DPP3:3

OVL_VERSION     EQU     10              ; (AS predefines VERSION)
RICHER_ONLY     EQU     0               ; preserves the full original u8 range
N_TABLES        EQU     143               ; the live tables (TABLES would be the label tables to AS)

; ---- control in XRAM, reached through EXTP #3 ----
W_MAGIC         EQU     0E6B0h           ; C0DAh once the handler has initialised the window
W_SEEN          EQU     0E6B2h           ; E2A6 at the last 0x6D frame
W_DIR           EQU     0E6B8h           ; base records: state, ID, offset/4, control/link, CRC32
W_COPY          EQU     0E6E0h           ; control end, followed by idle ignition and intake cam
W_BITMAP        EQU     0E7B0h           ; 20-byte allocation presence index, only with overflow records
COPY_BYTES      EQU     932
MOST_WRITE      EQU     112             ; data bytes in one write: its request is then 128 bytes, inside the firmware's 129
MAGIC           EQU     0C0DAh
SILENCE         EQU     300             ; nominal 3 s of processed firmware ticks
W_SESSION       EQU     0E6B4h
W_REV           EQU     0E7C4h
W_PATCH         EQU     09FC0h
REQ_SESSION     EQU     0F6D1h
REQ_REV         EQU     0F6D5h
BASE_SLOTS      EQU     5
W_HARD          EQU     09FD8h
ST_FREE         EQU     0
ST_LOADED       EQU     1
ST_READY        EQU     2
ST_ON           EQU     3

; ---- the firmware's RAM (R4-R7 of the plan) ----
F_STATE         EQU     0F900h          ; byte: 2 = synchronised
F_MODE          EQU     0FD2Ch          ; bits 9-12: idle, part load, overrun, fuel cut
F_TICKS         EQU     0E2A6h          ; +1 every 10 ms in the main loop
REQ_LEN         EQU     0F6CEh          ; the request: 12h at F6CD, the length, the command, then F6D0 on
REQ_SUB         EQU     0F6D0h
REQ_ARG         EQU     0F6D9h
REP_LEN         EQU     0F74Eh          ; the reply: 12h at F74D, the length, the status, then the payload
REP_STATUS      EQU     0F74Fh
REP_DATA        EQU     0F750h

; ======================================================================================================================
        ORG     0CB544h

header: DB      "LTOV"                  ; the signature the app reads with DS2 command 06 at 0C:B544
        DW      OVL_VERSION + 0200h     ; V10.2: the high byte is the minor
        DW      code_start & 0FFFFh
        DW      code_end & 0FFFFh
        DW      data_end & 0FFFFh
        DW      N_TABLES
        DW      MAGIC

code_start:

; ---- the table stubs: one per table, called by its sites' CALLS with R12/R13 = the table's flash address ----
; A stub substitutes the table's copy only for its own table's address; anything else goes to the reader untouched.
e_map0:
        CMP     R12, #3315h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #0
        JMPA    UC, subst
e_map1:
        CMP     R12, #35D4h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #1
        JMPA    UC, subst
e_map2:
        CMP     R12, #169Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #2
        JMPA    UC, subst
e_map3:
        CMP     R12, #2390h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #3
        JMPA    UC, subst
e_map4:
        CMP     R12, #229Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #4
        JMPA    UC, subst
e_map5:
        CMP     R12, #1FD6h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #5
        JMPA    UC, subst
e_map6:
        CMP     R12, #1FF6h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #6
        JMPA    UC, subst
e_map7:
        CMP     R12, #2016h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #7
        JMPA    UC, subst
e_map8:
        CMP     R12, #2036h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #8
        JMPA    UC, subst
e_map9:
        CMP     R12, #2056h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #9
        JMPA    UC, subst
e_map10:
        CMP     R12, #2090h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #10
        JMPA    UC, subst
e_map11:
        CMP     R12, #20B4h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #11
        JMPA    UC, subst
e_map12:
        CMP     R12, #2184h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #12
        JMPA    UC, subst
e_map13:
        CMP     R12, #21A8h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #13
        JMPA    UC, subst
e_map14:
        CMP     R12, #2278h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #14
        JMPA    UC, subst
e_map15:
        CMP     R12, #236Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #15
        JMPA    UC, subst
e_map16:
        CMP     R12, #27AAh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #16
        JMPA    UC, subst
e_map17:
        CMP     R12, #27CEh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #17
        JMPA    UC, subst
e_map18:
        CMP     R12, #2812h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #18
        JMPA    UC, subst
e_map19:
        CMP     R12, #281Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #19
        JMPA    UC, subst
e_map20:
        CMP     R12, #2842h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #20
        JMPA    UC, subst
e_map21:
        CMP     R12, #2866h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #21
        JMPA    UC, subst
e_map22:
        CMP     R12, #2A5Bh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #22
        JMPA    UC, subst
e_map23:
        CMP     R12, #2A98h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #23
        JMPA    UC, subst
e_map24:
        CMP     R12, #2ADCh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #24
        JMPA    UC, subst
e_map25:
        CMP     R12, #2BB5h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #25
        JMPA    UC, subst
e_map26:
        CMP     R12, #2BC5h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #26
        JMPA    UC, subst
e_map27:
        CMP     R12, #2C27h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #27
        JMPA    UC, subst
e_map28:
        CMP     R12, #2C4Bh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #28
        JMPA    UC, subst
e_map29:
        CMP     R12, #2C5Bh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #29
        JMPA    UC, subst
e_map30:
        CMP     R12, #2CDDh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #30
        JMPA    UC, subst
e_map31:
        CMP     R12, #2D0Dh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #31
        JMPA    UC, subst
e_map32:
        CMP     R12, #2D41h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #32
        JMPA    UC, subst
e_map33:
        CMP     R12, #2DC1h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #33
        JMPA    UC, subst
e_map34:
        CMP     R12, #2E41h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #34
        JMPA    UC, subst
e_map35:
        CMP     R12, #2E81h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #35
        JMPA    UC, subst
e_map36:
        CMP     R12, #2F11h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #36
        JMPA    UC, subst
e_map37:
        CMP     R12, #2F21h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #37
        JMPA    UC, subst
e_map38:
        CMP     R12, #2F61h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #38
        JMPA    UC, subst
e_map39:
        CMP     R12, #2FE5h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #39
        JMPA    UC, subst
e_map40:
        CMP     R12, #30A5h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #40
        JMPA    UC, subst
e_map41:
        CMP     R12, #3195h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #41
        JMPA    UC, subst
e_map42:
        CMP     R12, #31D5h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #42
        JMPA    UC, subst
e_map43:
        CMP     R12, #3470h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #43
        JMPA    UC, subst
e_map44:
        CMP     R12, #3494h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #44
        JMPA    UC, subst
e_map45:
        CMP     R12, #35F8h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #45
        JMPA    UC, subst
e_map46:
        CMP     R12, #3638h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #46
        JMPA    UC, subst
e_map47:
        CMP     R12, #3678h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #47
        JMPA    UC, subst
e_map48:
        CMP     R12, #3858h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #48
        JMPA    UC, subst
e_map49:
        CMP     R12, #3870h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #49
        JMPA    UC, subst
e_map50:
        CMP     R12, #38B0h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #50
        JMPA    UC, subst
e_map51:
        CMP     R12, #38F8h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #51
        JMPA    UC, subst
e_map52:
        CMP     R12, #393Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #52
        JMPA    UC, subst
e_map53:
        CMP     R12, #3A10h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #53
        JMPA    UC, subst
e_map54:
        CMP     R12, #3A4Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #54
        JMPA    UC, subst
e_map55:
        CMP     R12, #3AA0h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #55
        JMPA    UC, subst
e_map56:
        CMP     R12, #3AC0h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #56
        JMPA    UC, subst
e_map57:
        CMP     R12, #3AFAh
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #57
        JMPA    UC, subst
e_map58:
        CMP     R12, #3D27h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #58
        JMPA    UC, subst
e_map59:
        CMP     R12, #3D87h
        JMPA    NZ, to_reader
        CMP     R13, #001Dh
        JMPA    NZ, to_reader
        MOV     R2, #59
        JMPA    UC, subst
e_map60:
        CMP     R12, #0055h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #60
        JMPA    UC, subst
e_map61:
        CMP     R12, #0079h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #61
        JMPA    UC, subst
e_map62:
        CMP     R12, #00C9h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #62
        JMPA    UC, subst
e_map63:
        CMP     R12, #00EDh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #63
        JMPA    UC, subst
e_map64:
        CMP     R12, #0371h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #64
        JMPA    UC, subst
e_map65:
        CMP     R12, #03B1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #65
        JMPA    UC, subst
e_map66:
        CMP     R12, #03F1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #66
        JMPA    UC, subst
e_map67:
        CMP     R12, #0431h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #67
        JMPA    UC, subst
e_map68:
        CMP     R12, #0499h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #68
        JMPA    UC, subst
e_map69:
        CMP     R12, #04C9h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #69
        JMPA    UC, subst
e_map70:
        CMP     R12, #0551h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #70
        JMPA    UC, subst
e_map71:
        CMP     R12, #0581h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #71
        JMPA    UC, subst
e_map72:
        CMP     R12, #05B1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #72
        JMPA    UC, subst
e_map73:
        CMP     R12, #05F1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #73
        JMPA    UC, subst
e_map74:
        CMP     R12, #0639h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #74
        JMPA    UC, subst
e_map75:
        CMP     R12, #0703h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #75
        JMPA    UC, subst
e_map76:
        CMP     R12, #075Bh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #76
        JMPA    UC, subst
e_map77:
        CMP     R12, #07B3h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #77
        JMPA    UC, subst
e_map78:
        CMP     R12, #0803h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #78
        JMPA    UC, subst
e_map79:
        CMP     R12, #0853h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #79
        JMPA    UC, subst
e_map80:
        CMP     R12, #08A3h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #80
        JMPA    UC, subst
e_map81:
        CMP     R12, #08F3h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #81
        JMPA    UC, subst
e_map82:
        CMP     R12, #0943h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #82
        JMPA    UC, subst
e_map83:
        CMP     R12, #0993h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #83
        JMPA    UC, subst
e_map84:
        CMP     R12, #09E3h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #84
        JMPA    UC, subst
e_map85:
        CMP     R12, #0A33h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #85
        JMPA    UC, subst
e_map86:
        CMP     R12, #0A83h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #86
        JMPA    UC, subst
e_map87:
        CMP     R12, #0AD3h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #87
        JMPA    UC, subst
e_map88:
        CMP     R12, #0B03h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #88
        JMPA    UC, subst
e_map89:
        CMP     R12, #0B39h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #89
        JMPA    UC, subst
e_map90:
        CMP     R12, #0BB9h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #90
        JMPA    UC, subst
e_map91:
        CMP     R12, #0BF9h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #91
        JMPA    UC, subst
e_map92:
        CMP     R12, #0C39h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #92
        JMPA    UC, subst
e_map93:
        CMP     R12, #0D9Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #93
        JMPA    UC, subst
e_map94:
        CMP     R12, #0DDEh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #94
        JMPA    UC, subst
e_map95:
        CMP     R12, #0E1Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #95
        JMPA    UC, subst
e_map96:
        CMP     R12, #0E5Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #96
        JMPA    UC, subst
e_map97:
        CMP     R12, #0E9Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #97
        JMPA    UC, subst
e_map98:
        CMP     R12, #0EBEh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #98
        JMPA    UC, subst
e_map99:
        CMP     R12, #0EDEh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #99
        JMPA    UC, subst
e_map100:
        CMP     R12, #0EF6h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #100
        JMPA    UC, subst
e_map101:
        CMP     R12, #0F2Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #101
        JMPA    UC, subst
e_map102:
        CMP     R12, #0F6Eh
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #102
        JMPA    UC, subst
e_map103:
        CMP     R12, #0FC6h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #103
        JMPA    UC, subst
e_map104:
        CMP     R12, #1061h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #104
        JMPA    UC, subst
e_map105:
        CMP     R12, #10A1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #105
        JMPA    UC, subst
e_map106:
        CMP     R12, #10E1h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #106
        JMPA    UC, subst
e_map107:
        CMP     R12, #1121h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #107
        JMPA    UC, subst
e_map108:
        CMP     R12, #11A9h
        JMPA    NZ, to_reader
        CMP     R13, #001Eh
        JMPA    NZ, to_reader
        MOV     R2, #108
        JMPA    UC, subst
e_map109:
        CMP     R12, #175Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #109
        JMPA    UC, subst
e_map110:
        CMP     R12, #181Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #110
        JMPA    UC, subst
e_map111:
        CMP     R12, #183Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #111
        JMPA    UC, subst
e_map112:
        CMP     R12, #186Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #112
        JMPA    UC, subst
e_map113:
        CMP     R12, #18ECh
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #113
        JMPA    UC, subst
e_map114:
        CMP     R12, #192Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #114
        JMPA    UC, subst
e_map115:
        CMP     R12, #1944h
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #115
        JMPA    UC, subst
e_map116:
        CMP     R12, #195Ch
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #116
        JMPA    UC, subst
e_map117:
        CMP     R12, #2C72h
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #117
        JMPA    UC, subst
e_map118:
        CMP     R12, #2CB4h
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #118
        JMPA    UC, subst
e_map119:
        CMP     R12, #2CF4h
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #119
        JMPA    UC, subst
e_map120:
        CMP     R12, #2D3Ah
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #120
        JMPA    UC, subst
e_map121:
        CMP     R12, #2D7Ah
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #121
        JMPA    UC, subst
e_map122:
        CMP     R12, #2DBAh
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #122
        JMPA    UC, subst
e_map123:
        CMP     R12, #2DCAh
        JMPA    NZ, to_reader
        CMP     R13, #001Fh
        JMPA    NZ, to_reader
        MOV     R2, #123
        JMPA    UC, subst

; R2 identifies a catalogue entry. The original reader overwrites R2-R5 before use.
subst:
        EXTP    #3, #1
        MOV     R3, W_MAGIC
        CMP     R3, #MAGIC
        JMPR    NZ, to_reader
        MOV     R5, R2
        SHL     R5, #8
        OR      R5, #ST_ON
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    Z, rd_base
        MOV     R3, R2
        SHR     R3, #3
        ADD     R3, #W_BITMAP
        EXTP    #3, #1
        MOVB    RL4, [R3]
        MOVBZ   R4, RL4
        AND     R2, #7
        SHR     R4, R2
        JNB     R4.0, to_reader
rd_base:
        MOV     R3, #W_DIR
rd_loop:
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    Z, rd_found
        ADD     R3, #8
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    Z, rd_found
        ADD     R3, #8
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    Z, rd_found
        ADD     R3, #8
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    Z, rd_found
        ADD     R3, #8
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    Z, rd_found
rd_link:
        EXTP    #3, #1
        MOV     R4, [R3+2]
        SHR     R4, #8
        CMP     R4, #50
        JMPR    UGT, to_reader
        SHL     R4, #2
        ADD     R4, #W_COPY
        ADD     R3, #8
        CMP     R4, R3
        JMPR    ULT, to_reader
        MOV     R3, R4
        EXTP    #3, #1
        CMP     R5, [R3]
        JMPR    NZ, rd_link
rd_found:
        CALLA   UC, off_due
        CMP     R4, #0
        JMPR    NZ, to_reader
        EXTP    #3, #1
        MOVB    RL4, [R3+2]
        MOVBZ   R4, RL4
        SHL     R4, #2
        MOV     R13, #10h
        CMP     R4, #704
        JMPR    ULT, rd_external
        SUB     R4, #704
        ADD     R4, #26E0h
        MOV     R12, R4
        MOV     R13, #3
        JMPR    UC, to_reader
rd_external:
        ADD     R4, #1D00h
        MOV     R12, R4

to_reader:
        JMPS    0Dh, 029Ah

; ---- housekeeping: queue-1 task 09h's entry, every 10 ms at every engine state (D1). With the magic present, when the
; engine is not running or the app has been silent for 3 s, every On table goes to Ready, and stays there until the app
; switches it on again (D7). Then the task itself, with R3 and R4 as the scheduler left them. ----
hk:
        MOV     [-R0], R3
        MOV     [-R0], R4
        CALLA   UC, pops_tick
        CALLA   UC, guard_tick
        CALLA   UC, blip_tick
        CALLA   UC, watch_quiet
        CALLA   UC, watch_alias
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    NZ, hk_task
        CALLA   UC, off_due
        CMP     R4, #0
        JMPR    Z, hk_task
        CALLA   UC, all_off
hk_task:
        MOV     R4, [R0+]
        MOV     R3, [R0+]
        JMPS    0Dh, 080DEh

; ---- the DS2 0x6D handler: called by the dispatcher (09:FB76) with the request at F6CD ----
; It keeps R0 and saves R6-R9 and R15 on the user stack, as the firmware's handlers do; R1 is left alone. The serial-line task
; sends F74D.. once this returns.
handler:
        MOV     [-R0], R15
        MOV     [-R0], R9
        MOV     [-R0], R8
        MOV     [-R0], R7
        MOV     [-R0], R6
        EXTP    #3, #1
        MOV     R4, W_MAGIC
        CMP     R4, #MAGIC
        JMPR    Z, h_ready
        MOV     R5, #W_SEEN             ; the first frame since boot: zero E6B2-E6DF, then the magic
        MOV     R4, #0
h_zero:
        EXTP    #3, #1
        MOV     [R5], R4
        ADD     R5, #2
        CMP     R5, #W_COPY
        JMPR    ULT, h_zero
        MOVB    RL4, #0FFh
        EXTP    #3, #1
        MOVB    W_DIR+35, RL4
        CALLA   UC, hard_init
        MOV     R4, #MAGIC
        EXTP    #3, #1
        MOV     W_MAGIC, R4
        MOV     R4, #0
        EXTP    #3, #2
        MOV     W_REV, R4
        MOV     W_REV+2, R4
h_ready:
        CALLA   UC, off_due             ; a frame after a stop or a silence finds everything off first
        CMP     R4, #0
        JMPR    Z, h_stamp
        CALLA   UC, all_off
h_stamp:
        MOV     R4, F_TICKS             ; the keep-alive
        EXTP    #3, #1
        MOV     W_SEEN, R4
        MOVBZ   R4, REQ_LEN
        CMP     R4, #5
        JMPA    ULT, bad_len
        MOVBZ   R5, REQ_SUB
        CMP     R5, #0
        JMPR    NZ, h_open
        CMP     R4, #5
        JMPA    NZ, bad_len
        JMPA    UC, sub_status
h_open:
        CMP     R5, #10
        JMPR    NZ, h_patch
        CMP     R4, #9
        JMPA    NZ, bad_len
        JMPA    UC, sub_open
h_patch:
        CMP     R5, #9
        JMPR    NZ, h_watch
        CMP     R4, #29
        JMPA    NZ, bad_len
        JMPA    UC, sub_patch
h_watch:
        CMP     R5, #12
        JMPR    NZ, h_log
        CMP     R4, #8
        JMPA    NZ, bad_len
        JMPA    UC, sub_watch
h_log:
        CMP     R5, #11
        JMPR    NZ, h_auth
        CMP     R4, #5
        JMPA    NZ, bad_len
        JMPA    UC, sub_log
h_auth:
        CMP     R4, #13
        JMPA    ULT, bad_len
        CALLA   UC, authorize
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        MOVBZ   R4, REQ_LEN
        SUB     R4, #8
        MOVBZ   R5, REQ_SUB
        CMP     R5, #1
        JMPR    NZ, h_write
        CMP     R4, #9
        JMPA    NZ, bad_len
        JMPA    UC, sub_load
h_write:
        CMP     R5, #2
        JMPR    NZ, h_on
        CMP     R4, #8
        JMPA    ULT, bad_len
        JMPA    UC, sub_write
h_on:
        CMP     R5, #3
        JMPR    NZ, h_off
        CMP     R4, #10
        JMPA    NZ, bad_len
        JMPA    UC, sub_on
h_off:
        CMP     R5, #4
        JMPR    NZ, h_allon
        CMP     R4, #6
        JMPA    NZ, bad_len
        JMPA    UC, sub_off
h_allon:
        CMP     R5, #5
        JMPR    NZ, h_alloff
        CMP     R4, #5
        JMPA    NZ, bad_len
        JMPA    UC, sub_allon
h_alloff:
        CMP     R5, #6
        JMPR    NZ, h_free
        CMP     R4, #5
        JMPA    NZ, bad_len
        CALLA   UC, all_off
        JMPA    UC, ack
h_free:
        CMP     R5, #7
        JMPR    NZ, h_revert
        CMP     R4, #6
        JMPA    NZ, bad_len
        JMPA    UC, sub_free
h_revert:
        CMP     R5, #8
        JMPA    NZ, bad_sub
        CMP     R4, #5
        JMPA    NZ, bad_len
        JMPA    UC, sub_revert

; ---- the replies: refusals carry the reason in RL4; every path leaves through h_exit ----
bad_len:
        MOV     R4, #1
        JMPR    UC, refuse_b0
bad_sub:
        MOV     R4, #2
        JMPR    UC, refuse_b0
bad_table:
        MOV     R4, #3
        JMPR    UC, refuse_b0
bad_range:
        MOV     R4, #4
        JMPR    UC, refuse_b0
bad_overlap:
        MOV     R4, #5
refuse_b0:
        MOVB    RL5, #0B0h
        JMPR    UC, refuse
refuse_stopped:
        MOV     R4, #6
        JMPR    UC, refuse_b1
refuse_on:
        MOV     R4, #7
        JMPR    UC, refuse_b1
refuse_free:
        MOV     R4, #8
refuse_b1:
        MOVB    RL5, #0B1h
refuse:
        MOVB    REP_STATUS, RL5
        MOVB    REP_DATA, RL4
        MOVB    RL4, #5
        MOVB    REP_LEN, RL4
        JMPR    UC, h_exit
ack:
        CALLA   UC, advance_revision
ack_same:
        MOV     R12, #W_REV
        MOV     R13, #REP_DATA
        CALLA   UC, reply_dword
        MOVB    RL4, #0A0h
        MOVB    REP_STATUS, RL4
        MOVB    RL4, #8
        MOVB    REP_LEN, RL4
h_exit:
        MOV     R6, [R0+]
        MOV     R7, [R0+]
        MOV     R8, [R0+]
        MOV     R9, [R0+]
        MOV     R15, [R0+]
        RETS

; ---- load i, off: the flashed table copied to the copy area at off; Loaded ----
sub_load:
        CALLA   UC, watch_unalias
        MOVBZ   R2, REQ_ARG
        CMP     R2, #(N_TABLES - 1)
        JMPA    UGT, bad_table
        MOVBZ   R6, REQ_ARG+1
        SHL     R6, #8
        MOVBZ   R4, REQ_ARG+2
        OR      R6, R4
        MOV     R4, R6
        AND     R4, #3
        JMPA    NZ, bad_range
        CALLA   UC, table_info
        CMP     R6, #COPY_BYTES
        JMPA    UGE, bad_range
        MOV     R7, R6
        ADD     R7, R10
        CMP     R7, #COPY_BYTES
        JMPA    UGT, bad_range
        CMP     R6, #704
        JMPR    UGE, ld_boundary_ok
        CMP     R7, #668
        JMPA    UGT, bad_range
ld_boundary_ok:
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPR    Z, ld_new
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_ON
        JMPA    Z, refuse_on
        MOVBZ   R4, REQ_ARG+3
        CMP     R3, #W_COPY
        JMPR    UGE, ld_existing_extra
        CMP     R4, #255
        JMPA    NZ, bad_range
        JMPA    UC, ld_check_overlap
ld_existing_extra:
        CMP     R4, #50
        JMPA    UGT, bad_range
        SHL     R4, #2
        ADD     R4, #W_COPY
        CMP     R4, R3
        JMPA    NZ, bad_range
        JMPA    UC, ld_check_overlap
ld_new:
        MOVBZ   R4, REQ_ARG+3
        CMP     R4, #255
        JMPR    Z, ld_find_base
        CMP     R4, #50
        JMPA    UGT, bad_range
        SHL     R4, #2
        ADD     R4, #W_COPY
        MOV     R3, R4
        JMPA    UC, ld_check_overlap
ld_find_base:
        MOV     R3, #W_DIR
ld_base_loop:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPR    Z, ld_check_overlap
        ADD     R3, #8
        CMP     R3, #(W_DIR+8*BASE_SLOTS)
        JMPR    ULT, ld_base_loop
        JMPA    UC, bad_overlap
ld_check_overlap:
        MOV     R15, #0FFFFh
        CMP     R3, #W_COPY
        JMPR    ULT, ld_other_start
        MOV     R15, R3
        SUB     R15, #(W_COPY-704)
        MOV     R8, R15
        ADD     R8, #8
        CMP     R6, R8
        JMPR    UGE, ld_other_start
        CMP     R7, R15
        JMPA    UGT, bad_overlap
ld_other_start:
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    NZ, ld_index_needed
        CMP     R3, #W_COPY
        JMPR    ULT, ld_index_ok
ld_index_needed:
        CMP     R7, #912
        JMPA    UGT, bad_overlap
ld_index_ok:
        MOV     R9, #W_DIR
ld_overlap_loop:
        EXTP    #3, #1
        MOVB    RL4, [R9]
        CMPB    RL4, #ST_FREE
        JMPR    Z, ld_overlap_next
        CMP     R9, R3
        JMPR    NZ, ld_other
        EXTP    #3, #1
        MOVB    RL4, [R9+1]
        MOVBZ   R4, RL4
        CMP     R4, R2
        JMPA    NZ, bad_overlap
        JMPR    UC, ld_overlap_next
ld_other:
        MOV     [-R0], R2
        EXTP    #3, #1
        MOVB    RL4, [R9+1]
        MOVBZ   R2, RL4
        CALLA   UC, table_info
        MOV     R2, [R0+]
        EXTP    #3, #1
        MOVB    RL4, [R9+2]
        MOVBZ   R8, RL4
        SHL     R8, #2
        MOV     R5, R8
        ADD     R5, R10
        CMP     R15, #0FFFFh
        JMPR    Z, ld_other_index_ok
        CMP     R5, #912
        JMPA    UGT, bad_overlap
ld_other_index_ok:
        CALLA   UC, load_range_conflict
        CMP     R4, #0
        JMPA    NZ, bad_overlap
        CMP     R9, #W_COPY
        JMPR    ULT, ld_overlap_next
        MOV     R8, R9
        SUB     R8, #(W_COPY-704)
        MOV     R5, R8
        ADD     R5, #8
        CALLA   UC, load_range_conflict
        CMP     R4, #0
        JMPA    NZ, bad_overlap
ld_overlap_next:
        MOV     [-R0], R3
        MOV     R3, R9
        CALLA   UC, entry_next
        MOV     R9, R3
        MOV     R3, [R0+]
        CMP     R9, #0
        JMPA    NZ, ld_overlap_loop
        MOV     R4, R6
        SHR     R4, #2
        EXTP    #3, #2
        MOVB    [R3+2], RL4
        MOVB    [R3+1], RL2
        CALLA   UC, ram_info
        CALLA   UC, table_info
ld_copy:
        EXTP    R11, #1
        MOVB    RL4, [R12]
        EXTP    R14, #1
        MOVB    [R13], RL4
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, ld_copy
        MOV     R4, #0
        EXTP    #3, #2
        MOV     [R3+4], R4
        MOV     [R3+6], R4
        CALLA   UC, bitmap_include
        MOV     R4, #ST_LOADED
        EXTP    #3, #1
        MOVB    [R3], RL4
        CALLA   UC, link_entry
        JMPA    UC, ack

; Other allocation [R8,R5); requested data [R6,R7), optional record R15.
load_range_conflict:
        CMP     R7, R8
        JMPR    ULE, lrc_record
        CMP     R6, R5
        JMPR    ULT, lrc_bad
lrc_record:
        CMP     R15, #0FFFFh
        JMPR    Z, lrc_ok
        MOV     R4, R15
        ADD     R4, #8
        CMP     R4, R8
        JMPR    ULE, lrc_ok
        CMP     R15, R5
        JMPR    ULT, lrc_bad
lrc_ok:
        MOV     R4, #0
        RET
lrc_bad:
        MOV     R4, #1
        RET

; ---- write i, at, data: into a loaded or ready table's copy from its byte at; the table becomes Loaded ----
sub_write:
        MOVBZ   R2, REQ_ARG
        CMP     R2, #(N_TABLES - 1)
        JMPA    UGT, bad_table
        MOVBZ   R7, REQ_LEN
        SUB     R7, #16                 ; excludes the session and revision
        JMPA    Z, bad_range
        CMP     R7, #MOST_WRITE
        JMPA    UGT, bad_range
        MOVBZ   R6, REQ_ARG+1
        SHL     R6, #8
        MOVBZ   R4, REQ_ARG+2
        OR      R6, R4                  ; R6: at
        CALLA   UC, table_info          ; R10: the table's bytes
        CMP     R6, R10
        JMPA    UGT, bad_range          ; first, so the end below cannot wrap
        MOV     R8, R6
        ADD     R8, R7
        CMP     R8, R10
        JMPA    UGT, bad_range          ; the write runs past the table
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPA    Z, refuse_free
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPA    Z, refuse_free
        CMPB    RL4, #ST_ON
        JMPA    Z, refuse_on            ; never into a copy the reader may be reading
        CALLA   UC, feature_write_validate
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, ram_info
        ADD     R13, R6
        MOV     R12, #(REQ_ARG + 3)     ; the data: F6DC on
wr_copy:
        MOVB    RL4, [R12]
        EXTP    R14, #1
        MOVB    [R13], RL4
        ADD     R12, #1
        ADD     R13, #1
        SUB     R7, #1
        JMPR    NZ, wr_copy
        MOV     R4, #ST_LOADED          ; unconfirmed until its next on
        EXTP    #3, #1
        MOVB    [R3], RL4
        JMPA    UC, ack

; ---- on i, crc32: finalize a loaded copy after full-content CRC verification ----
sub_on:
        MOVBZ   R2, REQ_ARG
        CMP     R2, #(N_TABLES - 1)
        JMPA    UGT, bad_table
        CALLA   UC, running
        CMP     R4, #0
        JMPA    Z, refuse_stopped
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPA    Z, refuse_free
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPA    Z, refuse_free
        MOVBZ   R7, REQ_ARG+1
        SHL     R7, #8
        MOVBZ   R4, REQ_ARG+2
        OR      R7, R4                  ; high CRC word, wire big-endian
        MOVBZ   R6, REQ_ARG+3
        SHL     R6, #8
        MOVBZ   R4, REQ_ARG+4
        OR      R6, R4                  ; low CRC word
        CALLA   UC, verify
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        EXTP    #3, #1
        MOV     [R3+4], R6
        EXTP    #3, #1
        MOV     [R3+6], R7
        MOV     R4, #ST_ON              ; the state, written last
        EXTP    #3, #1
        MOVB    [R3], RL4
        JMPA    UC, ack

; ---- off i: On becomes Ready, the copy stays ----
sub_off:
        MOVBZ   R2, REQ_ARG
        CMP     R2, #(N_TABLES - 1)
        JMPA    UGT, bad_table
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPA    Z, refuse_free
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_ON
        JMPA    NZ, ack
        MOV     R4, #ST_READY
        EXTP    #3, #1
        MOVB    [R3], RL4
        JMPA    UC, ack

; ---- all on: every Ready table verified again with its stored CRC, then all of them On together; a Loaded
; table stays off. One that fails refuses the request, and nothing switches on ----
sub_allon:
        CALLA   UC, running
        CMP     R4, #0
        JMPA    Z, refuse_stopped
        MOV     R3, #W_DIR
an_check:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_READY
        JMPR    NZ, an_cnext
        EXTP    #3, #1
        MOVB    RL4, [R3+1]
        MOVBZ   R2, RL4
        EXTP    #3, #2
        MOV     R6, [R3+4]
        MOV     R7, [R3+6]
        CALLA   UC, verify
        CMP     R4, #0
        JMPA    NZ, refuse_b1
an_cnext:
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    NZ, an_check
        MOV     R3, #W_DIR
an_on:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_READY
        JMPR    NZ, an_onext
        MOVB    RL4, #ST_ON
        EXTP    #3, #1
        MOVB    [R3], RL4
an_onext:
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    NZ, an_on
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        JNB     R4.5, an_done
        OR      R4, #80h
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
an_done:
        JMPA    UC, ack

; ---- free i: a table not On back to Free ----
sub_free:
        CALLA   UC, watch_unalias
        MOVBZ   R2, REQ_ARG
        CMP     R2, #(N_TABLES - 1)
        JMPA    UGT, bad_table
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPA    Z, refuse_free
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_ON
        JMPA    Z, refuse_on
        CALLA   UC, free_entry
        JMPA    UC, ack

; ---- revert: every table Free, On ones included ----
sub_revert:
        CALLA   UC, watch_unalias
        MOV     R3, #W_DIR
rv_loop:
        CALLA   UC, free_entry
        ADD     R3, #8
        CMP     R3, #(W_DIR+8*BASE_SLOTS)
        JMPR    ULT, rv_loop
rv_extra:
        MOV     R3, #(W_DIR+32)
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    Z, rv_limiter
        CALLA   UC, free_entry
        JMPR    UC, rv_extra
rv_limiter:
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        AND     R4, #41h
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
        JMPA    UC, ack

; ---- status: 64 payload bytes, including the session, revision and limiter ----
sub_status:
        CALLA   UC, running
        MOV     R13, R4                 ; 1 while the engine runs
        MOV     R2, #REP_DATA
        MOV     R4, #544Ch              ; "LT"
        MOV     [R2], R4
        MOV     R4, #564Fh              ; "OV"
        MOV     [R2+2], R4
        MOV     R4, R13
        OR      R4, #(RICHER_ONLY*2)
        SHL     R4, #8
        OR      R4, #OVL_VERSION
        MOV     [R2+4], R4              ; the version, then the flags
        MOV     R3, #W_DIR
        MOV     R5, #(REP_DATA + 6)
st_entry:
        EXTP    #3, #1
        MOV     R4, [R3]                ; state, then catalogue ID
        MOV     [R5], R4
        EXTP    #3, #1
        MOV     R4, [R3+2]              ; packed offset/4 and control/link
        MOV     [R5+2], R4
        EXTP    #3, #1
        MOV     R4, [R3+6]              ; high CRC word first on the wire
        ROL     R4, #8
        MOV     [R5+4], R4
        EXTP    #3, #1
        MOV     R4, [R3+4]
        ROL     R4, #8
        MOV     [R5+6], R4
        ADD     R3, #8
        ADD     R5, #8
        CMP     R3, #(W_DIR + 8*BASE_SLOTS)
        JMPR    ULT, st_entry
        MOV     R9, #(COPY_BYTES-36)
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    Z, fr_no_index
        SUB     R9, #20
fr_no_index:
        MOV     R3, #W_DIR
fr_loop:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPR    Z, fr_next
        EXTP    #3, #1
        MOVB    RL4, [R3+1]
        MOVBZ   R2, RL4
        CALLA   UC, table_info
        SUB     R9, R10
fr_next:
        CMP     R3, #W_COPY
        JMPR    ULT, fr_step
        SUB     R9, #8
fr_step:
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    NZ, fr_loop
        ROL     R9, #8
        MOV     REP_DATA+46, R9
        MOVB    RL4, #0A0h
        MOVB    REP_STATUS, RL4
        MOV     R12, #W_SESSION
        MOV     R13, #(REP_DATA+48)
        CALLA   UC, reply_dword
        MOV     R12, #W_REV
        MOV     R13, #(REP_DATA+52)
        CALLA   UC, reply_dword
        MOV     R12, #W_HARD
        MOV     R13, #(REP_DATA+56)
        MOV     R10, #8
st_hard:
        EXTP    #10h, #1
        MOVB    RL4, [R12]
        MOVB    [R13], RL4
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, st_hard
        MOVB    RL4, #68
        MOVB    REP_LEN, RL4
        JMPA    UC, h_exit

; R6/R7 expected full-table CRC. No change-magnitude policy is applied in protocol 4.
verify:
        MOV     [-R0], R6
        MOV     [-R0], R7
        CALLA   UC, table_info
        CALLA   UC, ram_info
        CALLA   UC, feature_validate
        CMP     R4, #0
        JMPR    Z, vf_content
        MOV     R7, [R0+]
        MOV     R6, [R0+]
        RET
vf_content:
        MOV     R15, #0
        CALLA   UC, content_crc
        MOV     R7, [R0+]
        MOV     R6, [R0+]
        CMP     R4, R6
        JMPR    NZ, vf_crc
        CMP     R5, R7
        JMPR    NZ, vf_crc
        MOV     R4, #0
        RET
vf_crc:
        MOV     R4, #9
        RET

; R14 page, R13 pointer, R10 bytes. R2/R3 are preserved; result R4/R5 low/high.
content_crc:
        MOV     R8, #0FFFFh
        MOV     R9, #0FFFFh
crc_byte:
        EXTP    R14, #1
        MOVB    RL4, [R13]
        MOVBZ   R4, RL4
crc_update:
        XOR     R8, R4
        MOV     R5, R8
        AND     R5, #0FFh
        SHL     R5, #2
        ADD     R5, #(crc_table & 0FFFFh)
        MOV     R6, R8
        SHR     R6, #8
        MOV     R7, R9
        SHL     R7, #8
        OR      R6, R7
        SHR     R9, #8
        MOV     R8, R6
        EXTS    #0Ch, #2
        MOV     R4, [R5]
        MOV     R7, [R5+2]
        XOR     R8, R4
        XOR     R9, R7
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, crc_byte
        MOV     R4, R8
        XOR     R4, #0FFFFh
        MOV     R5, R9
        XOR     R5, #0FFFFh
        RET

; A complete request is copied out of the receive buffer before CRC validation.
; Private snapshot: session, revision, table/reserved/start, before CRC, after CRC, eight bytes, packet CRC.
sub_patch:
        MOV     R12, #REQ_SESSION
        MOV     R13, #W_PATCH
        MOV     R10, #24
pt_snapshot:
        MOVB    RL4, [R12]
        EXTP    #10h, #1
        MOVB    [R13], RL4
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, pt_snapshot
        MOV     R13, #W_PATCH
        MOV     R14, #10h
        MOV     R10, #20
        MOV     R15, #0
        CALLA   UC, content_crc
        MOV     R12, #(W_PATCH+20)
        CALLA   UC, patch_compare_crc
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        MOV     R12, #W_PATCH
        CALLA   UC, patch_dword
        CALLA   UC, check_session
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        EXTP    #10h, #2
        MOVBZ   R2, W_PATCH+8
        MOVBZ   R4, W_PATCH+9
        CMP     R2, #255
        JMPA    Z, sub_hard
        CMP     R2, #(N_TABLES-1)
        JMPA    UGT, bad_table
        CMP     R4, #0
        JMPA    NZ, bad_range
        MOV     R13, #(W_PATCH+12)
        MOV     R14, #10h
        CALLA   UC, feature_validate
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, table_info
        EXTP    #10h, #1
        MOV     R6, W_PATCH+10
        ROL     R6, #8
        JB      R6.0, pt_range
        SUB     R10, #8
        CMP     R6, R10
        JMPA    UGT, bad_range
        CALLA   UC, entry_addr
        CMP     R3, #0
        JMPA    Z, refuse_free
        EXTP    #3, #1
        MOVB    RL4, [R3]
        MOVBZ   R4, RL4
        CMP     R4, #ST_ON
        JMPA    NZ, refuse_free
        CALLA   UC, running
        CMP     R4, #0
        JMPA    Z, refuse_stopped
        MOV     R12, #(W_PATCH+4)
        CALLA   UC, patch_dword
        MOV     R4, R6
        AND     R4, R7
        CMP     R4, #0FFFFh
        JMPR    NZ, pt_revision
        MOV     R4, #13
        JMPA    UC, refuse_b1
pt_revision:
        CALLA   UC, check_revision
        CMP     R4, #0
        JMPR    Z, pt_before
        ; Only a retry of the immediately preceding revision may be acknowledged without a write.
        ADD     R6, #1
        ADDC    R7, #0
        CALLA   UC, check_revision
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, patch_actual_crc
        CALLA   UC, compare_entry_crc
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, patch_matches
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        JMPA    UC, ack_same
pt_range:
        JMPA    UC, bad_range
pt_before:
        CALLA   UC, revision_available
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, patch_actual_crc
        CALLA   UC, compare_entry_crc
        CMP     R4, #0
        JMPA    NZ, refuse_b1
        CALLA   UC, patch_result_crc
        EXTP    #10h, #2
        MOV     W_PATCH+20, R4
        MOV     W_PATCH+22, R5
        CALLA   UC, ram_info
        EXTP    #10h, #1
        MOV     R4, W_PATCH+10
        ROL     R4, #8
        ADD     R13, R4
        EXTP    #10h, #4
        MOV     R4, W_PATCH+12
        MOV     R5, W_PATCH+14
        MOV     R6, W_PATCH+16
        MOV     R7, W_PATCH+18
        ; EXTP protects these four aligned stores from interrupts and PEC. No PSW/IEN change.
patch_commit:
        EXTP    R14, #4
        MOV     [R13], R4
        MOV     [R13+2], R5
        MOV     [R13+4], R6
        MOV     [R13+6], R7
patch_committed:
        ; Full current CRC and exact prospective CRC were checked before the commit.
        ; Read back the only four words changed, avoiding another full-table scan.
        MOV     R12, #(W_PATCH+12)
        MOV     R10, #4
pt_readback:
        EXTP    R14, #1
        MOV     R4, [R13]
        EXTP    #10h, #1
        MOV     R5, [R12]
        CMP     R4, R5
        JMPR    NZ, pt_write_failed
        ADD     R12, #2
        ADD     R13, #2
        SUB     R10, #1
        JMPR    NZ, pt_readback
        JMPR    UC, pt_verified
pt_write_failed:
        MOV     R4, #ST_READY
        EXTP    #3, #1
        MOVB    [R3], RL4
        CALLA   UC, advance_revision
        MOV     R4, #9
        JMPA    UC, refuse_b1
pt_verified:
        EXTP    #10h, #2
        MOV     R6, W_PATCH+20
        MOV     R7, W_PATCH+22
        EXTP    #3, #2
        MOV     [R3+4], R6
        MOV     [R3+6], R7
        JMPA    UC, ack

patch_actual_crc:
        CALLA   UC, table_info
        CALLA   UC, ram_info
        MOV     R15, #0
        JMPA    UC, content_crc

; Compare computed R4/R5 with the big-endian dword at patch pointer R12.
patch_compare_crc:
        CALLA   UC, patch_dword
        CMP     R4, R6
        JMPA    NZ, pc_bad
        CMP     R5, R7
        JMPA    NZ, pc_bad
        MOV     R4, #0
        RET
pc_bad:
        MOV     R4, #9
        RET

patch_dword:
        EXTP    #10h, #2
        MOV     R7, [R12]
        MOV     R6, [R12+2]
        ROL     R7, #8
        ROL     R6, #8
        RET

; RAM page/offset are immutable, independent of directory contents.
ram_info:
        EXTP    #3, #1
        MOVB    RL4, [R3+2]
        MOVBZ   R13, RL4
        SHL     R13, #2
        MOV     R14, #10h
        CMP     R13, #704
        JMPR    ULT, ri_external
        SUB     R13, #704
        ADD     R13, #26E0h
        MOV     R14, #3
        RET
ri_external:
        ADD     R13, #1D00h
        RET

sub_open:
        MOV     R12, #REQ_SESSION
        CALLA   UC, request_dword
        MOV     R4, R6
        OR      R4, R7
        JMPA    Z, bad_range
        CALLA   UC, check_session
        CMP     R4, #0
        JMPA    Z, ack_same
        EXTP    #3, #2
        MOV     W_SESSION, R6
        MOV     W_SESSION+2, R7
        MOV     R4, #0
        EXTP    #3, #2
        MOV     W_REV, R4
        MOV     W_REV+2, R4
        JMPA    UC, ack_same

authorize:
        MOV     R12, #REQ_SESSION
        CALLA   UC, request_dword
        CALLA   UC, check_session
        CMP     R4, #0
        JMPR    NZ, auth_done
        MOV     R12, #REQ_REV
        CALLA   UC, request_dword
        CALLA   UC, check_revision
        CMP     R4, #0
        JMPR    NZ, auth_done
        CALLA   UC, revision_available
auth_done:
        RET

request_dword:
        MOVB    RL7, [R12]
        MOVBZ   R7, RL7
        SHL     R7, #8
        MOVB    RL4, [R12+1]
        MOVBZ   R4, RL4
        OR      R7, R4
        MOVB    RL6, [R12+2]
        MOVBZ   R6, RL6
        SHL     R6, #8
        MOVB    RL4, [R12+3]
        MOVBZ   R4, RL4
        OR      R6, R4
        RET

check_session:
        MOV     R4, R6
        OR      R4, R7
        JMPR    Z, session_bad
        EXTP    #3, #1
        CMP     R6, W_SESSION
        JMPR    NZ, session_bad
        EXTP    #3, #1
        CMP     R7, W_SESSION+2
        JMPR    NZ, session_bad
        MOV     R4, #0
        RET
session_bad:
        MOV     R4, #12
        RET

check_revision:
        EXTP    #3, #1
        CMP     R6, W_REV
        JMPR    NZ, revision_bad
        EXTP    #3, #1
        CMP     R7, W_REV+2
        JMPR    NZ, revision_bad
        MOV     R4, #0
        RET
revision_bad:
        MOV     R4, #13
        RET

revision_available:
        EXTP    #3, #2
        MOV     R4, W_REV
        AND     R4, W_REV+2
        CMP     R4, #0FFFFh
        JMPR    Z, revision_bad
        MOV     R4, #0
        RET

advance_revision:
        MOV     R4, #1
        EXTP    #3, #1
        ADD     W_REV, R4
        MOV     R4, #0
        EXTP    #3, #1
        ADDC    W_REV+2, R4
        RET

; Internal little-endian dword R12 to reply big-endian R13. R2/R3 unchanged.
reply_dword:
        EXTP    #3, #2
        MOV     R4, [R12+2]
        MOV     R5, [R12]
        ROL     R4, #8
        ROL     R5, #8
        MOV     [R13], R4
        MOV     [R13+2], R5
        RET

; ---- off_due: R4 = 1 when everything must be off: the engine not running, or 300 ticks since the last 0x6D frame;
; else 0. Uses R4 ----
off_due:
        CALLA   UC, running
        CMP     R4, #0
        JMPR    Z, od_yes
        MOV     R4, F_TICKS
        EXTP    #3, #1
        SUB     R4, W_SEEN
        CMP     R4, #SILENCE
        JMPR    UGE, od_yes
        MOV     R4, #0
        RET
od_yes:
        MOV     R4, #1
        RET

; ---- all_off: every On table to Ready, its copy kept; uses R3, R4 ----
all_off:
        CALLA   UC, watch_unalias
        MOV     [-R0], R5
        MOV     R5, #0
        MOV     R3, #W_DIR
ao_loop:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_ON
        JMPR    NZ, ao_next
        MOV     R5, #1
        MOV     R4, #ST_READY
        EXTP    #3, #1
        MOVB    [R3], RL4
ao_next:
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    NZ, ao_loop
        EXTP    #10h, #1
        MOVBZ   R4, W_HARD+7
        JNB     R4.7, ao_epoch
        MOV     R5, #1
        AND     R4, #7Fh
        EXTP    #10h, #1
        MOVB    W_HARD+7, RL4
ao_epoch:
        CMP     R5, #0
        JMPR    Z, ao_done
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+3
        ADD     R4, #1
        EXTP    #3, #1
        MOVB    W_DIR+3, RL4
        CMP     R4, #100h
        JMPR    NZ, ao_done
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+11
        ADD     R4, #1
        EXTP    #3, #1
        MOVB    W_DIR+11, RL4
ao_done:
        MOV     R5, [R0+]
        RET

; Fixed control bytes survive Free. Overflow records are unlinked before reuse.
free_entry:
        CALLA   UC, bitmap_remove
        CMP     R3, #W_COPY
        JMPR    ULT, fe_clear
        MOV     [-R0], R5
        MOV     [-R0], R6
        MOV     R6, R3
        MOV     R3, #(W_DIR+32)
fe_find:
        MOV     R5, R3
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    Z, fe_unlinked
        CMP     R3, R6
        JMPR    NZ, fe_find
        EXTP    #3, #1
        MOVB    RL4, [R3+3]
        EXTP    #3, #1
        MOVB    [R5+3], RL4
fe_unlinked:
        MOV     R3, R6
        MOV     R6, [R0+]
        MOV     R5, [R0+]
fe_clear:
        MOV     R4, #0
        EXTP    #3, #4
        MOV     [R3], R4
        MOVB    [R3+2], RL4
        MOV     [R3+4], R4
        MOV     [R3+6], R4
        RET

; Links increase within the 228-byte XRAM pool. Invalid links end the lookup.
entry_next:
        CMP     R3, #(W_DIR+32)
        JMPR    ULT, en_base
        EXTP    #3, #1
        MOVB    RL4, [R3+3]
        MOVBZ   R4, RL4
        CMP     R4, #50
        JMPR    UGT, en_end
        SHL     R4, #2
        ADD     R4, #W_COPY
        ADD     R3, #8
        CMP     R4, R3
        JMPR    ULT, en_end
        MOV     R3, R4
        RET
en_base:
        ADD     R3, #8
        RET
en_end:
        MOV     R3, #0
        RET

; Publish a complete extra record with one predecessor-link byte store.
link_entry:
        CMP     R3, #W_COPY
        JMPR    ULT, le_done
        MOV     R6, R3
        MOV     R3, #(W_DIR+32)
le_find:
        MOV     R5, R3
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    Z, le_insert
        CMP     R3, R6
        JMPR    Z, le_restore
        JMPR    ULT, le_find
le_insert:
        MOV     R4, #255
        CMP     R3, #0
        JMPR    Z, le_tail
        MOV     R4, R3
        SUB     R4, #W_COPY
        SHR     R4, #2
le_tail:
        EXTP    #3, #1
        MOVB    [R6+3], RL4
        MOV     R4, R6
        SUB     R4, #W_COPY
        SHR     R4, #2
        EXTP    #3, #1
        MOVB    [R5+3], RL4
le_restore:
        MOV     R3, R6
le_done:
        RET

; Find a loaded map by catalogue ID; zero when it has no allocation.
entry_addr:
        MOV     R3, #W_DIR
ea_loop:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPR    Z, ea_next
        EXTP    #3, #1
        MOVB    RL4, [R3+1]
        MOVBZ   R4, RL4
        CMP     R4, R2
        JMPR    Z, ea_done
ea_next:
        CALLA   UC, entry_next
        CMP     R3, #0
        JMPR    NZ, ea_loop
        MOV     R3, #0
ea_done:
        RET

; The first extra record reserves the final 20 pool-B bytes. Publish its head only
; after the complete index exists. Later updates change one allocation bit.
bitmap_include:
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    NZ, bi_set
        CMP     R3, #W_COPY
        JMPR    ULT, bi_done
        MOV     [-R0], R2
        MOV     [-R0], R3
        MOV     R4, #W_BITMAP
        MOV     R5, #0
bi_clear:
        EXTP    #3, #1
        MOV     [R4], R5
        ADD     R4, #2
        CMP     R4, #(W_BITMAP+20)
        JMPR    ULT, bi_clear
        MOV     R3, #W_DIR
bi_base:
        EXTP    #3, #1
        MOVB    RL4, [R3]
        CMPB    RL4, #ST_FREE
        JMPR    Z, bi_next
        EXTP    #3, #1
        MOVB    RL4, [R3+1]
        MOVBZ   R2, RL4
        CALLA   UC, bitmap_set
bi_next:
        ADD     R3, #8
        CMP     R3, #(W_DIR+40)
        JMPR    ULT, bi_base
        MOV     R3, [R0+]
        MOV     R2, [R0+]
bi_set:
        CALLA   UC, bitmap_set
bi_done:
        RET

; R2 is the allocated ID. R3 is preserved; R4-R6 are scratch.
bitmap_set:
        CALLA   UC, bitmap_bit
        EXTP    #3, #1
        MOVB    RL5, [R4]
        ORB     RL5, RL6
        EXTP    #3, #1
        MOVB    [R4], RL5
        RET

bitmap_bit:
        MOV     R4, R2
        SHR     R4, #3
        ADD     R4, #W_BITMAP
        MOV     R5, R2
        AND     R5, #7
        MOV     R6, #1
        SHL     R6, R5
        RET

bitmap_remove:
        EXTP    #3, #1
        MOVBZ   R4, W_DIR+35
        CMP     R4, #255
        JMPR    Z, br_done
        MOV     [-R0], R2
        MOV     [-R0], R5
        MOV     [-R0], R6
        EXTP    #3, #1
        MOVB    RL4, [R3+1]
        MOVBZ   R2, RL4
        CALLA   UC, bitmap_bit
        EXTP    #3, #1
        MOVB    RL5, [R4]
        XOR     R6, #0FFFFh
        ANDB    RL5, RL6
        EXTP    #3, #1
        MOVB    [R4], RL5
        MOV     R6, [R0+]
        MOV     R5, [R0+]
        MOV     R2, [R0+]
br_done:
        RET

; ---- table_info: table R2's flash page (R11), page offset (R12), bytes (R10); uses R5 ----
table_info:
        MOV     R5, R2
        SHL     R5, #3
        ADD     R5, #(tables & 0FFFFh)
        EXTS    #0Ch, #3
        MOV     R11, [R5]
        MOV     R12, [R5+2]
        MOV     R10, [R5+4]
        RET

; ---- running: R4 = 1 while the engine runs (F900 = 2 and FD2C bits 9-12), else 0; uses R4 ----
running:
        MOVB    RL4, F_STATE
        CMPB    RL4, #2
        JMPR    NZ, r_stopped
        MOV     R4, F_MODE
        AND     R4, #1E00h
        JMPR    Z, r_stopped
        MOV     R4, #1
        RET
r_stopped:
        MOV     R4, #0
        RET

; Exact prospective CRC from the verified before CRC and the eight-byte XOR difference.
; CRC(old) XOR CRC(new) = CRC(init=0, no final xor, old XOR new).
; Leading zero bytes leave the zero remainder unchanged; trailing zeros use precomputed
; GF(2) matrices, one for each power-of-two byte count. R2/R3 are preserved.
patch_result_crc:
        CALLA   UC, table_info
        CALLA   UC, ram_info
        EXTP    #10h, #1
        MOV     R4, W_PATCH+10
        ROL     R4, #8
        ADD     R13, R4
        SUB     R10, R4
        SUB     R10, #8
        MOV     R15, R10              ; unchanged suffix length
        MOV     R12, #(W_PATCH+12)
        MOV     R10, #8
        MOV     R8, #0
        MOV     R9, #0
delta_byte:
        EXTP    R14, #1
        MOVB    RL4, [R13]
        MOVBZ   R4, RL4
        EXTP    #10h, #1
        MOVB    RL5, [R12]
        MOVBZ   R5, RL5
        XOR     R4, R5
        XOR     R8, R4
        MOV     R5, R8
        AND     R5, #0FFh
        SHL     R5, #2
        ADD     R5, #(crc_table & 0FFFFh)
        MOV     R6, R8
        SHR     R6, #8
        MOV     R7, R9
        SHL     R7, #8
        OR      R6, R7
        SHR     R9, #8
        MOV     R8, R6
        EXTS    #0Ch, #2
        MOV     R4, [R5]
        MOV     R7, [R5+2]
        XOR     R8, R4
        XOR     R9, R7
        ADD     R12, #1
        ADD     R13, #1
        SUB     R10, #1
        JMPR    NZ, delta_byte
        MOV     R12, #(crc_zero_powers & 0FFFFh)
delta_power:
        CMP     R15, #0
        JMPR    Z, delta_done
        JNB     R15.0, delta_next
        MOV     R6, #0
        MOV     R7, #0
        MOV     R11, R12
        MOV     R10, #32
delta_bit:
        JNB     R8.0, delta_shift
        EXTS    #0Ch, #2
        MOV     R4, [R11]
        MOV     R5, [R11+2]
        XOR     R6, R4
        XOR     R7, R5
delta_shift:
        SHR     R8, #1
        MOV     R4, R9
        SHL     R4, #15
        OR      R8, R4
        SHR     R9, #1
        ADD     R11, #4
        SUB     R10, #1
        JMPR    NZ, delta_bit
        MOV     R8, R6
        MOV     R9, R7
delta_next:
        SHR     R15, #1
        ADD     R12, #128
        JMPR    UC, delta_power
delta_done:
        EXTP    #3, #2
        MOV     R6, [R3+4]
        MOV     R7, [R3+6]
        MOV     R4, R8
        MOV     R5, R9
        XOR     R4, R6
        XOR     R5, R7
        RET

compare_entry_crc:
        EXTP    #3, #2
        MOV     R6, [R3+4]
        MOV     R7, [R3+6]
        CMP     R4, R6
        JMPA    NZ, pc_bad
        CMP     R5, R7
        JMPA    NZ, pc_bad
        MOV     R4, #0
        RET

patch_matches:
        CALLA   UC, ram_info
        EXTP    #10h, #1
        MOV     R4, W_PATCH+10
        ROL     R4, #8
        ADD     R13, R4
        MOV     R12, #(W_PATCH+12)
        MOV     R10, #4
pm_loop:
        EXTP    R14, #1
        MOV     R4, [R13]
        EXTP    #10h, #1
        MOV     R5, [R12]
        CMP     R4, R5
        JMPA    NZ, pc_bad
        ADD     R12, #2
        ADD     R13, #2
        SUB     R10, #1
        JMPR    NZ, pm_loop
        MOV     R4, #0
        RET

; V4 limiter helpers are included here, before the header's catalogue pointer.
        INCLUDE "live_overlay_v4_limiter.inc"

        INCLUDE "live_overlay_v10_feature_helpers.inc"
        INCLUDE "live_overlay_v9_feature_stubs.inc"

        INCLUDE "live_overlay_v7_word_stubs.inc"

        INCLUDE "live_overlay_v10_pops.inc"

code_end:

; The catalogue: flash page, page offset, bytes, flags.
tables:
        DW      001Dh, 3315h, 320, 0 ; 0 ip_iga_ron98_pl__n__maf
        DW      001Dh, 35D4h, 36, 0 ; 1 ip_iga_ron98_is__n__maf
        DW      001Fh, 169Ch, 192, 0 ; 2 ip_af_target_ron98__maf__n
        DW      001Dh, 2390h, 192, 0 ; 3 ip_cam_sp_ron98_in_pl__n__maf_ivvt
        DW      001Dh, 229Ch, 192, 0 ; 4 ip_cam_sp_ron98_ex_pl__n__maf_ivvt
        DW      001Dh, 1FD6h, 32, 0 ; 5 ip_cam_sp_add_cat_pl_ex__tco_st__km_ctr
        DW      001Dh, 1FF6h, 32, 0 ; 6 ip_cam_sp_add_ex_ch_2_is_at__tco_st__km_ctr
        DW      001Dh, 2016h, 32, 0 ; 7 ip_cam_sp_add_ex_ch_2_is_mt__tco_st__km_ctr
        DW      001Dh, 2036h, 32, 0 ; 8 ip_cam_sp_add_in_ch_2_is_at__tco_st__km_ctr
        DW      001Dh, 2056h, 32, 0 ; 9 ip_cam_sp_add_in_ch_2_is_mt__tco_st__km_ctr
        DW      001Dh, 2090h, 36, 0 ; 10 ip_cam_sp_e85_ex_is__n__maf_ivvt
        DW      001Dh, 20B4h, 192, 0 ; 11 ip_cam_sp_e85_ex_pl__n__maf_ivvt
        DW      001Dh, 2184h, 36, 0 ; 12 ip_cam_sp_e85_in_is__n__maf_ivvt
        DW      001Dh, 21A8h, 192, 0 ; 13 ip_cam_sp_e85_in_pl__n__maf_ivvt
        DW      001Dh, 2278h, 36, 0 ; 14 ip_cam_sp_ron98_ex_is__n__maf_ivvt
        DW      001Dh, 236Ch, 36, 0 ; 15 ip_cam_sp_ron98_in_is__n__maf_ivvt
        DW      001Dh, 27AAh, 36, 0 ; 16 ip_fac_er__n__maf
        DW      001Dh, 27CEh, 64, 0 ; 17 ip_fac_iga_ch_2__tco__t_ast
        DW      001Dh, 2812h, 12, 0 ; 18 ip_fac_iga_tra__tia__fac_ron
        DW      001Dh, 281Eh, 36, 0 ; 19 ip_fac_iga_tra_gear__gear__vs
        DW      001Dh, 2842h, 36, 0 ; 20 ip_fac_igcfb__n__maf
        DW      001Dh, 2866h, 64, 0 ; 21 ip_fac_is_ivvt__tco__tco_st
        DW      001Dh, 2A5Bh, 16, 0 ; 22 ip_fac_knk_diag__tia__tco
        DW      001Dh, 2A98h, 64, 0 ; 23 ip_fac_pl_ivvt__tco__tco_st
        DW      001Dh, 2ADCh, 144, 0 ; 24 ip_fac_pvs_isa__pvs_cor_rpl__n
        DW      001Dh, 2BB5h, 16, 0 ; 25 ip_fac_ron_ad_max__tia__tco
        DW      001Dh, 2BC5h, 64, 0 ; 26 ip_fac_t_dly__tco__tco_st
        DW      001Dh, 2C27h, 36, 0 ; 27 ip_fac_tia_tco__tco__tia
        DW      001Dh, 2C4Bh, 16, 0 ; 28 ip_fac_tia_tco_is__tco__tia
        DW      001Dh, 2C5Bh, 16, 0 ; 29 ip_fac_toil_facron__toil__facron
        DW      001Dh, 2CDDh, 48, 0 ; 30 ip_iga_aj_ron91__n__maf
        DW      001Dh, 2D0Dh, 48, 0 ; 31 ip_iga_aj_ron98__n__maf
        DW      001Dh, 2D41h, 64, 0 ; 32 ip_iga_ch__n__maf
        DW      001Dh, 2DC1h, 64, 0 ; 33 ip_iga_ch_cat_var__n__maf
        DW      001Dh, 2E41h, 64, 0 ; 34 ip_iga_knk_diag__n__maf
        DW      001Dh, 2E81h, 64, 0 ; 35 ip_iga_knk_diag_ron__n__maf
        DW      001Dh, 2F11h, 16, 0 ; 36 ip_iga_maf_n_is__n__maf
        DW      001Dh, 2F21h, 64, 0 ; 37 ip_iga_maf_n_knk_diag__n__maf
        DW      001Dh, 2F61h, 36, 0 ; 38 ip_iga_maf_n_toil_is__n__maf
        DW      001Dh, 2FE5h, 192, 0 ; 39 ip_iga_optm_e85__n__maf
        DW      001Dh, 30A5h, 192, 0 ; 40 ip_iga_optm_ron98__n__maf
        DW      001Dh, 3195h, 64, 0 ; 41 ip_iga_ref_mon2
        DW      001Dh, 31D5h, 320, 0 ; 42 ip_iga_ron91_pl__n__maf
        DW      001Dh, 3470h, 36, 0 ; 43 ip_iga_e85_is__n__maf
        DW      001Dh, 3494h, 320, 0 ; 44 ip_iga_e85_pl__n__maf
        DW      001Dh, 35F8h, 64, 0 ; 45 ip_iga_tco_ch_at__tco__t_ast
        DW      001Dh, 3638h, 64, 0 ; 46 ip_iga_tco_ch_is__tco__t_ast
        DW      001Dh, 3678h, 64, 0 ; 47 ip_iga_tco_ch_mt__tco__t_ast
        DW      001Dh, 3858h, 24, 0 ; 48 ip_ivvthpwm__tmag__vb
        DW      001Dh, 3870h, 64, 0 ; 49 ip_lam_i_1__n__maf
        DW      001Dh, 38B0h, 64, 0 ; 50 ip_lam_i_2__n__maf
        DW      001Dh, 38F8h, 64, 0 ; 51 ip_lam_neg_p__n__maf
        DW      001Dh, 393Ch, 64, 0 ; 52 ip_lam_pos_p__n__maf
        DW      001Dh, 3A10h, 60, 0 ; 53 ip_maf_min_cop__n__iga_dif
        DW      001Dh, 3A4Ch, 60, 0 ; 54 ip_maf_min_cop_ron__n__iga_dif
        DW      001Dh, 3AA0h, 32, 0 ; 55 ip_maf_min_knk__n__tco
        DW      001Dh, 3AC0h, 32, 0 ; 56 ip_maf_min_knk_ron__n__tco
        DW      001Dh, 3AFAh, 36, 0 ; 57 ip_mis_a_fac__n__maf
        DW      001Dh, 3D27h, 96, 0 ; 58 ip_pvs_dif_cr_neg__pvs_max__n
        DW      001Dh, 3D87h, 96, 0 ; 59 ip_pvs_dif_neg_fac__pvs_max__n
        DW      001Eh, 0055h, 36, 0 ; 60 ip_soi_tco_1_is__n__ti
        DW      001Eh, 0079h, 80, 0 ; 61 ip_soi_tco_1_pl__n__ti
        DW      001Eh, 00C9h, 36, 0 ; 62 ip_soi_tco_2_is__n__ti
        DW      001Eh, 00EDh, 80, 0 ; 63 ip_soi_tco_2_pl__n__ti
        DW      001Eh, 0371h, 64, 0 ; 64 ip_t_vls_cyc_afl_max__n__maf
        DW      001Eh, 03B1h, 64, 0 ; 65 ip_t_vls_cyc_afr_max__n__maf
        DW      001Eh, 03F1h, 64, 0 ; 66 ip_t_vls_cycnr_afl_max__n__maf
        DW      001Eh, 0431h, 64, 0 ; 67 ip_t_vls_cycnr_afr_max__n__maf
        DW      001Eh, 0499h, 48, 0 ; 68 ip_temp_fac_ex_down__tes__tam
        DW      001Eh, 04C9h, 48, 0 ; 69 ip_temp_fac_ex_up__tes__tam
        DW      001Eh, 0551h, 48, 0 ; 70 ip_temp_fac_wall_down__tes__tam
        DW      001Eh, 0581h, 48, 0 ; 71 ip_temp_fac_wall_up__tes__tam
        DW      001Eh, 05B1h, 64, 0 ; 72 ip_temp_sp_tco__n__maf
        DW      001Eh, 05F1h, 64, 0 ; 73 ip_temp_sp_toil__n__maf
        DW      001Eh, 0639h, 36, 0 ; 74 ip_temp_sub_ecf_ini__tco__tam
        DW      001Eh, 0703h, 80, 0 ; 75 ip_ti_add_cop_step_2__n__maf
        DW      001Eh, 075Bh, 16, 0 ; 76 ip_ti_cast_ron98__tia__tco
        DW      001Eh, 07B3h, 80, 0 ; 77 ip_ti_cop_nr_1_1__n__maf
        DW      001Eh, 0803h, 80, 0 ; 78 ip_ti_cop_nr_1_2__n__maf
        DW      001Eh, 0853h, 80, 0 ; 79 ip_ti_cop_nr_2_1__n__maf
        DW      001Eh, 08A3h, 80, 0 ; 80 ip_ti_cop_nr_2_2__n__maf
        DW      001Eh, 08F3h, 80, 0 ; 81 ip_ti_cop_nr_3_1__n__maf
        DW      001Eh, 0943h, 80, 0 ; 82 ip_ti_cop_nr_3_2__n__maf
        DW      001Eh, 0993h, 80, 0 ; 83 ip_ti_cop_nr_4_1__n__maf
        DW      001Eh, 09E3h, 80, 0 ; 84 ip_ti_cop_nr_4_2__n__maf
        DW      001Eh, 0A33h, 80, 0 ; 85 ip_ti_cop_nr_5_1__n__maf
        DW      001Eh, 0A83h, 80, 0 ; 86 ip_ti_cop_nr_5_2__n__maf
        DW      001Eh, 0AD3h, 48, 0 ; 87 ip_ti_cr_cast_ron98__cyc__tco
        DW      001Eh, 0B03h, 32, 0 ; 88 ip_ti_cr_cst_ron98__cyc__tco
        DW      001Eh, 0B39h, 64, 0 ; 89 ip_ti_fac_fast_wf__maf__n
        DW      001Eh, 0BB9h, 64, 0 ; 90 ip_ti_fac_neg_fast_wf__maf__n
        DW      001Eh, 0BF9h, 64, 0 ; 91 ip_ti_fac_neg_slow_wf__maf__n
        DW      001Eh, 0C39h, 64, 0 ; 92 ip_ti_fac_slow_wf__maf__n
        DW      001Eh, 0D9Eh, 64, 0 ; 93 ip_ti_tco_neg_fast_wf__n__tco
        DW      001Eh, 0DDEh, 64, 0 ; 94 ip_ti_tco_neg_wf__n__tco
        DW      001Eh, 0E1Eh, 64, 0 ; 95 ip_ti_tco_pos_fast_wf__n__tco
        DW      001Eh, 0E5Eh, 64, 0 ; 96 ip_ti_tco_pos_wf__n__tco
        DW      001Eh, 0E9Eh, 32, 0 ; 97 ip_ti_tco_sawup__tco__km_ctr
        DW      001Eh, 0EBEh, 32, 0 ; 98 ip_ti_tco_sawup_is__tco__km_ctr
        DW      001Eh, 0EDEh, 24, 0 ; 99 ip_ti_tco_wup_ron98__tco__tco_st
        DW      001Eh, 0EF6h, 24, 0 ; 100 ip_ti_tco_wup_is_ron98__tco__tco_st
        DW      001Eh, 0F2Eh, 64, 0 ; 101 ip_ti_vo_wup__tco__vo
        DW      001Eh, 0F6Eh, 64, 0 ; 102 ip_ti_wup_ron98__n__maf
        DW      001Eh, 0FC6h, 100, 0 ; 103 ip_tmag__toil__tco
        DW      001Eh, 1061h, 64, 0 ; 104 ip_tq_loss_mon2
        DW      001Eh, 10A1h, 64, 0 ; 105 ip_tq_max_mon2
        DW      001Eh, 10E1h, 16, 0 ; 106 ip_tqi_dif_max_mon2
        DW      001Eh, 1121h, 64, 0 ; 107 ip_tqi_ref_mon2
        DW      001Eh, 11A9h, 64, 0 ; 108 ip_vls_sp_dly__n__maf
        DW      001Fh, 175Ch, 192, 0 ; 109 ip_af_target_e85__maf__n
        DW      001Fh, 181Ch, 32, 0 ; 110 ip_ti_cr_cst_e85__cyc__tco
        DW      001Fh, 183Ch, 48, 0 ; 111 ip_ti_cr_cast_e85__cyc__tco
        DW      001Fh, 186Ch, 64, 0 ; 112 ip_fac_rest_e85__t_ast__tco_stop
        DW      001Fh, 18ECh, 64, 0 ; 113 ip_ti_wup_e85__n__maf
        DW      001Fh, 192Ch, 24, 0 ; 114 ip_ti_tco_wup_e85__tco__tco_st
        DW      001Fh, 1944h, 24, 0 ; 115 ip_ti_tco_wup_is_e85__tco__tco_st
        DW      001Fh, 195Ch, 16, 0 ; 116 ip_ti_cast_e85__tia__tco
        DW      001Fh, 2C72h, 64, 0 ; 117 ip_iga_ref_mon
        DW      001Fh, 2CB4h, 64, 0 ; 118 ip_maf_sub_mon
        DW      001Fh, 2CF4h, 64, 0 ; 119 ip_pq_main_col_tq_mon
        DW      001Fh, 2D3Ah, 64, 0 ; 120 ip_tq_loss_mon
        DW      001Fh, 2D7Ah, 64, 0 ; 121 ip_tq_max_mon
        DW      001Fh, 2DBAh, 16, 0 ; 122 ip_tqi_dif_max_mon
        DW      001Fh, 2DCAh, 64, 0 ; 123 ip_tqi_ref_mon

        DW      001Ch, 132Ch, 8, 2 ; 124 launch_control
        DW      001Ch, 1334h, 8, 2 ; 125 no_lift_shift
        DW      001Ch, 133Ch, 8, 2 ; 126 rolling_anti_lag (7 fields + protected neighbor)

        DW      001Eh, 03674h, 12, 4 ; 127 ip_n_sp_is__tco
        DW      001Eh, 022E0h, 12, 4 ; 128 ip_isapwm__tco
        DW      001Eh, 0155Eh, 12, 4 ; 129 ip_acin_n_sp_is__tco
        DW      001Eh, 03680h, 16, 4 ; 130 ip_n_vim_tia__tia
        DW      001Fh, 00400h, 48, 4 ; 131 ip_t_puc_deacc_1__n__maf_mmv
        DW      001Fh, 00430h, 48, 4 ; 132 ip_t_puc_deacc_2__n__maf_mmv
        DW      001Fh, 00460h, 48, 4 ; 133 ip_t_puc_deacc_3__n__maf_mmv
        DW      001Fh, 00490h, 48, 4 ; 134 ip_t_puc_deacc_4__n__maf_mmv
        DW      001Fh, 004C0h, 48, 4 ; 135 ip_t_puc_deacc_5__n__maf_mmv
        DW      001Fh, 024DCh, 288, 4 ; 136 ip_tps_sp_pvs__pvs__n
        DW      001Fh, 025FCh, 32, 4 ; 137 ip_tps_sp_pvs_add_low_n__n_dif__
        DW      0033h, 03F80h, 8, 2 ; 138 pops
        DW      0033h, 03F88h, 8, 2 ; 139 pops stabs
        DW      0033h, 03F90h, 8, 2 ; 140 downshift blip
        DW      0033h, 03F98h, 8, 2 ; 141 downshift blip: gears and tap
        DW      0033h, 03FA0h, 8, 2 ; 142 downshift blip: gear ratios

crc_table:
        DW      00000h, 00000h
        DW      03096h, 07707h
        DW      0612Ch, 0EE0Eh
        DW      051BAh, 09909h
        DW      0C419h, 0076Dh
        DW      0F48Fh, 0706Ah
        DW      0A535h, 0E963h
        DW      095A3h, 09E64h
        DW      08832h, 00EDBh
        DW      0B8A4h, 079DCh
        DW      0E91Eh, 0E0D5h
        DW      0D988h, 097D2h
        DW      04C2Bh, 009B6h
        DW      07CBDh, 07EB1h
        DW      02D07h, 0E7B8h
        DW      01D91h, 090BFh
        DW      01064h, 01DB7h
        DW      020F2h, 06AB0h
        DW      07148h, 0F3B9h
        DW      041DEh, 084BEh
        DW      0D47Dh, 01ADAh
        DW      0E4EBh, 06DDDh
        DW      0B551h, 0F4D4h
        DW      085C7h, 083D3h
        DW      09856h, 0136Ch
        DW      0A8C0h, 0646Bh
        DW      0F97Ah, 0FD62h
        DW      0C9ECh, 08A65h
        DW      05C4Fh, 01401h
        DW      06CD9h, 06306h
        DW      03D63h, 0FA0Fh
        DW      00DF5h, 08D08h
        DW      020C8h, 03B6Eh
        DW      0105Eh, 04C69h
        DW      041E4h, 0D560h
        DW      07172h, 0A267h
        DW      0E4D1h, 03C03h
        DW      0D447h, 04B04h
        DW      085FDh, 0D20Dh
        DW      0B56Bh, 0A50Ah
        DW      0A8FAh, 035B5h
        DW      0986Ch, 042B2h
        DW      0C9D6h, 0DBBBh
        DW      0F940h, 0ACBCh
        DW      06CE3h, 032D8h
        DW      05C75h, 045DFh
        DW      00DCFh, 0DCD6h
        DW      03D59h, 0ABD1h
        DW      030ACh, 026D9h
        DW      0003Ah, 051DEh
        DW      05180h, 0C8D7h
        DW      06116h, 0BFD0h
        DW      0F4B5h, 021B4h
        DW      0C423h, 056B3h
        DW      09599h, 0CFBAh
        DW      0A50Fh, 0B8BDh
        DW      0B89Eh, 02802h
        DW      08808h, 05F05h
        DW      0D9B2h, 0C60Ch
        DW      0E924h, 0B10Bh
        DW      07C87h, 02F6Fh
        DW      04C11h, 05868h
        DW      01DABh, 0C161h
        DW      02D3Dh, 0B666h
        DW      04190h, 076DCh
        DW      07106h, 001DBh
        DW      020BCh, 098D2h
        DW      0102Ah, 0EFD5h
        DW      08589h, 071B1h
        DW      0B51Fh, 006B6h
        DW      0E4A5h, 09FBFh
        DW      0D433h, 0E8B8h
        DW      0C9A2h, 07807h
        DW      0F934h, 00F00h
        DW      0A88Eh, 09609h
        DW      09818h, 0E10Eh
        DW      00DBBh, 07F6Ah
        DW      03D2Dh, 0086Dh
        DW      06C97h, 09164h
        DW      05C01h, 0E663h
        DW      051F4h, 06B6Bh
        DW      06162h, 01C6Ch
        DW      030D8h, 08565h
        DW      0004Eh, 0F262h
        DW      095EDh, 06C06h
        DW      0A57Bh, 01B01h
        DW      0F4C1h, 08208h
        DW      0C457h, 0F50Fh
        DW      0D9C6h, 065B0h
        DW      0E950h, 012B7h
        DW      0B8EAh, 08BBEh
        DW      0887Ch, 0FCB9h
        DW      01DDFh, 062DDh
        DW      02D49h, 015DAh
        DW      07CF3h, 08CD3h
        DW      04C65h, 0FBD4h
        DW      06158h, 04DB2h
        DW      051CEh, 03AB5h
        DW      00074h, 0A3BCh
        DW      030E2h, 0D4BBh
        DW      0A541h, 04ADFh
        DW      095D7h, 03DD8h
        DW      0C46Dh, 0A4D1h
        DW      0F4FBh, 0D3D6h
        DW      0E96Ah, 04369h
        DW      0D9FCh, 0346Eh
        DW      08846h, 0AD67h
        DW      0B8D0h, 0DA60h
        DW      02D73h, 04404h
        DW      01DE5h, 03303h
        DW      04C5Fh, 0AA0Ah
        DW      07CC9h, 0DD0Dh
        DW      0713Ch, 05005h
        DW      041AAh, 02702h
        DW      01010h, 0BE0Bh
        DW      02086h, 0C90Ch
        DW      0B525h, 05768h
        DW      085B3h, 0206Fh
        DW      0D409h, 0B966h
        DW      0E49Fh, 0CE61h
        DW      0F90Eh, 05EDEh
        DW      0C998h, 029D9h
        DW      09822h, 0B0D0h
        DW      0A8B4h, 0C7D7h
        DW      03D17h, 059B3h
        DW      00D81h, 02EB4h
        DW      05C3Bh, 0B7BDh
        DW      06CADh, 0C0BAh
        DW      08320h, 0EDB8h
        DW      0B3B6h, 09ABFh
        DW      0E20Ch, 003B6h
        DW      0D29Ah, 074B1h
        DW      04739h, 0EAD5h
        DW      077AFh, 09DD2h
        DW      02615h, 004DBh
        DW      01683h, 073DCh
        DW      00B12h, 0E363h
        DW      03B84h, 09464h
        DW      06A3Eh, 00D6Dh
        DW      05AA8h, 07A6Ah
        DW      0CF0Bh, 0E40Eh
        DW      0FF9Dh, 09309h
        DW      0AE27h, 00A00h
        DW      09EB1h, 07D07h
        DW      09344h, 0F00Fh
        DW      0A3D2h, 08708h
        DW      0F268h, 01E01h
        DW      0C2FEh, 06906h
        DW      0575Dh, 0F762h
        DW      067CBh, 08065h
        DW      03671h, 0196Ch
        DW      006E7h, 06E6Bh
        DW      01B76h, 0FED4h
        DW      02BE0h, 089D3h
        DW      07A5Ah, 010DAh
        DW      04ACCh, 067DDh
        DW      0DF6Fh, 0F9B9h
        DW      0EFF9h, 08EBEh
        DW      0BE43h, 017B7h
        DW      08ED5h, 060B0h
        DW      0A3E8h, 0D6D6h
        DW      0937Eh, 0A1D1h
        DW      0C2C4h, 038D8h
        DW      0F252h, 04FDFh
        DW      067F1h, 0D1BBh
        DW      05767h, 0A6BCh
        DW      006DDh, 03FB5h
        DW      0364Bh, 048B2h
        DW      02BDAh, 0D80Dh
        DW      01B4Ch, 0AF0Ah
        DW      04AF6h, 03603h
        DW      07A60h, 04104h
        DW      0EFC3h, 0DF60h
        DW      0DF55h, 0A867h
        DW      08EEFh, 0316Eh
        DW      0BE79h, 04669h
        DW      0B38Ch, 0CB61h
        DW      0831Ah, 0BC66h
        DW      0D2A0h, 0256Fh
        DW      0E236h, 05268h
        DW      07795h, 0CC0Ch
        DW      04703h, 0BB0Bh
        DW      016B9h, 02202h
        DW      0262Fh, 05505h
        DW      03BBEh, 0C5BAh
        DW      00B28h, 0B2BDh
        DW      05A92h, 02BB4h
        DW      06A04h, 05CB3h
        DW      0FFA7h, 0C2D7h
        DW      0CF31h, 0B5D0h
        DW      09E8Bh, 02CD9h
        DW      0AE1Dh, 05BDEh
        DW      0C2B0h, 09B64h
        DW      0F226h, 0EC63h
        DW      0A39Ch, 0756Ah
        DW      0930Ah, 0026Dh
        DW      006A9h, 09C09h
        DW      0363Fh, 0EB0Eh
        DW      06785h, 07207h
        DW      05713h, 00500h
        DW      04A82h, 095BFh
        DW      07A14h, 0E2B8h
        DW      02BAEh, 07BB1h
        DW      01B38h, 00CB6h
        DW      08E9Bh, 092D2h
        DW      0BE0Dh, 0E5D5h
        DW      0EFB7h, 07CDCh
        DW      0DF21h, 00BDBh
        DW      0D2D4h, 086D3h
        DW      0E242h, 0F1D4h
        DW      0B3F8h, 068DDh
        DW      0836Eh, 01FDAh
        DW      016CDh, 081BEh
        DW      0265Bh, 0F6B9h
        DW      077E1h, 06FB0h
        DW      04777h, 018B7h
        DW      05AE6h, 08808h
        DW      06A70h, 0FF0Fh
        DW      03BCAh, 06606h
        DW      00B5Ch, 01101h
        DW      09EFFh, 08F65h
        DW      0AE69h, 0F862h
        DW      0FFD3h, 0616Bh
        DW      0CF45h, 0166Ch
        DW      0E278h, 0A00Ah
        DW      0D2EEh, 0D70Dh
        DW      08354h, 04E04h
        DW      0B3C2h, 03903h
        DW      02661h, 0A767h
        DW      016F7h, 0D060h
        DW      0474Dh, 04969h
        DW      077DBh, 03E6Eh
        DW      06A4Ah, 0AED1h
        DW      05ADCh, 0D9D6h
        DW      00B66h, 040DFh
        DW      03BF0h, 037D8h
        DW      0AE53h, 0A9BCh
        DW      09EC5h, 0DEBBh
        DW      0CF7Fh, 047B2h
        DW      0FFE9h, 030B5h
        DW      0F21Ch, 0BDBDh
        DW      0C28Ah, 0CABAh
        DW      09330h, 053B3h
        DW      0A3A6h, 024B4h
        DW      03605h, 0BAD0h
        DW      00693h, 0CDD7h
        DW      05729h, 054DEh
        DW      067BFh, 023D9h
        DW      07A2Eh, 0B366h
        DW      04AB8h, 0C461h
        DW      01B02h, 05D68h
        DW      02B94h, 02A6Fh
        DW      0BE37h, 0B40Bh
        DW      08EA1h, 0C30Ch
        DW      0DF1Bh, 05A05h
        DW      0EF8Dh, 02D02h

; Generated by tools/generate_crc_zero_powers.py; 9 x 32 columns.
crc_zero_powers:
; Advance a remainder by 1 zero bytes.
        DW      03096h, 07707h
        DW      0612Ch, 0EE0Eh
        DW      0C419h, 0076Dh
        DW      08832h, 00EDBh
        DW      01064h, 01DB7h
        DW      020C8h, 03B6Eh
        DW      04190h, 076DCh
        DW      08320h, 0EDB8h
        DW      00001h, 00000h
        DW      00002h, 00000h
        DW      00004h, 00000h
        DW      00008h, 00000h
        DW      00010h, 00000h
        DW      00020h, 00000h
        DW      00040h, 00000h
        DW      00080h, 00000h
        DW      00100h, 00000h
        DW      00200h, 00000h
        DW      00400h, 00000h
        DW      00800h, 00000h
        DW      01000h, 00000h
        DW      02000h, 00000h
        DW      04000h, 00000h
        DW      08000h, 00000h
        DW      00000h, 00001h
        DW      00000h, 00002h
        DW      00000h, 00004h
        DW      00000h, 00008h
        DW      00000h, 00010h
        DW      00000h, 00020h
        DW      00000h, 00040h
        DW      00000h, 00080h
; Advance a remainder by 2 zero bytes.
        DW      03141h, 0191Bh
        DW      06282h, 03236h
        DW      0C504h, 0646Ch
        DW      08A08h, 0C8D9h
        DW      01251h, 04AC2h
        DW      024A2h, 09584h
        DW      04F05h, 0F079h
        DW      0984Bh, 03B83h
        DW      03096h, 07707h
        DW      0612Ch, 0EE0Eh
        DW      0C419h, 0076Dh
        DW      08832h, 00EDBh
        DW      01064h, 01DB7h
        DW      020C8h, 03B6Eh
        DW      04190h, 076DCh
        DW      08320h, 0EDB8h
        DW      00001h, 00000h
        DW      00002h, 00000h
        DW      00004h, 00000h
        DW      00008h, 00000h
        DW      00010h, 00000h
        DW      00020h, 00000h
        DW      00040h, 00000h
        DW      00080h, 00000h
        DW      00100h, 00000h
        DW      00200h, 00000h
        DW      00400h, 00000h
        DW      00800h, 00000h
        DW      01000h, 00000h
        DW      02000h, 00000h
        DW      04000h, 00000h
        DW      08000h, 00000h
; Advance a remainder by 4 zero bytes.
        DW      06765h, 0B8BCh
        DW      0C88Bh, 0AA09h
        DW      09757h, 08F62h
        DW      028EFh, 0C5B4h
        DW      0579Fh, 05019h
        DW      0AF3Eh, 0A032h
        DW      0583Dh, 09B14h
        DW      0B63Bh, 0ED59h
        DW      06A37h, 001C2h
        DW      0D46Eh, 00384h
        DW      0A8DCh, 00709h
        DW      051B8h, 00E13h
        DW      0A370h, 01C26h
        DW      046E0h, 0384Dh
        DW      08DC0h, 0709Ah
        DW      01B80h, 0E135h
        DW      03141h, 0191Bh
        DW      06282h, 03236h
        DW      0C504h, 0646Ch
        DW      08A08h, 0C8D9h
        DW      01251h, 04AC2h
        DW      024A2h, 09584h
        DW      04F05h, 0F079h
        DW      0984Bh, 03B83h
        DW      03096h, 07707h
        DW      0612Ch, 0EE0Eh
        DW      0C419h, 0076Dh
        DW      08832h, 00EDBh
        DW      01064h, 01DB7h
        DW      020C8h, 03B6Eh
        DW      04190h, 076DCh
        DW      08320h, 0EDB8h
; Advance a remainder by 8 zero bytes.
        DW      0009Eh, 0CCAAh
        DW      0077Dh, 04225h
        DW      00EFAh, 0844Ah
        DW      01BB5h, 0D3E5h
        DW      0312Bh, 07CBBh
        DW      06256h, 0F976h
        DW      0C2EDh, 0299Dh
        DW      085DAh, 0533Bh
        DW      00BB4h, 0A677h
        DW      01129h, 0979Fh
        DW      02413h, 0F44Fh
        DW      04E67h, 033EFh
        DW      09CCEh, 067DEh
        DW      0399Ch, 0CFBDh
        DW      07579h, 0440Bh
        DW      0EAF2h, 08816h
        DW      0D3A5h, 0CB5Ch
        DW      0A10Bh, 04DC8h
        DW      04216h, 09B91h
        DW      0826Dh, 0EC53h
        DW      0029Bh, 003D6h
        DW      00536h, 007ACh
        DW      00A6Ch, 00F58h
        DW      014D8h, 01EB0h
        DW      029B0h, 03D60h
        DW      05360h, 07AC0h
        DW      0A6C0h, 0F580h
        DW      04BC1h, 03070h
        DW      09782h, 060E0h
        DW      02F04h, 0C1C1h
        DW      05849h, 058F3h
        DW      0B092h, 0B1E6h
; Advance a remainder by 16 zero bytes.
        DW      09191h, 0AE68h
        DW      02563h, 087A0h
        DW      04C87h, 0D431h
        DW      09F4Fh, 07313h
        DW      03E9Eh, 0E627h
        DW      07B7Dh, 0173Fh
        DW      0F6FAh, 02E7Eh
        DW      0EDF4h, 05CFDh
        DW      0DBE8h, 0B9FBh
        DW      0B191h, 0A886h
        DW      06563h, 08A7Ch
        DW      0CC87h, 0CF89h
        DW      09F4Fh, 04462h
        DW      03E9Eh, 088C5h
        DW      07B7Dh, 0CAFBh
        DW      0F0BBh, 04E87h
        DW      0E176h, 09D0Fh
        DW      0C4ADh, 0E16Eh
        DW      08F1Bh, 019ACh
        DW      01E36h, 03359h
        DW      03C6Ch, 066B2h
        DW      078D8h, 0CD64h
        DW      0F7F1h, 041B9h
        DW      0EFE2h, 08373h
        DW      0D985h, 0DD96h
        DW      0B54Bh, 0605Ch
        DW      06A96h, 0C0B9h
        DW      0D36Dh, 05A03h
        DW      0A6DAh, 0B407h
        DW      04BF5h, 0B37Eh
        DW      091ABh, 0BD8Dh
        DW      02517h, 0A06Ah
; Advance a remainder by 32 zero bytes.
        DW      005AAh, 0F1DAh
        DW      00D15h, 038C5h
        DW      01A2Ah, 0718Ah
        DW      03454h, 0E314h
        DW      06EE9h, 01D59h
        DW      0DDD2h, 03AB2h
        DW      0BBA4h, 07565h
        DW      07748h, 0EACBh
        DW      0E8D1h, 00EE7h
        DW      0D1A2h, 01DCFh
        DW      0A344h, 03B9Fh
        DW      04688h, 0773Fh
        DW      08D10h, 0EE7Eh
        DW      01C61h, 0078Ch
        DW      038C2h, 00F18h
        DW      07184h, 01E30h
        DW      0E308h, 03C60h
        DW      0C610h, 078C1h
        DW      08C20h, 0F183h
        DW      01E01h, 03876h
        DW      03C02h, 070ECh
        DW      07804h, 0E1D8h
        DW      0F649h, 018C1h
        DW      0EC92h, 03183h
        DW      0D924h, 06307h
        DW      0B248h, 0C60Fh
        DW      062D1h, 0576Eh
        DW      0C5A2h, 0AEDCh
        DW      08D05h, 086C8h
        DW      01C4Bh, 0D6E0h
        DW      03ED7h, 076B1h
        DW      07DAEh, 0ED62h
; Advance a remainder by 64 zero bytes.
        DW      02D95h, 08F35h
        DW      05D6Bh, 0C51Bh
        DW      0BC97h, 05147h
        DW      0792Eh, 0A28Fh
        DW      0F41Dh, 09E6Fh
        DW      0EE7Bh, 0E7AEh
        DW      0DAB7h, 0142Ch
        DW      0B56Eh, 02859h
        DW      06ADCh, 050B3h
        DW      0D5B8h, 0A166h
        DW      0AD31h, 099BCh
        DW      05C23h, 0E808h
        DW      0BE07h, 00B61h
        DW      07C0Eh, 016C3h
        DW      0F81Ch, 02D86h
        DW      0F038h, 05B0Dh
        DW      0E070h, 0B61Bh
        DW      0C6A1h, 0B746h
        DW      08B03h, 0B5FCh
        DW      01047h, 0B088h
        DW      026CFh, 0BA61h
        DW      04BDFh, 0AFB3h
        DW      091FFh, 08417h
        DW      025BFh, 0D35Eh
        DW      04D3Fh, 07DCDh
        DW      09A7Eh, 0FB9Ah
        DW      032BDh, 02C44h
        DW      0657Ah, 05888h
        DW      0CAF4h, 0B110h
        DW      093A9h, 0B950h
        DW      02113h, 0A9D0h
        DW      04467h, 088D1h
; Advance a remainder by 128 zero bytes.
        DW      0F533h, 033FFh
        DW      0EA66h, 067FFh
        DW      0D4CCh, 0CFFFh
        DW      0AFD9h, 0448Eh
        DW      05FB2h, 0891Dh
        DW      0B925h, 0C94Bh
        DW      0740Bh, 049E6h
        DW      0E816h, 093CCh
        DW      0D66Dh, 0FCE8h
        DW      0AA9Bh, 022A0h
        DW      05536h, 04541h
        DW      0AA6Ch, 08A82h
        DW      05299h, 0CE74h
        DW      0A373h, 04799h
        DW      046E6h, 08F33h
        DW      08B8Dh, 0C517h
        DW      0115Bh, 0515Eh
        DW      022B6h, 0A2BCh
        DW      0432Dh, 09E09h
        DW      0801Bh, 0E763h
        DW      00677h, 015B6h
        DW      00CEEh, 02B6Ch
        DW      019DCh, 056D8h
        DW      033B8h, 0ADB0h
        DW      06131h, 08011h
        DW      0C423h, 0DB53h
        DW      08E07h, 06DD6h
        DW      01C0Eh, 0DBADh
        DW      03E5Dh, 06C2Bh
        DW      07CBAh, 0D856h
        DW      0FF35h, 06BDDh
        DW      0FE6Ah, 0D7BBh
; Advance a remainder by 256 zero bytes.
        DW      071CBh, 0CE33h
        DW      0E5D7h, 04717h
        DW      0CBAEh, 08E2Fh
        DW      0911Dh, 0C72Eh
        DW      0247Bh, 0552Ch
        DW      048F6h, 0AA58h
        DW      097ADh, 08FC1h
        DW      0291Bh, 0C4F2h
        DW      05477h, 05295h
        DW      0A8EEh, 0A52Ah
        DW      0579Dh, 09124h
        DW      0A97Bh, 0F939h
        DW      054B7h, 02902h
        DW      0A96Eh, 05204h
        DW      052DCh, 0A409h
        DW      0A3F9h, 09363h
        DW      041B3h, 0FDB6h
        DW      08527h, 0201Dh
        DW      00A4Eh, 0403Bh
        DW      0149Ch, 08076h
        DW      02F79h, 0DB9Dh
        DW      058B3h, 06C4Bh
        DW      0B166h, 0D896h
        DW      0648Dh, 06A5Ch
        DW      0C91Ah, 0D4B8h
        DW      09475h, 07200h
        DW      028EAh, 0E401h
        DW      05795h, 01373h
        DW      0AF2Ah, 026E6h
        DW      05E54h, 04DCDh
        DW      0BCA8h, 09B9Ah
        DW      07F11h, 0EC44h

        INCLUDE "live_overlay_v9_watch.inc"
        INCLUDE "live_overlay_v9_triggers.inc"
        INCLUDE "live_overlay_v10_groups.inc"
        INCLUDE "live_overlay_v10_blip.inc"
        IF      $ > 0CFF80h
        ERROR   "the V10 code overruns the pops defaults"
        ENDIF

; The flashed pops settings: off, 4.0 s, -23.625 deg, a real AFR of 7.0 (70), 4,000 rpm, 70.5 deg C, 3.0 deg TPS, 20 km/h.
        ORG     0CFF80h
pops_defaults:
        DB      000h, 028h, 000h, 046h, 07Dh, 09Eh, 01Eh, 014h
; 139 pops stabs: a 2.0 s load, +5.0 deg TPS stabs of 150 ms, 250 ms gaps, a 480 rpm rise once stabbing.
stab_defaults:
        DB      014h, 019h, 00Fh, 019h, 00Fh, 000h, 000h, 000h
; 140 downshift blip: off, braking or tap, 30 km/h, 5,984 rpm, 1.5 s, 30.0 and 5.0 deg TPS, +20.25 deg.
blip_defaults:
        DB      000h, 003h, 01Eh, 0BBh, 00Fh, 096h, 019h, 075h
; 141 its gears and tap: 5th-4th, 4th-3rd, 3rd-2nd on, 2nd-1st off; 9.8 deg PVS within 0.4 s; a 150 ms lead.
gears_defaults:
        DB      001h, 001h, 001h, 000h, 015h, 028h, 00Fh, 000h
; 142 its ratios, 0.5 rpm per km/h: 1st 113.0, 2nd 67.0, 3rd 44.5, 4th 33.5.
ratio_defaults:
        DB      0E2h, 086h, 059h, 043h, 000h, 000h, 000h, 000h

data_end:

        INCLUDE "live_overlay_v6_feature_hooks.inc"

        ORG     0AC650h
        CALLS   0Ch, e_map0 & 0FFFFh
        ORG     0AC3A4h
        CALLS   0Ch, e_map1 & 0FFFFh
        ORG     0BD40Ah
        CALLS   0Ch, e_map2 & 0FFFFh
        ORG     0B3B48h
        CALLS   0Ch, e_map3 & 0FFFFh
        ORG     0B3BA2h
        CALLS   0Ch, e_map3 & 0FFFFh
        ORG     0B3B74h
        CALLS   0Ch, e_map4 & 0FFFFh
        ORG     0B3BEAh
        CALLS   0Ch, e_map4 & 0FFFFh
        ORG     0AAF52h
        CALLS   0Ch, e_map5 & 0FFFFh
        ORG     0AAF14h
        CALLS   0Ch, e_map6 & 0FFFFh
        ORG     0AAF3Eh
        CALLS   0Ch, e_map7 & 0FFFFh
        ORG     0AAF00h
        CALLS   0Ch, e_map8 & 0FFFFh
        ORG     0AAF2Ah
        CALLS   0Ch, e_map9 & 0FFFFh
        ORG     0B3A10h
        CALLS   0Ch, e_map10 & 0FFFFh
        ORG     0B3B84h
        CALLS   0Ch, e_map11 & 0FFFFh
        ORG     0B3BFAh
        CALLS   0Ch, e_map11 & 0FFFFh
        ORG     0B39E4h
        CALLS   0Ch, e_map12 & 0FFFFh
        ORG     0B3B58h
        CALLS   0Ch, e_map13 & 0FFFFh
        ORG     0B3BB2h
        CALLS   0Ch, e_map13 & 0FFFFh
        ORG     0B3A00h
        CALLS   0Ch, e_map14 & 0FFFFh
        ORG     0B39D4h
        CALLS   0Ch, e_map15 & 0FFFFh
        ORG     0D709Ah
        CALLS   0Ch, e_map16 & 0FFFFh
        ORG     0DD9AAh
        CALLS   0Ch, e_map17 & 0FFFFh
        ORG     0C3B72h
        CALLS   0Ch, e_map18 & 0FFFFh
        ORG     0D87A2h
        CALLS   0Ch, e_map19 & 0FFFFh
        ORG     0D70FCh
        CALLS   0Ch, e_map20 & 0FFFFh
        ORG     0C3C4Ch
        CALLS   0Ch, e_map21 & 0FFFFh
        ORG     0C3B12h
        CALLS   0Ch, e_map22 & 0FFFFh
        ORG     0C3C60h
        CALLS   0Ch, e_map23 & 0FFFFh
        ORG     0B10BCh
        CALLS   0Ch, e_map24 & 0FFFFh
        ORG     0C3A24h
        CALLS   0Ch, e_map25 & 0FFFFh
        ORG     0DB882h
        CALLS   0Ch, e_map26 & 0FFFFh
        ORG     0C3AE2h
        CALLS   0Ch, e_map27 & 0FFFFh
        ORG     0C3A82h
        CALLS   0Ch, e_map28 & 0FFFFh
        ORG     0C3AB2h
        CALLS   0Ch, e_map29 & 0FFFFh
        ORG     09D7D6h
        CALLS   0Ch, e_map30 & 0FFFFh
        ORG     09D7C8h
        CALLS   0Ch, e_map31 & 0FFFFh
        ORG     0DDAF4h
        CALLS   0Ch, e_map32 & 0FFFFh
        ORG     0DDB0Ch
        CALLS   0Ch, e_map33 & 0FFFFh
        ORG     0B5DFEh
        CALLS   0Ch, e_map34 & 0FFFFh
        ORG     0B5DE2h
        CALLS   0Ch, e_map35 & 0FFFFh
        ORG     0AD4CEh
        CALLS   0Ch, e_map36 & 0FFFFh
        ORG     0AD542h
        CALLS   0Ch, e_map37 & 0FFFFh
        ORG     0AD508h
        CALLS   0Ch, e_map38 & 0FFFFh
        ORG     090290h
        CALLS   0Ch, e_map39 & 0FFFFh
        ORG     090280h
        CALLS   0Ch, e_map40 & 0FFFFh
        ORG     091CFEh
        CALLS   0Ch, e_map41 & 0FFFFh
        ORG     0AC65Eh
        CALLS   0Ch, e_map42 & 0FFFFh
        ORG     0AC3B4h
        CALLS   0Ch, e_map43 & 0FFFFh
        ORG     0AC684h
        CALLS   0Ch, e_map44 & 0FFFFh
        ORG     0DD894h
        CALLS   0Ch, e_map45 & 0FFFFh
        ORG     0DD85Ch
        CALLS   0Ch, e_map46 & 0FFFFh
        ORG     0DD8A6h
        CALLS   0Ch, e_map47 & 0FFFFh
        ORG     0B31DCh
        CALLS   0Ch, e_map48 & 0FFFFh
        ORG     0B3224h
        CALLS   0Ch, e_map48 & 0FFFFh
        ORG     0D85D4h
        CALLS   0Ch, e_map48 & 0FFFFh
        ORG     0D862Ch
        CALLS   0Ch, e_map48 & 0FFFFh
        ORG     0DC040h
        CALLS   0Ch, e_map49 & 0FFFFh
        ORG     0DC0F6h
        CALLS   0Ch, e_map49 & 0FFFFh
        ORG     0DC026h
        CALLS   0Ch, e_map50 & 0FFFFh
        ORG     0DC0DCh
        CALLS   0Ch, e_map50 & 0FFFFh
        ORG     0DC092h
        CALLS   0Ch, e_map51 & 0FFFFh
        ORG     0DC146h
        CALLS   0Ch, e_map52 & 0FFFFh
        ORG     0C519Ah
        CALLS   0Ch, e_map53 & 0FFFFh
        ORG     0C52AAh
        CALLS   0Ch, e_map53 & 0FFFFh
        ORG     0C517Ah
        CALLS   0Ch, e_map54 & 0FFFFh
        ORG     0C528Ah
        CALLS   0Ch, e_map54 & 0FFFFh
        ORG     0B5D2Eh
        CALLS   0Ch, e_map55 & 0FFFFh
        ORG     0B5D1Eh
        CALLS   0Ch, e_map56 & 0FFFFh
        ORG     0D7166h
        CALLS   0Ch, e_map57 & 0FFFFh
        ORG     0BCF24h
        CALLS   0Ch, e_map58 & 0FFFFh
        ORG     0BCF44h
        CALLS   0Ch, e_map59 & 0FFFFh
        ORG     0CB138h
        CALLS   0Ch, e_map60 & 0FFFFh
        ORG     0CB184h
        CALLS   0Ch, e_map61 & 0FFFFh
        ORG     0CB128h
        CALLS   0Ch, e_map62 & 0FFFFh
        ORG     0CB174h
        CALLS   0Ch, e_map63 & 0FFFFh
        ORG     09EC8Eh
        CALLS   0Ch, e_map64 & 0FFFFh
        ORG     09ECC0h
        CALLS   0Ch, e_map65 & 0FFFFh
        ORG     09E962h
        CALLS   0Ch, e_map66 & 0FFFFh
        ORG     09E876h
        CALLS   0Ch, e_map67 & 0FFFFh
        ORG     098B3Eh
        CALLS   0Ch, e_map68 & 0FFFFh
        ORG     098B2Eh
        CALLS   0Ch, e_map69 & 0FFFFh
        ORG     098B1Eh
        CALLS   0Ch, e_map70 & 0FFFFh
        ORG     098B0Eh
        CALLS   0Ch, e_map71 & 0FFFFh
        ORG     0970D2h
        CALLS   0Ch, e_map72 & 0FFFFh
        ORG     097110h
        CALLS   0Ch, e_map73 & 0FFFFh
        ORG     0A9F9Ch
        CALLS   0Ch, e_map74 & 0FFFFh
        ORG     0C9C38h
        CALLS   0Ch, e_map74 & 0FFFFh
        ORG     0D5CEEh
        CALLS   0Ch, e_map74 & 0FFFFh
        ORG     0D5F10h
        CALLS   0Ch, e_map74 & 0FFFFh
        ORG     0C4E40h
        CALLS   0Ch, e_map75 & 0FFFFh
        ORG     0CB3D8h
        CALLS   0Ch, e_map76 & 0FFFFh
        ORG     0C4C34h
        CALLS   0Ch, e_map77 & 0FFFFh
        ORG     0C4D46h
        CALLS   0Ch, e_map78 & 0FFFFh
        ORG     0C4C42h
        CALLS   0Ch, e_map79 & 0FFFFh
        ORG     0C4C6Ah
        CALLS   0Ch, e_map79 & 0FFFFh
        ORG     0C4D54h
        CALLS   0Ch, e_map80 & 0FFFFh
        ORG     0C4D7Ch
        CALLS   0Ch, e_map80 & 0FFFFh
        ORG     0C4C78h
        CALLS   0Ch, e_map81 & 0FFFFh
        ORG     0C4CA0h
        CALLS   0Ch, e_map81 & 0FFFFh
        ORG     0C4D8Ah
        CALLS   0Ch, e_map82 & 0FFFFh
        ORG     0C4DB2h
        CALLS   0Ch, e_map82 & 0FFFFh
        ORG     0C4CAEh
        CALLS   0Ch, e_map83 & 0FFFFh
        ORG     0C4CD6h
        CALLS   0Ch, e_map83 & 0FFFFh
        ORG     0C4DC0h
        CALLS   0Ch, e_map84 & 0FFFFh
        ORG     0C4DE8h
        CALLS   0Ch, e_map84 & 0FFFFh
        ORG     0C4CE4h
        CALLS   0Ch, e_map85 & 0FFFFh
        ORG     0C4D0Ch
        CALLS   0Ch, e_map85 & 0FFFFh
        ORG     0C4DF6h
        CALLS   0Ch, e_map86 & 0FFFFh
        ORG     0C4E1Eh
        CALLS   0Ch, e_map86 & 0FFFFh
        ORG     0CABD4h
        CALLS   0Ch, e_map87 & 0FFFFh
        ORG     0CA922h
        CALLS   0Ch, e_map88 & 0FFFFh
        ORG     0C583Ah
        CALLS   0Ch, e_map89 & 0FFFFh
        ORG     0C586Ah
        CALLS   0Ch, e_map90 & 0FFFFh
        ORG     0C58D6h
        CALLS   0Ch, e_map91 & 0FFFFh
        ORG     0C58A6h
        CALLS   0Ch, e_map92 & 0FFFFh
        ORG     0C552Ah
        CALLS   0Ch, e_map93 & 0FFFFh
        ORG     0C54F0h
        CALLS   0Ch, e_map94 & 0FFFFh
        ORG     0C553Ah
        CALLS   0Ch, e_map95 & 0FFFFh
        ORG     0C54E0h
        CALLS   0Ch, e_map96 & 0FFFFh
        ORG     0C34BCh
        CALLS   0Ch, e_map97 & 0FFFFh
        ORG     0C34CCh
        CALLS   0Ch, e_map98 & 0FFFFh
        ORG     0CB352h
        CALLS   0Ch, e_map99 & 0FFFFh
        ORG     0CB37Eh
        CALLS   0Ch, e_map100 & 0FFFFh
        ORG     0B48D4h
        CALLS   0Ch, e_map101 & 0FFFFh
        ORG     0CAD7Ah
        CALLS   0Ch, e_map102 & 0FFFFh
        ORG     0C3C1Ch
        CALLS   0Ch, e_map103 & 0FFFFh
        ORG     091C00h
        CALLS   0Ch, e_map104 & 0FFFFh
        ORG     091B92h
        CALLS   0Ch, e_map105 & 0FFFFh
        ORG     091BCAh
        CALLS   0Ch, e_map105 & 0FFFFh
        ORG     091E4Ch
        CALLS   0Ch, e_map106 & 0FFFFh
        ORG     091DF2h
        CALLS   0Ch, e_map107 & 0FFFFh
        ORG     0D9B6Ch
        CALLS   0Ch, e_map108 & 0FFFFh
        ORG     0BD41Ah
        CALLS   0Ch, e_map109 & 0FFFFh
        ORG     0CA912h
        CALLS   0Ch, e_map110 & 0FFFFh
        ORG     0CABC4h
        CALLS   0Ch, e_map111 & 0FFFFh
        ORG     0CA97Ch
        CALLS   0Ch, e_map112 & 0FFFFh
        ORG     0CAD6Ah
        CALLS   0Ch, e_map113 & 0FFFFh
        ORG     0CB342h
        CALLS   0Ch, e_map114 & 0FFFFh
        ORG     0CB36Eh
        CALLS   0Ch, e_map115 & 0FFFFh
        ORG     0CB3C8h
        CALLS   0Ch, e_map116 & 0FFFFh
        ORG     0D240Eh
        CALLS   0Ch, e_map117 & 0FFFFh
        ORG     0D2FBAh
        CALLS   0Ch, e_map118 & 0FFFFh
        ORG     0D302Ch
        CALLS   0Ch, e_map119 & 0FFFFh
        ORG     0D3AEAh
        CALLS   0Ch, e_map120 & 0FFFFh
        ORG     0D3ABCh
        CALLS   0Ch, e_map121 & 0FFFFh
        ORG     0D256Eh
        CALLS   0Ch, e_map122 & 0FFFFh
        ORG     0D24BEh
        CALLS   0Ch, e_map123 & 0FFFFh
        ORG     0AA3ACh
        CALLS   0Ch, hard_mode & 0FFFFh
        ORG     0D8C88h
        CALLS   0Ch, hard_gears & 0FFFFh
        ORG     0D8CFCh
        CALLS   0Ch, hard_hysteresis & 0FFFFh
        ORG     0D8D18h
        CALLS   0Ch, hard_refresh & 0FFFFh
        ORG     09FC8Ah
        CALLS   0Ch, handler & 0FFFFh
        ORG     0EE3E2h
        DW      hk & 0FFFFh, 000Ch
        ORG     0EFE00h
        DW      (data_end - 1) & 0FFFFh, 000Ch

        ORG 0C372Eh
        CALLS 0Ch, word_map127 & 0FFFFh
        ORG 0C36BEh
        CALLS 0Ch, word_map128 & 0FFFFh
        ORG 0C374Eh
        CALLS 0Ch, word_map129 & 0FFFFh
        ORG 0AFEF0h
        CALLS 0Ch, word_map130 & 0FFFFh
        ORG 0AD89Eh
        CALLS 0Ch, word_map131 & 0FFFFh
        ORG 0AD84Eh
        CALLS 0Ch, word_map132 & 0FFFFh
        ORG 0AD862h
        CALLS 0Ch, word_map133 & 0FFFFh
        ORG 0AD876h
        CALLS 0Ch, word_map134 & 0FFFFh
        ORG 0AD88Ah
        CALLS 0Ch, word_map135 & 0FFFFh
        ORG 09B12Ah
        CALLS 0Ch, word_map136 & 0FFFFh
        ORG 09B018h
        CALLS 0Ch, word_map137 & 0FFFFh

        ORG     0AA7BCh
        JMPS    0Ch, pops_gate & 0FFFFh
        NOP
        NOP
        ORG     0AD424h
        CALLS   0Ch, pops_spark & 0FFFFh
        ORG     0BD434h
        JMPS    0Ch, pops_afr & 0FFFFh
        ORG     09B138h
        CALLS   0Ch, pops_throttle & 0FFFFh
        ORG     0BD3B2h
        JMPS    0Ch, pops_target & 0FFFFh
        ORG     0D022Ah
        JMPS    0Ch, watch_rd0 & 0FFFFh
        ORG     0D0262h
        JMPS    0Ch, watch_rd1 & 0FFFFh
        ORG     0D0454h
        JMPS    0Ch, watch_rd2 & 0FFFFh
        ORG     0D042Eh
        JMPS    0Ch, watch_rd3 & 0FFFFh
        ORG     0D0168h
        JMPS    0Ch, watch_rd4 & 0FFFFh
        ORG     0D029Ah
        JMPS    0Ch, watch_rd5 & 0FFFFh
        ORG     0D03D2h
        JMPS    0Ch, watch_rd6 & 0FFFFh
        ORG     0D043Ah
        JMPS    0Ch, watch_rd7 & 0FFFFh
        ORG     0D04B0h
        JMPS    0Ch, watch_ds0 & 0FFFFh
        ORG     0D05C8h
        JMPS    0Ch, watch_ds1 & 0FFFFh
        ORG     0D0776h
        JMPS    0Ch, watch_ds2 & 0FFFFh
        ORG     0D089Eh
        JMPS    0Ch, watch_ds3 & 0FFFFh
        ORG     0D09BCh
        JMPS    0Ch, watch_ds4 & 0FFFFh
        ORG     0D0B60h
        JMPS    0Ch, watch_ds5 & 0FFFFh
        ORG     0D0C88h
        JMPS    0Ch, watch_ds6 & 0FFFFh
        ORG     0D0EC2h
        JMPS    0Ch, watch_ds7 & 0FFFFh
        ORG     0D1092h
        JMPS    0Ch, watch_ds8 & 0FFFFh
        ORG     0D06E4h
        JMPS    0Ch, watch_ds9 & 0FFFFh
        ORG     0D0AD6h
        JMPS    0Ch, watch_ds10 & 0FFFFh
        ORG     0D0DA4h
        JMPS    0Ch, watch_ds11 & 0FFFFh
        ORG     0D0E30h
        JMPS    0Ch, watch_ds12 & 0FFFFh
        ORG     0D0FE2h
        JMPS    0Ch, watch_ds13 & 0FFFFh
        ORG     0D1018h
        JMPS    0Ch, watch_ds14 & 0FFFFh
        ORG     0D1052h
        JMPS    0Ch, watch_ds15 & 0FFFFh

        ORG     0AAAA0h
        JMPS    0Ch, blip_reentry & 0FFFFh
        ORG     0BD4D4h
        JMPS    0Ch, pops_fuel & 0FFFFh
