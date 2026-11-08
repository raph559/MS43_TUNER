# MS43 Tuner design system

Every MS43 Tuner screen (Live, Tune, Compare, Performance, Settings and the dialogs) is built on this system. There are two themes, Day and Night, with one layout: they differ only in the colour tokens (section 1) and the map colour stops (section 2).

Four rules hold the system together:

1. **Warm hues are signals, cool hues are data.** Orange means now, red means read this warning, coral means "A is higher" in Compare. Maps use cool hues only, so the live cursor is the only warm mark on a map.
2. **Every number is set in Bahnschrift with tabular figures.** The tabular figures keep changing readings aligned (section 10).
3. **The pane is the highest-contrast surface in both themes.** Data sits on it. Frame and fill step toward mid grey.
4. **Nothing is invented.** Values, limits, counts and names come from the bin, the XDF, the ADX or the logger. Where a value is missing the screen says `n/a`.

---

## 1. Colour tokens

### 1.1 Surfaces, lines, text, accent, warning

Three surface tones in all: frame, pane, fill.

| Token | Role | Day | Night | Reason |
|---|---|---|---|---|
| `Frame` | Window canvas: header, rail, inspector, status bar | `#F2F4F7` | `#101215` | One step toward mid grey so the chrome recedes behind the data pane. |
| `Pane` | Data surface: grid, Live cluster and channel list, Compare grid, inputs, dialogs (Day) | `#FFFFFF` | `#0A0B0D` | The tone farthest from mid grey in each theme, so numbers sit on the highest contrast. No shadow implies depth in either theme, so the light-up/dark-down flip is a contrast rule, not a depth flip. |
| `Fill` | Segmented tracks, selected rows and headers, banners, hatch lines, popups (Night) | `#E9ECF1` | `#1F2328` | The third and last tone; marks "selected" or "grouped" without a new hue. |
| `Track` | Dial and bar tracks, off gear slots | `#F2F4F7` (= Frame) | `#1F2328` (= Fill) | Alias, not a new tone: the value it uses in each theme keeps the orange mark at 3:1 or more on top of it (3.18 Day, 6.06 Night). |
| `Hairline` | Decorative separation between frame regions and panes, list dividers | `#E1E5EA` | `#23272D` | Below 3:1 on purpose: it separates areas, it never identifies a control. |
| `Edge` | Boundary of text fields, switch off tracks and checkboxes; idle status dot; the value slider's track beyond the value (5.14) | `#727C8C` | `#6E7681` | 3:1 or more against all three surfaces (WCAG 1.4.11), so the controls with no label inside are findable. |
| `Button` | Secondary button, picker and tool group surface | `#FFFFFF` | `#1F2328` | The fill and label identify the button. WCAG 1.4.11 does not require a contrasting boundary when the text identifies the control. |
| `ButtonEdge` | 1 px edge of buttons, pickers, tool groups and the selected segment | `#CDD3DB` | `#30363D` | Soft on purpose (1.37 to 1.54:1): it shapes the surface, it does not carry identification. |
| `ButtonRaised` | Selected segment; buttons and pickers on a Night dialog card (itself `#1F2328`); picker hover | `#FFFFFF` | `#2D333B` | One step up from `Button`, so a raised control still shows on a `Fill` surface. |
| `Ink1` | Primary text, values, focus ring, primary button fill, selection outline | `#121826` | `#F3F5F7` | Near-black and near-white with a slight cool cast that matches the map hues. |
| `Ink2` | Secondary text, labels, icons, quiet button labels | `#3D4656` | `#AEB4BD` | Labels defer to values but stay far above 4.5:1. |
| `Ink3` | Tertiary metadata, placeholders, `n/a`, disabled labels | `#5B6576` | `#858C97` | Lowest text level; still 4.5:1 or more on every surface. Never used for anything you act on. |
| `Now` | Live marks: cursor dot, value arcs and bars, gear slot, connected dot, now-ticks | `#EA5B0C` | `#FF7A1A` | Marks live activity. Day fill keeps 3:1 on Pane and Frame. |
| `NowText` | Live text: rate, "Connected", live cursor readout | `#B4470A` | `#FF7A1A` | Day orange darkened to pass 4.5:1 as text; Night orange already passes. |
| `Warn` | Warning and error icon, banner stripe, inline error icon | `#B42318` | `#FF6259` | One signal red for every warning and error, always with an icon and text. It sits 12 (Day) and 22 (Night) degrees of OKLCH hue redder than orange, and Day red is much darker than Day orange (1.88:1 apart); the icon and text, not the hue, carry the meaning. |
| `Caution` | Amber: the caution banner's stripe and icon (5.8), a suspect run's flag icon, a condition mismatch in a Performance comparison | `#A15C00` | `#E8A33D` | Added with the Performance tab (performance spec 5): oil below working temperature is worth a look but is not a danger, so it cannot take `Warn`, and orange means now. Only ever a stripe or an icon beside `Ink1` text, never text or a fill, so amber is never the only cue. |
| `Hover` | Overlay on any surface under the pointer | `Ink1` at 6 % | `Ink1` at 8 % | An overlay instead of a fourth surface tone. |
| `Pressed` | Overlay while pressed | `Ink1` at 12 % | `Ink1` at 14 % | Same reason; the pressed label stays 4.5:1 or more (below). |
| `Shadow` | The one Day elevation (popups only) | `0 8 24 -8 #3D121826` | none | See section 4. |

