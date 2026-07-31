using MS43Logger.Core;
using MS43Logger.Core.Tuning;

namespace MS43Logger.Core.Tests;

/// <summary>Replayed logger frames against the car bin: which map the ECU is using, and with what weight.</summary>
public class MapUseTests
{
    private const int State = 0x00, Status1 = 0x01, Vanos = 0x02, Speed = 0x06, Fraction = 0x10, Index = 0x11, Octane = 0x40;

    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly MapUseRules Rules = MapUseRules.LoadBundled("43X001")!;

    private static CalibrationDocument Car() => CalibrationDocument.Open(TestData.Find(TestData.CarBin)!, TestData.Find(TestData.Xdf)!);

    private static MapUse Use(BinImage image, string title, params (int Offset, byte Value)[] bytes)
    {
        var table = Ms43xTables.Value.Single(t => t.Title == title);
        return MapUseBinding.Resolve(Rules, image, table.Z.Data.Address!.Value).Evaluate(Ms43x, LivePayloads.Build(Ms43x, bytes));
    }

    private static readonly Lazy<IReadOnlyList<XdfTable>> Ms43xTables = new(() => Car().Xdf.Tables);

    private static string Text(string title, params (int, byte)[] bytes) => Use(Car().Image, title, bytes).Text;

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VeBlendFollowsActiveVe()
    {
        Assert.Equal("VE 3 72 % · VE 4 28 %", Text("ip_map_ve_3__map__n", (Fraction, 0x47), (Index, 0x03)));
        Assert.Equal("VE 4 28 % · VE 3 72 %", Text("ip_map_ve_4__map__n", (Fraction, 0x47), (Index, 0x03)));
        Assert.Equal("Not in use · VE 3 72 % · VE 4 28 %", Text("ip_map_ve_1__map__n", (Fraction, 0x47), (Index, 0x03)));
        Assert.Equal("In use", Text("ip_map_ve_3__map__n", (Fraction, 0x00), (Index, 0x03)));
        Assert.Equal("In use", Text("ip_map_ve_8__map__n", (Fraction, 0x00), (Index, 0x08)));
        Assert.Equal("In use", Text("ip_map_ve_8__map__n", (Fraction, 0x00), (Index, 0x00)));
        Assert.Equal("In use", Text("ip_map_ve_8__map__n", (Fraction, 0x80), (Index, 0x08)));
        Assert.Equal("VE 1 <1 % · VE 2 99 %", Text("ip_map_ve_1__map__n", (Fraction, 0xFE), (Index, 0x01)));

        var use = Use(Car().Image, "ip_map_ve_3__map__n", (Fraction, 0x47), (Index, 0x03));
        Assert.Equal(MapUseState.InUse, use.State);
        Assert.Equal((255 - 71) / 256.0, use.Shares.Single(share => share.Label == "VE 3").Weight, 12);
        Assert.Equal(71 / 256.0, use.Shares.Single(share => share.Label == "VE 4").Weight, 12);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void PartLoadIgnitionBlendsByOctane()
    {
        Assert.Equal("RON98 72 % · RON91 28 %", Text("ip_iga_ron98_pl__n__maf", (State, 0x04), (Octane, 71)));
        Assert.Equal("RON91 28 % · RON98 72 %", Text("ip_iga_ron91_pl__n__maf", (State, 0x04), (Octane, 71)));
        Assert.Equal("Not in use · RON98 72 % · RON91 28 %", Text("ip_iga_e85_pl__n__maf", (State, 0x04), (Octane, 71)));
        Assert.Equal("In use", Text("ip_iga_ron98_pl__n__maf", (State, 0x04), (Octane, 0)));
        Assert.Equal("Not in use · RON98 100 %", Text("ip_iga_ron91_pl__n__maf", (State, 0x04), (Octane, 0)));
        Assert.Equal(MapUseState.NotInUse, Use(Car().Image, "ip_iga_ron91_pl__n__maf", (State, 0x04)).State);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void LoadStateSwitchesIdleAndPartLoad()
    {
        Assert.Equal("Not in use", Text("ip_iga_ron98_pl__n__maf", (State, 0x02)));
        Assert.Equal("In use", Text("ip_iga_ron98_is__n__maf", (State, 0x02)));
        Assert.Equal("Not in use · RON98 100 %", Text("ip_iga_e85_is__n__maf", (State, 0x02)));
        foreach (byte start in new byte[] { 0x01, 0x00 })
        {
            Assert.Equal("Not in use", Text("ip_iga_ron98_pl__n__maf", (State, start)));
            Assert.Equal("Not in use", Text("ip_iga_ron98_is__n__maf", (State, start)));
        }

        foreach (byte overrun in new byte[] { 0x08, 0x10 })
        {
            Assert.Equal("Selection unknown", Text("ip_iga_ron98_pl__n__maf", (State, overrun)));
            Assert.Equal("Not in use", Text("ip_iga_ron98_is__n__maf", (State, overrun)));
        }

        Assert.Equal("Selection unknown", Text("ip_iga_ron98_pl__n__maf", (State, 0x06)));
        Assert.Equal("Selection unknown", Text("ip_iga_ron98_is__n__maf", (State, 0x06)));
        Assert.Contains("Full-load enrichment", Use(Car().Image, "ip_iga_ron98_is__n__maf", (State, 0x06)).Detail);
        Assert.Equal("In use", Text("ip_af_target_ron98__maf__n", (State, 0x06)));
        Assert.Equal("Not in use", Text("ip_af_target_ron98__maf__n", (State, 0x04)));
        Assert.Equal("In use", Text("ip_iga_optm_ron98__n__maf", (State, 0x00)));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VanosIdleNeedsActiveVanos()
    {
        Assert.Equal("In use", Text("ip_cam_sp_ron98_in_is__n__maf_ivvt", (State, 0x02), (Vanos, 0x02)));
        Assert.Equal("In use", Text("ip_cam_sp_ron98_ex_is__n__maf_ivvt", (State, 0x02), (Vanos, 0x02)));
        Assert.Equal("Not in use", Text("ip_cam_sp_ron98_in_is__n__maf_ivvt", (State, 0x02), (Vanos, 0x01)));
        Assert.Equal("Not in use", Text("ip_cam_sp_ron98_in_is__n__maf_ivvt", (State, 0x04), (Vanos, 0x02)));
        Assert.Equal("Selection unknown", Text("ip_cam_sp_ron98_in_is__n__maf_ivvt", (State, 0x06), (Vanos, 0x02)));
        Assert.Equal("Selection unknown", Text("ip_cam_sp_ron98_ex_is__n__maf_ivvt", (State, 0x06), (Vanos, 0x02)));
        Assert.Equal("In use", Text("ip_cam_sp_ron98_in_pl__n__maf_ivvt", (State, 0x04), (Vanos, 0x02)));
        Assert.Equal("Not in use · RON98 100 %", Text("ip_cam_sp_e85_in_pl__n__maf_ivvt", (State, 0x04), (Vanos, 0x02)));
        Assert.Equal("Not in use", Text("ip_cam_sp_ron98_in_pl__n__maf_ivvt", (State, 0x02), (Vanos, 0x02)));
        Assert.Equal("Not in use", Text("ip_cam_sp_ron98_in_fl__n", (State, 0x06), (Vanos, 0x03)));
    }

    private static MapUse LoadUse(BinImage image, string title, byte state, long rpm, long e33b)
    {
        var table = Ms43xTables.Value.Single(t => t.Title == title);
        var payload = LivePayloads.Build(Ms43x, new (int, byte)[] { (State, state), (Vanos, 0x02) }, ("N", rpm), ("MAFTI", e33b << 8 | 0x80));
        return MapUseBinding.Resolve(Rules, image, table.Z.Data.Address!.Value).Evaluate(Ms43x, payload);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VanosFullLoadIsLoadAboveTheCurve()
    {
        var image = Car().Image;
        foreach (byte state in new byte[] { 0x04, 0x06 })
        {
            Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", state, 3000, 31).Text);
            Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_ex_fl__n", state, 3000, 31).Text);
            Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_in_pl__n__maf_ivvt", state, 3000, 31).Text);
            Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_ex_pl__n__maf_ivvt", state, 3000, 31).Text);
            Assert.Equal("Not in use · RON98 100 %", LoadUse(image, "ip_cam_sp_e85_in_fl__n", state, 3000, 31).Text);

            Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", state, 3000, 30).Text);
            Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_ex_fl__n", state, 3000, 30).Text);
            Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_pl__n__maf_ivvt", state, 3000, 30).Text);
            Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_ex_pl__n__maf_ivvt", state, 3000, 30).Text);
            Assert.Equal("Not in use · RON98 100 %", LoadUse(image, "ip_cam_sp_e85_ex_pl__n__maf_ivvt", state, 3000, 30).Text);
        }

        var use = LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 3000, 31);
        Assert.Equal(MapUseState.InUse, use.State);
        Assert.Equal(1.0, use.Shares.Single().Weight);
        Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x02, 3000, 31).Text);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VanosFullLoadReadsTheCurveFromTheOpenBin()
    {
        // Breakpoint 3 of axis 0x74B6C is 2200 rpm and breakpoint 4 is 2500; the curve is not interpolated, so 2200-2499 reads entry 3.
        var image = Car().Image.Clone();
        image.WriteElement(new XdfData(0x75D7F, 8, false, true, 16, 1, null), 3, 40);

        Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2300, 31).Text);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_pl__n__maf_ivvt", 0x04, 2300, 31).Text);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2300, 41).Text);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2499, 41).Text);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2199, 31).Text);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2500, 31).Text);
        Assert.Equal("Not in use", LoadUse(Car().Image, "ip_cam_sp_ron98_in_fl__n", 0x04, 2300, 30).Text);

        image.WriteElement(new XdfData(0x75D7F, 8, false, true, 16, 1, null), 15, 255);
        Assert.Equal("In use", LoadUse(image, "ip_cam_sp_ron98_in_pl__n__maf_ivvt", 0x04, 8000, 255).Text);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void VanosLoadChoiceIsUnknownWhileItIsHeld()
    {
        var image = Car().Image;
        foreach (byte held in new byte[] { 0x08, 0x10, 0x01, 0x00 })
        {
            foreach (var title in new[] { "ip_cam_sp_ron98_in_fl__n", "ip_cam_sp_ron98_ex_pl__n__maf_ivvt" })
            {
                var use = LoadUse(image, title, held, 3000, 31);
                Assert.Equal("Selection unknown", use.Text);
                Assert.Contains("FD38.14", use.Detail);
            }
        }

        Assert.Contains("Overrun", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x08, 3000, 31).Detail);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AnUnreadableCurveMakesTheLoadChoiceUnknown()
    {
        var image = Car().Image.Clone();
        image.WriteElement(new XdfData(0x74B6C, 16, false, true, 1, 1, null), 0, 0);

        var use = LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x04, 3000, 31);
        Assert.Equal("Selection unknown", use.Text);
        Assert.Contains("0x75D7F", use.Detail);
        Assert.Equal("Not in use", LoadUse(image, "ip_cam_sp_ron98_in_fl__n", 0x02, 3000, 31).Text);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void CatHeatingIsRuledOutWhenTheBitIsClear()
    {
        foreach (var title in new[] { "ip_iga_ch__n__maf", "ip_iga_ch_cat_var__n__maf", "ip_iga_ch_is__n", "ip_iga_ch_2__n__pvs_av", "ip_ti_ch__n__maf", "ip_fac_ivvt_ch__n__maf_ivvt" })
        {
            Assert.Equal("Not in use", Text(title, (Status1, 0x00), (State, 0x04)));
        }

        Assert.Equal("Possibly in use", Text("ip_iga_ch__n__maf", (Status1, 0x08), (State, 0x04)));
        Assert.Equal("Not in use", Text("ip_iga_ch__n__maf", (Status1, 0x08), (State, 0x02)));
        Assert.Equal("Not in use", Text("ip_iga_ch__n__maf", (Status1, 0x08), (State, 0x06)));
        Assert.Equal("Not in use", Text("ip_iga_ch_is__n", (Status1, 0x08), (State, 0x04), (Speed, 30)));
        Assert.Equal("Possibly in use", Text("ip_iga_ch_is__n", (Status1, 0x08), (State, 0x04), (Speed, 0)));
        Assert.Equal("Possibly in use", Text("ip_iga_ch_is__n", (Status1, 0x08), (State, 0x02), (Speed, 30)));
        Assert.Equal("Possibly in use", Text("ip_iga_ch_2__n__pvs_av", (Status1, 0x08)));
        Assert.Equal("Not in use", Text("ip_fac_ivvt_ch__n__maf_ivvt", (Status1, 0x08)));
        Assert.Equal("Possibly in use", Text("ip_fac_ivvt_ch__n__maf_ivvt", (Status1, 0x0A)));
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void FlexFuelOnMakesBlendsUnknown()
    {
        var image = Car().Image.Clone();
        image.WriteElement(new XdfData(0x70DF8, 8, false, true, 1, 1, null), 0, 1);

        var use = Use(image, "ip_iga_ron98_pl__n__maf", (State, 0x04), (Octane, 71));
        Assert.Equal("Selection unknown", use.Text);
        Assert.Equal(MapUseState.Unknown, use.State);
        Assert.Contains("flex fuel", use.Detail);
        Assert.Equal("Not in use", Use(image, "ip_iga_ron98_pl__n__maf", (State, 0x02)).Text);
        Assert.Equal("In use", Use(image, "ip_map_ve_3__map__n", (Index, 0x03)).Text);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void AnE85FactorOutsideTheBlendIsUnknown()
    {
        var image = Car().Image.Clone();
        image.WriteElement(new XdfData(0x70A11, 8, false, true, 1, 1, null), 0, 0x70);

        Assert.Equal("Selection unknown", Use(image, "ip_iga_ron98_pl__n__maf", (State, 0x04)).Text);
    }

    [RealFileFact(TestData.CarBin, TestData.Xdf)]
    public void NoRuleSaysSelectionUnknown()
    {
        var image = Car().Image;
        var plain = Use(image, "id_t_tco_puc_deacc__tco", (State, 0x04));
        Assert.Equal("Selection unknown", plain.Text);
        Assert.Equal(MapUseState.Unknown, plain.State);
        Assert.Contains("No rule", plain.Detail);

        var leftOut = Use(image, "ip_iga_aj_ron98__n__maf", (State, 0x04));
        Assert.Equal("Selection unknown", leftOut.Text);
        Assert.Contains("FD18.15", leftOut.Detail);

        Assert.Equal("Selection unknown", MapUseBinding.Resolve(null, image, 0x77315).Evaluate(Ms43x, new byte[73]).Text);
    }
}
