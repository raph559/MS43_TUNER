# Performance tests: design

Date: 2026-10-21. Owner-approved in conversation; mockups in `.superpowers/brainstorm/2180-1792586062/content/` (placement.html, run-and-results.html, untracked).
Applies to: MS43 Tuner (`src/MS43Logger.AvaloniaApp`, `src/MS43Logger.Core`), branch `feature/tuning-core`.
Design system: `docs/superpowers/specs/2026-10-20-ms43-restyle-system.md` governs every surface below (tokens, type, buttons, segments, banners, charts).

Marks: *(owner)* = the owner said it; *(call)* = my decision, open to change; *(research)* = from the 2026-10-21 research (channel map, ECU-tuning catalogue, feasibility critique).

## 1. Intent

The owner tunes their own E46 M54B25 on MS43X001 and wants the app to measure the car the way a tuner needs: *(owner)* "add everything possible that could be helpful to tune my car", choose the test, and save each result with a name and a description. *(owner)* Oil below working temperature must raise a warning, and coolant below operating temperature a **red, dangerous** warning.

Why it matters: a tune change is only worth keeping if a measurement shows it helped, under comparable conditions, without knock. Today the app has a 440 px window with two times (0–100 and 0–200 km/h) that read optimistic and are lost on close.

Success: the owner drives a short session, runs a few pulls before and after a bin change, and the app tells them, with the conditions and any knock in view, whether the change gained or lost, and keeps every run to look back at.

## 2. Goals, non-goals, anti-goals

Goals
1. A **Performance** tab *(owner, mockup A)*: test list on the left (like the Tune rail), the chosen test in the pane, a **Run / Results** switch at the top.
2. Warm-up warnings on Live and on Performance *(owner)*: red danger for coolant, amber for oil (thresholds in section 5).
3. Tests in the first release *(owner approved the list)*: full-throttle pull in a gear, knock check, roll-ons, standing starts, speed and gear calibration, link test (section 6).
4. Each finished run can be **saved** with a name and a description *(owner)*, and kept with the tune, the conditions and every reading (section 8).
5. A **results library**: search, rename, edit the description, delete, and pick two to **compare** with overlaid traces against rpm (or time), the difference, the conditions side by side, and the tables that changed between the two tunes (section 9).
6. Fix what the tests depend on *(research)*: the gear readout (section 4.1), interpolated crossing times, the late start of today's 0–100.

Non-goals (this release)
- GPS, accelerometer or wideband hardware; any measurement the DS2 frame does not carry.
- Dyno-grade power: wheel power is an **estimate**, always labelled so.
- Later tests *(owner agreed)*: coast-down, part-throttle cell sweep, throttle response, idle quality, rev-limiter check, 0–400 m, braking, warm-up log, repeatability sets with shaded noise bands, export.
- Writing to the ECU. The app never writes ECU flash (standing rule).

Anti-goals (a failure even if it works)
- A figure that looks precise but is not: every estimate is labelled, every suspect or invalid run says why.
- Encouraging full throttle on a cold engine. The bin does not protect a cold engine *(research: `c_tco_min_fl` = −39.75 °C on the car bin)*; the app's red warning is the only guard.
- Slowing or destabilising logging. Tests consume the samples Live already receives; they never add DS2 requests.
- Losing a run silently: a run in progress survives switching tabs; an unsaved finished run is only dropped by Discard, Run again, or arming another test.

## 3. Constraints

- C#/.NET 10, Avalonia 11.3.12, existing MVVM style (`ViewModelBase`, `SetField`), xUnit + Avalonia.Headless tests, test-first.
- Logging: one DS2 request per sample returns the whole frame (73 bytes for ms43x, 82 for the stock v2.9 definitions); every channel of a sample shares one moment. Expected 18–37 samples/s *(research, never measured on the car)*. `VS` is whole km/h (8-bit). `N` is 1 rpm.
- Both definitions must work: ms43x (`Siemens_MS43_MS43X001_Logging.adx`, ids such as N, VS, GEAR, PVS, TCO, TOIL) and the stock v2.9 ones (RPM, VEHICLESPEED, GEAR, ACCELERATORPED, COOLANTTEMP, OILTEMP, …). A test whose channels are missing says which ones, and cannot be armed.
- Design system: Night look B and Day look A tokens; one primary button per screen; numbers in the data font with tabular figures; banners per system 5.x; soft-edged buttons (2026-10-21 change).
- Standing repo rules: never stage README.md, `.superpowers/`, artifacts or testdata; XDF files are never committed.