Map stops (`Map1` to `Map7`) and the Compare pair (`DivBelow`, `DivAbove`) are in section 2. Cell text uses `CellDark #000000` and `CellLight #FFFFFF` (section 2.3).

### 1.2 Contrast ratios

Text needs 4.5:1, non-text (edges, marks, focus) needs 3:1.

| Foreground | Day on Pane / Frame / Fill | Night on Pane / Frame / Fill | Use |
|---|---|---|---|
| `Ink1` | 17.73 / 16.09 / 14.97 | 18.02 / 17.17 / 14.45 | text, focus ring, selection outline |
| `Ink2` | 9.51 / 8.63 / 8.03 | 9.43 / 8.99 / 7.57 | text, icons |
| `Ink3` | 5.89 / 5.34 / 4.97 | 5.81 / 5.53 / 4.66 | text |
| `NowText` | 5.46 / 4.96 / 4.61 | 7.55 / 7.19 / 6.06 | text |
| `Warn` | 6.57 / 5.97 / 5.55 | 6.70 / 6.38 / 5.37 | icon, stripe, may be text |
| `Caution` | 5.19 / 4.71 / 4.38 | 9.13 / 8.70 / 7.33 | icon, stripe (never text) |
| `Edge` | 4.22 / 3.83 / 3.56 | 4.29 / 4.08 / 3.44 | control boundary |
| `Now` (fill) | 3.50 / 3.18 / **2.96** | 7.55 / 7.19 / 6.06 | non-text marks |
| `Hairline` | 1.27 / 1.15 / 1.07 | 1.31 / 1.25 / 1.05 | decorative only |

Other pairs: primary button label (`Pane` on `Ink1`) 17.73 Day, 18.02 Night; on hover (`Pane` on `Ink2`) 9.51 Day, 9.43 Night. `Ink2` on `Hover` over Pane 8.42 Day, 7.99 Night. `Ink2` on `Pressed` over Fill 6.33 Day, 4.99 Night; `Ink1` there 11.80 Day, 9.52 Night.

Rules that follow from the table:

- Day `Now` fails on `Fill` (2.96). A Day orange mark never sits on Fill; where a live mark must appear on a selected row or banner, use `NowText` (4.61).
- `Ink3` drops below 4.5:1 on Pressed, so no interactive label is ever Ink3. Interactive labels are Ink1 or Ink2.
- `Hairline` never marks a control. If removing it would hide a control, the control needs `Edge`.

### 1.3 Focus ring

A 2 px `Ink1` ring drawn outside the control with a 2 px gap in the colour of the surface the control sits on. Corner radius = control radius + 4. Never orange, never inside the control. Measured: Day 17.73 (Pane), 16.09 (Frame), 14.97 (Fill); Night 18.02, 17.17, 14.45. Every focusable control gets it: tabs, file menu, Save, port and definition pickers, Rescan, Connect, search fields, switches, rail rows, toolbar buttons, the Value field, the grid (on its active cell), Pin, segmented controls, the value slider's thumb and its − and +, Live rows and pins, dialog buttons. In Avalonia: a shared `FocusAdorner` template, `outline: none` style resets are forbidden.

### 1.4 Orange dose (one accent, now only)

Orange marks the thing that moves with the engine and the state of the link. Live numerals stay `Ink1`: a number on a live screen is already "now", and colouring every number orange would empty the accent of meaning.

| Orange (`Now` / `NowText`) | Never orange |
|---|---|
| Live cursor dot, its trail and its axis ticks on a map | Buttons, including Connect |
| Value arc tip and arc on dials, bar fill in tiles, active gear slot, strip "now" tick and cursor line | Selection, focus, hover |
| Connected dot and the word "Connected" | Live numerals (rpm, load, advance and every channel value) |
| The app mark's arc and needle while logging (5.1) | The app mark at rest |
| Logging rate ("38 samples/s") | Sparkline lines and end dots (Ink2 line, Ink1 end dot) |
| Live cursor readout under the grid | Idle live-cursor legend (drawn as an Ink2 outline dot) |
| Performance, while a run is live: the knock bars' fill, the run's progress bar fill, the newest point of the live run chart (the `Now` dot) | Run traces on every chart, live or stored (5.17); knock bars in a stored result (Ink2) |
| | Static zones such as the rev limiter band (Ink2) |
| | The caution banner (amber `Caution`, 1.1) |

When samples stop (link lost, cursor stale after 1 s), every orange mark turns `Ink3` and the screen shows the age; the live run chart's `Now` dot becomes an `Ink3` ring. An idle screen (not connected) shows zero orange.

---

## 2. Map colour

### 2.1 Shared hue path

One hue path, sea-teal at the low end to ultramarine at the high end (OKLCH hue 182 to 270). Reason: it holds no warm hue, so the orange cursor, the red warning and the coral "higher" are never confused with a map value; and the hue turns as the value rises, a second cue on top of lightness that separates neighbouring bands. It stops at ultramarine and never reaches violet.

The same stop index has the same hue in both themes. Only lightness is adapted: **the higher the value, the farther the cell is from the pane tone.** Day runs light to dark, Night runs dark to light, so the peak of a map stands out in both themes and the shape of the map reads the same.

