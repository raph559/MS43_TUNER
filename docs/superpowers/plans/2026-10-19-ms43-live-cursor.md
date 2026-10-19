# MS43 Live Cursor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While the logger runs, the map, curve or axis shown in the Tune tab shows where the engine is on it: the fractional cell found as the ECU finds it, a crosshair, the surrounding cells outlined by weight, a short fading trail and the interpolated value, in orange; tables whose inputs the logger does not carry say "No live input" and why.

**Architecture:** UI-free maths and mapping in `MS43Logger.Core.Tuning`: the ECU's axis search, cell weights and trail; a committed input map (`definitions/ms43x001_live_inputs.json`, generated from the MS43LAB code catalogue) that links each table's axes to ECU RAM variables and each RAM variable to a logger channel; a per-table binding that turns a raw logger payload into a cursor fix. In the app, a `LiveCursorController` keeps only the latest sample (delivered on the UI thread by the existing post), and a 30 Hz timer computes one fix for the visible table and hands it to `TableEditorViewModel.Live`, which the map grid, curve chart and 3D surface draw.

**Tech Stack:** C#/.NET 10, Avalonia 11.3.12, xUnit 2.5.3, Avalonia.Headless.XUnit 11.3.12 (tests only), `System.Text.Json`; Python 3 for the one-off generator script (run with `python -I`).

**Spec:** `docs/superpowers/specs/2026-10-19-ms43-tuning-design.md` (§4.2 inputs, §5.1 tokens and "orange means now", §6.6 live cursor, §8 tests). Builds on `docs/superpowers/plans/2026-10-19-ms43-tuning-core.md` and `2026-10-19-ms43-tune-ui.md`.

## Facts this plan rests on (checked 2026-10-19 on `testdata/local` copies)

- **ECU axis search** (disassembly of `0D:0060` u8 and `0D:00B6` u16, interpolating; `0D:0462`, `0D:039A` not interpolating), axis = `{count, v0 … vn-1}`, unsigned compares: input ≥ last → index n−1, fraction 0; input ≤ first → index 0, fraction 0; otherwise scan down from the top for the highest i with vᵢ ≤ input; equal → fraction 0, else fraction = `((input − vᵢ) << 16) / (vᵢ₊₁ − vᵢ)` (integer division, units of 1/65536). Non-interpolating searches keep the index and drop the fraction.
- **Derived inputs:** `0x128B0`: `807F = 8080 = E0E4 > 0x1FE0 ? 0xFF : E0E4 >> 5`. No byte writes exist to `E33B`, `E32D`, `E329`, `E0FF`, so each is the high byte of the logged word. `E0E6` has other writers and is not mapped.
- **Code catalogue for MS43X001:** `python -I ../MS43LAB/tools/ms43_calmaps.py testdata/local/CAR_current_43X001_SP95_STAGE1.bin --functions ../MS43LAB/ghidra/out_ms43x/functions.tsv --csv <out>` → 856 tables (391 maps, 465 curves), 3 unresolved calls (the VE maps). SHA-256: car bin `8cfe3124…6c0717`, tool `45decfa7…f233f901`, functions.tsv `11882a8b…dcc962`.
- **Coverage estimate** (XDF tables whose data address is in the catalogue and whose axes match by address): 381 fully logged inputs, 14 more through a high-byte approximation, 440 with an unlogged input, 2 axis mismatches; the 8 VE maps are added by hand.
- **Car bin axes** used in tests: `0x7501A` (rpm, u16) 800, 1000, 1250, 1500, 1750, 2200, 2500, 3000, 3250, 3750, 4000, 4250, 4500, 4750, 5000, 5500, 5900, 6480, 6980, 6990; `0x75044` (MAFIGA, u16) 778, 1180, 1651, 2359, …, 14154; `0x740F8` (807F, u8) 28, 38, 55, 63, 78, 94; `0x740FF` (E33B, u8) 11, 16, 21, 25, 29, 39; `0x7419A` (8433, u8) 51, 104, 144, 174; `0x77E60` (MAP, u16) 603, 1206, 1809, 2412, 3015, …; `0x77E82` (rpm, u16) 192, 448, 704, 992, 1504, 2016, ….

## Global Constraints

