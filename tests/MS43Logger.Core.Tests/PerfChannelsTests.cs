using MS43Logger.Core.Performance;

namespace MS43Logger.Core.Tests;

public class PerfChannelsTests
{
    // The presets and their bands are performance spec 6.1 to 6.4; later tasks build every run from them.
    [Fact]
    public void PresetsMatchTheSpec()
    {
        Assert.Equal(2000, PerfTestSpec.Pull(3).StartRpm);
        Assert.Equal(6400, PerfTestSpec.Pull(3).EndRpm);
        Assert.Equal(6954, PerfTestSpec.Pull(3, 6954).EndRpm);
        Assert.Equal((1500d, 6400d), (PerfTestSpec.Pull(2).StartRpm!.Value, PerfTestSpec.Pull(2).EndRpm!.Value));
        Assert.Equal((1500d, 4500d), (PerfTestSpec.Pull(4).StartRpm!.Value, PerfTestSpec.Pull(4).EndRpm!.Value));

        var knock = PerfTestSpec.KnockCheck();
        Assert.Equal(PerfTestKind.KnockCheck, knock.Kind);
        Assert.Equal(4, knock.Gear);
        Assert.Equal((1500d, 4500d), (knock.StartRpm!.Value, knock.EndRpm!.Value));

        Assert.Equal((60d, 100d), (PerfTestSpec.RollOn(3).StartKmh!.Value, PerfTestSpec.RollOn(3).EndKmh!.Value));
        Assert.Equal((80d, 120d), (PerfTestSpec.RollOn(4).StartKmh!.Value, PerfTestSpec.RollOn(4).EndKmh!.Value));
        Assert.Equal(200, PerfTestSpec.StandingStart().EndKmh);

        Assert.Equal("3rd pull", PerfTestSpec.Pull(3).ShortName);
        Assert.Equal("Roll-on 80 to 120", PerfTestSpec.RollOn(4).ShortName);
        Assert.Equal("pull-3", PerfTestSpec.Pull(3).Preset);
    }

    [Fact]
    public void EveryPresetHasItsKeyAndName()
    {
        var specs = new[]
        {
            PerfTestSpec.Pull(2), PerfTestSpec.Pull(3), PerfTestSpec.Pull(4), PerfTestSpec.KnockCheck(), PerfTestSpec.RollOn(3),
            PerfTestSpec.RollOn(4), PerfTestSpec.StandingStart(), PerfTestSpec.SpeedCalibration(), PerfTestSpec.LinkTest(), PerfTestSpec.Pedal(),
        };

        Assert.Equal(
            new[] { "pull-2", "pull-3", "pull-4", "knock-4", "rollon-3", "rollon-4", "standing", "calibration", "link", "pedal" },
            specs.Select(s => s.Preset));
        Assert.Equal(
            new[]
            {
                "2nd pull", "3rd pull", "4th pull", "Knock check", "Roll-on 60 to 100", "Roll-on 80 to 120", "Standing start",
                "Speed calibration", "Link test", "Pedal",
            },
            specs.Select(s => s.ShortName));
        Assert.Equal(PerfTestKind.Pull, PerfTestSpec.Pull(2).Kind);
        Assert.Equal(PerfTestKind.RollOn, PerfTestSpec.RollOn(3).Kind);
        Assert.Equal(3, PerfTestSpec.RollOn(3).Gear);
        Assert.Equal(0, PerfTestSpec.StandingStart().StartKmh);
        Assert.Null(PerfTestSpec.StandingStart().Gear);
        Assert.Equal("Roll-on 65 to 100", (PerfTestSpec.RollOn(3) with { StartKmh = 65 }).ShortName);
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfTestSpec.Pull(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfTestSpec.RollOn(2));
    }

    private static readonly AdxDefinition Ms43x = BundledDefinitions.Load("ms43x");
    private static readonly AdxDefinition B25 = BundledDefinitions.Load("b25");

    private static PerfFrame Frame(AdxDefinition adx, byte[] payload, TimeSpan at = default) =>
        PerfChannels.Read(adx, adx.DecodePayload(payload), payload, at);

    private static PerfFrame Frame(LiveSample sample, AdxDefinition adx) =>
        PerfChannels.Read(adx, sample.Values, sample.Payload.Span, sample.RequestedAt);

    private static (string, long)[] NoKnock() =>
        Enumerable.Range(1, 6).Select(cylinder => ($"IGAKNK{cylinder}", 128L)).ToArray();