## 4. Groundwork

### 4.1 Gear
`GEAR` = X/32 on byte 0x05, whose low five bits are the high byte of `N`, so the decoded value is gear + (N >> 8 & 0x1F)/32 (3.22 at 2000 rpm, 3.78 at 6400) *(research)*. The current readout uses `Math.Round` and shows one gear too high above about 4100 rpm, and n/a in 5th. **Gear is `floor(GEAR)`** everywhere, Live readout included. Gear 0 means neutral or unknown.

### 4.2 Sample time
`LiveSample.Time` is the wall clock after the reply was read and decoded. Tests need a steadier clock: the logger records a **monotonic timestamp taken when the request is sent** (Stopwatch based) and the reply duration, on every sample *(call: request time is closest to when the DME latched the values)*. Test math uses the monotonic time; the wall clock stays for display and file names.

### 4.3 Status bits
The ADX bitmasks (35 in ms43x) are parsed but never decoded. Add decoding by id from a sample's payload, so tests can read: clutch `CSSTATE`, brake `BLSSTATE`, overrun cut `PUCSTATE`, catalyst heating `CHSTATE`, warm-up `WUPSTATE`, overheat lamp `TENGSTATE`, VANOS state, misfire `MISSTATE`. A bitmask missing from the active definition reads as unknown, never as off.

### 4.3b Channel names
The two definition families name and scale things differently *(research)*. A single mapping turns a sample into the test's frame: rpm (`N` / `RPM`), speed (`VS` / `VEHICLESPEED`), gear (`GEAR`, floored), pedal (`PVS` / `ACCELERATORPED`), throttle (`TPS`), coolant (`TCO` / `COOLANTTEMP`), oil (`TOIL` / `OILTEMP`), intake air (`TIA` / `INTAKEAIRTEMP`), pressure in kPa (`AMP` kPa / `HPA` hPa divided by 10), battery (`VB` / `BATTERYVOLTAGE`), ignition per cylinder and average (`IGA1` to `IGA6`, `IGAAVG` / `IGNITION1` to `IGNITION6`, `IGNITIONAVG`), knock retard per cylinder (`IGAKNK1` to `IGAKNK6`; raw 128 is zero and retard decodes negative, stored as positive degrees of retard; **not in the stock definitions**), VANOS (`CAMAVIN`, `CAMAVEX` / `VANOSINTAKE`, `VANOSEXHAUST`), airflow (`MAF`), load (`MAFTI` / `ENGINELOADTI`), AFR target (`AFTARGET`, ms43x only), RON adaptation normalised to 0..1 (`RONADFAC` 0..1 / `RONADFACTOR` 0..100), long-term trims (`TIADFAC1/2`, `TIADADD1/2`, ms43x only), and the bits of 4.3 (clutch `CSSTATE` / `CLUTCHSWITCH`, brake `BLSSTATE` / `BRAKELIGHTSWIT`). A value or bit the definition lacks is missing, never zero; a test lists what it lacks (the knock check is unavailable on stock definitions).

### 4.4 Full throttle
Full throttle is the pedal (`PVS` / `ACCELERATORPED`) at or above 90 % of the **full-pedal value**, with the throttle (`TPS`) not far behind. `FLSTATE` is information only: it means full-load enrichment, not full throttle *(research)*. What the pedal reads when floored on this car is unknown (the ADX range tops at 120 °PVS, which a floored pedal may never reach), so the full-pedal value is **calibrated once**: the Setup group's "Pedal" step asks for the pedal pressed fully with the ignition on and the engine off, and stores the reading. Until that is done, pull-type tests cannot be armed and say "Calibrate the pedal first", with a button to the step. Any higher reading seen later raises the stored value *(call)*. "Pedal below 30 %" and every other pedal percentage are relative to this value.