- Plan-1 and plan-2 constraints hold: `net10.0`, no new NuGet packages in `src/`, invariant culture in Core, originals in `../MS43LAB/bins/` never modified (the generator reads the `testdata/local` copy).
- The app never writes to the ECU; logging sends only the ADX telegrams, with unchanged timing (same 20 ms pause, same 3-timeout rule).
- Nothing new runs on the polling thread: the logger only adds the payload array it already holds to `LiveSample`. All cursor work runs on the UI thread, at most 30 redraws per second, and only for the item shown in the Tune tab while that tab is visible.
- Matching is done on raw values in the ECU's own domain (spec §6.6); physical units are for display only.
- Orange (`NeedleBrush`, Night `#FF6A1A`, Day `#E05206`) is used only for the live cursor, its trail and the live readout; a stale cursor (no sample for 1 s) is drawn in `Ink3Brush`.
- A table with no mapped input never shows a cursor; its readout starts with `No live input:`.
- Never launch the GUI on the user's screen; verify through headless tests and screenshots in `artifacts/screens/`.
- Commit only when the user asks, with no AI co-author trailer.

## Review Focus

- **Gear bits in the rpm word** (frame offset 0x04 carries gear in bits 13–15): the cursor must use `N & 0x1FFF`, not the whole word. Test in Task 4 (sample B has gear 3).
- **Engine off the map** (rpm above the last breakpoint, load below the first): the cursor sits on the edge cell as the ECU clamps, and the readout shows the real input marked "(off the map)". Tests in Tasks 1 and 4.
- **The user edits an axis while logging**: the cursor must follow the new breakpoints at once, not the ones read when the table was opened. Test in Task 4 (`EditedAxisMovesTheCursor`) and Task 5 (`AxisEditRebinds`).
- **Logging with the wrong definition** (stock `b25` ADX, or a 430069 bin): no cursor and a stated reason, never a cursor read from the wrong bytes. Tests in Tasks 4 and 5.
- **Logger stops or stalls** (disconnect, timeouts): the cursor turns grey after 1 s without samples and disappears on disconnect. Test in Task 5.

---

### Task 1: Axis search, cell weights and trail

**Files:**
- Create: `src/MS43Logger.Core/Tuning/LiveCursorMath.cs`
- Test: `tests/MS43Logger.Core.Tests/LiveCursorMathTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct AxisPosition(int Index, double Fraction, int Clamped)` with `double Position => Index + Fraction`; `Clamped` is −1 when the input is below the first breakpoint, +1 above the last, else 0.
  - `public static class AxisSearch { public static AxisPosition Find(IReadOnlyList<long> breakpoints, long input, bool interpolate); }` (throws `ArgumentException` on an empty list).
  - `public readonly record struct CellWeight(int Row, int Col, double Weight)`; `public static class CellWeights { public static IReadOnlyList<CellWeight> Around(AxisPosition? row, AxisPosition? col, int rows, int cols); public static double Interpolate(double[,] values, IReadOnlyList<CellWeight> cells); }` (a null axis means index 0, fraction 0; cells with weight 0 or outside the table are left out; `Interpolate` is NaN when a weighted cell is NaN).
  - `public readonly record struct TrailPoint(double Row, double Col, double Opacity)`; `public sealed class CursorTrail(int capacity = 32, TimeSpan? life = null)` (life defaults to 1.2 s) with `void Add(double row, double col, TimeSpan time)`, `IReadOnlyList<TrailPoint> Points(TimeSpan now)` (oldest first, opacity `1 − age / life`, expired points dropped), `void Clear()`.

- [ ] **Step 1: Write the failing tests**