| Stop | Hue | Day | Day text | Ratio | Night | Night text | Ratio |
|---|---|---|---|---|---|---|---|
| `Map1` (lowest) | 182 | `#CCF1EA` | black | 17.34 | `#02423A` | white | 11.39 |
| `Map2` | 198 | `#9EDFE0` | black | 14.09 | `#005C5E` | white | 7.80 |
| `Map3` | 214 | `#6DC7DB` | black | 10.84 | `#077688` | white | 5.31 |
| `Map4` | 230 | `#4FAAD3` | black | 8.02 | `#368EB3` | black | 5.68 |
| `Map5` | 246 | `#3D89C6` | black | 5.59 | `#68A4D9` | black | 7.90 |
| `Map6` | 258 | `#3568B2` | white | 5.56 | `#97BDF4` | black | 10.90 |
| `Map7` (highest) | 270 | `#324496` | white | 8.74 | `#C8D6FF` | black | 14.52 |

Stops sit at t = 0, 1/6, 2/6 and so on up to 1, and are mixed linearly in sRGB between neighbours. Neighbouring stops differ by 1.23 to 1.57:1, so the map's shape reads; with the colour range fitted to the working cells, the example map's 20 to 39 °CRK band spans `Map4` to `Map7` (3.5:1 apart) instead of the 2.07:1 of the old Day ramp.

