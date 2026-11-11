using System.Text.Json;

namespace MS43Logger.Core.LiveTuning;

/// <summary>Live-map IDs and flash locations, including reserved entry 139.</summary>
public static class OverlayCatalogue
{
    public static IReadOnlyList<OverlayMap> BaseMaps { get; } = LoadCatalogue();

    private static IReadOnlyList<OverlayMap> WordMaps { get; } = Array.AsReadOnly(new[]
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
    });

    public static IReadOnlyList<OverlayMap> Maps { get; } = Array.AsReadOnly(BaseMaps
        .Concat(OverlaySettings.LaunchMaps)
        .Concat(WordMaps)
        .Append(new OverlayMap(OverlaySettings.Pops, "pops", "Pops & bangs", OverlaySettings.DefaultsAddress, 8, false, OverlaySettings.PopsGroup))
        .Append(new OverlayMap(OverlaySettings.Reserved, "reserved_139", "Reserved", 0xCFF88, 8, false, Reserved: true))
        .Append(new OverlayMap(OverlaySettings.Blip, "blip", "Downshift blip", 0xCFF90, 8, false, OverlaySettings.BlipGroup))
        .Append(new OverlayMap(OverlaySettings.BlipGears, "blip_gears", "Downshift blip: gears and tap", 0xCFF98, 8, false, OverlaySettings.BlipGearsGroup))
        .Append(new OverlayMap(OverlaySettings.BlipRatios, "blip_ratios", "Downshift blip: gear ratios", 0xCFFA0, 8, false, OverlaySettings.BlipRatiosGroup))
        .ToArray());

    private static IReadOnlyList<OverlayMap> LoadCatalogue()
    {
        var assembly = typeof(OverlayCatalogue).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("live_overlay_byte_maps.json")))!;
        using var doc = JsonDocument.Parse(stream);
        return Array.AsReadOnly(doc.RootElement.EnumerateArray().Select(e =>
        {
            var id = e.GetProperty("id").GetInt32();
            if (id < 5) return OverlayContract.Maps[id] with { RicherOnly = false };
            var name = e.GetProperty("name").GetString()!;
            var group = name.Contains("cam_") ? "VANOS" : name.Contains("iga_") ? "Timing" : "Other maps";
            return new OverlayMap(id, name, group, e.GetProperty("address").GetInt32(), e.GetProperty("bytes").GetInt32(), false);
        }).ToArray());
    }

    private static OverlayMap Word(int id, string name, string group, int address, int bytes) =>
        new(id, name, group, address, bytes, false, ElementBits: 16);
}