```csharp
static readonly long[] Rpm = { 800, 1000, 1250, 1500, 1750, 2200, 2500, 3000 };
[Fact] public void BetweenBreakpointsGivesSixteenBitFraction() =>
    Assert.Equal(new AxisPosition(5, 61166 / 65536.0, 0), AxisSearch.Find(Rpm, 2480, true));
[Fact] public void OnABreakpointFractionIsZero() => Assert.Equal(new AxisPosition(5, 0, 0), AxisSearch.Find(Rpm, 2200, true));
[Fact] public void BelowFirstClampsLow() => Assert.Equal(new AxisPosition(0, 0, -1), AxisSearch.Find(Rpm, 500, true));
[Fact] public void FirstBreakpointIsNotOffTheMap() => Assert.Equal(new AxisPosition(0, 0, 0), AxisSearch.Find(Rpm, 800, true));
[Fact] public void AboveLastClampsHigh() => Assert.Equal(new AxisPosition(7, 0, 1), AxisSearch.Find(Rpm, 7200, true));
[Fact] public void LastBreakpointIsTheLastIndex() => Assert.Equal(new AxisPosition(7, 0, 0), AxisSearch.Find(Rpm, 3000, true));
[Fact] public void NotInterpolatingKeepsTheLowerIndex() => Assert.Equal(new AxisPosition(3, 0, 0), AxisSearch.Find(new long[] { 28, 38, 55, 63, 78, 94 }, 77, false));
[Fact] public void EqualNeighboursTakeTheUpperOne()
{
    Assert.Equal(new AxisPosition(2, 0, 0), AxisSearch.Find(new long[] { 100, 200, 200, 300 }, 200, true));
    Assert.Equal(new AxisPosition(2, 0.5, 0), AxisSearch.Find(new long[] { 100, 200, 200, 300 }, 250, true));
}
[Fact] public void SinglePointAxis() => Assert.Equal(new AxisPosition(0, 0, 1), AxisSearch.Find(new long[] { 50 }, 60, true));
[Fact] public void FourCellsWeightedBilinearly()
{
    var cells = CellWeights.Around(new AxisPosition(5, 0.25, 0), new AxisPosition(1, 0.5, 0), 20, 16);
    Assert.Equal(new[] { new CellWeight(5, 1, 0.375), new CellWeight(5, 2, 0.375), new CellWeight(6, 1, 0.125), new CellWeight(6, 2, 0.125) }, cells);
}
[Fact] public void EdgeCellHasAllTheWeight() =>
    Assert.Equal(new[] { new CellWeight(19, 15, 1) }, CellWeights.Around(new AxisPosition(19, 0, 1), new AxisPosition(15, 0, 1), 20, 16));
[Fact] public void CurveUsesRowsOnly() =>
    Assert.Equal(new[] { new CellWeight(1, 0, 0.75), new CellWeight(2, 0, 0.25) }, CellWeights.Around(new AxisPosition(1, 0.25, 0), null, 4, 1));
[Fact] public void InterpolateIsTheWeightedSumAndNaNPropagates()
{
    var v = new double[,] { { 0, 10 }, { 20, double.NaN } };
    Assert.Equal(5, CellWeights.Interpolate(v, CellWeights.Around(new AxisPosition(0, 0, 0), new AxisPosition(0, 0.5, 0), 2, 2)), 9);
    Assert.True(double.IsNaN(CellWeights.Interpolate(v, CellWeights.Around(new AxisPosition(0, 0.5, 0), new AxisPosition(0, 0.5, 0), 2, 2))));
}
[Fact] public void TrailFadesAndKeepsCapacity()
{
    var trail = new CursorTrail(capacity: 3, life: TimeSpan.FromSeconds(1));
    for (var i = 0; i < 5; i++) trail.Add(i, i, TimeSpan.FromMilliseconds(100 * i));
    var points = trail.Points(TimeSpan.FromMilliseconds(500));
    Assert.Equal(new[] { 2.0, 3.0, 4.0 }, points.Select(p => p.Row));
    Assert.Equal(0.7, points[0].Opacity, 9); Assert.Equal(0.9, points[2].Opacity, 9);
    Assert.Empty(trail.Points(TimeSpan.FromSeconds(2)));
}
```

- [ ] **Step 2: Run** `dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~LiveCursorMathTests"` → build fails (types missing).
- [ ] **Step 3: Implement** `AxisSearch`, `CellWeights`, `CursorTrail` in `LiveCursorMath.cs`. `Find` follows the ECU rules in "Facts" exactly, using `long` integer arithmetic for the fraction; `Around` orders cells row-major (row, then column).
- [ ] **Step 4: Run** the same filter → all pass.
- [ ] **Step 5: Checkpoint**: `dotnet test MS43Logger.sln` → all pass. Commit only if authorized: `feat: ECU axis search, cell weights and cursor trail`.

### Task 2: Live input map (generator, data file, loader)

**Files:**
- Create: `scripts/make_live_inputs.py`, `definitions/ms43x001_live_inputs.json` (generated, committed), `src/MS43Logger.Core/Tuning/LiveInputMap.cs`
- Modify: `src/MS43Logger.Core/MS43Logger.Core.csproj` (embed `..\..\definitions\*.json` like the ADX files)
- Test: `tests/MS43Logger.Core.Tests/LiveInputMapTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record LiveInputSource(string Ram, string Channel, int PacketOffset, int Bits, long Mask, int Shift, long Add, long Max, bool Approximate, string Evidence)` with `long Apply(long channelRaw)` = `clamp(((channelRaw & Mask) >> Shift (or << −Shift when negative)) + Add, 0, Max)`.
  - `public sealed record CatalogAxis(long CountAddress, int ElementBits, string Input)`; `public sealed record CatalogTable(long Data, string? Name, bool Interpolates, IReadOnlyList<CatalogAxis> Axes, string Source)`.
  - `public sealed class LiveInputMap { static LiveInputMap Load(Stream json); static LiveInputMap? LoadBundled(string? softwareId); string SoftwareId; IReadOnlyDictionary<string, string> Provenance; IReadOnlyDictionary<string, LiveInputSource> Inputs; IReadOnlyList<CatalogTable> Tables; CatalogTable? FindTable(long data); IReadOnlyList<string> InputsOfAxis(long countAddress); }` (`Inputs` keyed by RAM name, case-insensitive; `InputsOfAxis` = the distinct inputs of every table axis at that address; `Load` throws `InvalidDataException` naming the problem on bad JSON or a missing field).