    [Fact]
    public void Ms43xFrameReadsEveryChannel()
    {
        var payload = LivePayloads.Build(Ms43x, [(0x00, 0x80)],
        [
            .. NoKnock(),
            ("N", 5000 | 4 << 13), ("VS", 120), ("PVS", 213), ("TCO", 187), ("TOIL", 171), ("AMP", 48), ("IGAKNK2", 124),
            ("TPS", 100), ("TIA", 98), ("VB", 137), ("IGA1", 112), ("CAMAVIN", 64), ("CAMAVEX", 32), ("MAF", 2000),
            ("MAFTI", 10000), ("AFTARGET", 125), ("RONADFAC", 51), ("TIADFAC1", 130), ("TIADFAC2", 125),
        ]);

        var f = Frame(Ms43x, payload, TimeSpan.FromSeconds(1.5));

        Assert.Equal(5000, f.Rpm);
        Assert.Equal(4, f.Gear);
        Assert.Equal(120, f.SpeedKmh);
        Assert.Equal(99.84, f.Pedal!.Value, 2);
        Assert.Equal(46.9, f.Throttle!.Value, 1);
        Assert.Equal(91.7, f.CoolantC!.Value, 1);
        Assert.Equal(88.1, f.OilC!.Value, 1);
        Assert.Equal(25.2, f.IntakeC!.Value, 1);
        Assert.Equal(101.9, f.PressureKpa!.Value, 1);
        Assert.Equal(14.0, f.BatteryV!.Value, 1);
        Assert.Equal(6, f.Ignition.Count);
        Assert.Equal(30, f.Ignition[0]!.Value, 3);
        Assert.NotNull(f.IgnitionAvg);
        Assert.Equal(6, f.KnockRetard.Count);
        Assert.Equal(1.5, f.KnockRetard[1]!.Value, 3);
        Assert.Equal(0, f.KnockRetard[0]!.Value, 3);
        Assert.False(double.IsNegative(f.KnockRetard[0]!.Value));
        Assert.Equal(84, f.VanosIn!.Value, 3);
        Assert.Equal(-72, f.VanosEx!.Value, 3);
        Assert.Equal(500, f.MafKgh!.Value, 3);
        Assert.Equal(423.9, f.LoadMgStk!.Value, 1);
        Assert.Equal(12.5, f.AfrTarget!.Value, 3);
        Assert.Equal(0.2, f.RonAdaptation!.Value, 2);
        Assert.Equal(0.8, f.LongTermTrim1!.Value, 1);
        Assert.Equal(-1.2, f.LongTermTrim2!.Value, 1);
        Assert.True(f.Clutch);
        Assert.False(f.Brake);
        Assert.False(f.OverheatLamp);
        Assert.False(f.CatalystHeating);
        Assert.Equal(TimeSpan.FromSeconds(1.5), f.At);
    }

    // GEAR is byte 0x05 / 32 and carries N's high bits below the gear, so it reads 3.78 at 6400 rpm in 3rd.
    [Fact]
    public void GearIsFloored()
    {
        var payload = LivePayloads.Build(Ms43x, ("N", 6400 | 3 << 13));

        Assert.Equal(3.78, Ms43x.DecodePayload(payload).Single(v => v.Definition.Id == "GEAR").Value, 2);
        Assert.Equal(3, Frame(Ms43x, payload).Gear);
        Assert.Equal(6400, Frame(Ms43x, payload).Rpm);
        Assert.Equal(0, Frame(Ms43x, LivePayloads.Build(Ms43x, ("N", 750L))).Gear);
    }

    // Stock definitions log no knock per cylinder and no AFR target; those read as missing, never as zero.
    [Fact]
    public void StockFrameHasNoKnock()
    {
        var payload = LivePayloads.Build(B25, [(0x00, 0x10)],
            [("RPM", 3000), ("GEAR", 3), ("VEHICLESPEED", 67), ("HPA", 12288), ("RONADFACTOR", 128), ("COOLANTTEMP", 187)]);

        var f = Frame(B25, payload);

        Assert.Empty(f.KnockRetard);
        Assert.Null(f.AfrTarget);
        Assert.Null(f.LongTermTrim1);
        Assert.Null(f.LongTermTrim2);
        Assert.Equal(0.082921489 * 12288 / 10, f.PressureKpa!.Value, 6);
        Assert.Equal(100 / 255d * 128 / 100, f.RonAdaptation!.Value, 6);
        Assert.Equal(3000, f.Rpm);
        Assert.Equal(3, f.Gear);
        Assert.Equal(67, f.SpeedKmh);
        Assert.Equal(91.7, f.CoolantC!.Value, 1);
        Assert.Equal(6, f.Ignition.Count);
        Assert.All(f.Ignition, value => Assert.NotNull(value));
        Assert.NotNull(f.LoadMgStk);
        Assert.NotNull(f.VanosIn);
        Assert.True(f.Clutch);
        Assert.False(f.Brake);
        Assert.False(f.OverheatLamp);
        Assert.False(f.CatalystHeating);
    }

    [Fact]
    public void StatusBitsReadOnBothFamilies()
    {
        // ms43x: brake 0x00 & 0x40, catalyst heating 0x01 & 0x08, overheat lamp 0x01 & 0x80.
        var ms43x = Frame(Ms43x, LivePayloads.Build(Ms43x, [(0x00, 0x40), (0x01, 0x88)]));
        Assert.Equal((false, true, true, true), (ms43x.Clutch!.Value, ms43x.Brake!.Value, ms43x.OverheatLamp!.Value, ms43x.CatalystHeating!.Value));

        // Stock: brake 0x00 & 0x08, overheat lamp lv_teng_state 0x03 & 0x01, catalyst heating 0x03 & 0x80.
        var stock = Frame(B25, LivePayloads.Build(B25, [(0x00, 0x08), (0x03, 0x81)]));
        Assert.Equal((false, true, true, true), (stock.Clutch!.Value, stock.Brake!.Value, stock.OverheatLamp!.Value, stock.CatalystHeating!.Value));
    }