### 4.5 Speed from rpm
`VS` steps are too coarse for acceleration. Within a gear, speed is `v = k_g × N`, with `k_g` (km/h per rpm) from the speed calibration (section 6.5) or, until there is one, from the run itself (median of VS/N over samples at ≥ 30 km/h, flagged "uncalibrated"). A run whose `VS` disagrees with `k_g × N` by more than 3 % at ≥ 30 km/h has slip (wheelspin, clutch, converter) and is flagged.

### 4.6 Crossing times
Every "time at X" is the linear interpolation between the two samples around X, never the stamp of the first sample past X.

## 5. Warm-up and condition warnings

| Condition | Level | Banner text (example) | Clears |
|---|---|---|---|
| Coolant below 80 °C | **Red, danger** *(owner)* | "Coolant 64 °C: engine not at operating temperature (80 °C). Do not use full throttle; results will not be valid." | at ≥ 80 °C held 10 s; returns below 75 °C *(call: hysteresis)* |
| Oil below 80 °C | Amber *(owner)* | "Oil 71 °C: not at working temperature yet (80 °C)." | same rule |
| Coolant or oil at ≥ 80 °C, still in its 10 s hold | Same level as its cold banner *(call)* | "Coolant 81 °C: nearly at operating temperature. Do not use full throttle yet." / "Oil 88 °C: nearly at working temperature, holding 10 s." | when the hold ends (cleared), or below 80 °C (back to the cold text) |
| Oil temperature not reported | Grey info | "Oil temperature not reported by this ECU." | when it moves |
| Coolant above 115 °C or oil above 130 °C | Amber *(call)* | "Coolant 117 °C: running hot." | 5 °C below |
| Overheat lamp bit (`TENGSTATE`) | Red *(call)* | "The ECU reports engine overheating." | when the bit clears |

- 80 °C for both *(research: the M54 map thermostat holds 95–110 °C at part load and pulls to 85–90 °C at sustained full load, so 90 °C would false-alarm mid-pull)*.
- Oil is "not reported" when it reads ≤ −40 or ≥ 150 °C, or stays within 2 °C while coolant rises 20 °C.
- **Amber is new to the design system**, which today has one signal red (`Warn`) and keeps orange (`Now`) for live marks *(research)*. Add a **`Caution`** token pair (Day and Night) used only for the amber banner's stripe and icon, with a `Border.banner.caution` style beside `.banner.warn`; the banner text stays Ink1 as on every banner. The stripe and icon must reach 3:1 against Fill and Pane in both themes (check with the contrast helper), and the token joins `ThemeTokensTests` *(call)*.
- The thresholds are fixed values in this release, not settings *(call)*.
- Banners show on **Live** (top of the screen, below the connection banner) and on **Performance** (top of the pane) while connected. Each banner shows the reading and the target; under the banner, a warming estimate ("about 6 min") once the rate is known *(call, optional polish)*.
- Effect on runs: coolant red blocks auto-start (the ready check waits) *(call)*; a run made anyway (coolant dropped mid-run) is kept but marked **cold, not comparable** and left out of comparisons by default. Oil amber or not reported marks a run **suspect**.

## 6. Tests

All tests share: an **Arm** action (primary button), a **ready check** list that turns each condition green, automatic start when the conditions are met, a **running** view, and a **result** view with Save / Discard / Run again. Arming waits; nothing is timed until the start condition. Any test can be cancelled. Lifting, braking, pressing the clutch or changing gear ends a pull-type run as **incomplete**; an incomplete run keeps what it measured and can be saved.