- JSON format (one table per line so diffs stay readable):

```json
{ "format": 1, "softwareId": "43X001",
  "provenance": { "catalogue": "...", "catalogueTool": "...", "bin": "...", "functions": "...", "frame": "...", "generator": "...", "date": "2026-10-19" },
  "inputs": [ { "ram": "E0E4", "channel": "N", "offset": "0x04", "bits": 16, "mask": "0x1FFF", "shift": 0, "add": 0, "max": 8191, "approximate": false, "evidence": "..." } ],
  "tables": [ { "data": "0x77315", "name": "ip_iga_ron98_pl__n__maf", "interp": true, "axes": [["0x7501A", 16, "E0E4"], ["0x75044", 16, "E32C"]], "source": "catalogue" } ] }
```

- [ ] **Step 1: Write `scripts/make_live_inputs.py CATALOGUE.csv --bin BIN --functions TSV --tool PY --xdf XDF --out JSON`.** It reads the catalogue rows (row axis, then column axis when present; element `u8` → 8, `u16` → 16; inputs copied verbatim, `?`, `#…` and `a/b` included), adds the names from the XDF by data address, appends the 8 VE maps by hand (`data` = 0x7EE20 + (k−1)·0x200, axes `["0x77E60", 16, "8FD6"], ["0x77E82", 16, "E0E4"]`, interp true, source `hand-added [inf]: XDF ip_map_ve_k__map__n on ldpm_ve_map (0.0082921489*X kPa, the ADX MAP scale) × ldpm_ve_n; read through 0D:0168 from computed pointers at 0x3B9F8/0x3BA20/0x3BA48`), writes the SHA-256 of bin, tool and functions.tsv into `provenance`, and writes this input table (offset/bits from the ms43x ADX; evidence text as given):

| ram | channel | offset/bits | mask | shift | add | max | approx | evidence |
|---|---|---|---|---|---|---|---|---|
| E0E4 | N | 0x04/16 | 0x1FFF | 0 | 0 | 8191 | no | frame 0x04 = E0E4 & 0x1FFF, gear in bits 13–15 (0C:467E) |
| 807F | N | 0x04/16 | 0x1FFF | 5 | 0 | 255 | no | 0x128B0: 807F = E0E4 > 0x1FE0 ? 0xFF : E0E4 >> 5 |
| 8080 | N | 0x04/16 | 0x1FFF | 5 | 0 | 255 | no | 0x128CA: 8080 = 807F |
| E33A / E33B | MAFTI | 0x12/16 | 0xFFFF | 0 / 8 | 0 | 65535 / 255 | no | frame 0x12 = E33A; E33B is its high byte (no byte writes) |
| E32C / E32D | MAFIGA | 0x14/16 | 0xFFFF | 0 / 8 | 0 | 65535 / 255 | no | frame 0x14 = E32C |
| E328 / E329 | MAFIVVT | 0x16/16 | 0xFFFF | 0 / 8 | 0 | 65535 / 255 | no | frame 0x16 = E328 |
| E320 | MAFMMV | 0x18/16 | 0xFFFF | 0 | 0 | 65535 | no | frame 0x18 |
| E330 | MAFMES | 0x1A/16 | 0xFFFF | 0 | 0 | 65535 | no | frame 0x1A |
| E32E | MAF | 0x1C/16 | 0xFFFF | 0 | 0 | 65535 | no | frame 0x1C |
| 8FD6 | MAP | 0x08/16 | 0xFFFF | 0 | 0 | 65535 | no | frame 0x08 |
| 847F, 8432, 8433, 842F, 8439 | VS, TIA, TCO, TOIL, TCOEX | 0x06, 0x0A, 0x0B, 0x0C, 0x0D / 8 | 0xFF | 0 | 0 | 255 | no | frame bytes |
| E0FF | PVS | 0x2A/8 | 0xFF | 0 | 0 | 255 | no | frame 0x2A = E0FE high byte = E0FF |
| E0FE, E15E, 84C8 | PVS, TPS, AMP | 0x2A, 0x2B, 0x07 / 8 | 0xFF | −8 | 128 | 65535 | yes | frame carries only the high byte |

