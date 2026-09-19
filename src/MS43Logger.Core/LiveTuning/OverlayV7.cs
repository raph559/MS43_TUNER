namespace MS43Logger.Core.LiveTuning;

public static class OverlayV7
{
    public const int Version = 7, IndexBytes = 20;

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(OverlayV6.Maps.Concat(new[]
    {
        Word(127, "ip_n_sp_is__tco", "Idle speed", 0x7B674, 12),
        Word(128, "ip_isapwm__tco", "Idle airflow", 0x7A2E0, 12),
        Word(129, "ip_acin_n_sp_is__tco", "Idle speed", 0x7955E, 12),
        Word(130, "ip_n_vim_tia__tia", "DISA", 0x7B680, 16),
        Word(131, "ip_t_puc_deacc_1__n__maf_mmv", "Overrun fuel cut", 0x7C400, 48),
        Word(132, "ip_t_puc_deacc_2__n__maf_mmv", "Overrun fuel cut", 0x7C430, 48),
        Word(133, "ip_t_puc_deacc_3__n__maf_mmv", "Overrun fuel cut", 0x7C460, 48),
        Word(134, "ip_t_puc_deacc_4__n__maf_mmv", "Overrun fuel cut", 0x7C490, 48),
        Word(135, "ip_t_puc_deacc_5__n__maf_mmv", "Overrun fuel cut", 0x7C4C0, 48),
        Word(136, "ip_tps_sp_pvs__pvs__n", "Pedal / throttle", 0x7E4DC, 288),
        Word(137, "ip_tps_sp_pvs_add_low_n__n_dif__", "Pedal / throttle", 0x7E5FC, 32),
    }).ToArray());

    private static OverlayMap Word(int id, string name, string group, int address, int bytes) =>
        new(id, name, group, address, bytes, false, ElementBits: 16);
}