### 6.1 Full-throttle pull (main before/after test)
- Presets: **3rd** 2000 → limit (default), **2nd** 1500 → 6400 rpm (road-legal speeds), **4th** 1500 → 4500 rpm. The end rpm defaults to the open bin's rev limit for that gear minus 150 rpm (`id_n_max_max_mt__gear`, read at arm time from the row whose gear-axis value equals the gear; 7104 rpm on the car bin), else 6400 *(research)*. Start and end are editable; the band is stored with the result and only equal bands are compared.
- Ready: gear matches (floor), rpm below start − 200 and above start − 700, pedal below 30 %, coolant OK. Starts on full throttle; the measured window begins at the interpolated start-rpm crossing.
- Measures: interpolated times between rpm marks (start → end, 2000 → 4000, 3000 → 6000, 4000 → 6000 when inside the band); **wheel power and torque estimate** vs rpm (section 7); per 250 rpm bin: ignition average, worst knock retard and which cylinder, VANOS intake and exhaust, airflow (MAF), load (MAFTI), AFR target, injector duty, DME torque.
- Running view: big rpm, elapsed time, progress from start to end rpm with rpm ticks, six knock bars, a line of live values (ignition, VANOS in, AFR target, IAT), and a **live chart of rpm against time from the start crossing** with the chosen baseline's trace dashed behind it, so being ahead or behind shows at once *(call; the placement mockup's run pane had this chart)*. No power is computed live.
- Baseline: the Run options carry a "Compare against" picker, defaulting to the most recent saved result of the same test and gear that is not cold, with "None" as a choice *(call)*. The result view shows the deltas against it.
- Result: the times, peak power and torque with rpm (labelled estimate, wheels), knock summary, conditions, tune.

### 6.2 Knock check
A 4th-gear pull from 1500 to 4500 rpm (high load, low rpm) focused on knock. Same mechanics as 6.1. Result: per-cylinder knock retard against rpm (six strips), event list (cylinder, rpm, degrees), ignition average vs rpm, `RONADFAC` before and after (a **rising** value means the DME is learning poor fuel or knock *(research)*). Knock retard uses `IGAKNK1–6`; ignition minus map is not knock.

### 6.3 Roll-ons
60 → 100 km/h in 3rd and 80 → 120 km/h in 4th (both editable). Ready: in gear, steady 5–15 km/h below the start speed, pedal below 30 %. Starts on full throttle; time zero is the interpolated crossing of the start speed using `k_g × N`. Result: time, 10 km/h splits, knock summary. Flag when full throttle came less than 3 km/h before the start speed.