The surface plot in the inspector and the colour-scale bar use the same stops. No other ramp exists in the app (the old "deep-sea" `HeatRamp` and look B's viridis ramp are replaced).

### 2.2 Colour range and the storage limit

- The colour range is the minimum and maximum of the item's **working** cells.
- A cell whose raw value is the storable minimum or maximum (raw 0 or raw 255 for an 8 bit map) is **at the storage limit**. It is hatched (45 degree, 1 px `Fill` lines every 4 px on `Pane`), keeps its number in `Ink1` (17.73 Day / 18.02 Night on Pane, 14.97 / 14.45 on Fill) and is left out of the colour range **only when it is an outlier**: fewer than half of the item's cells sit at that limit, and the limit value lies outside the range of the other cells by at least a quarter of that range. Reason: in `ip_iga_ron98_pl__n__maf` the 6990 rpm row holds raw 0 (-23.6 °CRK) in every column, 26.6 °CRK below the working minimum of a 36 °CRK range; it would otherwise push the 304 working cells into the top 57 % of the ramp. Ordinary values that happen to sit at a raw limit stay normal cells: raw 0 meaning 0 next to small positive values, raw 255 meaning the neutral factor 0.996, and the 40 tables of the MS43X001 XDF that are entirely at one limit (in the analysed MS43X001 bin: 347 of 1479 tables have limit cells, 81 are at least half at a limit).
- A map whose cells all hold one value is drawn in `Map4` with text per 2.3, and the scale caption says so: "All 36 cells are 0.00 %".
- The scale caption says what happened: "Colour: 3.0 to 39.0 °CRK. Hatched cells are at the storage limit (-23.6)." The surface plot leaves hatched cells out of its height range and says so under the plot ("The 6990 rpm row (storage limit) is not drawn."); with no hatched cells it draws every cell.
- Cell fill is a redundant encoding: every cell prints its number at 4.5:1 or more, so a light cell that sits close to the Day pane (`Map1` is 1.21:1 against white) is still found by its number and its place in the grid. The grid block itself is framed by its headers.

### 2.3 Per-cell text rule

Cell text is `#000000` when the fill's relative luminance is 0.1791 or more, otherwise `#FFFFFF`. At that threshold both choices give 4.58:1, so **every possible fill, including any mix between stops, the flash frames and the Compare mixes, gets 4.58:1 or more.** Checked over 121 interpolated colours per theme: minimum 4.59. Pure black and white are required: with `Ink1` (`#121826`) in place of black the guarantee drops to 4.20:1 near mid-grey fills.

### 2.4 Cell states (shape first, colour second)

Each state has its own shape, so none depends on colour alone.

| State | Drawing (both themes) | Reason |
|---|---|---|
| Normal | Fill from 2.1, number centred, Bahnschrift 12, text per 2.3. Cells 2 px apart, radius 4 | Gutters draw the grid without lines. |
| Storage limit | Hatch from 2.2 | Says "the byte cannot go further", out of the colour range. |
| Selected range | One rectangle around the whole range: 2 px `Ink1` outside, 1 px `Pane` keyline inside it. Row and column headers in the range: `Fill` background, `Ink1` SemiBold | The double line is visible on any fill (one of the two lines always passes 3:1). It is a rectangle, not a ring around each cell. |
| Keyboard focus (grid focused) | The focus ring from 1.3 around the active cell, outside its bounds, drawn above everything | Gap ring is reserved for focus; the selection rectangle sits inside the cells, the focus ring outside. |
| Changed since save | 6 px right-angle triangle in the cell's top-right corner, in the cell text colour (4.58:1 or more against its own fill) | The same triangle marks changed items in the rail and the unsaved chip in the header. |
| Differs from B (Compare A or B view) | 6 px ring (circle outline, 1.5 px) in the cell's top-right corner, cell text colour | Ring = differs, everywhere (rail too). |
| Live cursor | 8 px `Now` dot with a 2 px `Pane` halo at the interpolated position (it sits between cells, where the ECU reads). Trail: the controller's last fixes as a polyline in `Now`, 2 px for the newest segment thinning to 1 px for the oldest, each with a 1 px `Pane` casing. Axis ticks: a 3 px `Now` bar in the row and column header gutters at the same position | A dot, not a ring, so it cannot be mistaken for selection. Age is shown by width, not transparency, so every visible segment keeps its contrast (halo: 3.50 Day, 7.55 Night). No crosshair across the cells: orange on mid-ramp cells is about 1.0:1. |
| Live cursor stale (no sample for 1 s) | Dot becomes an 8 px `Ink3` ring with the `Pane` halo; trail and ticks hidden; readout turns `Ink3` and ends with "(stale)" | Orange only while data is live. |
| Live cursor off the map | Dot clamped to the grid edge as a ring in `Now`; readout ends with "(off the map)" (existing copy) | |
| Typing in the cell | Active cell: `Pane` fill, 2 px `Ink1` inset border, typed digits right-aligned in Bahnschrift 12 with a caret. Key line: "Enter sets 12 cells to 27.8 °CRK. Esc cancels." | Matches MapGrid: Enter sets the selection, Esc cancels. |
| Just edited | 450 ms flash, section 10.3 | |

### 2.5 Compare A − B (diverging pair)

Zero is the pane colour; A below B mixes toward blue, A above B toward coral, by |t|^0.7 (existing `HeatRamp.Diverging`), t = (A − B) divided by the largest absolute difference in the item. A is the bin tuned and B the one compared with, so the grid reads how far A is from B. Coral is taken on its pink side (hue 8 to 12) so it stays 30 degrees or more from the orange accent.

| Token | Day | Night | Text at the end | Mid mix (t = 0.5) |
|---|---|---|---|---|
| `DivBelow` (A lower) | `#1F53B8` | `#6CA7F5` | Day white 7.03, Night black 8.48 | Day `#7595D3` black 7.00, Night `#466B9C` white 5.46 |
| `DivAbove` (A higher) | `#CE2854` | `#F87F99` | Day white 5.18, Night black 8.49 | Day `#E17B96` black 7.48, Night `#9D5263` white 5.50 |

Cell text follows 2.3 (lowest mix measured: 4.68 Day, 5.46 Night). Cells with no difference keep the `Pane` fill and print `0.0` in `Ink3` (5.89 Day, 5.81 Night) so the changed cells carry the colour. The scale caption reads "A − B: -3.0 to +3.0 °CRK, 12 of 320 cells differ" with the real numbers.

---

## 3. Type

### 3.1 Font roles

| Role | Font | Reason |
|---|---|---|
| UI text | Segoe UI Variable Text (11 to 16 px), Segoe UI Variable Display (20 px); fallback Segoe UI | Windows native, hinted for small sizes. |
| Every data value | Bahnschrift with `+tnum` | DIN 1451, the German road-sign and automotive voice; it ties the screen to the car and is the identity motif. Installed with Windows. |
| Raw identifiers | Cascadia Mono 12 (13 SemiBold for the map id in the title line) | Ids, addresses and scalings must be read character by character; monospace does that at small size, never as a large heading. |

Data values: grid cells, breakpoints, Live readings and channel values, rate, elapsed time, sample and row counts, rail counts, details values, scale ticks, the Value field, sizes like "20 × 16". Raw identifiers: XDF item ids (`ip_iga_ron98_pl__n__maf`), axis ids (`ldpm_n_14`), channel ids (`N`, `MAFIGA`), addresses (`0x77315`), scaling formulas (`0.375*X-23.625`), firmware id (`MS43X001`), ADX definition (`ms43x`). File names (bin, XDF, CSV) and port names (`COM1`) are UI text in Segoe UI, truncated in the middle.

### 3.2 Scale

| Step | Font | Size / line | Weight | Used for | Reason |
|---|---|---|---|---|---|
| UI 11 | Segoe Text | 11 / 14 | 400, 600 | kbd hints ("Ctrl K"), scale units, surface axis labels | Smallest crisp Segoe size; never on anything you act on. |
| UI 12 | Segoe Text | 12 / 16 | 400, 600 | labels, key line, inspector labels, status bar, tooltips, chips | One step under body so labels defer to values. |
| UI 13 | Segoe Text | 13 / 18 | 400, 600 | body, buttons, inputs, menus, rail rows, tabs | Windows body size; readable at a desk and at arm's length in the car. |
| UI 16 | Segoe Text | 16 / 22 | 600 | wordmark, dialog titles, Live pane titles | The single heading step inside the shell. |
| UI 20 | Segoe Display | 20 / 26 | 600 | empty-state titles | The only large text that is not data. |
| Data 12 | Bahnschrift | 12 / 16 | 400 (600 selected headers) | grid cells, breakpoints, counts, ticks | Fits "-23.6" in a 44 px column at the 1024 px minimum. |
| Data 13 | Bahnschrift | 13 / 18 | 400, 600 | numbers inside 13 px lines, Value field | Matches the surrounding UI 13 line. |
| Data 16 | Bahnschrift | 16 / 20 | 600 | Live channel values, edit-limit value | Glanceable in the car. |
| Data 24 | Bahnschrift | 24 / 28 | 400 | strip readouts, gear and speed, pinned tiles, a run's elapsed time and result figures | Context readings. |
| Data 40 | Bahnschrift | 40 / 44 | 400 | load and advance on dials | Second tier. |
| Data 56 | Bahnschrift | 56 / 60 | 400 | rpm on dials; load and advance in tiles; a value's reading on the Tune pane; the running reading on Performance | Primary reading. |
| Data 88 | Bahnschrift | 88 / 92 | 350 (SemiLight) | rpm in the tiles layout only | Hero; SemiLight keeps the large figure from turning heavy. Never 300. |

### 3.3 Numbers (checked against `bahnschrift.ttf`)

- **Tabular figures are mandatory.** Bahnschrift's default digits are proportional ("1" is 681 units, "0" is 1130); its `tnum` feature fixes them. Avalonia: `FontFeatures="+tnum"` on controls, `FormattedText.SetFontFeatures` in MapGrid, Gauge, Sparkline and the other drawn controls.
- **Minus is "-" (U+002D) in every number.** Bahnschrift has no U+2212; MapGrid's current replacement of "-" by U+2212 must go, or the minus falls back to another font. UI labels in Segoe UI keep the true minus where it is part of a name: "A − B" (Compare, and two Performance runs with the newer as A).
- **No digit grouping**: 2480 rpm, 7104 rpm, 9576 rows. Bahnschrift has neither a thin nor a narrow no-break space, and right-aligned tabular columns do not need grouping.
- **No arrows and no "≈" inside a Bahnschrift run** (both missing). Write words: "map 27.5 °CRK".
- Decimals. The Live channel list and the pinned tiles show each channel at its ADX precision (`digcount`), as the app does today (MAFIGA 112.41, AMP 101.20). The readout band (tiles and dials) rounds for a glance: Engine Speed and Vehicle Speed whole numbers, Engine Load Ignition and Ignition Angle Average one decimal, Gear whole (today's gauges round to 0, 0 and 1 decimals). Map cells, breakpoints and the Tune live readout keep the XDF item's decimals, as today. One formatter per role, so a value never shows two precisions inside the same widget.
- Missing value: `n/a` in Segoe UI, `Ink3`. Never "0", and never a dash (the app's current placeholder is an en dash; it goes).
- Time: `00:04:12`, Bahnschrift.
- Width: if a column is narrower than its widest number plus 8 px, cells switch to Bahnschrift SemiCondensed; if still too narrow, the grid scrolls horizontally with a sticky row header. Verify in the build that `FontStretch="SemiCondensed"` and weight 350 resolve to Bahnschrift's named instances; otherwise use the family names "Bahnschrift SemiCondensed" and "Bahnschrift SemiLight".

---

## 4. Space, radius, elevation, borders

### 4.1 Spacing (4 px base)

| Step | Use | Reason |
|---|---|---|
| 2 | grid cell gutter, segment inset | Draws the grid without lines. |
| 4 | icon to label, inside chips | Ties an icon to its label. |
| 8 | between controls in a group, list row padding | One unit apart reads as one group. |
| 12 | between groups in a toolbar, between label and its field | Separates groups without a divider. |
| 16 | pane padding at 1024, section gaps in the inspector | The base pane margin. |
| 24 | pane padding at 1440, title to toolbar to grid | Separates the pane's blocks. |
| 32 | empty-state blocks | Lets a lone message breathe. |

### 4.2 Radius

| Radius | Use | Reason |
|---|---|---|
| 4 | grid cells, selection rectangle, chips, kbd hint, tooltips | Small things in dense areas; a larger radius eats cell area. |
| 8 | buttons, inputs, pickers, segmented tracks, list rows, menus | Everything you click. |
| 12 | dialogs, Live dial and tile panels | Surfaces that hold other things. |
| Nested | inner radius = outer minus padding (segments 6); focus ring = control + 4 | Concentric corners. |
| Circle / pill | status dot, live dot, switch knob and switch track, value slider thumb only | The switch knob and the slider thumb are grabbed and dragged, and the round shape says so; the switch's round track shows its travel. No pill buttons, inputs or chips, and the slider track keeps square ends. |

### 4.3 Elevation

- **Day: one level, popups only** (menus, picker drop-downs, tooltips, dialogs, the inspector overlay below 1280 px): `Shadow` plus a 1 px `Hairline`. Reason: these overlap content and must lift off what is under them. Panes, buttons, cells, tiles and the selected cell have no shadow; panes are separated by flat tone and a hairline.
- **Night: none.** Popups use `Fill` with a 1 px `Edge`. Reason: a shadow on `#0A0B0D` is invisible; the edge is the separation.
- Dialog scrim: `Ink1` at 30 % (Day), `#000000` at 50 % (Night).

### 4.4 Borders

| Line | Token | Where |
|---|---|---|
| Hairline | `Hairline` | between header, rail, pane, inspector, status bar; list dividers; Day popups |
| Control edge | `Edge` | text fields, switch off track, checkbox, the value slider's track beyond the value, idle status dot, Night popups |
| Button edge | `ButtonEdge` | secondary buttons, pickers, tool groups, selected segment |
| State line | `Ink1` | selection rectangle, selected tab underline, selected rail row bar, focus ring |
| Warning stripe | `Warn`, 3 px left | warning banners only (it marks real state) |

No border around tiles or cards: Live tiles are pane areas divided by hairlines, like a table.

---

## 5. Components

All controls are 32 px tall (Live list rows and pins 32 px hit areas for use in the car). Labels are UI 13 unless stated. States: rest, hover (`Hover` overlay), pressed (`Pressed` overlay), focus (1.3), disabled. Their state changes follow section 10.3.

### 5.1 Header (48 px, `Frame`, hairline below)

Left to right:

1. **App mark and wordmark.** The app's dial mark follows the icon style of section 6: a 20 px dial, a 270 degree arc open at the bottom and a needle, 1.5 px stroke, butt caps. At rest: arc `Ink3`, needle `Ink2` at the rest position. While connected and logging: arc and needle in `Now`, the needle at the current engine speed (0 to 8000 rpm over the 270 degree sweep), redrawn with the live readings and never tweened. Then the wordmark "MS43 Tuner", UI 16 SemiBold `Ink1`. Below 1280 px wide the wordmark is hidden (the window title carries it) and the mark stays.
2. **Tabs** Live / Tune / Compare / Performance: text tabs, 48 px tall hit areas, 12 px side padding. Selected: `Ink1` SemiBold and a 2 px `Ink1` underline on the header's bottom edge. Others: `Ink2`, hover overlay. Keep the tabs as text controls rather than a segmented picker. No tab has a shortcut. Below 1280 px the tab padding drops to 8 px and the margins after the mark and after the tabs to 8 px, so the fourth tab fits at 1024 px in the header's worst state (connected on a slow link, unsaved edits; `HeaderTests.HeaderAndStatusBarNeverOverlap`).
3. **File block** (all tabs): file menu button = bin name, UI 13 SemiBold, middle ellipsis, then a chevron. Menu: "Open bin…" (Ctrl O), "Use another XDF…", "Save as…" (Ctrl Shift S). At 1280 px and wider a second line under the name: "Firmware" `Ink3` + `MS43X001` Cascadia `Ink2` + "XDF" `Ink3` + XDF file name `Ink2`, UI 12. Then the **save-state chip** and **Save**.
   - Clean: chip "Saved" with a check icon.
   - Dirty: chip "1 item not saved" (count of items, from `ModifiedItems`) with the changed triangle, `Ink1` SemiBold, never orange. Grid key and rail say cells and items with the same nouns (copy rule 9.3).
   - **Save**: primary button on Tune and on Compare after a copy, never on Performance; disabled when clean with the tooltip "Nothing to save".
   - No bin: "No bin open" in `Ink3` and no chip.
4. Flexible space.
5. **Rate** (only while connected): "38" Data 13 SemiBold `NowText` + " samples/s" UI 12 `Ink2`. Connected but nothing yet: "No samples yet" `Ink2`. Under 10 samples/s: the warning icon and "slow link"; below 1280 px the words "slow link" go and the rate and its icon stay. Hidden when not connected.
6. **Port** picker ("COM1"), **Rescan ports** icon button, **Definition** picker (`ms43x` in Cascadia). Below 1280 px these three merge into one picker button "COM1 · ms43x" that opens a popover with the same three controls (accessible name "Connection settings").
7. **Connect** / **Disconnect**. Connect is the primary button on Live and on Performance when not connected (a test needs the link); a secondary button on Tune and Compare (Save is their primary). Connected: "Disconnect" as a quiet button, and on Performance the chosen test's Arm becomes the primary.
8. **Settings**: a 32 px quiet icon button with the gear icon at the right end, tooltip "Settings (Ctrl ,)", accessible name "Settings". It opens the Settings dialog (5.12).

### 5.2 Buttons

| Kind | Rest | Hover | Pressed | Disabled | Use |
|---|---|---|---|---|---|
| Primary | `Ink1` fill, `Pane` label SemiBold | `Ink2` fill | `Ink1` fill | `Fill` fill, `Ink3` label | One per screen: Save (Tune, Compare after a copy), Connect (Live and Performance, not connected), on Performance the test's Arm (connected), Save result (a run finished) or Save calibration (capturing), the main action of an empty state. |
| Secondary | `Button` fill (`ButtonRaised` in a dialog), 1 px `ButtonEdge`, `Ink1` label | `Hover` | `Pressed` | no fill, `Hairline` edge, `Ink3` label | Second actions: Open XDF…, Copy B into A, Browse…, banner actions. |
| Quiet | no fill or edge, `Ink2` label | `Hover` | `Pressed`, `Ink1` label | `Ink3` label | Pin, Change…, Edit in Tune, Disconnect, Performance tests (opens the Performance tab), Show details, Run again, Back to the list, and a result row's Delete…, Open and Rename. |
| Tool group | one `Button` surface with a `ButtonEdge` outline around several text buttons, 1 px `ButtonEdge` dividers | per button | per button | per button | Map toolbar: [Value][Set][Add][Scale %], [Interpolate][Smooth] with an X / Y / XY segmented control on maps (TunerPro's directions; remembered), [Undo][Redo]. With a B open, the compare controls leave the toolbar for a `Hairline` strip under it, "Compare with <B file>": [A][B][A − B], [Units][Raw][%] while A − B shows (one setting with Compare), Copy from B. |

Disabled keeps its label at 4.5:1 or more (`Ink3`) and loses its boundary (Hairline), which is what says "not operable". Every disabled control has a tooltip with the reason: "Nothing to undo", "Select a range first: Interpolate fills between its corners."

### 5.3 Inputs and pickers

Text fields: `Pane` fill, 1 px `Edge`, radius 8, 32 px, text `Ink1`, placeholder `Ink3`. Pickers take the button look: `Button` fill, 1 px `ButtonEdge`, `ButtonRaised` on hover; their label and chevron identify them. Number fields (Value, any numeric entry) set typed digits in Bahnschrift 13 with the unit as a suffix in `Ink3`. Focus shows the ring, not a border colour change. Invalid input: the field keeps its edge; the message appears under or beside it with the warning icon and text ("'abc' is not a number."). Pickers add a chevron in `Ink2`. The Value field is never prefilled.

Search fields (Tune rail, Live channel filter, Compare list) share one shortcut, **Ctrl K**, shown as an 11 px kbd hint, and a Clear button (icon only) once text is typed.

### 5.4 Segmented control

Track `Fill`, radius 8, 2 px inset. Selected segment: `ButtonRaised` fill, 1 px `ButtonEdge`, `Ink1` SemiBold against `Ink2` for the others (the label weight and ink carry the state; the fill is 1.18 Day, 1.24 Night against the track). Others: `Ink2` on the track, hover overlay. Inside a Night dialog, whose surface is itself `Fill`, the track is `Frame` so it stays visible; the selected segment stays `ButtonRaised`. Used for: Night / Day (status bar, accessible name "Theme"), A / B / A − B (Compare), trend window chips (Live strips), and on Performance Run / Results, the gear, the direction A / B, the gearbox and the compare chart's trace. Not used for the main tabs.

### 5.5 Switch and checkbox

Switch 36 × 20, track radius 10. In the Settings dialog the label sits left and the switch right, so every control in the dialog shares one right edge; elsewhere the label sits to the right of the switch. Off: `Pane` track, 1 px `Edge` (3.83 Day, 4.08 Night on Frame), 14 px knob in `Ink3` at the left. On: `Ink1` track, `Pane` knob at the right. Position and fill both change, so the state is never colour only. The label sits to the right and is part of the hit area. Checkbox: 16 px box with the same off and on tokens and a check icon; the Performance results tick two rows with it to compare them. Switches: "Changed only", "Differs from B", "Pinned only", "Write CSV log", "Show cold runs".

### 5.6 List rows

28 px in the rail, 32 px in Live. Hover overlay. Selected: `Fill` background, 2 px `Ink1` bar on the left edge (state marker, 14.97 Day / 14.45 Night), label `Ink1` SemiBold.

- Rail category: disclosure chevron, name UI 13 `Ink2`, count Data 12 `Ink3` right-aligned. Categories expand in place.
- Rail item (indented 20 px): kind glyph `Ink2`, item id Cascadia 12, then markers on the right: changed triangle, differs ring, pin. The current item is selected.
- Groups at the top of the rail: "Pinned", then "Recent", then the 65 categories in XDF order. The list scrolls; nothing is cut from it.
- Live channel row: pin button (32 × 32 hit area), ADX title UI 13 `Ink1`, channel id Cascadia 12 `Ink3`, sparkline 80 px (`Ink2` 1.25 px line, `Ink1` end dot, minimum span per unit so flat channels stay flat: 5 °C, 1 V, 2 %, 100 rpm, 10 mg/stk, 2 kPa), value Data 16 `Ink1` right-aligned, unit UI 12 `Ink3`.
- Live channel list columns: at 1200 px and wider, the left column holds the Pinned group and the right column the Not pinned group, and each column scrolls on its own (both virtualized). Pinned channels therefore never scroll away while you look through the rest. With no pins, the left column shows "No pinned channels. Use the pin on a row to keep it at the top." Below 1200 px the list is one column, Pinned first, then Not pinned, scrolling as one. Tab order: the left column top to bottom, then the right column; arrow keys move within a column.

### 5.7 Chips (real status only)

22 px, radius 4, `Fill` background, UI 12 SemiBold `Ink1`, optional 12 px icon. Allowed chips: "Saved", "1 item not saved", "Read-only". Nothing else: no type chips ("Map"), no unit chips, no "Live" badges.

### 5.8 Banners

Full width at the top of the pane, above the toolbar, radius 8, `Fill` background, 16 px icon, text UI 13 `Ink1`, one action at the right (secondary button).

- Warning or error: `Warn` icon (5.55 Day / 5.37 Night on Fill) and a 3 px `Warn` stripe on the left edge. Not dismissible while its cause holds.
- Caution: `Caution` icon (4.38 Day / 7.33 Night on Fill) and a 3 px `Caution` stripe on the left edge (`Border.banner.caution`), for a condition short of danger: oil below working temperature, coolant or oil running hot, a suspect run's flag. The text stays `Ink1`, as on every banner.
- Info: `Ink2` info icon, no stripe, dismissible with a quiet "Hide" button.

The warm-up banners (performance spec 5) show on Live under the connection banner and at the top of the Performance pane while connected: coolant below operating temperature and the ECU's overheat lamp as warnings, cold oil and running hot as cautions, oil temperature not reported as info. Each shows the reading and the target, updates in place as the reading changes, and clears by itself with its condition, so none has an action or "Hide".

Inline messages under the grid (edit refusals, clamps) use the same icon and text without the banner box.

### 5.9 Tooltips, menus, dialogs

Tooltips: 500 ms delay, max width 320, radius 4, padding 6 × 8, UI 12 `Ink1`. Day: `Pane` + Hairline + Shadow. Night: `Fill` + `Edge`. A tooltip never holds the only copy of information the user needs (Avalonia does not show tooltips on keyboard focus): the undo label is also in the key line, disabled reasons are also in the status line when the user presses the control.

Menus: same surfaces, radius 8, items 32 px, shortcut right-aligned in `Ink3`. Dialogs: radius 12, padding 24, title UI 16, body UI 13 `Ink2`, buttons right-aligned with the primary last; Esc cancels; Enter triggers the primary.

### 5.10 Empty-state block

Centred in the pane, max width 480: title UI 20 `Ink1`, one or two lines UI 13 `Ink2`, then the primary and at most one secondary button, 24 px apart. Loading uses the same block with a 2 px indeterminate bar in `Ink2` (no spinner alone) and text naming what is loading.

### 5.11 Status bar (28 px, `Frame`, hairline above)

Left: connection dot and text. Not connected: 8 px `Edge` ring, "Not connected" `Ink2`. Connecting: same ring, "Connecting to MS43 (ms43x) on COM1…". Connected: `Now` dot, "Connected" in `NowText`, then " to COM1 · ms43x" `Ink2`. While logging: "· Logging 00:04:12 to ms43-20261019-2225.csv · 9576 rows" (numbers Bahnschrift). Right: on the Live screen only, the label "Readouts" (UI 12 `Ink2`) and a segmented control Tiles / Dials (accessible name "Readouts"); then the Night / Day segmented control. Both are shortcuts to the settings of the same names in the Settings dialog and always show the same state. When the bar runs short of width, the word "Readouts" drops before anything else.

### 5.12 Settings dialog

Modal dialog (5.9), about 560 px wide, title "Settings", a close icon button (accessible name "Close"), Esc closes it. One line under the title: "Changes apply right away and are saved." There is no Apply, Save or Cancel button: every control applies at once and is written to `settings.json`. Rows: label left (UI 13 `Ink1`, an optional second line UI 12 `Ink2`), control right; section headings UI 13 SemiBold; spacing from 4.1. At 1024 × 680 the body scrolls inside the dialog while the title stays. Ctrl , opens it from any screen.

| Section | Row | Control | Default |
|---|---|---|---|
| Appearance | Theme | segmented: Night / Day / Follow Windows | Night |
| Appearance | Live readouts | segmented: Tiles / Dials | Tiles |
| Live | Trend length | segmented: 10 s / 30 s / 60 s | 10 s |
| Live | Log folder | path field and "Browse…" | Documents\MS43 Logs |
| Live | Start writing the CSV log when connected | switch | off |
| Live | Note in `Ink2` with the info icon: "Logging timing is fixed: the pause between requests and the stop after 3 timeouts in a row keep logging as reliable as before." | none | |
| Tune | Open maps showing | segmented: Physical values / Raw values | Physical values |
| Tune | Live cursor trail | segmented: Off / Short / Long | Short |
| Tune | Reopen the last bin when the app starts | switch | on |
| Saving | Keep a .bak copy before the first save; second line "The .bak holds the file as it was on disk before your first save in a session." | switch | on |
| Saving | Add changes to the edit log next to the bin (TunerPro format) | switch | on |

Sections are 24 px apart with no divider. Segmented controls are full 32 px controls. When `settings.json` cannot be written, a warning banner appears at the top of the dialog body: "Not saved: <reason>. Your choices apply until you close the app." with the action "Try again" (today `AppSettings.Save` swallows write errors; the build makes it report them). With Theme set to Follow Windows, the status bar's Night / Day shows the theme in effect; pressing one of them sets the Theme setting to that fixed value.

### 5.13 Tune item panes (values, switches, flags, curves, axes)

Mockups: `tune-items-{day,night}.html`. The shell, rail and inspector stay as for a map; only the pane changes.

| Kind | Pane | Inspector |
|---|---|---|
| Value | Title row (kind glyph, id, XDF description). Toolbar: the Value field with its unit and "Set" (secondary, Save stays the one primary), then Undo / Redo, in the place the map's Value field takes. Below: the current value as a Data 56 reading with its unit (UI 13 `Ink3`), then the value slider of 5.14 with − and +, then the key line (the latest edit message). | Edit limits (storable range and step, the XDF range when it narrows the storable one, value and raw), Details (address, size, units, scaling) |
| Switch | Title row shows the first line of the XDF description ("Configuration switch - Load input"); the numbered lines become the choices. The choices are an always-visible list (radio group) with an `Edge` border: each row shows the raw number (Data 12 `Ink3`) and the label; the chosen row has the selection bar, SemiBold and a check icon. Picking writes at once, through the undo history, as today. No dropdown: three or four choices are faster to read all at once. | Edit limits (number of choices "from the XDF description", the XDF range when it narrows the storable one, value and raw), Details |
| Flag | A switch (5.5) with the word "On" or "Off" beside it; the state is never colour only. | Details with the bit mask |
| Curve | The one-column table as a vertical grid (one column of cells, breakpoints on the left), same cell rules as a map. The value unit is shown above the column (the single column's unit, since these tables carry no Z unit in this XDF). | A Curve chart instead of the Surface: square points filled with their cell's map colour and an `Ink2` edge (the edge carries the mark, light map stops are under 3:1 on Pane), the selected point framed in `Ink1`, an `Ink2` line and axes, hairline grid, round tick values. The round dot stays reserved for the live cursor. Then Details and Colour scale. |
| Axis | The breakpoints as a one-column grid, rows numbered 1 to n (the refusal message counts breakpoints that way), and an info line: "Breakpoints must not go down: an edit that makes one fall below its neighbour is refused." | "Used by" with the tables that use the axis (from `XdfCatalog.AxisUsers`), Details |
| Read-only | "Read-only" chip in the title row, toolbar disabled with the reason, info line (7.1). None of the 3776 items of the MS43X001 XDF is read-only (checked), so this state is drawn only in the states sheet. | |

A newly opened item selects its first cell (as `TuneViewModel` does), so Interpolate starts disabled with its reason.