- [ ] **Step 2: Generate the file**: run the catalogue command from "Facts" into the scratchpad, then `python -I scripts/make_live_inputs.py <catalogue.csv> --bin testdata/local/CAR_current_43X001_SP95_STAGE1.bin --functions ../MS43LAB/ghidra/out_ms43x/functions.tsv --tool ../MS43LAB/tools/ms43_calmaps.py --xdf testdata/local/Siemens_MS43_MS43X001_512K.xdf --out definitions/ms43x001_live_inputs.json`. Expected: 864 tables. Running it twice gives identical bytes.
- [ ] **Step 3: Write the failing tests:** `BundledMapLoadsFor43X001` (`LoadBundled("43X001")` not null, 864 tables, `Provenance["bin"]` contains `8cfe3124`; `LoadBundled("430069")` and `LoadBundled(null)` are null), `InputsTransformRawChannels` (E0E4 of `2480 | 3 << 13` → 2480; 807F of 2480 → 77; 807F of 8191 → 255; E33B of 0x1500 → 21; E0FE of 0x40 → 0x4080 and `Approximate`), `KnownTablesArePresent` (0x77315: interp, axes (0x7501A, 16, E0E4), (0x75044, 16, E32C); 0x7927A: not interp, axes (0x740F8, 8, 807F), (0x740FF, 8, E33B); 0x7EE20: hand-added, source starts with `hand-added`), `AxisInputsAreCollected` (`InputsOfAxis(0x7501A)` = [E0E4]), `BrokenJsonIsRefused` (missing `inputs` → `InvalidDataException` containing "inputs").
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~LiveInputMapTests"` → fails; **implement** `LiveInputMap` with `System.Text.Json` (`JsonDocument`, hex strings parsed with invariant culture); **run** → pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: MS43X001 live input map from the code catalogue`.

### Task 3: Raw channel values in live samples

**Files:**
- Modify: `src/MS43Logger.Core/AdxDefinition.cs` (public raw read), `src/MS43Logger.Core/LiveLogger.cs` (payload in the sample)
- Test: `tests/MS43Logger.Core.Tests/LiveLoggerTests.cs`

**Interfaces:**
- Produces: `public static long AdxDefinition.ReadRaw(AdxValueDefinition value, ReadOnlySpan<byte> payload)` (the existing native extraction, before the equation; throws `InvalidOperationException` for floating-point values); `public sealed record LiveSample(DateTimeOffset Time, long Index, IReadOnlyList<DecodedValue> Values, ReadOnlyMemory<byte> Payload = default)`.

