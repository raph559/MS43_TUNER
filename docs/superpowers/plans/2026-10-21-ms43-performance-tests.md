# Performance tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Performance tab that runs tuning tests from the live DS2 samples (pulls, knock check, roll-ons, standing starts, speed calibration, link test), warns on a cold engine, and saves, lists and compares named results.

**Architecture:** Pure Core code in `src/MS43Logger.Core/Performance/` turns decoded samples into a frame (`PerfFrame`), runs a test's state machine (`PerfRun`), analyses a window with a pure function (`RunAnalysis`), stores results as JSON (`PerfResultStore`) with the tune's identity (`TuneIdentity`), and compares two results by recomputing both (`PerfCompare`). The app adds a warm-up banner to Live, two chart controls, a `PerformanceViewModel` fed by `MainWindowViewModel.SampleShown`, and a `PerformanceView` as a fourth header tab; the old `PerformanceWindow` goes.

**Tech Stack:** C#/.NET 10, Avalonia 11.3.12, System.Text.Json, System.IO.Compression, xUnit, Avalonia.Headless.XUnit.

**Spec:** `docs/superpowers/specs/2026-10-21-ms43-performance-tests-design.md` (read it with this plan; section numbers below are the spec's). Design system: `docs/superpowers/specs/2026-10-20-ms43-restyle-system.md`.

## Global Constraints

- Never write ECU flash; tests never send DS2 requests of their own (they read `SampleShown` samples only).
- No commits and no pushes: the owner commits on request. Never stage README.md, `.superpowers/`, artifacts or testdata.
- Run only the test classes a task names (`dotnet test <project> --filter "FullyQualifiedName~ClassA|FullyQualifiedName~ClassB"`); the full suite runs once at the end, in the background.
- Tests must never touch `%AppData%\MS43Tuner`: every store or settings path comes from `AppSettings.Folder` (tests use temp folders, `TestShell.NewFolder()`).
- UI copy: sentence case; no em or en dash; "…" never "..."; U+2212 only in "B − A"; ranges as "0 to 100 km/h"; units after a space ("6.84 s", "118 kW"); `n/a` for a missing value. `GateTests.UiTextHasNoDashesOrOldGlyphs` enforces it.
- Numbers in the data font with tabular figures (`data*` classes, `NumberText`, `ReadingText`); one primary button per screen; orange (`Now`) only for live marks; red (`Warn`) only with an icon and text.
- New C# follows the file it sits beside: `ViewModelBase.SetField`, `/// <summary>` on public members whose meaning is not obvious, short why-comments citing the spec section ("(performance spec 5)").
- Thresholds (spec 5): coolant cold below 80 °C, clears at ≥ 80 °C held 10 s, returns below 75 °C; oil the same with amber; oil not reported when ≤ −40 or ≥ 150 °C or within 2 °C while coolant rose ≥ 20 °C; coolant hot above 115 °C (clears below 110), oil hot above 130 °C (clears below 125); overheat lamp bit is danger.
- Full throttle (spec 4.4): pedal ≥ 90 % of the calibrated full-pedal value; "pedal below 30 %" is 30 % of that value; pull-type tests cannot arm until the pedal is calibrated.
- Gear is `floor(GEAR)` (spec 4.1); 0 means neutral or unknown.
- Every crossing time is a linear interpolation between the two samples around the level (spec 4.6).

## Review Focus

1. **A run interrupted by a disconnect or a tab switch**: switching tabs keeps the run; a disconnect ends it as incomplete with what it measured, and the warm-up state resets. Pinned in Task 6 (`LinkLostEndsARunAsIncomplete`) and Task 11 (`RunSurvivesSwitchingTabs`).
2. **A definition without a channel** (stock b25: no `IGAKNK*`, no `AFTARGET`; any definition without oil): the test says what is missing and cannot arm, never shows zeros. Pinned in Task 3 (`StockFrameHasNoKnock`) and Task 11 (`KnockCheckIsOffOnStockDefinitions`).
3. **A corrupt, foreign or newer results file in the folder**: the list skips it with a reason and still shows the others. Pinned in Task 7 (`BadFilesNeverBreakTheList`).
4. **Bursty sample timing** (a 400 ms retry gap, several samples at the same instant, non-monotonic stamps): crossings and derivatives stay finite and the run is flagged, never crashes. Pinned in Task 5 (`DerivativeSurvivesDuplicateTimes`) and Task 6 (`GapFlagsTheRun`).
5. **Comparing results made with different car values or bands**: Compare recomputes both with the current values and uses the shared rpm range. Pinned in Task 8 (`CompareRecomputesWithCurrentCarValues`, `CompareUsesTheSharedBand`).

---

## File map

Core (`src/MS43Logger.Core/`):
- Modify `LiveLogger.cs` (sample times), `AdxDefinition.cs` (bitmask decoding), `BundledDefinitions.cs` (definition hash).
- Create `Performance/PerfModel.cs` (shared records and enums), `Performance/PerfChannels.cs` (sample to frame), `Performance/WarmUpMonitor.cs`, `Performance/RunMath.cs`, `Performance/RunAnalysis.cs`, `Performance/PerfRun.cs` (state machine), `Performance/TuneIdentity.cs`, `Performance/PerfResult.cs` (stored shape), `Performance/PerfResultStore.cs`, `Performance/PerfCompare.cs`.

Core tests (`tests/MS43Logger.Core.Tests/`): modify `LiveLoggerTests.cs`, `LoggerCoreTests.cs`, `LivePayloads.cs`; create `SyntheticRuns.cs` (also linked into the app tests), `PerfChannelsTests.cs`, `WarmUpMonitorTests.cs`, `RunMathTests.cs`, `PerfRunTests.cs`, `PerfResultStoreTests.cs`, `PerfCompareTests.cs`.

App (`src/MS43Logger.AvaloniaApp/`):
- Modify `MainWindowViewModel.cs` (gear floor, warm-up, remove old performance code), `AppSettings.cs`, `ShellViewModel.cs`, `MainWindow.axaml(.cs)`, `Views/LiveView.axaml(.cs)`, `Theme/Tokens.axaml`, `Theme/Controls.axaml`, `Theme/Motion.axaml`, `Controls/CurveChart.cs`, `MS43Logger.AvaloniaApp.Tests.csproj` (link `SyntheticRuns.cs`).
- Create `Controls/ChartScale.cs`, `Controls/RunChart.cs`, `Controls/CylinderBars.cs`, `WarmUpBannerViewModel.cs`, `PerformanceViewModel.cs`, `PerformanceRunViewModel.cs`, `PerformanceResultsViewModel.cs`, `Views/PerformanceView.axaml(.cs)`.
- Delete `PerformanceWindow.axaml(.cs)`.

App tests: modify `ReadoutBandTests.cs`, `ThemeTokensTests.cs`, `ChartTests.cs`, `AppSettingsTests.cs`, `LiveStateTests.cs`, `HeaderTests.cs`, `ShellTests.cs`, `MotionTests.cs`, `DialogTests.cs`, `GateScreens.cs`, `GateClickThrough.cs`, `GateTests.cs`; create `WarmUpBannerTests.cs`, `RunChartTests.cs`, `PerformanceRunTests.cs`, `PerformanceResultsTests.cs`, `PerformanceViewTests.cs`.

Docs: modify `docs/superpowers/specs/2026-10-20-ms43-restyle-system.md` (tabs, Caution token, charts) in Task 13.

---

### Task 1: Sample times, bitmask decoding, definition hash

**Files:**
- Modify: `src/MS43Logger.Core/LiveLogger.cs:10,63-90`, `src/MS43Logger.Core/AdxDefinition.cs:32-48,581`, `src/MS43Logger.Core/BundledDefinitions.cs`, `tests/MS43Logger.Core.Tests/LivePayloads.cs:161`
- Test: `tests/MS43Logger.Core.Tests/LiveLoggerTests.cs`, `tests/MS43Logger.Core.Tests/LoggerCoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record LiveSample(DateTimeOffset Time, long Index, IReadOnlyList<DecodedValue> Values, ReadOnlyMemory<byte> Payload = default, TimeSpan RequestedAt = default, TimeSpan RepliedAt = default);` (times on the run's monotonic clock, zero at `RunAsync`).
  - `AdxBitmaskDefinition.IsSet(ReadOnlySpan<byte> payload) : bool` (throws `InvalidOperationException` when the payload is shorter than `PacketOffset + 1`); added as a body member, the positional members stay as they are.
  - `AdxDefinition.FindBitmask(string id) : AdxBitmaskDefinition?` (case-insensitive, first id wins).
  - `AdxDefinition.DecodeBitmask(string id, ReadOnlySpan<byte> payload) : bool?` (null when the definition lacks it or the payload is short; never throws).
  - `BundledDefinitions.Sha256(string variant) : string` (lower-case hex of the embedded file).
  - `LivePayloads.Build` sizes frames from `adx.GetMonitorExchange().Response.PacketSize` (82 for b22/b25/b30, 73 for ms43x).

- [ ] **Step 1: Write the failing tests**

```csharp
// LiveLoggerTests
[Fact] public async Task SamplesCarryRequestAndReplyTimes()
{   // FakeSource steps that Thread.Sleep(5) before returning Payload(…), three samples
    Assert.All(samples, s => Assert.True(s.RequestedAt < s.RepliedAt));
    Assert.True(samples[0].RepliedAt <= samples[1].RequestedAt);
    Assert.True(samples[1].RepliedAt <= samples[2].RequestedAt);
}
// LoggerCoreTests (ms43x = BundledDefinitions.Load("ms43x"), payload = new byte[73])
[Fact] public void BitmasksDecodeFromThePayload()
{   payload[0x01] = 0x04;  Assert.True(ms43x.DecodeBitmask("WUPSTATE", payload)); Assert.False(ms43x.DecodeBitmask("CHSTATE", payload));
    payload[0x02] = 0x02;  Assert.True(ms43x.DecodeBitmask("VANOSACTIVE", payload)); Assert.False(ms43x.DecodeBitmask("VANOSPASSIVE", payload));
    payload[0x02] = 0x00;  Assert.True(ms43x.DecodeBitmask("VANOSPASSIVE", payload));
    payload[0x00] = 0x80;  Assert.True(ms43x.DecodeBitmask("cSsTaTe", payload));
    Assert.Null(ms43x.DecodeBitmask("NOSUCHBIT", payload));
    Assert.Null(ms43x.DecodeBitmask("WUPSTATE", new byte[1]));
    var b30 = BundledDefinitions.Load("b30"); var stock = new byte[82]; stock[0] = 0x10;
    Assert.True(b30.DecodeBitmask("CLUTCHSWITCH", stock));
}
[Fact] public void BundledDefinitionsHaveAStableHash()
{   var a = BundledDefinitions.Sha256("ms43x");
    Assert.Matches("^[0-9a-f]{64}$", a); Assert.Equal(a, BundledDefinitions.Sha256("MS43X")); Assert.NotEqual(a, BundledDefinitions.Sha256("b25")); }
[Fact] public void StockPayloadsAreEightyTwoBytes() => Assert.Equal(82, LivePayloads.Build(BundledDefinitions.Load("b25"), ("RPM", 3000L)).Length);
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~LiveLoggerTests|FullyQualifiedName~LoggerCoreTests"`
Expected: build errors (`RequestedAt`, `DecodeBitmask`, `Sha256` not defined).

- [ ] **Step 3: Implement**
  - In `LiveLogger.Loop`, take `requestedAt = clock.Elapsed` before `PollPayload()` and `repliedAt` after it; record the rate with `repliedAt`; pass both to `LiveSample`.
  - Bitmasks: build an `OrdinalIgnoreCase` dictionary in the constructor with `TryAdd`.
  - `Sha256`: `SHA256.HashData(stream)` over the same manifest resource `Load` reads; `Convert.ToHexStringLower`.

- [ ] **Step 4: Run them and see them pass**, plus the classes they live beside.

Run: `dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~LiveLoggerTests|FullyQualifiedName~LoggerCoreTests|FullyQualifiedName~LiveCursor"`
Expected: PASS.

---

### Task 2: Shared performance model

**Files:**
- Create: `src/MS43Logger.Core/Performance/PerfModel.cs`
- Test: `tests/MS43Logger.Core.Tests/PerfChannelsTests.cs` (the presets test lives here)

**Interfaces:**
- Produces (exact shapes; every later task uses them):

```csharp
namespace MS43Logger.Core.Performance;

public enum PerfTestKind { Pull, KnockCheck, RollOn, StandingStart, SpeedCalibration, LinkTest, Pedal }
public enum FlagSeverity { Info, Suspect, Invalid, Cold }
public enum RunStage { Waiting, Running, Finished, Cancelled }

/// One test with its options. Preset is a stable key ("pull-3", "pull-2", "pull-4", "knock-4", "rollon-3", "rollon-4",
/// "standing", "calibration", "link", "pedal"); the bands are spec 6.1 to 6.4.
public sealed record PerfTestSpec(PerfTestKind Kind, string Preset, int? Gear,
    double? StartRpm = null, double? EndRpm = null, double? StartKmh = null, double? EndKmh = null)
{
    public static PerfTestSpec Pull(int gear, double? endRpm = null);   // 2: 1500 to 6400; 3: 2000 to endRpm ?? 6400; 4: 1500 to 4500
    public static PerfTestSpec KnockCheck();                           // gear 4, 1500 to 4500
    public static PerfTestSpec RollOn(int gear);                        // 3: 60 to 100 km/h; 4: 80 to 120 km/h
    public static PerfTestSpec StandingStart();                         // 0 to 200 km/h
    public static PerfTestSpec SpeedCalibration();
    public static PerfTestSpec LinkTest();                              // 30 s
    public static PerfTestSpec Pedal();
    public string ShortName { get; }  // "3rd pull", "Knock check", "Roll-on 80 to 120", "Standing start", "Speed calibration", "Link test", "Pedal"
}

public sealed record Figure(string Key, string Label, double? Value, string Unit, int Decimals);
public sealed record Trace(string Key, string Label, string XUnit, string YUnit, IReadOnlyList<double> X, IReadOnlyList<double> Y);
public sealed record RunFlag(string Code, FlagSeverity Severity, string Message);
public sealed record RunFigures(IReadOnlyList<Figure> Figures, IReadOnlyList<Trace> Traces, IReadOnlyList<RunFlag> Flags, string Headline);
public sealed record ReadyCheck(string Label, bool Met, string Detail);

/// The values the power estimate and the speed tests use (spec 7, 6.5).
public sealed record CarValues(double MassKg, string TyreSize, bool Automatic,
    double WheelInertia = 4.5, double EngineInertia = 0.20, double CdA = 0.60, double Crr = 0.011);
public sealed record AnalysisInputs(CarValues Car, IReadOnlyDictionary<int, double> KmhPerRpm, double FullPedal);

/// Coolant, oil (null when not reported), intake air, pressure (kPa), battery, RON adaptation (0..1), long-term trims.
public sealed record Conditions(double? CoolantC, double? OilC, double? IntakeC, double? PressureKpa, double? BatteryV,
    double? RonAdaptation, double? LongTermTrim1, double? LongTermTrim2);
```

- [ ] **Step 1: Write the failing test** `PresetsMatchTheSpec`: `Pull(3).StartRpm == 2000`, `Pull(3).EndRpm == 6400`, `Pull(3, 6954).EndRpm == 6954`, `Pull(2)` is 1500 to 6400, `Pull(4)` is 1500 to 4500, `KnockCheck()` is gear 4, 1500 to 4500, `RollOn(3)` is 60 to 100, `RollOn(4)` is 80 to 120, `StandingStart().EndKmh == 200`, `Pull(3).ShortName == "3rd pull"`, `RollOn(4).ShortName == "Roll-on 80 to 120"`, `Pull(3).Preset == "pull-3"`.
- [ ] **Step 2:** run `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~PerfChannelsTests`; expected build error.
- [ ] **Step 3:** implement `PerfModel.cs` with the shapes above.
- [ ] **Step 4:** same command; expected PASS.

---

### Task 3: From a sample to a frame

**Files:**
- Create: `src/MS43Logger.Core/Performance/PerfChannels.cs`, `tests/MS43Logger.Core.Tests/SyntheticRuns.cs`
- Test: `tests/MS43Logger.Core.Tests/PerfChannelsTests.cs`

**Interfaces:**
- Consumes: Task 1 (`DecodeBitmask`, `LiveSample.RequestedAt`), Task 2.
- Produces:

```csharp
/// One sample in the tests' terms (spec 4.3b). Missing channels are null; Ignition and KnockRetard have six entries
/// (null where missing) or are empty when the definition has none. KnockRetard is positive degrees of retard.
public sealed record PerfFrame(TimeSpan At, double? Rpm, double? SpeedKmh, int? Gear, double? Pedal, double? Throttle,
    double? CoolantC, double? OilC, double? IntakeC, double? PressureKpa, double? BatteryV,
    IReadOnlyList<double?> Ignition, double? IgnitionAvg, IReadOnlyList<double?> KnockRetard,
    double? VanosIn, double? VanosEx, double? MafKgh, double? LoadMgStk, double? AfrTarget,
    double? RonAdaptation, double? LongTermTrim1, double? LongTermTrim2,
    bool? Clutch, bool? Brake, bool? OverheatLamp, bool? CatalystHeating);

public static class PerfChannels
{
    public static PerfFrame Read(AdxDefinition definition, IReadOnlyList<DecodedValue> values, ReadOnlySpan<byte> payload, TimeSpan at);
    /// Ids of what the test needs that this definition lacks, in the user's words: "engine speed", "vehicle speed",
    /// "gear", "pedal", "coolant temperature", "knock per cylinder".
    public static IReadOnlyList<string> Missing(AdxDefinition definition, PerfTestKind kind);
}
```

- `SyntheticRuns` (test helper, namespace `MS43Logger.Core.Tests`): `public static IReadOnlyList<LiveSample> Pull(AdxDefinition ms43x, int gear, double fromRpm, double toRpm, double seconds, double rate = 30, double kmhPer1000 = 22.3, double coolantC = 92, double oilC = 88, double pedalPercent = 100, double knockCyl4 = 0)` builds ms43x payloads with `LivePayloads.Build` (gear in `N`'s high bits as `rpm | gear << 13`; `VS` = rpm × kmhPer1000 / 1000 rounded; `PVS` raw for the pedal with full = 100 °PVS; `TCO`, `TOIL` raw by their equations; `IGAKNK4` raw = 128 − retard / 0.375), times `RequestedAt = i / rate` s, preceded by 1 s held at `fromRpm` with the pedal at 10 %, then the pedal at `pedalPercent` and rpm rising linearly from `fromRpm` to `toRpm` over `seconds`. Also `Idle(ms43x, seconds, rate, coolantC, oilC)` and `Standing(ms43x, seconds0to100, rate)` (2 s stopped, then speed rising linearly to 100 km/h, rpm in 1st gear).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Ms43xFrameReadsEveryChannel()   // a payload built with N 5000 in 4th, VS 120, PVS 213 raw, TCO 92 °C, TOIL 88 °C, AMP raw 48 (101.9 kPa), IGAKNK2 raw 124 (1.5° retard), CSSTATE bit set
{   Assert.Equal(5000, f.Rpm); Assert.Equal(4, f.Gear); Assert.Equal(120, f.SpeedKmh);
    Assert.Equal(101.9, f.PressureKpa!.Value, 1); Assert.Equal(1.5, f.KnockRetard[1]!.Value, 3); Assert.Equal(0, f.KnockRetard[0]!.Value, 3);
    Assert.True(f.Clutch); Assert.Equal(6, f.Ignition.Count); Assert.Equal(TimeSpan.FromSeconds(1.5), f.At); }
[Fact] public void GearIsFloored()                  // N 6400 in 3rd: GEAR decodes 3.78 → 3
[Fact] public void StockFrameHasNoKnock()           // b25 payload: KnockRetard empty, AfrTarget null, PressureKpa = HPA / 10, RonAdaptation = RONADFACTOR / 100
[Fact] public void MissingNamesWhatATestLacks()     // Missing(b25, KnockCheck) contains "knock per cylinder"; Missing(ms43x, Pull) is empty
[Fact] public void SyntheticPullCrossesItsBand()    // SyntheticRuns.Pull(ms43x, 3, 1700, 6500, 6): first frame 1700 rpm at 10 % pedal, last frame 6500, gear 3 throughout
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~PerfChannelsTests`; expected build errors.
- [ ] **Step 3:** implement. Look values up by id in an `OrdinalIgnoreCase` dictionary built per sample; the id pairs are spec 4.3b; bits via `DecodeBitmask` with `CSSTATE`/`CLUTCHSWITCH`, `BLSSTATE`/`BRAKELIGHTSWIT`, `TENGSTATE`, `CHSTATE`/`FD96.5`.
- [ ] **Step 4:** same command; expected PASS.

---

### Task 4: Warm-up monitor

**Files:**
- Create: `src/MS43Logger.Core/Performance/WarmUpMonitor.cs`
- Test: `tests/MS43Logger.Core.Tests/WarmUpMonitorTests.cs`

**Interfaces:**
- Consumes: Task 3 `PerfFrame`.
- Produces:

```csharp
public enum WarmUpLevel { Info, Caution, Danger }
/// Code: "coolant-cold", "oil-cold", "oil-unreported", "coolant-hot", "oil-hot", "overheat".
public sealed record WarmUpBanner(WarmUpLevel Level, string Code, string Text);
public sealed class WarmUpMonitor
{
    public void Push(PerfFrame frame);
    public void Reset();
    public IReadOnlyList<WarmUpBanner> Banners { get; }   // danger first, then caution, then info
    public bool CoolantCold { get; }
    public bool OilCold { get; }
    public bool OilUnreported { get; }
    public event EventHandler? Changed;                     // raised only when Banners changes
}
```

- [ ] **Step 1: Write the failing tests** (frames built directly as `PerfFrame` with only the temperatures and `At` set)

```csharp
[Fact] public void ColdCoolantIsDanger()
{   m.Push(F(0, coolant: 64, oil: 60));
    var b = Assert.Single(m.Banners, x => x.Code == "coolant-cold");
    Assert.Equal(WarmUpLevel.Danger, b.Level);
    Assert.Equal("Coolant 64 °C: engine not at operating temperature (80 °C). Do not use full throttle; results will not be valid.", b.Text);
    Assert.Equal("Oil 60 °C: not at working temperature yet (80 °C).", Assert.Single(m.Banners, x => x.Code == "oil-cold").Text);
    Assert.Equal("coolant-cold", m.Banners[0].Code); }
[Fact] public void WarmAtTheFirstReadingNeedsNoWait()      // first frame coolant 92, oil 88 → no banner
[Fact] public void ColdClearsAfterTenSecondsAtEighty()     // 79 at t0, 80 at t1..t10.9 → still cold; 80 at t11 → cleared
[Fact] public void HysteresisHoldsBetweenSeventyFiveAndEighty() // warm, then 77 → still warm; 74 → cold
[Fact] public void OilThatNeverMovesIsNotReported()        // oil 40 while coolant 50 → 72 over 600 s → "oil-unreported", Info, "Oil temperature not reported by this ECU.", no "oil-cold"
[Fact] public void ImplausibleOilIsNotReported()           // oil -48 → oil-unreported
[Fact] public void HotAndOverheat()                        // coolant 117 → "coolant-hot" Caution "Coolant 117 °C: running hot."; OverheatLamp true → "overheat" Danger "The ECU reports engine overheating."
[Fact] public void ChangedFiresOnlyOnChange()              // two identical cold frames → Changed raised once
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~WarmUpMonitorTests`; expected build errors.
- [ ] **Step 3:** implement the rules of the Global Constraints. Temperatures in texts have 0 decimals (`NumberFormat` lives in the app, so format with `"0"` and `CultureInfo.InvariantCulture`).
- [ ] **Step 4:** same command; expected PASS.

---

### Task 5: Run math

**Files:**
- Create: `src/MS43Logger.Core/Performance/RunMath.cs`
- Test: `tests/MS43Logger.Core.Tests/RunMathTests.cs`

**Interfaces:**
- Consumes: Task 2 `CarValues`.
- Produces:

```csharp
public static class RunMath
{
    /// First upward crossing of level, linear between the samples around it (spec 4.6); null if never.
    public static double? CrossingTime(IReadOnlyList<double> t, IReadOnlyList<double> v, double level);
    /// Time at v = 0 from a least-squares line through the points with low ≤ v ≤ high (spec 6.4); null with under 3 points or a slope ≤ 0.
    public static double? BackExtrapolatedStart(IReadOnlyList<double> t, IReadOnlyList<double> v, double low = 3, double high = 20);
    /// dv/dt at each point from a least-squares quadratic over |t - t_i| ≤ halfWindow (spec 7); NaN where fewer than minPoints distinct times.
    public static double[] Derivative(IReadOnlyList<double> t, IReadOnlyList<double> v, double halfWindow = 0.35, int minPoints = 5);
    /// Rolling circumference in metres: 0.97 × the free circumference of "205/55 R16" (π × (rim × 25.4 + 2 × width × aspect / 100) mm); null if unparsable.
    public static double? TyreCircumference(string size);
    public static double AirDensity(double pressureKpa, double intakeC);          // p × 1000 / (287.05 × (T + 273.15))
    /// Wheel power (kW) and torque at the crank speed (Nm) per sample (spec 7). kmhPerRpm is km/h per rpm in this gear.
    public static (double[] PowerKw, double[] TorqueNm) Power(IReadOnlyList<double> t, IReadOnlyList<double> rpm, double kmhPerRpm, CarValues car, double airDensity);
    /// Mean (or max) of values in rpm bins of width centred on from, from + width, … up to to.
    public static IReadOnlyList<(double Rpm, double Value)> Bin(IReadOnlyList<double> rpm, IReadOnlyList<double> values, double from, double to, double width = 250, bool max = false);
    public static double Median(IEnumerable<double> values);
}
```

Power: `v = kmhPerRpm × N / 3.6`; `a = kmhPerRpm / 3.6 × dN/dt` (Derivative of rpm); `r = C / 2π`; `m_eff = m + J_w / r² + J_e × ((2π / 60) × 3.6 / kmhPerRpm)²`; `F = m_eff × a + ½ × ρ × CdA × v² + Crr × m × 9.81`; `P = F × v / 1000`; `T = P × 1000 × 60 / (2π × N)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CrossingIsInterpolated()          // t {0,1,2}, v {0,10,20}: level 15 → 1.5; level 25 → null; v {0,10,10,20} level 10 → 1.0
[Fact] public void StartIsBackExtrapolated()         // v = 10 × (t - 0.4) for t in 0.5..3 step 0.1 → 0.4 ± 1e-9; two points → null
[Fact] public void DerivativeOfAQuadraticIsExact()   // v = 3t² + 2t sampled at 30/s: Derivative at t = 1 → 8 ± 1e-6
[Fact] public void DerivativeSurvivesDuplicateTimes()// times with a repeated stamp and a 0.4 s gap: all values finite or NaN, none infinite, no throw
[Fact] public void TyreCircumference()               // "205/55 R16" → 1.926 ± 0.005; "225/45 R17" → 1.889 ± 0.005; "junk" → null
[Fact] public void AirDensityAtSeaLevel()            // AirDensity(101.325, 15) → 1.225 ± 0.001
[Fact] public void PowerWithoutLossesIsMassTimesAccelTimesSpeed()
{   // CarValues(1000, "205/55 R16", false, WheelInertia: 0, EngineInertia: 0, CdA: 0, Crr: 0), kmhPerRpm 0.0223,
    // rpm rising 2000 → 6000 linearly over 8 s at 30/s: a = 0.0223/3.6 × 500 m/s², at 4000 rpm v = 0.0223 × 4000 / 3.6
    Assert.Equal(1000 * a * v / 1000, p.PowerKw[mid], 2); }
[Fact] public void BinsAverageOrTakeTheMax()         // rpm {1990, 2010, 2240, 2260}, values {1, 3, 5, 9}: mean bins at 2000 → 2, 2250 → 7; max → 3 and 9
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~RunMathTests`; expected build errors.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** same command; expected PASS.

---

### Task 6: Run state machine and analysis

**Files:**
- Create: `src/MS43Logger.Core/Performance/PerfRun.cs`, `src/MS43Logger.Core/Performance/RunAnalysis.cs`
- Test: `tests/MS43Logger.Core.Tests/PerfRunTests.cs`

**Interfaces:**
- Consumes: Tasks 2 to 5.
- Produces:

```csharp
public sealed record RecordedSample(TimeSpan At, byte[] Payload);
public sealed record RunOutcome(PerfTestSpec Spec, bool Complete, RunFigures Figures, Conditions Start, Conditions End,
    IReadOnlyList<RecordedSample> Samples, int WindowStart, int WindowEnd);

public sealed class PerfRun
{
    public PerfRun(PerfTestSpec spec, AnalysisInputs inputs, WarmUpMonitor warmUp);
    public PerfTestSpec Spec { get; }
    public RunStage Stage { get; }
    public IReadOnlyList<ReadyCheck> Checks { get; }        // spec 6: each condition with its live detail
    public string Status { get; }                            // "Waiting for full throttle…", "Running", "Done", "Incomplete: pedal lifted at 5210 rpm" (no digit grouping)
    public double Progress { get; }                          // 0..1 of the band (or speed range, or time for link and calibration)
    public double Elapsed { get; }                           // seconds since the start crossing
    public double? Rpm { get; }
    public IReadOnlyList<double?> Knock { get; }             // latest per-cylinder retard
    public IReadOnlyList<(double T, double Rpm)> Live { get; } // rpm against time since the start crossing
    public IReadOnlyDictionary<int, double> Captured { get; }  // speed calibration: km/h per rpm per gear so far
    public double? PedalReading { get; }                    // pedal step: highest reading while running
    public RunOutcome? Outcome { get; }                      // set when Finished (complete or incomplete)
    public event EventHandler? Changed;
    public void Push(PerfFrame frame, ReadOnlyMemory<byte> payload);
    public void Cancel();                                    // Stage = Cancelled, no outcome
    public void LinkLost();                                  // Running → Finished, incomplete, flag "link-lost"; Waiting → Cancelled
}

public static class RunAnalysis
{
    /// Pure: the same window and inputs always give the same figures (spec 9: Compare recomputes with it).
    public static RunFigures Analyse(PerfTestSpec spec, IReadOnlyList<PerfFrame> window, AnalysisInputs inputs, bool complete);
}
```

Behaviour (spec 6):
- Pull and knock check. Checks: "Engine warm" (coolant not cold), "Pedal calibrated", "{n}th gear" (floor gear equals), "Below {start} rpm" (start − 700 ≤ rpm ≤ start − 200), "Pedal released" (< 30 %), then "Full throttle" (the start). All met but full throttle → Status "Waiting for full throttle…". Running when the pedal reaches 90 % with the others met; the window starts at the interpolated start-rpm crossing and ends at the interpolated end crossing (Finished, complete). Ends incomplete when: pedal below 90 % for two samples, clutch or brake on, gear changes, rpm falls 100 below its peak, link lost. Elapsed and Live count from the start crossing.
- Roll-on. Checks: warm, pedal calibrated, gear, "Steady below {start} km/h" (start − 15 ≤ v ≤ start − 5), pedal released. Speed is `KmhPerRpm[gear] × rpm` when calibrated, else `VS` with the flag "uncalibrated". Ends like a pull.
- Standing start. Checks: warm, "Stopped" (VS 0 for 1 s). Running at the first VS > 0; t0 back-extrapolated over 3 to 20 km/h; ends at 200 km/h (complete) or when speed falls 5 km/h below its peak or the pedal stays released for 1 s (complete if 100 was reached).
- Speed calibration. Running at once and never finishes on its own; captures a gear when VS ≥ 50, speed within ±1 km/h and rpm within ±50 for 3 s, pedal below 60 %; `Captured[gear]` = median VS/N over those 3 s (a later capture of the same gear replaces it). The view model reads `Captured`, saves it, then calls `Cancel()`. No Outcome.
- Link test. Running at once for 30 s of `At`; Outcome figures "rate" (/s, 1 decimal), "interval-median", "interval-p95", "interval-max" (ms, 0 decimals), "gaps" (intervals > 300 ms).
- Pedal. Running at once; `PedalReading` = highest pedal seen; Finished after 2 s held within 1 % of that peak while the rpm is 0 or missing. Outcome is not produced.
- Samples kept: from 2 s before the window to its end (`RecordedSample(At, payload.ToArray())`).
- Analysis figures (keys and labels):
  - Pull: `time` ("{start} to {end} rpm", s, 2), `t-2000-4000`, `t-3000-6000`, `t-4000-6000` (only when inside the band), `peak-power` ("Peak power, wheels (estimate)", kW, 0) with `peak-power-rpm`, `peak-torque` ("Peak torque, wheels (estimate)", Nm, 0) with `peak-torque-rpm`, `knock-max` (°, 1) with `knock-cyl`, `iat-start`, `iat-end` (°C, 0). Traces against rpm: `power`, `torque`, `ignition`, `knock` (max over cylinders), `vanos-in`, `vanos-ex`, `afr`, `maf`; against time: `rpm-time`. Headline "6.84 s, 118 kW".
  - Knock check: `knock-max`, `knock-cyl`, `knock-events` (drops ≥ 0.75° between samples), `knock-max-1` … `knock-max-6`, `ron-start`, `ron-end` (2 decimals). Traces `ignition`, `knock`, and `knock-1` … `knock-6`. Headline "No knock" or "1.5° on cylinder 4".
  - Roll-on: `time` ("{start} to {end} km/h", s, 2), 10 km/h splits `split-{a}-{b}`; traces `speed-time`, `rpm-time`. Headline "4.92 s".
  - Standing start: `t-0-100`, `t-0-200`, `t-100-200`, `t-0-50`, `t-50-100` (each only when reached), `launch-rpm`, and `shift-1`, `shift-2`, … (s, 2: each clutch-on period after t0, when the definition has the clutch bit); trace `speed-time`. Headline "7.6 s to 100".
  - Link test: as above. Headline "34.2 samples/s".
- Flags (code, severity, message): `cold` (Cold, "Engine cold: not comparable."), `oil-cold` (Suspect), `oil-unreported` (Info), `incomplete` (Invalid, "Incomplete: {reason}."), `link-lost` (Invalid), `gap` (Suspect, "A {ms} ms gap in the samples."; any interval > 300 ms), `slow-link` (Suspect, under 10 samples/s), `slip` (Suspect, VS and the calibrated speed disagree by more than 3 % at ≥ 30 km/h), `uncalibrated` (Info), `knock` (Suspect, "Cylinder {n} pulled up to {deg}° between {a} and {b} rpm."), `hot-intake` (Suspect, intake above 45 °C), `late-throttle` (Suspect, roll-on full throttle less than 3 km/h before the start speed).

- [ ] **Step 1: Write the failing tests** (frames come from `SyntheticRuns` through `PerfChannels.Read`, inputs `AnalysisInputs(new CarValues(1480, "205/55 R16", false), new Dictionary<int,double>{[3]=0.0223,[4]=0.0298}, 100)`)

```csharp
[Fact] public void PullWaitsThenTimesItsBand()       // Pull(ms43x, 3, 1700, 6500, 6.0): Status "Waiting for full throttle…" during the 1 s hold; then Finished, Complete; figure "time" (2000 to 6400 rpm) = 6.0 × 4400 / 4800 = 5.5 ± 0.02 s
[Fact] public void PullIsIncompleteWhenThePedalLifts() // pedal falls to 50 % at 5000 rpm → Finished, !Complete, flag "incomplete" with "pedal lifted"
[Fact] public void ColdEngineDoesNotStart()          // coolant 64 → Stage stays Waiting, Checks "Engine warm" not met
[Fact] public void PedalNotCalibratedDoesNotStart()  // inputs.FullPedal = 0 → "Pedal calibrated" not met
[Fact] public void KnockIsReportedPerCylinder()      // knockCyl4 1.5 between 5000 and 5600 rpm → figure "knock-max" 1.5, "knock-cyl" 4, flag "knock"
[Fact] public void StandingStartIsBackExtrapolated() // Standing(ms43x, 7.6): t-0-100 within 0.05 s of 7.6
[Fact] public void LinkTestReportsTheRate()          // Idle at 30/s for 31 s → "rate" 30 ± 0.5, "gaps" 0
[Fact] public void GapFlagsTheRun()                  // a pull with a 0.45 s hole → flag "gap", figures finite
[Fact] public void LinkLostEndsARunAsIncomplete()    // LinkLost() mid-pull → Finished, !Complete, flag "link-lost"
[Fact] public void AnalysisIsPure()                  // Analyse twice on the same window → equal Figures values
[Fact] public void SamplesStartTwoSecondsEarly()     // Outcome.Samples[0].At ≤ window start − 2 s (when available) and WindowStart points at the first in-band sample
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~PerfRunTests`; expected build errors.
- [ ] **Step 3:** implement. One class with a switch on `Spec.Kind`; keep the per-kind logic in private methods; analysis in `RunAnalysis`.
- [ ] **Step 4:** same command, then `--filter "FullyQualifiedName~Perf|FullyQualifiedName~RunMath|FullyQualifiedName~WarmUp"`; expected PASS.

---

### Task 7: Tune identity, stored result, results store

**Files:**
- Create: `src/MS43Logger.Core/Performance/TuneIdentity.cs`, `src/MS43Logger.Core/Performance/PerfResult.cs`, `src/MS43Logger.Core/Performance/PerfResultStore.cs`
- Test: `tests/MS43Logger.Core.Tests/PerfResultStoreTests.cs`

**Interfaces:**
- Consumes: Tasks 2 and 6; `CalibrationDocument`, `BinImage`, `XdfDefinition.FindByTitle`, `CalibrationReader`.
- Produces:

```csharp
public sealed record TuneIdentity(string? BinName, string? BinPath, string? SoftwareId, string? CalibrationName,
    string XdfTitle, string? XdfFile, bool HasUnsavedEdits, IReadOnlyList<string> ModifiedItems,
    string SavedSha256, string WorkingSha256, double? RevLimitRpm, bool? LaunchControl, string Snapshot)
{
    /// Null without a document. The snapshot and limits come from the saved image (spec 8); RevLimitRpm is the
    /// id_n_max_max_mt__gear row whose gear-axis value equals gear (null when gear is null or the table is missing).
    public static TuneIdentity? From(CalibrationDocument? document, int? gear);
    public static string Sha256(BinImage image);                 // lower-case hex
    public static string Snapshot(BinImage image);               // zlib + base64 of 0x70000..0x7FFFF
    public static BinImage Restore(string snapshot);             // a full-size image, zeros outside the region
}

public sealed class PerfResult   // JSON shape, PascalCase, schema first
{
    public int Schema { get; init; } = 1;
    public string Id { get; init; } = "";          // Guid "N"
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Direction { get; set; }          // "A", "B" or null: which way the road was driven (spec 7)
    public DateTimeOffset Created { get; init; }
    public string AppVersion { get; init; } = "";
    public PerfTestSpec Spec { get; init; } = default!;
    public bool Complete { get; init; }
    public RunFigures Figures { get; init; } = default!;
    public Conditions Start { get; init; } = default!;
    public Conditions End { get; init; } = default!;
    public AnalysisInputs Inputs { get; init; } = default!;
    public TuneIdentity? Tune { get; init; }
    public string Variant { get; init; } = "";      // "ms43x", "b25", …
    public string DefinitionFile { get; init; } = "";
    public string DefinitionSha256 { get; init; } = "";
    public DateTimeOffset SamplesStart { get; init; }
    public double[] SampleAt { get; init; } = [];   // seconds on the run clock
    public string[] Payloads { get; init; } = [];   // base64
    public int WindowStart { get; init; }
    public int WindowEnd { get; init; }
    public static PerfResult From(RunOutcome outcome, string name, string description, AnalysisInputs inputs, TuneIdentity? tune,
        string variant, AdxDefinition definition, DateTimeOffset created, string appVersion);
}

public sealed record PerfResultHeader(string FileName, string Id, string Name, string Description, DateTimeOffset Created,
    PerfTestKind Kind, string Preset, string? BinName, string? SavedSha256, string Headline, IReadOnlyList<RunFlag> Flags,
    bool Complete, bool ReadOnly);

public sealed class PerfResultStore
{
    public PerfResultStore(string folder, TimeProvider? time = null);
    public string Folder { get; }
    public IReadOnlyList<PerfResultHeader> List();        // newest first; skips *.tmp, unreadable files and schema 0; newer schema → ReadOnly
    public IReadOnlyList<string> Skipped { get; }          // "perf-…json: not a result file" for each skipped file at the last List()
    public PerfResult? Load(string fileName);
    public string? Save(PerfResult result);                 // returns the file name ("perf-20261021-143212.json", "-2" on a clash); null on failure with LastError
    public bool Update(string fileName, string name, string description, string? direction);   // rewrites in place, never renames
    public bool Delete(string fileName);
    public string? LastError { get; }                       // "Not saved: {reason}" style, no trailing full stop in the reason
}
```

JSON: one static `JsonSerializerOptions` (indented, `AllowNamedFloatingPointLiterals`, string enums that read case-insensitively and fall back on unknown names). Write to `path + ".tmp"`, move over, delete the temp in `finally`.

- [ ] **Step 1: Write the failing tests** (temp folder per test, deleted in `Dispose`)

```csharp
[Fact] public void SaveAndLoadRoundTrip()            // PerfResult with NaN in a figure, two samples → Load gives equal Name, Description, Figures (NaN kept), Payloads
[Fact] public void FileNamesComeFromTheClock()       // ManualTime-like TimeProvider at 2026-10-21 14:32:12 local → "perf-20261021-143212.json", second save → "perf-20261021-143212-2.json"
[Fact] public void RenameKeepsTheFileName()          // Update(file, "new", "desc") → List() shows "new" under the same FileName
[Fact] public void BadFilesNeverBreakTheList()       // a corrupt file, a "{}" file (schema 0), a "x.tmp" and a schema 2 file beside a good one → List() has the good one and the schema 2 one (ReadOnly); Skipped names the corrupt and schema-0 files
[Fact] public void FailedSaveSaysWhy()               // folder is a file (blocker) → Save returns null, LastError starts with "Not saved: "
[Fact] public void DeleteRemovesTheFile()
[Fact] public void BlankImageHash()                  // TuneIdentity.Sha256(BinImage.FromBytes(new byte[BinImage.Size])) == "07854d2fef297a06ba81685e660c332de36d5d18d546927d30daad6d7fda1541"
[Fact] public void SnapshotRoundTrips()              // an image with bytes set at 0x70010 and 0x7FFF0 → Restore(Snapshot(img)) equal over 0x70000..0x7FFFF, zero at 0x10
[RealFileFact(TestData.CarBin, TestData.Xdf)] public void CarBinIdentity() // From(open car bin, 3): SoftwareId "43X001", RevLimitRpm 7104, LaunchControl true, HasUnsavedEdits false
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~PerfResultStoreTests`; expected build errors.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** same command; expected PASS.

---

### Task 8: Compare

**Files:**
- Create: `src/MS43Logger.Core/Performance/PerfCompare.cs`
- Test: `tests/MS43Logger.Core.Tests/PerfCompareTests.cs`

**Interfaces:**
- Consumes: Tasks 3, 6, 7; `BinComparer.Compare(XdfDefinition, BinImage, BinImage) : BinComparison`.
- Produces:

```csharp
public sealed record FigureDelta(string Key, string Label, double? A, double? B, double? Delta, string Unit, int Decimals);
public sealed record Mismatch(string Code, string Message);   // "intake", "pressure", "coolant", "tune-same"
public sealed record PerfComparison(RunFigures A, RunFigures B, double? SharedFrom, double? SharedTo,
    IReadOnlyList<FigureDelta> Deltas, IReadOnlyList<Mismatch> Mismatches, IReadOnlyList<ItemDifference> TableDifferences,
    bool Recomputed, double? Noise);

public static class PerfCompare
{
    /// Recomputes both from their samples with current (spec 9.2), over the rpm band both pulls share.
    public static PerfComparison Compare(PerfResult a, PerfResult b, AnalysisInputs current, XdfDefinition? xdf, IReadOnlyList<PerfResult>? sameTune = null);
    /// Frames rebuilt from a stored result (decodes its payloads with BundledDefinitions.Load(result.Variant)).
    public static IReadOnlyList<PerfFrame> Frames(PerfResult result);
    /// Standard deviation of the headline figure over results sharing test, gear and SavedSha256; null under three.
    public static double? Noise(IReadOnlyList<PerfResult> results, string figureKey);
}
```

Mismatch rules: intake air more than 5 °C apart ("Intake air {a} and {b} °C: more than 5 °C apart."), pressure at least 2.1 kPa apart, coolant more than 8 °C apart, same saved hash ("Same tune in both runs.", info). `Recomputed` is true when any recomputed headline figure differs from the stored one by more than 0.5 %.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void CompareRecomputesWithCurrentCarValues() // A saved with mass 1480, B with 1600; current 1480 → both peak-power recomputed with 1480; Recomputed true
[Fact] public void CompareUsesTheSharedBand()              // A 2000 to 6400, B 2000 to 6954 → SharedTo 6400, "t-4000-6000" compared, "time" compared over 2000 to 6400
[Fact] public void MismatchesAreNamed()                    // intake 31 vs 38 °C → Mismatch "intake"
[Fact] public void TablesThatDifferComeFromTheSnapshots()  // synthetic XDF (BinComparerTests.Synthetic pattern) and two snapshots differing in one table → one TableDifferences item with that title
[Fact] public void NoiseNeedsThreeRuns()                   // two same-tune results → null; three with peak-power 115, 116, 117 → 1.0
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.Core.Tests --filter FullyQualifiedName~PerfCompareTests`; expected build errors.
- [ ] **Step 3:** implement.
- [ ] **Step 4:** same command, then the whole Core project in the background: `dotnet test tests/MS43Logger.Core.Tests`; expected PASS.

---

### Task 9: Gear readout, Caution token, settings

**Files:**
- Modify: `src/MS43Logger.AvaloniaApp/MainWindowViewModel.cs:430`, `src/MS43Logger.AvaloniaApp/Theme/Tokens.axaml` (both dictionaries), `src/MS43Logger.AvaloniaApp/Theme/Controls.axaml:571` (beside `.banner.warn`), `src/MS43Logger.AvaloniaApp/AppSettings.cs`
- Test: `tests/MS43Logger.AvaloniaApp.Tests/ReadoutBandTests.cs`, `ThemeTokensTests.cs`, `StyleTests.cs`, `AppSettingsTests.cs`

**Interfaces:**
- Produces:
  - Tokens `CautionColor`/`CautionBrush`: Day `#A15C00`, Night `#E8A33D`; style `Border.banner.caution` (BorderBrush Caution, BorderThickness 3,0,0,0).
  - `AppSettings`: `double? FullPedal`; `Dictionary<int, double> KmhPerRpm` (km/h per rpm by gear); `bool SpeedGpsChecked`; `double TestMassKg = 1480`; `string TyreSize = "205/55 R16"`; `bool Automatic`; `double? PerformanceRailWidth`; `bool PerformanceRailCollapsed`; `Dictionary<string, double[]> PerformanceBands` (preset → [start, end]). `Normalize` keeps mass in 500..3500, a parsable tyre (via `RunMath.TyreCircumference`), finite positive `KmhPerRpm` entries for gears 1..6, `FullPedal` in 10..130, bands with start < end.

- [ ] **Step 1: Write the failing tests**

```csharp
// ReadoutBandTests
[AvaloniaFact] public void GearIsFlooredNotRounded() { Sample(("GEAR", 4.59375)); Assert.Equal(4, live.Gear); Sample(("GEAR", 0.6)); Assert.Null(live.Gear); }
// ThemeTokensTests: [InlineData("Caution", "#A15C00", "#E8A33D")] in TokensMatchTheSystem
// StyleTests
[AvaloniaTheory] [InlineData("Dark")] [InlineData("Light")]
public void CautionReadsOnFillAndPane(string theme)   // contrast(Caution, Fill) ≥ 3 and contrast(Caution, Pane) ≥ 3 with the WCAG helper from SettingsTests
[AvaloniaFact] public void CautionBannerHasACautionStripe() // Border classes banner caution → BorderBrush #E8A33D at Night, thickness 3,0,0,0
// AppSettingsTests: defaults (1480, "205/55 R16", FullPedal null, KmhPerRpm empty), round trip of every new field, and fall-backs for mass 0, NaN pedal, "junk" tyre, a band [6000, 2000]
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~ReadoutBandTests|FullyQualifiedName~ThemeTokensTests|FullyQualifiedName~StyleTests|FullyQualifiedName~AppSettingsTests"`; expected failures.
- [ ] **Step 3:** implement (`Math.Floor` for both calls in `Gear`).
- [ ] **Step 4:** same command; expected PASS.

---

### Task 10: Chart controls

**Files:**
- Create: `src/MS43Logger.AvaloniaApp/Controls/ChartScale.cs`, `Controls/RunChart.cs`, `Controls/CylinderBars.cs`
- Modify: `src/MS43Logger.AvaloniaApp/Controls/CurveChart.cs:201-270` (use `ChartScale`)
- Test: `tests/MS43Logger.AvaloniaApp.Tests/RunChartTests.cs`, `ThemeTokensTests.cs:92-98`

**Interfaces:**
- Produces:
  - `internal static class ChartScale { Ticks(double min, double max, int steps = 4); Format(double value, double step); Text(...); Baseline(...); }` moved from `CurveChart` unchanged in behaviour.
  - `public sealed class RunChart : Control` with styled properties `TraceA`, `TraceB` (`IReadOnlyList<Point>?`, x = rpm or seconds), `LabelA`, `LabelB` (string?), `XUnits`, `YUnits` (string?), `IsLive` (bool), `Version` (int); read-only `XTickLabels`, `YTickLabels`, `PlotRect`. Look (spec 9.2): Pane frame with Hairline edge, radius 8; Hairline grid; Data 12 Ink3 ticks; A Ink1 2 px solid; B Ink2 1.5 px dashed `{4, 2}`; legend line top-left in UI 12 Ink2 ("━ A" style text is not used: draw a 16 px line sample then the label); live: A drawn Ink2 with the last point as the Now dot on a Pane halo, Ink3 ring when `!IsLive`.
  - `public sealed class CylinderBars : Control` with `Values` (`IReadOnlyList<double?>?`), `Maximum` (default 4), `WarnFrom` (default 1.5), `IsLive`, `Version`; six Track bars with numbers 1 to 6 below; fill Now when live, Ink2 otherwise; a value ≥ WarnFrom gets `Icon.Warn` in Warn above its bar; missing value draws no fill and its label shows "n/a"; `AutomationProperties.Name` "Knock retard per cylinder".

- [ ] **Step 1: Write the failing tests**

```csharp
[AvaloniaFact] public void CurveChartTicksAreUnchanged()     // ChartScale.Ticks(1500, 7000) and (0, 39) give the same ticks CurveChart gave before (record them from the old private method first)
[AvaloniaFact] public void RunChartDrawsASolidAndADashedTrace() // 400×240, A and B two different straight lines: pixels near Ink1 along A at many x; along B, both Ink2 pixels and Pane gaps
[AvaloniaFact] public void RunChartTicksCoverTheData()       // A x 2000..7000 → XTickLabels first "2000", last "7000"
[AvaloniaFact] public void LiveRunChartEndsInTheNowDot()     // IsLive true → orange pixels near the last point; IsLive false → none
[AvaloniaFact] public void CylinderBarsWarnFromOneAndAHalf()  // Values {0,0,0,1.5,0,0}: warn-red pixels above bar 4 only
[AvaloniaFact] public void IdleBarsHaveNoOrange()            // IsLive false → zero orange pixels (ReadoutBandTests.OrangePixels rule)
// ThemeTokensTests.CustomControlsRepaintOnThemeChange: add RunChart and CylinderBars
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~RunChartTests|FullyQualifiedName~ChartTests|FullyQualifiedName~ThemeTokensTests"`; expected failures.
- [ ] **Step 3:** implement, copying CurveChart's frame, plot rect, axis and live-dot code.
- [ ] **Step 4:** same command; expected PASS.

---

### Task 11: Warm-up banners on Live, and the Performance view models

**Files:**
- Create: `src/MS43Logger.AvaloniaApp/WarmUpBannerViewModel.cs`, `PerformanceViewModel.cs`, `PerformanceRunViewModel.cs`, `PerformanceResultsViewModel.cs`
- Modify: `src/MS43Logger.AvaloniaApp/MainWindowViewModel.cs` (warm-up, TOIL pinned, remove the old performance code at lines 56-62, 77-104, 271-374, 675-679, 841, 953-956), `Views/LiveView.axaml` (banners after line 298), `ShellViewModel.cs`, `tests/MS43Logger.AvaloniaApp.Tests/MS43Logger.AvaloniaApp.Tests.csproj` (link `SyntheticRuns.cs`)
- Test: `tests/MS43Logger.AvaloniaApp.Tests/WarmUpBannerTests.cs`, `PerformanceRunTests.cs`, `PerformanceResultsTests.cs`; move the performance parts of `DialogTests.cs` here and delete them there.

**Interfaces:**
- Consumes: Tasks 1 to 10; `MainWindowViewModel.SampleShown`, `CurrentDefinition`, `SelectedVariant`, `IsConnected`; `TuneViewModel.Document`, `Settings`; `ConfirmActionDialog` pattern.
- Produces:
  - `MainWindowViewModel.WarmUp : WarmUpMonitor` (fed in `ShowSample(LiveSample)`, reset on disconnect) and `WarmUpBanners : ObservableCollection<WarmUpBannerViewModel>`; `WarmUpBannerViewModel(WarmUpBanner banner)` with `Text`, `IsDanger`, `IsCaution`, `IsInfo`.
  - `ShellViewModel.Performance : PerformanceViewModel`, built in the constructor as `new PerformanceViewModel(live, tune)`, fed by `live.SampleShown`; `PerformanceTab = "Performance"`, `IsPerformance` (wiring of the tab itself is Task 12).
  - `PerformanceViewModel : ViewModelBase, ISidePanel` with `Entries` (groups "Pulls", "Acceleration", "Setup"; tests "Full-throttle pull", "Knock check", "Roll-on", "Standing start", "Speed calibration", "Car", "Pedal", "Link test"), `SelectedEntry`, `IsRunMode`/`IsResultsMode`, `IsVisible`, `Run : PerformanceRunViewModel`, `Results : PerformanceResultsViewModel`, `Push(LiveSample, AdxDefinition)`, `LinkLost()`, `Func<string, Task<bool>>? AskToDelete`, rail members as `CompareViewModel:265-291` with `PerformanceRailWidth`/`PerformanceRailCollapsed`.
  - `PerformanceRunViewModel`: options (`GearChoices`, `SelectedGear`, `StartRpm`, `EndRpm`, `StartKmh`, `EndKmh` as text, `BaselineChoices` with "None", `SelectedBaseline`), `CanArm`, `ArmText` ("Arm pull", "Arm roll-on", "Arm start", "Start capture", "Read pedal", "Start link test"), `ArmTip` (why it is off: "Connect to the car to run a test.", "Calibrate the pedal first.", "This definition does not log knock per cylinder."), `Arm()`, `Cancel()`, the run projection (`Stage`, `Checks`, `Status`, `RpmText`, `ElapsedText`, `Progress`, `Knock`, `LiveTrace`, `BaselineTrace`, `Version`), the result (`Figures`, `Flags`, `ConditionsText`, `TuneText`, `Name`, `Description`, `Direction` ("A", "B" or null), `Save()`, `Discard()`, `RunAgain()`), Setup (`Captured` per gear, `GpsKmh` with the speedometer reading `GpsVsKmh` at that moment, `SaveCalibration()` which multiplies every captured km/h per rpm by `GpsKmh / GpsVsKmh` when both are given and sets `SpeedGpsChecked`, car fields `MassKg`, `TyreSize`, `Automatic` writing `AppSettings` and saving), banners (`WarmUp` list shared from Live).
  - `PerformanceResultsViewModel`: `Rows` (`ResultRow` with `Name`, `TestText`, `DateText`, `TuneText`, `Headline`, `FlagTags`, `IsPicked`, `IsCold`), `SearchText`, `ShowCold`, `Refresh()`, `Rename(row, name)`, `EditDescription(row, text)`, `DeleteAsync(row)`, `CanCompare` (exactly two picked), `Compare()`, `Comparison` (`Metric` segment Power/Torque/Ignition/Knock/VANOS/AFR/MAF/Rpm, `TraceA`, `TraceB`, `Deltas`, `Mismatches`, `Tables`, `NoiseText`, `RecomputedNote`), `Open(row)` (read-only result view).
  - Identity captured at `Arm()`: `TuneIdentity.From(tune.Document, gear)`; the end rpm of a 3rd-gear pull defaults to `RevLimitRpm − 150` when known; the band used is stored in `PerformanceBands`.

- [ ] **Step 1: Write the failing tests** (shell from `TestShell.Create(live: new MainWindowViewModel(new ManualTime(), () => ["COM1"]))`, samples from `SyntheticRuns` pushed with the `LiveFeed.Push` pattern so `SampleShown` fires)

```csharp
// WarmUpBannerTests
[AvaloniaFact] public void ColdCoolantShowsARedBannerOnLive()   // Idle coolant 64 → LiveView has a banner with classes "banner warn" and the exact spec 5 text
[AvaloniaFact] public void ColdOilShowsAnAmberBanner()          // oil 60 → classes "banner caution"
[AvaloniaFact] public void BannersClearOnDisconnect()
[AvaloniaFact] public void OilIsPinnedByDefault()               // TOIL among the pinned rows
// PerformanceRunTests
[AvaloniaFact] public void RailListsTheTests()                  // entry titles in the spec 9.1 order
[AvaloniaFact] public void ArmIsOffUntilConnected()             // ArmTip "Connect to the car to run a test."
[AvaloniaFact] public void PullRunsFromLiveSamples()            // FullPedal 100, connect, Arm, push a synthetic 3rd pull → Stage Finished, Figures has "time", Name "3rd pull 12:00"
[AvaloniaFact] public void SaveWritesAResultWithTheTune()       // Save() → one file under settings.Folder/results; Results.Rows has it; with no bin open TuneText "No bin open"
[AvaloniaFact] public void RunSurvivesSwitchingTabs()           // SelectedTab = Live mid-pull, then back → run still Running and finishes
[AvaloniaFact] public void KnockCheckIsOffOnStockDefinitions()  // b25 → ArmTip "This definition does not log knock per cylinder."
[AvaloniaFact] public void CalibrationIsSavedToSettings()       // steady 100 km/h in 5th for 4 s → Captured[5]; SaveCalibration → Settings.KmhPerRpm[5]
[AvaloniaFact] public void PedalStepStoresTheFullPedal()        // pedal 96 °PVS held 2 s → Settings.FullPedal 96
// PerformanceResultsTests
[AvaloniaFact] public void CompareNeedsExactlyTwo()
[AvaloniaFact] public void ColdRunsAreHiddenUntilAsked()
[AvaloniaFact] public void DeleteAsksFirst()                    // AskToDelete returning false keeps the file; true removes it
[AvaloniaFact] public void CompareShowsPowerTraces()            // two saved pulls → Comparison.TraceA and TraceB non-empty for Metric Power
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~WarmUpBannerTests|FullyQualifiedName~PerformanceRunTests|FullyQualifiedName~PerformanceResultsTests"`; expected failures.
- [ ] **Step 3:** implement. Take run time from `LiveSample.RequestedAt` when not zero, else `sample.Time` minus the first sample's time. Never use the clock at arrival.
- [ ] **Step 4:** same command plus `|FullyQualifiedName~LiveStateTests|FullyQualifiedName~DialogTests|FullyQualifiedName~ReadoutBandTests`; expected PASS.

---

### Task 12: Performance view and shell

**Files:**
- Create: `src/MS43Logger.AvaloniaApp/Views/PerformanceView.axaml(.cs)`
- Modify: `MainWindow.axaml` (tab after line 85, view after line 226, compact header rules lines 40-67), `MainWindow.axaml.cs` (`OnShortcut`: Ctrl+B and Ctrl+K on Performance; Ctrl+Z ignored there; `AskToDelete` wiring as lines 35-36), `ShellViewModel.cs` (`SelectedTab` setter, `IsPerformance`, `ConnectIsPrimary => (IsLive || IsPerformance) && !Live.IsConnected`, `SaveIsPrimary => IsTune || IsCompare`), `Theme/Motion.axaml:45`, `Views/LiveView.axaml:471` and `.axaml.cs:90-96` (the "Performance tests" button sets the shell tab), `Views/RailSizer.cs:27` (search box optional)
- Delete: `src/MS43Logger.AvaloniaApp/PerformanceWindow.axaml`, `PerformanceWindow.axaml.cs`
- Test: `tests/MS43Logger.AvaloniaApp.Tests/PerformanceViewTests.cs`, `HeaderTests.cs:160,361-373`, `ShellTests.cs`, `MotionTests.cs:94-107`

**Interfaces:**
- Consumes: Task 11 view models; Task 10 controls; `RailSizer`; list row styles (`Border.row`, `.current`, `TextBlock.group`) as in `CompareView.axaml:63-120`.
- Produces: `PerformanceView` with named controls the gates press: `TabPerformance`, `PerformanceRail`, `PerformanceResizeHandle`, `PerformanceCollapseButton`, `PerformanceExpandButton`, `RunModeButton`, `ResultsModeButton`, `ArmButton`, `CancelRunButton`, `SaveResultButton`, `DiscardResultButton`, `RunAgainButton`, `ResultNameBox`, `ResultDescriptionBox`, direction segment `DirectionA`, `DirectionB`, gear segment `Gear2`, `Gear3`, `Gear4`, `BaselinePicker`, `SaveCalibrationButton`, `GpsSpeedBox`, `GpsVsBox`, `MassBox`, `TyreBox`, `GearboxManual`, `GearboxAutomatic`, `ResultsSearchBox`, `ShowColdSwitch`, `CompareButton`, per-row `PickBox`, `RenameButton`, `DeleteResultButton`, `OpenResultButton`, metric segment `MetricPower` … `MetricRpm`, `CompareChart` (`RunChart`), `LiveRunChart` (`RunChart`), `KnockBars` (`CylinderBars`), `RunProgress` (`ReadoutBar`), `RunRpm` (`ReadingText` data56).

- [ ] **Step 1: Write the failing tests**

```csharp
[AvaloniaFact] public void PerformanceTabShowsTheView()          // TabPerformance.IsChecked = true → PerformanceView visible, Live/Tune/Compare hidden; Screens.Save "performance-run-night-1440"
[AvaloniaFact] public void ConnectIsPrimaryOnPerformanceWhileDisconnected() // and Arm is primary once connected; Save never primary on Performance
[AvaloniaFact] public void LivePerformanceButtonOpensTheTab()
[AvaloniaFact] public void RunViewShowsChecksThenTheResult()     // drive a synthetic pull; ArmButton press → checks list visible; finished → SaveResultButton visible and primary; Screens.Save "performance-result-night-1440"
[AvaloniaFact] public void ResultsCompareShowsTheChart()         // two saved results, both picked, CompareButton → CompareChart has TraceA and TraceB; Screens.Save "performance-compare-night-1440"
[AvaloniaFact] public void CtrlBTogglesTheRailOnPerformance()
// HeaderTests: the tab loop adds PerformanceTab; HeaderAndStatusBarNeverOverlap passes at 1024 connected on a slow link with unsaved edits
// MotionTests.ANewScreenFadesIn covers PerformanceView
```

- [ ] **Step 2:** `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~PerformanceViewTests|FullyQualifiedName~HeaderTests|FullyQualifiedName~ShellTests|FullyQualifiedName~MotionTests"`; expected failures.
- [ ] **Step 3:** implement the view per spec 9 and the mockups (`.superpowers/brainstorm/2180-1792586062/content/run-and-results.html` and `placement.html`, option A): rail like Compare's list; Run pane top to bottom: banners, options row (gear segment, band, baseline picker, Arm primary), ready-check list, running block (RunRpm, elapsed, RunProgress with rpm ticks, KnockBars, live values line, LiveRunChart), result block (figures grid, flags, conditions and tune lines, name and description boxes, Save/Discard/Run again). Results pane: search, Show cold runs switch, rows, Compare; comparison: metric segment, CompareChart, difference panel. Reading styles copied from `LiveView.axaml:41-70`. Header compaction under `Window.compactheader`: `ToggleButton.tab` padding 8 and indicator margin 8, `Brand`/`Tabs` right margins 8, hide the "slow link" words (keep the value and icon).
- [ ] **Step 4:** same command; expected PASS.

---

### Task 13: Gates, removal, docs

**Files:**
- Modify: `tests/MS43Logger.AvaloniaApp.Tests/GateScreens.cs` (replace the `PerformanceWindow` states with shell states "performance/run", "performance/running", "performance/result", "performance/results", "performance/compare", "performance/cold"), `GateClickThrough.cs` (`TabPerformance` in `ShellControls`; delete lines 132-136; rewrite `PerformanceAsync` as a shell screen pressing every named control of Task 12 with its effect), `GateTests.cs:391-395` (drop the `Shell is null` branch), `DialogTests.cs` (only save-prompt and log tests remain)
- Modify docs: `docs/superpowers/specs/2026-10-20-ms43-restyle-system.md` (5.1 tabs incl. Performance; 1.1 the `Caution` token; 1.4 the knock bars and live run trace on the orange list; 5.x chart: RunChart A/B look; 5.8 caution banner; section 8 layout table; replace the "Performance tests" quiet button text with "opens the Performance tab")

- [ ] **Step 1:** update the gate files; run `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~GateTests"`; expected PASS with `artifacts/gate/click-through-performance.md` regenerated and zero gaps, identical Night and Day lines.
- [ ] **Step 2:** grep the solution for `PerformanceWindow`, `ArmPerformanceTest`, `Time100`; expected no matches outside docs history.
- [ ] **Step 3:** update the restyle-system doc sections listed above.
- [ ] **Step 4:** full suite in the background: `dotnet test MS43Logger.sln`; expected PASS (Core and app).

---

## Execution waves (for the orchestrator)

- Wave 1, in parallel: Task 1; Tasks 2 → 3 → 4 → 5 → 6 (one chain); Tasks 9 and 10 (app visuals, independent of Core changes).
- Wave 2: Task 7 → 8 (Core) beside nothing else; then Task 11, Task 12, Task 13 in order.
- Each task ends with its named tests passing; no commits.