### 6.4 Standing starts
0 → 100, 0 → 200 and 100 → 200 km/h, one run gives all that it reaches. Ready: stopped (VS 0 for 1 s). Time zero is **back-extrapolated**: a straight-line fit of speed against time over 3–20 km/h, taken at 0 km/h *(research; replaces today's late start)*. Result: times, splits (0–50, 50–100), launch rpm, shift times (clutch bit), flags for wheelspin (ratio check) and DSC intervention when the definition has it. This replaces the PerformanceWindow.

### 6.5 Speed and gear calibration (Setup)
While open, captures each gear automatically once speed and rpm are steady (≥ 3 s, speed ≥ 50 km/h, light pedal). Shows per gear: km/h per 1000 rpm, overall ratio, and the closest Getrag 5-speed ratio *(research: 4.21 / 2.49 / 1.66 / 1.24 / 1.00)*. Optional: type the GPS speed shown on a phone; the speedometer error it gives **corrects the km/h-per-rpm figures**, so speed thresholds (roll-ons, standing starts) and the power estimate use true speed. Without a GPS entry the speedometer is trusted and the result says "speed not GPS-checked" in its details *(call)*. Saved to settings (not a result) and used by every test. The **Car** item in the Setup group holds the values for the power estimate (section 7): test mass with driver, tyre size, gearbox (manual, automatic), and the advanced defaults. These live only on the Performance tab, not in the Settings dialog, so one screen owns them *(call)*.

### 6.6 Link test (Setup)
30 s with the engine idling or the car parked: samples per second (median), the interval spread (min, max, 95th percentile), gaps over 300 ms, retries. Plain advice when the rate is under 15/s (for example the USB adapter's latency timer). Can be saved as a result.

## 7. Power estimate

Shown as **wheel power and torque, estimate** *(anti-goal: never presented as measured)*.
- Speed `v = k_g × N` (m/s); acceleration from a local quadratic fit of rpm against time over ±0.35 s (at least 5 samples), `a = k_g × dN/dt`. Never differentiate `VS`.
- Effective mass `m_eff = m + J_w / r² + J_e × (dN/dv)²` with defaults `J_w` 4.5 kg·m² (four wheels), `J_e` 0.20 kg·m², `r` from the tyre size.
- Force `F = m_eff × a + ½ × ρ × CdA × v² + Crr × m × g`; defaults CdA 0.60 m², Crr 0.011; air density from `AMP` and intake temperature.
- Wheel power `P = F × v`; torque at the crank speed `T = P × 60 / (2π × N)` (a wheel figure, before losses).
- Defaults for an E46 325i saloon manual: about 1410 kg kerb, so the test mass defaults to 1480 kg with driver *(call; the owner confirms in the Car card)*.
- No DIN or SAE correction in this release *(call)*; the compare view flags condition mismatches instead (intake air more than 5 °C apart, pressure at least one sensor step, 2.1 kPa, apart).
- Validity: sign of slope is unknown on public roads; the result says "one direction" unless the owner marks the run's direction *(call: an optional A/B direction field on the result)*.

## 8. Saved result

One file per result, JSON, schema version 1, in a `results` folder beside settings.json (`%AppData%\MS43Tuner\results\`; always derived from the settings folder so tests never touch the owner's data) *(call: the logs folder is user-movable)*, written atomically (temp file then move, the temp removed on failure). File names come from the time (`perf-yyyyMMdd-HHmmss.json`, `-2` on a clash); the owner's name lives inside the file, so renaming never renames the file. One unreadable file never breaks the list; a file from a newer schema is listed read-only. Contents:
- **Identity**: id, name *(owner)*, description *(owner)*, direction (optional), created time, app version.
- **Test**: kind, preset, gear, band or speeds, the settings used (full-pedal value, `k_g` source).
- **Tune**, captured when the run is armed (the owner may switch bins before saving): bin file name and path, firmware id, calibration name, XDF title and file, unsaved edits yes/no and which items, SHA-256 of the saved file and of the working image, the rev limit and launch-control settings the run used, and a **calibration snapshot**: 0x70000 to 0x7FFFF of the *saved* image (unsaved edits cannot be in the car), zlib-compressed and base64, about 43 KB, so two results can be compared table by table even after the bins changed on disk *(call)*. No bin open: the Tune section is empty and the result says so.
- **Conditions** at arm, start and end: coolant, oil (or not reported), intake air, pressure, battery, `RONADFAC`, long-term trims, the flags of section 5.
- **Car** values used by the estimate.
- **Figures**: the test's computed numbers and the per-bin table.
- **Samples**: every sample of the window plus 2 s before it, stored by column: one start wall time, then per sample the monotonic request time and the raw payload in base64, so any channel or status bit can be decoded again later; the definition's variant (`ms43x`, `b25`, ...), file name and SHA-256. About 90 to 120 KB per result.
- **Flags**: code, severity (info, suspect, invalid, cold), message, sample range; overall verdict.

## 9. Screens

### 9.1 Performance tab *(mockup A)*
- Header gains a fourth tab, **Performance**, after Compare. There are no tab shortcuts today, so none is added. The header must still pass the 1024 px no-overlap check in its worst state (connected on a slow link, unsaved edits); recover the about 99 px the tab needs under the compact header by narrower tab padding (12 to 8 px), tighter brand and tab margins, and dropping the words "slow link" while the rate value and its icon stay *(call)*.
- Primary button: on Performance, **Connect** is primary while disconnected (a test needs the link) and the test's **Arm** is primary once connected; the header's Save is never primary there *(call)*.
- Shortcuts: Ctrl+B collapses or opens the Performance rail; Ctrl+K focuses the results search on Results; Ctrl+Z does nothing on Performance.
- Left rail (resizable and collapsible like Tune's, shared `ISidePanel`): a **Run / Results** segment, then the tests grouped: Pulls (Full-throttle pull, Knock check), Acceleration (Roll-on, Standing start), Setup (Speed calibration, Car, Link test, Pedal). Each test shows a one-line summary and, when a channel is missing, why it is off.
- Pane, **Run**: banners; the test's options (gear segment, band, speeds); the **Arm** primary button; then the ready check, running or result view as in the mockup.
- Pane, **Run**, result view: the name (pre-filled, for example "3rd pull 14:32") and the description are edited inline under the figures, then **Save result** (primary at that moment), Discard, Run again *(mockup)*.
- Pane, **Results**: search, a list (name, test, date, tune, headline figure, flag tags), row actions (rename, edit description, delete with confirmation via the existing confirm dialog), tick two (a per-row tick, not list multi-select) and **Compare**. Opening a result shows its full result view, read-only except name and description.
- **Analysis is a pure function** of a result's raw samples, its definition, the Car values and the speed calibration. A result stores the figures computed when it was made, plus the inputs used *(call)*.
- The Live screen's "Performance tests" button opens this tab; `PerformanceWindow` and its view-model code in `MainWindowViewModel` are removed, and their tests move to the new screen.

### 9.2 Compare two results *(mockup)*
- Overlay chart against rpm (pull tests) or time from start (speed tests): **A in Ink1, 2 px solid; B in Ink2, 1.5 px dashed**, with a legend, so shape carries the difference (system 2.4); traces are data, never orange or coral *(call)*. A segment picks the trace: Power, Torque, Ignition, Knock, VANOS, AFR, MAF, rpm vs time. While a run is live, its trace is Ink2 with the newest point as the Now dot (Ink3 when stale), as the Tune curve does.
- Knock bars: six bars, Now fill while live, Ink2 in a stored result; a cylinder at or above 1.5 degrees of retard also gets the warning icon and a text line, never colour alone *(call)*.
- **Compare recomputes both results** from their raw samples with the current Car values and speed calibration, so a change of mass, tyres or calibration between the two runs never shows up as a tune gain; when the stored figures differ from the recomputed ones, a note says so *(call)*.
- Two pulls with different rpm bands are compared over the **rpm range they share**; interval times are shown only for marks inside both bands *(call)*. The last band used per preset is remembered, so before and after runs normally match.
- Difference panel: headline deltas (times, peak power), condition mismatches highlighted, and the **tables that differ** between the two calibration snapshots (via the existing BinComparer), each with its largest change.
- Cold results are hidden from the compare picker by default ("Show cold runs" toggle) *(call)*.
- When at least three saved, non-cold results share the test, gear and tune fingerprint (saved-image SHA-256), the headline shows their spread over the shared rpm range as "noise ±x" *(call; the full repeatability set is later)*.

### 9.3 Live
- The warm-up banners at the top.
- Gear readout fixed (4.1).
- Oil temperature pinned by default next to coolant *(call)*.

## 10. Left to the builder
- Class and file layout, the chart control's internals, and the test engine's internal state machine.
- Exact JSON property names and compression (deflate + base64 is fine).
- How the rail's test rows and the results list reuse existing list styles.
- Text of flags and advice, within the design system's voice.
- Whether the knock strips and the overlay share one chart control.

## 11. Open risks
- The real sample rate is unknown until the link test runs on the car; below about 10/s, knock events can fall between samples (flagged "may be undercounted").
- Whether `TOIL` is measured or modelled by the DME is unverified; the not-reported rule covers a frozen value.
- Gear detection relies on the DME's own GEAR (computed from N/VS), so the ratio check is not independent; the calibration's `k_g` is the independent check.
- Byte order for 16-bit ms43x channels was proved from the ADX and firmware, not from a live capture *(memory: owner car check pending)*.