- [ ] **Step 1: Failing tests:** `ReadRawFollowsTheChannelLayout` (ms43x N with the word `2480 | 3 << 13` written in the channel's byte order → `ReadRaw` = 27056, decoded N = 2480; TCO byte 124 → 124), `LoggerPublishesThePayload` (fake source as in the existing tests → `samples[1].Payload.ToArray()` equals the second payload).
- [ ] **Step 2–4:** run → fail; implement (the logger passes the array `PollPayload` returned; `Ds2Session.ExtractPayload` already returns a new array per poll, so no copy is made); run → pass.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: keep the raw payload in live samples`.

### Task 4: Table binding and the replay acceptance test

**Files:**
- Create: `src/MS43Logger.Core/Tuning/LiveTableBinding.cs`
- Test: `tests/MS43Logger.Core.Tests/LiveTableBindingTests.cs`, `tests/MS43Logger.Core.Tests/LivePayloads.cs` (helper: `static byte[] Build(AdxDefinition adx, params (string Channel, long Raw)[] values)` writing each raw in its channel's offset, size and byte order into a 73-byte payload)

**Interfaces:**
- Consumes: Tasks 1–3; `CalibrationDocument`, `XdfTable`, `ValueCodec.ToPhysical`, `BinImage.ReadElement`.
- Produces:
  - `public sealed record LiveAxisBinding(LiveInputSource Source, IReadOnlyList<long> Breakpoints, XdfMath Math, string Units, int? DecimalPlaces)`.
  - `public sealed record LiveReading(string Channel, long Input, double Physical, string Units, int? DecimalPlaces, int Clamped)`; `public sealed record CursorFix(AxisPosition? Row, AxisPosition? Col, IReadOnlyList<CellWeight> Cells, double Value, IReadOnlyList<LiveReading> Inputs, bool Approximate)`.
  - `public sealed class LiveTableBinding { static LiveTableBinding Resolve(LiveInputMap? map, CalibrationDocument document, XdfTable table); XdfTable Table; bool HasInput; string? Reason; LiveAxisBinding? Row; LiveAxisBinding? Col; bool Interpolates; string Describe(); string? CheckLogger(AdxDefinition adx); CursorFix Locate(AdxDefinition adx, ReadOnlySpan<byte> payload, double[,] physical); }`.
- Rules:
  - Rows follow the XDF Y axis, columns the X axis; only an axis with more than one point needs an input. An XDF axis's first-value address is its linked table's data address, else its own data address; it matches the catalogue axis whose `CountAddress + ElementBits / 8` equals it.
  - A table not in the catalogue whose own data is an axis (`FindTable` fails, `InputsOfAxis(data − bytes)` has exactly one input) binds that input on its long dimension, interpolating.
  - Breakpoints are read from the open image at the catalogue axis (count from the bin, unsigned, LSB first); the count must equal the XDF point count.
  - `Math`, `Units`, `DecimalPlaces` of an axis: the linked table's Z values when linked, else the axis's own (same precedence as `CalibrationReader.ReadAxis`). `LiveReading.Physical` = `ToPhysical(Math, input)`, so it shows the input itself, even off the map.
  - Exact reasons, in this order: no map → `No live input: there is no input map for firmware {SoftwareId ?? "unknown"}.`; not in catalogue → `No live input: the code catalogue does not list this table.`; axis address or count mismatch → `No live input: the XDF axes of this table are not the axes the ECU searches.`; input with `?`, `#` or `/` → `No live input: the {rows|columns} input is computed in code or varies by call site ({input}).`; RAM not in `Inputs` → `No live input: the {rows|columns} follow ECU variable {ram}, which the logger does not read.`
  - `CheckLogger` returns `No live input: the logger definition {adx.SourceName} does not carry {channel} at offset 0x{offset:X2}.` when a needed channel is missing or has another offset or size, else null. `Describe()` = `Live input: {channel} ({units}) on rows × {channel} ({units}) on columns.` (one part for a curve).

- [ ] **Step 1: Write the failing tests** (all `[RealFileFact(TestData.CarBin, TestData.Xdf)]`, ms43x ADX from `BundledDefinitions`):
  - `ReplayedSamplesMoveTheCursorToTheExpectedCells` on `ip_iga_ron98_pl__n__maf`, four payloads in order:
    A (N 800, MAFIGA 778) → Row (0, 0, 0), Col (0, 0, 0), Cells [(0,0,1)], Value 6.375;
    B (N `2480 | 3 << 13`, MAFIGA 1415) → Row (5, 61166/65536, 0), Col (1, 32698/65536, 0), Cells (5,1), (5,2), (6,1), (6,2) with product weights, Value = `CellWeights.Interpolate`, `Inputs[0].Physical` 2480;
    C (N 7200, MAFIGA 20000) → Row (19, 0, 1), Col (15, 0, 1), Value −23.625, `Inputs[0].Physical` 7200;
    D (N 500, MAFIGA 0) → Row (0, 0, −1), Col (0, 0, −1).
  - `DerivedInputsUseTheEcuVariables`: `id_o2_int_max_mt__n__maf`, N 2480 and MAFTI 0x1500 → not interpolating, Cells [(3, 2, 1)].
  - `CoolantCurve`: `id_t_tco_puc_deacc__tco`, TCO 124 → Row (1, 0, 0), Col null, Cells [(1, 0, 1)].
  - `VeMapUsesPressureAndRpm`: `ip_map_ve_1__map__n`, MAP 3000, N 2000 → Row (3, 63905/65536, 0), Col (4, 63488/65536, 0).
  - `AxisTableShowsItsInput`: `ldpm_n_14`, N 2480 → Row (5, 61166/65536, 0).
  - `EditedAxisMovesTheCursor`: set `ldpm_n_14` row 5 to 2300 through `TableEdits.Set` + `EditHistory.Apply`, resolve again → N 2480 gives Row (5, 58982/65536, 0).
  - `NoLiveInputReasons`: `id_lam_tco_min__tco_st` → `HasInput` false, Reason ends `ECU variable 8450, which the logger does not read.`; `Resolve(null, …)` with the stock bin → `…for firmware 430069.`; `CheckLogger(BundledDefinitions.Load("b25"))` on the RON98 map → starts `No live input: the logger definition`.
  - `CoverageOfTheCarXdf`: at least 380 XDF tables have `HasInput`; `ip_iga_ron98_pl__n__maf`, `ip_af_target_ron98__maf__n` and the 8 VE maps are among them.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~LiveTableBindingTests"` → build fails.
- [ ] **Step 3: Implement** `LiveTableBinding` by the rules above.
- [ ] **Step 4: Run** → all pass (not skipped).
- [ ] **Step 5: Checkpoint.** Commit message: `feat: bind tables to live inputs and locate the cursor`.

### Task 5: Cursor controller in the app

**Files:**
- Create: `src/MS43Logger.AvaloniaApp/LiveCursorController.cs`; test `tests/MS43Logger.AvaloniaApp.Tests/LiveCursorTests.cs`
- Modify: `MainWindowViewModel.cs` (`SampleShown`, `CurrentDefinition`, post the whole sample), `TableEditorViewModel.cs` (`Live`), `TuneViewModel.cs` (owns the controller, rebinds), `ShellViewModel.cs` (wiring), `Views/TuneView.axaml` (readout), `Theme/Controls.axaml` (`TextBlock.live` style)

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `MainWindowViewModel`: `public event EventHandler<LiveSample>? SampleShown`; `public AdxDefinition? CurrentDefinition { get; internal set; }` (replaces `_currentDefinition`); `internal void ShowSample(LiveSample sample)` (calls the existing `ShowSample(sample.Values)` then raises `SampleShown`; the logger callback posts this instead).
  - `public sealed record LiveCursorState(CursorFix Fix, IReadOnlyList<TrailPoint> Trail, bool IsStale)`; `TableEditorViewModel.Live` (`LiveCursorState?`, raises `PropertyChanged`).
  - `public sealed class LiveCursorController : ViewModelBase { LiveCursorController(TimeProvider? time = null); const int MaxRedrawsPerSecond = 30; static readonly TimeSpan StaleAfter /* 1 s */; bool AutoTick; bool IsVisible; LiveTableBinding? Binding; string? Readout; bool ReadoutIsLive; void Bind(CalibrationDocument? document, TableEditorViewModel? editor); void Push(LiveSample sample, AdxDefinition? definition); void Clear(); void Tick(); }`; `TuneViewModel.LiveCursor`.
- Behaviour:
  - `Push` stores the latest sample and arrival time only. With `AutoTick` (default true) a `DispatcherTimer` at `1000 / MaxRedrawsPerSecond` ms calls `Tick`; it starts on the first `Push` and stops in `Clear`.
  - `Tick` does nothing unless `IsVisible`, an editor is bound, `Binding.HasInput` and a sample exists. With a new sample since the last tick it checks the logger (`CheckLogger` reason → `Readout` = reason, `editor.Live` = null), else locates the fix against `editor.Values.Physical`, adds it to the trail and sets `editor.Live`. Without a new sample it only flips `IsStale` once when the last sample is older than `StaleAfter`. Several samples between ticks give one update.
  - `Readout` while live: `{in1} {units1} · {in2} {units2} → {value} {zUnits}` with each number formatted by `ValueCodec.Format` at its decimals, `−` for minus, `" (off the map)"` after a clamped input, `"≈ "` before the value when `Approximate`. Not logging: `Describe()` + ` Start logging to see the engine on this map.`; no input: the reason. `ReadoutIsLive` is true only while live and fresh.
  - `TuneViewModel` calls `Bind` when the editor changes and after every `Document.Changed` (axis edits change breakpoints) and clears the trail on a new table. `ShellViewModel`: `live.SampleShown` → `tune.LiveCursor.Push(sample, live.CurrentDefinition)`; `IsConnected` false → `Clear()`; `IsVisible` follows `IsTune`.
  - Inspector: under the surface/curve, `<TextBlock Name="LiveReadout" Classes="num" Classes.live="{Binding LiveCursor.ReadoutIsLive}" Text="{Binding LiveCursor.Readout}" TextWrapping="Wrap" />`; default foreground `Ink3Brush`, `.live` uses `NeedleBrush`.

- [ ] **Step 1: Failing headless tests** (open the car file with `TuneScreen.OpenAsync`, set `AutoTick = false`, samples built with `LivePayloads.Build` (the Core test file, linked into this project with `<Compile Include="..\MS43Logger.Core.Tests\LivePayloads.cs" Link="LivePayloads.cs" />`), definition `BundledDefinitions.Load("ms43x")`):
  `SampleMovesTheCursorOnTheVisibleMap` (push sample B, `Tick` → `Editor.Live.Fix.Cells[0]` = (5,1); `ReadoutIsLive`; `Readout` starts `2480 rpm · `), `TenSamplesBetweenTicksGiveOneRedraw` (count `Live` property changes: 1), `HiddenTabDoesNothing` (Live tab selected → `Editor.Live` stays null), `SwitchingTableClearsTheTrail` (two ticks on the RON98 map, select `ip_af_target_ron98__maf__n`, one tick → `Trail.Count` 1), `StaleAfterOneSecond` (fake `TimeProvider` advanced 1.1 s → `IsStale` true, `ReadoutIsLive` false), `DisconnectClears` (`shell.Live.Disconnect()` → `Editor.Live` null), `NoInputTableSaysSo` (select `id_lam_tco_min__tco_st` → `Readout` starts `No live input:`), `WrongLoggerDefinitionSaysSo` (push with the `b25` definition → `Readout` starts `No live input: the logger definition`, `Editor.Live` null), `AxisEditRebinds` (edit `ldpm_n_14` row 5 to 2300 while the RON98 map is shown, push N 2480 → `Fix.Row.Fraction` = 58982/65536).
- [ ] **Step 2–4:** run `dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~LiveCursorTests"` → fail; implement; run → pass. Run `LiveViewTests` too: the Live screen behaves as before.
- [ ] **Step 5: Checkpoint.** Commit message: `feat: live cursor controller with throttled redraws for the visible map`.

### Task 6: Drawing the cursor

**Files:**
- Modify: `src/MS43Logger.AvaloniaApp/Controls/MapGrid.cs`, `Controls/CurveChart.cs`, `Controls/Surface3D.cs`
- Test: `tests/MS43Logger.AvaloniaApp.Tests/LiveCursorDrawingTests.cs`

**Interfaces:**
- Consumes: `TableEditorViewModel.Live` (Task 5), `CellBounds` (existing).
- Produces: `public Point MapGrid.CursorPoint(double row, double col)` (centre of cell ⌊row⌋, ⌊col⌋ plus the fraction times the cell pitch `cellWidth + Gap` / `CellHeight + Gap`; one-column tables use the column centre); `public Point CurveChart.CursorPoint(double index, double value)`; `Surface3D` draws its marker through `SurfaceMath.Project`.
- Look (cursor brush = `NeedleBrush`, or `Ink3Brush` when stale; everything drawn after the cells and selection):
  - Map grid: 1 px crosshair lines across the whole cell area at 0.7 opacity (only the row line for a one-column table); each weighted cell outlined with a pen of thickness `1 + 2·w` and opacity `0.35 + 0.65·w`; trail points as 2.5 px dots at their opacity, joined by 1 px segments at half that opacity; a 4 px dot ringed in `DialBrush` at the fix.
  - Curve chart: a 1 px vertical line at the fractional point (x interpolated between the two axis values), a 4.5 px dot at (x, Value), the two neighbouring points ringed by weight, trail dots on the curve.
  - Surface: a 4 px dot at (col / (cols − 1), row / (rows − 1), normalised Value) with a 1 px drop line to the floor, trail dots on the surface.
- [ ] **Step 1: Failing headless tests:** `CursorPointIsBetweenCellCentres` (fraction 0.5 lands halfway between two centres), `OrangeOutlinesTheCellsAroundTheEngine` (sample B; in `CaptureRenderedFrame()` the pixel on the left edge of cell (5,1) and on the crosshair at `CursorPoint` is within 40 of `#FF6A1A` per channel; a cell far from the cursor, (15,12), has no such pixel on its edge), `StaleCursorIsNotOrange` (stale state → no `#FF6A1A` pixel on the crosshair), `CurveAndSurfaceDrawTheirMarker` (curve `id_t_tco_puc_deacc__tco` and the RON98 surface each contain an orange pixel at the marker).
- [ ] **Step 2–4:** run `--filter "FullyQualifiedName~LiveCursorDrawingTests"` → fail; implement; run → pass.
- [ ] **Step 5: Verify visually:** save headless screenshots of the RON98 map with a moving cursor and trail (Night and Day), a coolant curve, a VE map, and a "No live input" table to `artifacts/screens/live-cursor-*.png`; compare with the mockup's cursor and fix differences. **Checkpoint.** Commit message: `feat: draw the live cursor on map, curve and surface`.

### Task 7: Verification and hand-over

- [ ] **Step 1:** `dotnet test MS43Logger.sln` → all pass, none of the real-file tests skipped; `dotnet build -c Release` → 0 warnings added.
- [ ] **Step 2:** Confirm `git status` shows no change under `testdata/` and that `../MS43LAB/bins/` is untouched (timestamps and SHA-256 as before).
- [ ] **Step 3:** Report to the user with the screenshots: what was verified (replay test, headless drawing, throttle), and what only they can check on the car: the cursor follows rpm and load on the RON98 part-load map and the VE map, the rate shown stays the same as before, and the VE input (marked [inf]) matches what the engine does.
