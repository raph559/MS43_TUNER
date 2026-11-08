        ORG     0BFCF2h
        CALLS   0Ch, feature_lc_config & 0FFFFh
        ORG     0BFD24h
        CALLS   0Ch, feature_lc_speed & 0FFFFh
        ORG     0BFD16h
        CALLS   0Ch, feature_lc_pedal_enter & 0FFFFh
        ORG     0BFD2Eh
        CALLS   0Ch, feature_lc_pedal_exit & 0FFFFh
        ORG     0D8CA2h
        CALLS   0Ch, feature_lc_rpm & 0FFFFh
        ORG     0AC6C2h
        CALLS   0Ch, feature_lc_ign & 0FFFFh
        NOP
        NOP
        ORG     0BD43Ah
        CALLS   0Ch, feature_lc_afr & 0FFFFh
        NOP
        NOP
        ORG     0BFD44h
        CALLS   0Ch, feature_nls_config & 0FFFFh
        ORG     0BFD62h
        CALLS   0Ch, feature_nls_speed & 0FFFFh
        ORG     0BFD6Ch
        CALLS   0Ch, feature_nls_pedal_enter & 0FFFFh
        ORG     0BFD8Eh
        CALLS   0Ch, feature_nls_pedal_exit & 0FFFFh
        ORG     0BFD76h
        CALLS   0Ch, feature_nls_rpm & 0FFFFh
        ORG     0AC6CCh
        CALLS   0Ch, feature_nls_ign & 0FFFFh
        NOP
        NOP
        ORG     0BD444h
        CALLS   0Ch, feature_nls_afr & 0FFFFh
        NOP
        NOP
        ORG     0BFDA4h
        CALLS   0Ch, feature_ral_config & 0FFFFh
        ORG     0BFDBEh
        CALLS   0Ch, feature_ral_pedal_enter & 0FFFFh
        ORG     0BFDE8h
        CALLS   0Ch, feature_ral_pedal_exit & 0FFFFh
        ORG     0BFDC8h
        CALLS   0Ch, feature_ral_rpm & 0FFFFh
        ORG     0AC6D6h
        CALLS   0Ch, feature_ral_ign & 0FFFFh
        NOP
        NOP
        ORG     0BD44Eh
        CALLS   0Ch, feature_ral_afr & 0FFFFh
        NOP
        NOP
