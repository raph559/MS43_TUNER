# MS43 Tuning Core (phases 0–1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show the real logging refresh rate (phase 0), and build the UI-free core that reads a TunerPro XDF and a 512 KiB MS43 bin and gives every table, scalar and flag in physical units, with a CLI `show` command (phase 1).

**Architecture:** New `MS43Logger.Core.Tuning` namespace (folder `src/MS43Logger.Core/Tuning/`): XDF model and parser, bin image, firmware identification, value codec (raw ↔ physical), calibration reader, text formatter. The CLI and later the Avalonia Tune tab consume it. Phase 0 adds a `SampleRateMeter` to Core and shows its output in the app footer and the CLI.

**Tech Stack:** C#/.NET 10, xUnit 2.5.3, Avalonia 11.3.12 (phase 0 footer only), `System.Xml.Linq`.

**Spec:** `docs/superpowers/specs/2026-10-19-ms43-tuning-design.md` (sections 4.1, 6.2, 6.3, 6.7, 7, 8).

## Global Constraints

- Target `net10.0`; no new NuGet packages in `src/` projects.
- Never modify `../MS43LAB/bins/*` or the user's XDF; tests read copies in `testdata/local/` (git-ignored).
- Parse XML with `DtdProcessing.Prohibit` and no resolver; refuse DTD/ENTITY.
- All number parsing and formatting in Core and CLI uses `CultureInfo.InvariantCulture`.
- File offset = address & 0x7FFFF; a bin is exactly 524,288 bytes.
- The app never writes to the ECU; nothing here touches the serial code paths except reading timestamps.
- Commit only when the user has authorized it; every "Checkpoint" step runs the full suite.
- Do not launch the desktop app on the user's screen without asking; build it instead.

## Review Focus