    // DSC's torque intervention: ms43x TQINTVASR or TQINTVMSR (0x0F & 0xE0 reads 0x20 or 0x40), the stock
    // STATE_TQ_INTV (0x03 & 0x02).
    [Fact]
    public void TorqueInterventionReadsOnBothFamilies()
    {
        Assert.True(Frame(Ms43x, LivePayloads.Build(Ms43x, [(0x0F, 0x20)])).TorqueIntervention);
        Assert.True(Frame(Ms43x, LivePayloads.Build(Ms43x, [(0x0F, 0x40)])).TorqueIntervention);
        // 0x60 is the torque limit, not DSC.
        Assert.False(Frame(Ms43x, LivePayloads.Build(Ms43x, [(0x0F, 0x60)])).TorqueIntervention);
        Assert.False(Frame(Ms43x, LivePayloads.Build(Ms43x, [(0x0F, 0x00)])).TorqueIntervention);
        Assert.True(Frame(B25, LivePayloads.Build(B25, [(0x03, 0x02)])).TorqueIntervention);
        Assert.False(Frame(B25, LivePayloads.Build(B25, [(0x03, 0x00)])).TorqueIntervention);
        Assert.Null(PerfChannels.Read(Ms43x, [], ReadOnlySpan<byte>.Empty, TimeSpan.Zero).TorqueIntervention);
    }

    // Injector duty and the DME's torque per bin, the malfunction light for a limited pull, and the
    // retries of the poll read on both families.
    [Fact]
    public void DutyTorqueLampAndRetriesRead()
    {
        var ms43x = LivePayloads.Build(Ms43x, [(0x01, 0x40)], ("N", 3000L), ("TQITQR", 250L));
        var f = PerfChannels.Read(Ms43x, Ms43x.DecodePayload(ms43x), ms43x, TimeSpan.Zero, retries: 2);
        Assert.Equal(Ms43x.DecodePayload(ms43x).Single(v => v.Definition.Id == "INJDC").Value, f.InjectorDuty);
        Assert.Equal(250, f.DmeTorque);
        Assert.True(f.MalfunctionLamp);
        Assert.Equal(2, f.Retries);
        Assert.False(Frame(Ms43x, LivePayloads.Build(Ms43x, ("N", 3000L))).MalfunctionLamp);
        Assert.Null(Frame(Ms43x, LivePayloads.Build(Ms43x, ("N", 3000L))).Retries);

        // Stock: injection time 8 ms at 3000 rpm is 20 % duty; the EML light is 0x03 & 0x20.
        var stock = LivePayloads.Build(B25, [(0x03, 0x20)], ("RPM", 3000L), ("FUELPULSE1", 1500L), ("ENGINETORQUE", 300L));
        var s = Frame(B25, stock);
        Assert.Equal(20, s.InjectorDuty!.Value, 0.1);
        Assert.Equal(300, s.DmeTorque);
        Assert.True(s.MalfunctionLamp);
    }

    [Fact]
    public void AValueTheSampleLacksIsMissing()
    {
        var payload = LivePayloads.Build(Ms43x, ("N", 3000L), ("TCO", 187L));
        var values = Ms43x.DecodePayload(payload)
            .Where(v => v.Definition.Id != "TCO")
            .Select(v => v.Definition.Id == "N" ? v with { Value = double.NaN } : v)
            .ToList();

        var f = PerfChannels.Read(Ms43x, values, payload, TimeSpan.Zero);

        Assert.Null(f.CoolantC);
        Assert.Null(f.Rpm);
        Assert.NotNull(f.OilC);

        var empty = PerfChannels.Read(Ms43x, [], ReadOnlySpan<byte>.Empty, TimeSpan.Zero);
        Assert.Null(empty.Rpm);
        Assert.Null(empty.Clutch);
        Assert.Equal(6, empty.KnockRetard.Count);
        Assert.All(empty.KnockRetard, value => Assert.Null(value));
    }

    [Fact]
    public void MissingNamesWhatATestLacks()
    {
        Assert.Contains("knock per cylinder", PerfChannels.Missing(B25, PerfTestKind.KnockCheck));
        Assert.Equal(new[] { "knock per cylinder" }, PerfChannels.Missing(B25, PerfTestKind.KnockCheck));
        foreach (var kind in Enum.GetValues<PerfTestKind>())
        {
            Assert.Empty(PerfChannels.Missing(Ms43x, kind));
            if (kind != PerfTestKind.KnockCheck)
            {
                Assert.Empty(PerfChannels.Missing(B25, kind));
            }
        }
    }
}