- A Windows French locale (decimal comma): values, CSV timestamps and output must still use `.`; tests run under `fr-FR` in Tasks 1 and 6.
- A 430069 XDF with an MS43X001 bin (or the reverse): a warning, never silent values; tests in Tasks 5 and 8.
- A bin that is not 512 KiB (the user's Downloads hold 32 KiB and 1 MiB files): a clear refusal; test in Task 5.
- An item whose data runs past the end of the image: listed and skipped, never read past; test in Task 7.
- An equation that is not finite for some raw value (`50/X` at raw 0): shown as `—`, never chosen when converting back; test in Task 6.

---

### Task 1: Sample rate meter

**Files:**
- Create: `src/MS43Logger.Core/SampleRateMeter.cs`
- Test: `tests/MS43Logger.Core.Tests/SampleRateMeterTests.cs`

**Interfaces:**
- Produces: `public sealed class SampleRateMeter(int windowSize = 64)` with `void Record(TimeSpan timestamp)`, `void Reset()`, `RateStats? Current { get; }` (null until two samples); `public sealed record RateStats(double SamplesPerSecond, double AverageMs, double MinMs, double MaxMs, int Intervals)`; `public static class LogRateAnalyzer { public static RateStats? FromCsv(TextReader reader); }` (uses the `timestamp_utc` column, all intervals, skips rows whose timestamp does not parse).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void SteadyIntervalsGiveRateAndStats()
{
    var meter = new SampleRateMeter();
    foreach (var ms in new[] { 0, 25, 50, 75, 100 }) meter.Record(TimeSpan.FromMilliseconds(ms));
    var s = meter.Current!;
    Assert.Equal(40, s.SamplesPerSecond, 6); Assert.Equal(25, s.AverageMs, 6);
    Assert.Equal(25, s.MinMs, 6); Assert.Equal(25, s.MaxMs, 6); Assert.Equal(4, s.Intervals);
}
[Fact] public void WindowKeepsOnlyRecentIntervals()
{
    var meter = new SampleRateMeter(windowSize: 2);
    foreach (var ms in new[] { 0, 100, 110, 120 }) meter.Record(TimeSpan.FromMilliseconds(ms));
    Assert.Equal(10, meter.Current!.MaxMs, 6); Assert.Equal(2, meter.Current.Intervals);
}
[Fact] public void NoStatsBeforeTwoSamplesOrAfterReset()
{
    var meter = new SampleRateMeter();
    meter.Record(TimeSpan.Zero); Assert.Null(meter.Current);
    meter.Record(TimeSpan.FromMilliseconds(30)); meter.Reset(); Assert.Null(meter.Current);
}
[Fact] public void CsvLogGivesBaselineRateUnderFrenchLocale()
{
    var saved = CultureInfo.CurrentCulture;
    CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
    try
    {
        const string csv = "timestamp_utc,sample_index,N\n" +
            "2026-03-10T12:00:00.0000000+00:00,0,800\n2026-03-10T12:00:00.0260000+00:00,1,805\n" +
            "not-a-time,2,810\n2026-03-10T12:00:00.0520000+00:00,3,812\n";
        var s = LogRateAnalyzer.FromCsv(new StringReader(csv))!;
        Assert.Equal(26, s.AverageMs, 6); Assert.Equal(1000.0 / 26, s.SamplesPerSecond, 6); Assert.Equal(2, s.Intervals);
    }
    finally { CultureInfo.CurrentCulture = saved; }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~SampleRateMeterTests"`
Expected: build error, `SampleRateMeter` does not exist.

- [ ] **Step 3: Implement `SampleRateMeter`, `RateStats`, `LogRateAnalyzer` in `src/MS43Logger.Core/SampleRateMeter.cs`**

A queue of the last `windowSize` intervals; `SamplesPerSecond = 1000 / AverageMs`. `FromCsv` finds the `timestamp_utc` column by header name and parses with `DateTimeOffset.TryParse(..., CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, ...)`; it accepts quoted fields.

- [ ] **Step 4: Run the tests and see them pass** (same command; expected: 4 passed)

- [ ] **Step 5: Checkpoint**: `dotnet test MS43Logger.sln` → all pass (12). Commit only if authorized: `feat: add sample rate meter and CSV rate analysis`.

### Task 2: Show the rate in the app and the CLI

**Files:**
- Modify: `src/MS43Logger.AvaloniaApp/MainWindowViewModel.cs` (replace `_sampleRateStopwatch`/`_sampleRateBaseCount`/`UpdateSampleRate` with a `SampleRateMeter`; add `RateText`)
- Modify: `src/MS43Logger.AvaloniaApp/MainWindow.axaml` (footer `TextBlock` bound to `RateText`, next to the variant and port)
- Modify: `src/MS43Logger.Cli/Program.cs` (meter in the polling loop; end-of-run summary; `rate` subcommand)

**Interfaces:**
- Consumes: `SampleRateMeter`, `RateStats`, `LogRateAnalyzer.FromCsv` (Task 1).
- Produces: CLI usage `ms43-logger rate PATH.csv`; `MainWindowViewModel.RateText` (string).

- [ ] **Step 1: In the view model**, record `Stopwatch.Elapsed` for every decoded sample on the polling thread (the meter is the only shared state; guard it with a lock), and refresh `RateText` on the UI thread at most every 500 ms: `"{SamplesPerSecond:0} samples/s, {MinMs:0}–{MaxMs:0} ms"` (invariant culture), or `"No samples yet"` when `Current` is null. Reset on connect and disconnect. Keep `SamplesPerSecond` for compatibility, fed from the meter.
- [ ] **Step 2: In the footer**, add the `RateText` block, styled like the existing `Variant:`/`Port:` blocks.
- [ ] **Step 3: In the CLI**, record each polled sample; print `Stopped after {n} sample(s), {rate:0.0} samples/s (avg {avg:0.0} ms, min {min:0} ms, max {max:0} ms).` Dispatch `rate` when `args[0] == "rate"`: print `{Intervals + 1} samples, {rate:0.0} samples/s, interval avg {avg:0.0} ms, min {min:0.0} ms, max {max:0.0} ms`; exit 1 with `Error: no timestamps found in PATH` when null. Add both to `PrintUsage`.
- [ ] **Step 4: Verify**: `dotnet build MS43Logger.sln` → 0 errors; write the CSV from Task 1's test to a temp file and run `dotnet run --project src/MS43Logger.Cli -- rate <temp.csv>` → `3 samples, 38.5 samples/s, interval avg 26.0 ms, min 26.0 ms, max 26.0 ms`.
- [ ] **Step 5: Checkpoint** as in Task 1. Commit message: `feat: show logging refresh rate in app footer and CLI`.

### Task 3: XDF model and parser

**Files:**
- Create: `src/MS43Logger.Core/Tuning/XdfModel.cs` (records), `src/MS43Logger.Core/Tuning/XdfDefinition.cs` (loader and lookups)
- Test: `tests/MS43Logger.Core.Tests/XdfDefinitionTests.cs` (builds small XDF strings in code)

**Interfaces:**
- Produces:
  - `public sealed record XdfData(long? Address, int ElementBits, bool Signed, bool LsbFirst, int Rows, int Cols, string? UnsupportedReason)`; `Address` is absolute (base offset applied); `Count => Rows * Cols`; `IsSupported => UnsupportedReason is null`.
  - `public enum XdfVariableKind { Raw, Link, Unsupported }`; `public sealed record XdfVariable(string Id, XdfVariableKind Kind, int? LinkId)`; `public sealed record XdfMath(string Equation, IReadOnlyList<XdfVariable> Variables)`.
  - `public sealed record XdfAxis(string Id, string Units, int Count, XdfData? Data, int? LinkedTableId, IReadOnlyList<string> Labels, XdfMath Math, int? DecimalPlaces)`.
  - `public sealed record XdfZ(XdfData Data, XdfMath Math, string Units, int? DecimalPlaces, int? OutputType, double? Min, double? Max)`.
  - `public abstract record XdfItem(int UniqueId, string Title, string Description, IReadOnlyList<int> CategoryIds)`; `XdfTable(... , XdfAxis X, XdfAxis Y, XdfZ Z)`; `XdfConstant(... , XdfData Data, XdfMath Math, string Units, int? DecimalPlaces, int? OutputType, double? RangeLow, double? RangeHigh)`; `XdfFlag(... , XdfData Data, long Mask)`.
  - `public sealed class XdfDefinition` with `static Load(Stream, string sourceName)`, `static LoadFromFile(string path)`, `SourceName`, `Title`, `long BaseOffset`, `IReadOnlyDictionary<int,string> Categories` (key = `CATEGORYMEM category` value = `CATEGORY index` + 1), `Tables`, `Constants`, `Flags`, `IReadOnlyList<string> Warnings`, `XdfTable? FindTableById(int)`, `XdfConstant? FindConstantById(int)` (first in file order when an id repeats), `XdfItem? FindByTitle(string)` (case-insensitive; tables, then constants, then flags).

- [ ] **Step 1: Write the failing tests**, one per rule below, each asserting the exact field values:
  `BaseOffsetIsAddedToAddresses` (offset 458752, subtract 0, z address `0x7315` → `0x77315`), `SubtractedBaseOffset` (subtract 1 → address − offset), `TypeFlagsGiveSignAndByteOrder` (`0x03` → signed, LSB first; absent on 8 bit → DEFAULTS `signed="0" lsbfirst="1"`), `UnknownTypeFlagMakesDataUnsupported` (`0x04`), `NonZeroStrideOnAddressedDataIsUnsupported`, `RowsAndColsDefaultToOne`, `AxisLinkLabelsAndMissingLink` (link → `LinkedTableId`; labels kept as strings; `embedinfo type="3"` without `linkobjid` → no link and a warning naming the table), `VariablesByKind` (`X00` untyped → Raw; `type="link" linkid="0x3236"` → Link 0x3236; `type="address"` → Unsupported), `CategoriesUseIndexPlusOne` (index `0x84` "Idle Speed" → key 133), `DuplicateIdKeepsFirstAndWarns`, `FlagMaskIsParsed` (`0x02` → 2), `ConstantFieldsAreParsed` (units, outputtype, decimalpl, rangelow/high), `DoctypeIsRefused` (throws `XmlException`), `MissingHeaderIsRefused` (`InvalidDataException` "XDF header is missing").
- [ ] **Step 2: Run** `dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~XdfDefinitionTests"` → fails to build.
- [ ] **Step 3: Implement the parser.** Read through `XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })` into `XDocument`. Hex attributes accept `0x` prefixes; `BASEOFFSET offset` is decimal. Axis `Data` is null when the axis has no `mmedaddress`. Unknown typeflag bits, or non-zero strides on data with an address, set `UnsupportedReason`; `-32/0` strides on axes without an address are ignored.
- [ ] **Step 4: Run** the same filter → all pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: parse TunerPro XDF definitions`.

### Task 4: Real XDF and the test-data locator

**Files:**
- Create: `tests/MS43Logger.Core.Tests/TestData.cs`
- Modify: `.gitignore` (add `testdata/local/`)
- Test: `tests/MS43Logger.Core.Tests/RealXdfTests.cs`

**Interfaces:**
- Produces: `internal static class TestData { const string CarBin = "CAR_current_43X001_SP95_STAGE1.bin", ReleaseBin = "MS43X001_release_E46_M54B25.bin", StockBin = "MS43_430069_stock.bin", Xdf = "Siemens_MS43_MS43X001_512K.xdf"; static string? Find(string fileName); }` (env `MS43_TESTDATA` folder first, else `<folder holding MS43Logger.sln>/testdata/local/`); `public sealed class RealFileFactAttribute(params string[] files) : FactAttribute` that sets `Skip` when a file is missing.

- [ ] **Step 1: Copy the test files** (copies, originals untouched): the three bins from `../MS43LAB/bins/` and the user's `Siemens_MS43_MS43X001_512K.xdf` (Downloads) into `testdata/local/`. Check `git status` shows nothing new under `testdata/`.
- [ ] **Step 2: Write the failing tests** with `[RealFileFact(TestData.Xdf)]`:
  `CountsMatch` (1,479 tables, 2,293 constants, 4 flags; `Title == "MS43X001"`; `BaseOffset == 0x70000`), `EveryAxisLinkResolves` (1,253 links, each `FindTableById` non-null), `KnownQuirksAreReported` (exactly one warning contains `ip_ectpwm_i__tco_dif`; a warning mentions the duplicate id 0x2738; `FindTableById(0x2738)!.Title == "ip_n_ecf_temp_cat__temp_cat"`), `CategoriesResolve` (`ip_iga_ron98_pl__n__maf` categories include "Ignition").
- [ ] **Step 3: Implement `TestData` and `RealFileFactAttribute`.**
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~RealXdfTests"` → 4 passed (not skipped).
- [ ] **Step 5: Checkpoint.** Commit message: `test: real MS43X001 XDF checks with local test data`.

### Task 5: Bin image, element reads, firmware identification

**Files:**
- Create: `src/MS43Logger.Core/Tuning/BinImage.cs`, `src/MS43Logger.Core/Tuning/FirmwareIdentifier.cs`
- Test: `tests/MS43Logger.Core.Tests/BinImageTests.cs`

**Interfaces:**
- Consumes: `XdfData`, `XdfDefinition.Title` (Task 3).
- Produces: `public sealed class BinImage { const int Size = 524288; static BinImage Load(string path); static BinImage FromBytes(byte[] bytes, string name = "image.bin"); string Name; ReadOnlySpan<byte> Bytes; static int ToOffset(long address); bool Contains(XdfData data); long ReadElement(XdfData data, int index); }` (`index` is row-major; throws `ArgumentOutOfRangeException` outside the image); `public sealed record FirmwareInfo(string? SoftwareId, string? CalibrationName)`; `public static class FirmwareIdentifier { static FirmwareInfo Identify(BinImage image); static bool Matches(FirmwareInfo info, XdfDefinition xdf); }`.

- [ ] **Step 1: Write the failing tests:** `WrongSizeIsRefused` (32,768 bytes → `InvalidDataException` whose message contains "524288" and "32768"), `ReadsBothByteOrdersSignsAndSizes` (bytes `34 12` at 0x77315: u16 LSB 0x1234, MSB 0x3412; `FF` as signed 8 bit → −1; u32 LSB), `RowMajorIndex` (index 3 of a 2×2 u16 at A reads A+6), `AddressIsMirrored` (0xF7315 reads like 0x77315), `DataPastTheEndIsNotContained` (16-bit at 0x7FFFF → `Contains` false, `ReadElement` throws), `IdentifiesFirmware` (synthetic `ca43X001.DAT` at 0x70040 → `43X001`; all `FF` → nulls), and with `[RealFileFact]`: car → `("43X001", "ca43X001.DAT")`, stock → `("430069", "ca430069.DAT")`, `Matches` true for car + XDF, false for stock + XDF.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~BinImageTests"` → fails to build.
- [ ] **Step 3: Implement.** `Identify` reads ASCII at 0x70040 up to the first `00`/`FF` (max 16 chars); `SoftwareId` is the text between `ca` and `.DAT`. `Matches` is `xdf.Title.Contains(SoftwareId, OrdinalIgnoreCase)`, false when `SoftwareId` is null.
- [ ] **Step 4: Run** → all pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: bin image reads and firmware identification`.

### Task 6: Value codec

**Files:**
- Create: `src/MS43Logger.Core/Tuning/ValueCodec.cs`
- Test: `tests/MS43Logger.Core.Tests/ValueCodecTests.cs`

**Interfaces:**
- Consumes: `XdfDefinition`, `XdfMath`, `XdfData`, `BinImage` (Tasks 3, 5); `ExpressionEvaluator.Evaluate` (existing).
- Produces: `public sealed class ValueCodec(XdfDefinition xdf, BinImage image) { double ToPhysical(XdfMath math, long raw); long ToRaw(XdfMath math, XdfData data, double physical); static string Format(double physical, long raw, int? decimalPlaces, int? outputType, int elementBits); }`.

- [ ] **Step 1: Write the failing tests:**
  - `LinearBothWays`: `0.375*X-23.625` → raw 0 = −23.625; `ToRaw(−10)` = 36; 1000 → 255; −1000 → 0 (8-bit unsigned).
  - `VariableIdsAreCaseInsensitiveAndAnyRawName`: `x` and `0.1*X00` (raw 3200 → 320.0).
  - `LinkedConstantFeedsY`: a constant `0.1 * X` raw 147 and `(1-(0.0039058823*X-0.5))*Y` raw 128 → 14.7007 (±1e-4).
  - `NonFiniteIsNaNAndNeverChosen`: `50/X` raw 0 → NaN; raw 10 → 5.0; `ToRaw(5.0)` = 10; `ToRaw(4.9)` = 10.
  - `Signed32BitLinearRoundTrips`: `0.00000029802322*X-0.0000001509884`, raws −5 and 1000.
  - `FormatRules` under `fr-FR` (restored in `finally`): (320.0, dp 1) → `320.0`; (159.99999, outputtype 2) → `160`; (raw 20, outputtype 3, 16 bits) → `0x0014`; (12.3456, dp null) → `12.35`; NaN → `—`.
  - `[RealFileFact(CarBin, Xdf)] EveryCellRoundTrips`: for every supported table cell and constant, `ToPhysical(ToRaw(p))` equals `p = ToPhysical(raw)` (finite `p` only), and `ToRaw(p) == raw` whenever the equation is linear with a non-zero slope.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ValueCodecTests"` → fails to build.
- [ ] **Step 3: Implement.** Variables go in an `OrdinalIgnoreCase` dictionary: Raw kinds → raw; Link → the linked constant's physical value read from the image (depth limit 4, missing → NaN); Unsupported → NaN. Non-finite results → NaN. `ToRaw`: 8 bit → test all 256 raws; 16 bit → if the five points {min, min+1, mid, max−1, max} are collinear and the slope is non-zero, invert algebraically and round, else test all raws (cache per equation, element type and link values); 32 bit → linear only, else `NotSupportedException`. Ties go to the lower raw; the result is clamped to the element range. `Format` follows spec §6.2 (2 decimals when absent; outputtype 2 rounds away from zero; 3 is `0x` + upper-case hex padded to `bits/4`; NaN → `—`).
- [ ] **Step 4: Run** → all pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: raw/physical value codec for XDF equations`.

### Task 7: Calibration reader

**Files:**
- Create: `src/MS43Logger.Core/Tuning/CalibrationReader.cs`
- Test: `tests/MS43Logger.Core.Tests/CalibrationReaderTests.cs`

**Interfaces:**
- Consumes: Tasks 3, 5, 6.
- Produces: `public sealed record AxisValues(string? Name, string Units, IReadOnlyList<double> Values, IReadOnlyList<string> Labels, int? DecimalPlaces)`; `public sealed record TableValues(XdfTable Table, int Rows, int Cols, long[,] Raw, double[,] Physical, AxisValues X, AxisValues Y)`; `public sealed record ConstantValue(long Raw, double Physical, string Display)`; `public sealed class CalibrationReader(XdfDefinition xdf, BinImage image) { TableValues ReadTable(XdfTable table); ConstantValue ReadConstant(XdfConstant constant); bool ReadFlag(XdfFlag flag); IReadOnlyList<string> FindOutOfRangeItems(); }`.

- [ ] **Step 1: Write the failing tests.** Synthetic: an axis with a direct address reads its values; a label axis returns `Labels` and parsed `Values`; an item at 0x7FFFF (16 bit) appears in `FindOutOfRangeItems` and `ReadTable` throws `InvalidDataException` naming it. Real car (`[RealFileFact(CarBin, Xdf)]`):
  - `ip_iga_ron98_pl__n__maf`: 20 × 16; `Physical[0,0..5]` = 6.375, 6.375, 6.375, 6.375, 6.75, 8.25; every `Physical[19,c]` = −23.625; `Y.Name` "ldpm_n_14", `Y.Values` = 800, 1000, 1250, 1500, 1750, 2200, 2500, 3000, 3250, 3750, 4000, 4250, 4500, 4750, 5000, 5500, 5900, 6480, 6980, 6990; `X.Name` "ldpm_maf_iga_2", `X.Values[0]` = 32.979 (±0.001), `X.DecimalPlaces` = 1.
  - `ldpm_n_11` (16 × 1) = 800, 1250, 1750, 2200, 2500, 3000, 3250, 3750, 4096, 4250, 4500, 5000, 5900, 6480, 6980, 6990.
  - `ip_af_target_ron98__maf__n` `Physical[0,0]` = 14.7007 (±1e-4); `id_n_max_max_mt__gear` `Raw[0,0]` = 222, `Physical[0,0]` = 7104.
  - Constants: `c_conf_n_max` 1, `c_conf_lc` 1, `c_conf_nls` 1, `c_conf_load` 1, `c_conf_bc` 0, `c_inj_flow` 225, `c_lc_n_max` raw 141 = 4512, `c_icl_sup_frq` raw 10 = 5.0, `c_t_cp_1` Display `320.0`, `c_sum_a_ftp_min` Display `160`, `c_dtc_ad_mec_ref_ivvt_ex_0` Display `0x0014`.
  - Flags on word 0x0002 at 0x707D0: `lv_mil_ind_knk_lv_1` false, `lv_mil_ind_knk_lv_2` true, `lv_mil_ind_ff_err` false, `lv_mil_ind_ob_prot` false.
  - `FindOutOfRangeItems()` is empty for the car bin.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CalibrationReaderTests"` → fails to build.
- [ ] **Step 3: Implement.** Axis values: linked table (its Z data and math; `DecimalPlaces` = the axis's own, else the linked table's; units = the axis's, else the linked table's), else the axis's own data, else labels (parsed with invariant culture where numeric, `NaN` otherwise), else indices 0..n−1. Unsupported data → read raw with `Physical` NaN.
- [ ] **Step 4: Run** → all pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: read XDF tables, constants and flags from a bin`.

### Task 8: Text formatter and CLI `show`

**Files:**
- Create: `src/MS43Logger.Core/Tuning/TableTextFormatter.cs`
- Modify: `src/MS43Logger.Cli/Program.cs` (dispatch `show`; usage text)
- Test: `tests/MS43Logger.Core.Tests/TableTextFormatterTests.cs`

**Interfaces:**
- Consumes: Tasks 5–7.
- Produces: `public static class TableTextFormatter { static string Format(TableValues values, bool raw); static string FormatConstant(XdfConstant constant, ConstantValue value); }`; CLI `ms43-logger show --bin PATH --xdf PATH NAME [--raw]`.

- [ ] **Step 1: Write the failing golden test** on a synthetic 2 × 3 table (title `t1`, address 0x77315, units `°CRK`, X axis 10.0/20.0/30.0 mg/stk with 1 decimal, Y axis 800/1000 rpm with 0 decimals, values 1.5 … 6.5 with 1 decimal):

```
t1  0x77315  2 × 3  °CRK
rpm \ mg/stk   10.0   20.0   30.0
800             1.5    2.5    3.5
1000            4.5    5.5    6.5
```

  plus `RawModePrintsRawValues` and `ConstantLine` (`c_lc_n_max = 4512 (raw 141)`).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~TableTextFormatterTests"` → fails.
- [ ] **Step 3: Implement** the formatter (description on the second line when present; the first column is the corner `"{yUnits} \ {xUnits}"` and the row labels, left-aligned to the widest of them; every value column is right-aligned to its widest header or cell + 3) and the CLI command: load both files, print `Warning: XDF "<title>" does not match firmware <id>` to stderr when `FirmwareIdentifier.Matches` is false, print the item, and on an unknown name exit 1 listing up to 5 titles that contain the text.
- [ ] **Step 4: Verify**: tests pass, then `dotnet run --project src/MS43Logger.Cli -- show --bin testdata/local/CAR_current_43X001_SP95_STAGE1.bin --xdf testdata/local/Siemens_MS43_MS43X001_512K.xdf ip_iga_ron98_pl__n__maf` prints 20 rows, the first starting `800` with `6.4`, the last starting `6990` with `-23.6` in every column; with `--xdf` and `testdata/local/MS43_430069_stock.bin` the warning appears.
- [ ] **Step 5: Checkpoint**, then report to the user with the `show` output of two or three tables for comparison with TunerPro (spec §8, real-world acceptance). Commit message: `feat: CLI show command for XDF items`.
