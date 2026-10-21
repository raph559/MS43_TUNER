# MS43 Tuner design system

Companion to `2026-10-20-ms43-restyle-design.md`. The one system every MS43 Tuner screen (Live, Tune, Compare, Settings and the dialogs) is rebuilt on. Day uses look A and Night uses look B (the owner picked both, R-37). One layout, two skins: the files differ only in the colour token values in section 1 and the map stop values in section 2. Everything else in this document applies to both themes unchanged.

Sources: `.superpowers/design/2026-10-20-restyle/first-drafts/dir-a.html` (Day) and `first-drafts/dir-b.html` (Night), local only and not committed because the mockups carry the owner's car data; the four screen audits; the app code (`src/MS43Logger.AvaloniaApp`, `src/MS43Logger.Core`), the installed fonts in `C:\Windows\Fonts`. Every contrast ratio below was produced by `antislop-human/contrast-check.py`. Helper scripts used to derive the colours: `.superpowers/design/2026-10-20-restyle/clean/tools/` (local only).

Four rules hold the system together:

1. **Warm hues are signals, cool hues are data.** Orange means now, red means read this warning, coral means "B is higher" in Compare. Maps use cool hues only, so the live cursor is the only warm mark on a map.
2. **Every number is set in Bahnschrift with tabular figures.** That DIN 1451 voice is the identity motif (section 10).
3. **The pane is the highest-contrast surface in both themes.** Data sits on it. Frame and fill step toward mid grey.
4. **Nothing is invented.** Values, limits, counts and names come from the bin, the XDF, the ADX or the logger. Where a value is missing the screen says `n/a`.

---

## 1. Colour tokens

### 1.1 Surfaces, lines, text, accent, warning

Three surface tones in all (R-29): frame, pane, fill.

| Token | Role | Day | Night | Reason |
|---|---|---|---|---|
| `Frame` | Window canvas: header, rail, inspector, status bar | `#F2F4F7` | `#101215` | One step toward mid grey so the chrome recedes behind the data pane. |
| `Pane` | Data surface: grid, Live cluster and channel list, Compare grid, inputs, dialogs (Day) | `#FFFFFF` | `#0A0B0D` | The tone farthest from mid grey in each theme, so numbers sit on the highest contrast. No shadow implies depth in either theme, so the light-up/dark-down flip is a contrast rule, not a depth flip. |
| `Fill` | Segmented tracks, selected rows and headers, banners, hatch lines, popups (Night) | `#E9ECF1` | `#1F2328` | The third and last tone; marks "selected" or "grouped" without a new hue. |
| `Track` | Dial and bar tracks, off gear slots | `#F2F4F7` (= Frame) | `#1F2328` (= Fill) | Alias, not a new tone: the value it uses in each theme keeps the orange mark at 3:1 or more on top of it (3.18 Day, 6.06 Night). |
| `Hairline` | Decorative separation between frame regions and panes, list dividers | `#E1E5EA` | `#23272D` | Below 3:1 on purpose: it separates areas, it never identifies a control. |
| `Edge` | Boundary of every control you type into, toggle or pick from; secondary buttons; idle status dot; the value slider's track beyond the value (5.14) | `#727C8C` | `#6E7681` | 3:1 or more against all three surfaces (WCAG 1.4.11), so inputs and switches are findable. |
| `Ink1` | Primary text, values, focus ring, primary button fill, selection outline | `#121826` | `#F3F5F7` | Near-black and near-white with a slight cool cast that matches the map hues. |
| `Ink2` | Secondary text, labels, icons, quiet button labels | `#3D4656` | `#AEB4BD` | Labels defer to values but stay far above 4.5:1. |
| `Ink3` | Tertiary metadata, placeholders, `n/a`, disabled labels | `#5B6576` | `#858C97` | Lowest text level; still 4.5:1 or more on every surface. Never used for anything you act on. |
| `Now` | Live marks: cursor dot, value arcs and bars, gear slot, connected dot, now-ticks | `#EA5B0C` | `#FF7A1A` | The one accent (lead decision). Day fill keeps 3:1 on Pane and Frame. |
| `NowText` | Live text: rate, "Connected", live cursor readout | `#B4470A` | `#FF7A1A` | Day orange darkened to pass 4.5:1 as text; Night orange already passes. |
| `Warn` | Warning and error icon, banner stripe, inline error icon | `#B42318` | `#FF6259` | One signal red for every warning and error, always with an icon and text. It sits 12 (Day) and 22 (Night) degrees of OKLCH hue redder than orange, and Day red is much darker than Day orange (1.88:1 apart); the icon and text, not the hue, carry the meaning. |
| `Hover` | Overlay on any surface under the pointer | `Ink1` at 6 % | `Ink1` at 8 % | An overlay instead of a fourth surface tone. |
| `Pressed` | Overlay while pressed | `Ink1` at 12 % | `Ink1` at 14 % | Same reason; the pressed label stays 4.5:1 or more (below). |
| `Shadow` | The one Day elevation (popups only) | `0 8 24 -8 #3D121826` | none | See section 4. |

Map stops (`Map1` to `Map7`) and the Compare pair (`DivBelow`, `DivAbove`) are in section 2. Cell text uses `CellDark #000000` and `CellLight #FFFFFF` (section 2.3).

### 1.2 Verified contrast (contrast-check.py)

Text needs 4.5:1, non-text (edges, marks, focus) needs 3:1.

| Foreground | Day on Pane / Frame / Fill | Night on Pane / Frame / Fill | Use |
|---|---|---|---|
| `Ink1` | 17.73 / 16.09 / 14.97 | 18.02 / 17.17 / 14.45 | text, focus ring, selection outline |
| `Ink2` | 9.51 / 8.63 / 8.03 | 9.43 / 8.99 / 7.57 | text, icons |
| `Ink3` | 5.89 / 5.34 / 4.97 | 5.81 / 5.53 / 4.66 | text |
| `NowText` | 5.46 / 4.96 / 4.61 | 7.55 / 7.19 / 6.06 | text |
| `Warn` | 6.57 / 5.97 / 5.55 | 6.70 / 6.38 / 5.37 | icon, stripe, may be text |
| `Edge` | 4.22 / 3.83 / 3.56 | 4.29 / 4.08 / 3.44 | control boundary |
| `Now` (fill) | 3.50 / 3.18 / **2.96** | 7.55 / 7.19 / 6.06 | non-text marks |
| `Hairline` | 1.27 / 1.15 / 1.07 | 1.31 / 1.25 / 1.05 | decorative only |

Other pairs: primary button label (`Pane` on `Ink1`) 17.73 Day, 18.02 Night; on hover (`Pane` on `Ink2`) 9.51 Day, 9.43 Night. `Ink2` on `Hover` over Pane 8.42 Day, 7.99 Night. `Ink2` on `Pressed` over Fill 6.33 Day, 4.99 Night; `Ink1` there 11.80 Day, 9.52 Night.

Rules that follow from the table:

- Day `Now` fails on `Fill` (2.96). A Day orange mark never sits on Fill; where a live mark must appear on a selected row or banner, use `NowText` (4.61).
- `Ink3` drops below 4.5:1 on Pressed, so no interactive label is ever Ink3. Interactive labels are Ink1 or Ink2.
- `Hairline` never marks a control. If removing it would hide a control, the control needs `Edge`.

### 1.3 Focus ring

A 2 px `Ink1` ring drawn outside the control with a 2 px gap in the colour of the surface the control sits on. Corner radius = control radius + 4. Never orange, never inside the control. Measured: Day 17.73 (Pane), 16.09 (Frame), 14.97 (Fill); Night 18.02, 17.17, 14.45. Every focusable control gets it: tabs, file menu, Save, port and definition pickers, Rescan, Connect, search fields, switches, rail rows, toolbar buttons, the Value field, the grid (on its active cell), Pin, segmented controls, the value slider's thumb and its − and +, Live rows and pins, dialog buttons. In Avalonia: a shared `FocusAdorner` template, `outline: none` style resets are forbidden (R-32).

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
| | Static zones such as the rev limiter band (Ink2) |

When samples stop (link lost, cursor stale after 1 s), every orange mark turns `Ink3` and the screen shows the age. An idle screen (not connected) shows zero orange.

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
- A cell whose raw value is the storable minimum or maximum (raw 0 or raw 255 for an 8 bit map) is **at the storage limit**. It is hatched (45 degree, 1 px `Fill` lines every 4 px on `Pane`), keeps its number in `Ink1` (17.73 Day / 18.02 Night on Pane, 14.97 / 14.45 on Fill) and is left out of the colour range **only when it is an outlier**: fewer than half of the item's cells sit at that limit, and the limit value lies outside the range of the other cells by at least a quarter of that range. Reason: in `ip_iga_ron98_pl__n__maf` the 6990 rpm row holds raw 0 (-23.6 °CRK) in every column, 26.6 °CRK below the working minimum of a 36 °CRK range; it would otherwise push the 304 working cells into the top 57 % of the ramp. Ordinary values that happen to sit at a raw limit stay normal cells: raw 0 meaning 0 next to small positive values, raw 255 meaning the neutral factor 0.996, and the 40 tables of the MS43X001 XDF that are entirely at one limit (checked on the owner's bin: 347 of 1479 tables have limit cells, 81 are at least half at a limit).
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

### 2.5 Compare B − A (diverging pair)

Zero is the pane colour; B below A mixes toward blue, B above A toward coral, by |t|^0.7 (existing `HeatRamp.Diverging`), t = (B − A) divided by the largest absolute difference in the item. Coral is taken on its pink side (hue 8 to 12) so it stays 30 degrees or more from the orange accent.

| Token | Day | Night | Text at the end | Mid mix (t = 0.5) |
|---|---|---|---|---|
| `DivBelow` (B lower) | `#1F53B8` | `#6CA7F5` | Day white 7.03, Night black 8.48 | Day `#7595D3` black 7.00, Night `#466B9C` white 5.46 |
| `DivAbove` (B higher) | `#CE2854` | `#F87F99` | Day white 5.18, Night black 8.49 | Day `#E17B96` black 7.48, Night `#9D5263` white 5.50 |

Cell text follows 2.3 (lowest mix measured: 4.68 Day, 5.46 Night). Cells with no difference keep the `Pane` fill and print `0.0` in `Ink3` (5.89 Day, 5.81 Night) so the changed cells carry the colour. The scale caption reads "B − A: -3.0 to +3.0 °CRK, 12 of 320 cells differ" with the real numbers.

---

## 3. Type

### 3.1 Font roles

| Role | Font | Reason |
|---|---|---|
| UI text | Segoe UI Variable Text (11 to 16 px), Segoe UI Variable Display (20 px); fallback Segoe UI | Windows native, hinted for small sizes, the owner's OS. |
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
| Data 24 | Bahnschrift | 24 / 28 | 400 | strip readouts, gear and speed, pinned tiles | Context readings. |
| Data 40 | Bahnschrift | 40 / 44 | 400 | load and advance on dials | Second tier. |
| Data 56 | Bahnschrift | 56 / 60 | 400 | rpm on dials; load and advance in tiles; a value's reading on the Tune pane | Primary reading. |
| Data 88 | Bahnschrift | 88 / 92 | 350 (SemiLight) | rpm in the tiles layout only | Hero; SemiLight keeps the large figure from turning heavy. Never 300. |

### 3.3 Numbers (checked against `bahnschrift.ttf`)

- **Tabular figures are mandatory.** Bahnschrift's default digits are proportional ("1" is 681 units, "0" is 1130); its `tnum` feature fixes them. Avalonia: `FontFeatures="+tnum"` on controls, `FormattedText.SetFontFeatures` in MapGrid, Gauge, Sparkline and the other drawn controls.
- **Minus is "-" (U+002D) in every number.** Bahnschrift has no U+2212; MapGrid's current replacement of "-" by U+2212 must go, or the minus falls back to another font. UI labels in Segoe UI keep the true minus where it is part of a name: "B − A".
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

### 4.2 Radius (R-11)

| Radius | Use | Reason |
|---|---|---|
| 4 | grid cells, selection rectangle, chips, kbd hint, tooltips | Small things in dense areas; a larger radius eats cell area. |
| 8 | buttons, inputs, pickers, segmented tracks, list rows, menus | Everything you click. |
| 12 | dialogs, Live dial and tile panels | Surfaces that hold other things. |
| Nested | inner radius = outer minus padding (segments 6); focus ring = control + 4 | Concentric corners. |
| Circle / pill | status dot, live dot, switch knob and switch track, value slider thumb only | The switch knob and the slider thumb are grabbed and dragged, and the round shape says so; the switch's round track shows its travel. No pill buttons, inputs or chips, and the slider track keeps square ends. |

### 4.3 Elevation (R-12)

- **Day: one level, popups only** (menus, picker drop-downs, tooltips, dialogs, the inspector overlay below 1280 px): `Shadow` plus a 1 px `Hairline`. Reason: these overlap content and must lift off what is under them. Panes, buttons, cells, tiles and the selected cell have no shadow; panes are separated by flat tone and a hairline.
- **Night: none.** Popups use `Fill` with a 1 px `Edge`. Reason: a shadow on `#0A0B0D` is invisible; the edge is the separation.
- Dialog scrim: `Ink1` at 30 % (Day), `#000000` at 50 % (Night).

### 4.4 Borders

| Line | Token | Where |
|---|---|---|
| Hairline | `Hairline` | between header, rail, pane, inspector, status bar; list dividers; Day popups |
| Control edge | `Edge` | inputs, pickers, switch off track, checkbox, the value slider's track beyond the value, secondary buttons, selected segment, idle status dot, Night popups |
| State line | `Ink1` | selection rectangle, selected tab underline, selected rail row bar, focus ring |
| Warning stripe | `Warn`, 3 px left | warning banners only (it marks real state) |

No border around tiles or cards: Live tiles are pane areas divided by hairlines, like a table.

---

## 5. Components

All controls are 32 px tall (Live list rows and pins 32 px hit areas for use in the car). Labels are UI 13 unless stated. States: rest, hover (`Hover` overlay), pressed (`Pressed` overlay), focus (1.3), disabled. Their state changes fade as 10.3 says (MOTION 2).

### 5.1 Header (48 px, `Frame`, hairline below)

Left to right:

1. **App mark and wordmark.** The owner kept the app's dial mark, redrawn in the icon style of section 6: a 20 px dial, a 270 degree arc open at the bottom and a needle, 1.5 px stroke, butt caps. At rest: arc `Ink3`, needle `Ink2` at the rest position. While connected and logging: arc and needle in `Now`, the needle at the current engine speed (0 to 8000 rpm over the 270 degree sweep), redrawn with the live readings and never tweened. Then the wordmark "MS43 Tuner", UI 16 SemiBold `Ink1`. Below 1280 px wide the wordmark is hidden (the window title carries it) and the mark stays.
2. **Tabs** Live / Tune / Compare: text tabs, 48 px tall hit areas. Selected: `Ink1` SemiBold and a 2 px `Ink1` underline on the header's bottom edge. Others: `Ink2`, hover overlay. Not a segmented pill (R-30).
3. **File block** (all tabs): file menu button = bin name, UI 13 SemiBold, middle ellipsis, then a chevron. Menu: "Open bin…" (Ctrl O), "Use another XDF…", "Save as…" (Ctrl Shift S). At 1280 px and wider a second line under the name: "Firmware" `Ink3` + `MS43X001` Cascadia `Ink2` + "XDF" `Ink3` + XDF file name `Ink2`, UI 12. Then the **save-state chip** and **Save**.
   - Clean: chip "Saved" with a check icon.
   - Dirty: chip "1 item not saved" (count of items, from `ModifiedItems`) with the changed triangle, `Ink1` SemiBold, never orange. Grid key and rail say cells and items with the same nouns (copy rule 9.3).
   - **Save**: primary button on Tune and on Compare after a copy; disabled when clean with the tooltip "Nothing to save".
   - No bin: "No bin open" in `Ink3` and no chip.
4. Flexible space.
5. **Rate** (only while connected): "38" Data 13 SemiBold `NowText` + " samples/s" UI 12 `Ink2`. Connected but nothing yet: "No samples yet" `Ink2`. Under 10 samples/s: the warning icon and "slow link". Hidden when not connected.
6. **Port** picker ("COM1"), **Rescan ports** icon button, **Definition** picker (`ms43x` in Cascadia). Below 1280 px these three merge into one picker button "COM1 · ms43x" that opens a popover with the same three controls (accessible name "Connection settings").
7. **Connect** / **Disconnect**. Connect is the primary button on Live when not connected; a secondary button on Tune and Compare (Save is their primary). Connected: "Disconnect" as a quiet button.
8. **Settings**: a 32 px quiet icon button with the gear icon at the right end, tooltip "Settings (Ctrl ,)", accessible name "Settings". It opens the Settings dialog (5.12).

### 5.2 Buttons

| Kind | Rest | Hover | Pressed | Disabled | Use |
|---|---|---|---|---|---|
| Primary | `Ink1` fill, `Pane` label SemiBold | `Ink2` fill | `Ink1` fill | `Fill` fill, `Ink3` label | One per screen: Save (Tune, Compare after a copy), Connect (Live, not connected), the main action of an empty state. |
| Secondary | `Pane` fill, 1 px `Edge`, `Ink1` label | `Hover` | `Pressed` | no fill, `Hairline` edge, `Ink3` label | Second actions: Open XDF…, Copy B into A, Browse…, banner actions. |
| Quiet | no fill or edge, `Ink2` label | `Hover` | `Pressed`, `Ink1` label | `Ink3` label | Pin, Change…, Edit in Tune, Disconnect, Performance tests, Show details. |
| Tool group | one `Edge` outline around several text buttons, 1 px `Hairline` dividers | per button | per button | per button | Map toolbar: [Value][Set][Add][Scale %], [Interpolate][Smooth], [Undo][Redo]. |

Disabled keeps its label at 4.5:1 or more (`Ink3`) and loses its boundary (Hairline), which is what says "not operable". Every disabled control has a tooltip with the reason: "Nothing to undo", "Select a range first: Interpolate fills between its corners."

### 5.3 Inputs and pickers

`Pane` fill, 1 px `Edge`, radius 8, 32 px, text `Ink1`, placeholder `Ink3`. Number fields (Value, any numeric entry) set typed digits in Bahnschrift 13 with the unit as a suffix in `Ink3`. Focus shows the ring, not a border colour change. Invalid input: the field keeps its edge; the message appears under or beside it with the warning icon and text ("'abc' is not a number."). Pickers add a chevron in `Ink2`. The Value field is never prefilled.

Search fields (Tune rail, Live channel filter, Compare list) share one shortcut, **Ctrl K**, shown as an 11 px kbd hint, and a Clear button (icon only) once text is typed.

### 5.4 Segmented control

Track `Fill`, radius 8, 2 px inset. Selected segment: `Pane` fill, 1 px `Edge` (3.56 Day, 3.44 Night against the track), `Ink1` SemiBold. Others: `Ink2` on the track, hover overlay. Inside a Night dialog, whose surface is itself `Fill`, the track is `Frame` so it stays visible; the selected segment stays `Pane` with `Edge`. Used for: Night / Day (status bar, accessible name "Theme"), A / B / B − A (Compare), trend window chips (Live strips). Not used for the main tabs.

### 5.5 Switch and checkbox

Switch 36 × 20, track radius 10. In the Settings dialog the label sits left and the switch right, so every control in the dialog shares one right edge; elsewhere the label sits to the right of the switch. Off: `Pane` track, 1 px `Edge` (3.83 Day, 4.08 Night on Frame), 14 px knob in `Ink3` at the left. On: `Ink1` track, `Pane` knob at the right. Position and fill both change, so the state is never colour only. The label sits to the right and is part of the hit area. Checkbox: 16 px box with the same off and on tokens and a check icon. Switches: "Changed only", "Differs from B", "Pinned only", "Write CSV log".

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
- Info: `Ink2` info icon, no stripe, dismissible with a quiet "Hide" button.

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

### 5.14 Value slider

A value (an XDF constant that is not a switch) shows a slider under its reading, with − at the low end and + at the high end; the Value field and Set stay above it for an exact number. Mockup: `tune-items-*.html` (main frame, drag frame, XDF-range frame).

- Span: each end is the XDF range's end where the XDF range narrows the storable range there, otherwise the storable end. When the current value lies outside the span by more than half a raw step, the span widens to include it. Typed values outside the XDF range are still stored, with the existing warning.
- Steps: the arrow keys and − and + move one raw step and write at once, one undo step each (like + and − on a map); Page Up and Page Down move 10 steps; Home and End go to the span's ends. Dragging shows the value under the thumb in the reading and in the key line ("Release to set 4000 rpm. Esc cancels."); releasing writes it as one undo step, and Esc cancels the drag.
- Look: track 4 px with square ends, `Ink2` from the span's start to the value and `Edge` beyond it (`Track` measured 1.10:1 Day and 1.25:1 Night on Pane, so the span's end vanished; `Edge` is 4.22 / 4.29); thumb a 20 px `Ink1` circle with a 2 px `Pane` ring, which is also the focus gap; the span's ends labelled under the track in Data 12 `Ink3` with units. Each XDF end on the span is a short `Ink2` tick with a 1 px `Pane` casing on each side, explained by one legend line ("Ticks: the XDF range, 480 to 8160 rpm. Typed values from 0 to 65535 rpm are stored, with a warning outside the ticks."). No orange: nothing on the slider is live.
- − and + are secondary 32 px icon buttons named "One step down" and "One step up", with the step as their tooltip ("-32 rpm", "+32 rpm").
- Focus: the ring of 1.3 round the thumb. Tab order: Value, Set, Undo, Redo, −, thumb, +.

### 5.15 Compare list and pane

Mockups: `compare-{day,night}.html`.

- List pane 280 px (224 at 1024): the A and B file block (A, B, "Change…"), a search field (5.3, Ctrl K; the build adds a text filter to `CompareViewModel`), the kind filter as a segmented control with each count under its label (All, Maps, Curves, Axes, Values; 44 px tall, so the five labels and counts fit one row), then the differing items under their category headings. Each row is two lines: kind glyph and id in Cascadia `Ink1`; summary ("293 of 320" or "4512 → 3520", the arrow set in Segoe because Bahnschrift has no arrow) and the unit in `Ink3` (raised to `Ink2` on a hovered or pressed selected row, where `Ink3` drops below 4.5:1). The last row opens the bytes outside the XDF: "18 ranges, 11907 bytes outside the XDF (11436 differ)".
- Pane: title row; the axis banner when an axis differs (info banner without "Hide": it explains how every cell is compared, so it stays while the item is shown); the segmented control B − A / A / B; "Copy B into A" (secondary) and "Edit in Tune" (quiet); the grid (rows flex from 26 down to 20 px before the grid scrolls); the scale with the B − A range of this item ("B − A: -21.0 to +61.1 °CRK, 293 of 320 cells differ").
- A value item shows A and B as Data 56 readings side by side with "B − A: -992 rpm" under them.
- The bytes view is a four-column table: Range ("0x03C43 to 0x03C9B"), Bytes, Differ, Area.
- After Copy B into A the copied item drops out of the list and the selection moves to the first listed item, as today.

---

## 6. Icons (R-04)

One set, drawn for this app: 16 px grid, 1.5 px stroke, **butt caps and mitre joins**, so terminals are cut square like Bahnschrift's strokes instead of the rounded Lucide look. Colour is the text colour of the control (`Ink2`, `Ink1` on hover and selection). Never orange, except the live dot, which is a mark rather than an icon. In Avalonia: `Path` with `StrokeLineCap="Flat"` and `StrokeJoin="Miter"`, geometry in one resource file.

| Icon | Where | Why it exists |
|---|---|---|
| Search (lens) | search fields | Marks the field as search. |
| Clear (×) | search fields with text | Clears the query; icon only. |
| Pin (pushpin; outline = not pinned, filled = pinned) | rail rows, Tune title row, Live rows | The action is "pin"; a star would mean "favourite" (the old ★/☆ goes). |
| Chevron | pickers (down), rail categories (right / down) | Shows that something opens or expands in place. |
| Rescan (two arrows on a circle) | next to Port | Finds a cable plugged in after launch; icon only. |
| Warning (triangle with !) | warning banners, inline errors | Pairs with `Warn` so the state is not colour only. |
| Info (i in a circle) | info banners, clamp notes | |
| Check | "Saved" chip | Confirms the clean state. |
| Kind glyphs: map (3 × 3 grid), curve (polyline), value (single bar), switch, flag, axis (ruler) | rail rows, title line | Tell item kinds apart in a list of ids. |
| Markers: changed (triangle), differs (ring), live (dot) | grid cells, rail, header chip, legends | One shape per meaning across the app. |
| Minus, plus | − and + beside a value slider | One raw step down or up. |
| Gear (six flat, square-cut teeth and a hub) | header, opens Settings | Settings is the one place for preferences; the gear is the conventional sign for it, drawn in this set's square-cut style. |
| App mark (open 270 degree dial and needle, 20 px) | header, left of the wordmark | The app's own mark (owner, 2026-10-20); see 5.1. |
| Close (×) | dialogs | Closes the dialog; tooltip "Close (Esc)". |

Dropped: file icon in the header, plug on Connect, folder in the CSV field, stopwatch, moon and sun (the segments say Night and Day), undo and redo arrows (the buttons say Undo and Redo), star, sparkle of any kind.

Icon-only buttons (Rescan ports, Clear search, row pins, the slider's − and +) have a tooltip, an `AutomationProperties.Name`, a 32 × 32 hit area and the focus ring. The name equals the tooltip ("Rescan ports", "Clear search", "Pin to top" / "Unpin"), except − and +, named "One step down" and "One step up" with the step as their tooltip ("-32 rpm", "+32 rpm").

---

## 7. States (R-27)

Each state names its cause and gives one next action. Copy marked "existing" is already in the app; the rest replaces the app's text when the screen is rebuilt.

### 7.1 Tune

| State | Cause | What shows | Copy | Action |
|---|---|---|---|---|
| First run, no bin | Nothing opened yet | Rail and inspector hidden; empty-state block in the pane; header "No bin open" | "Open a bin to start tuning" / "Choose the 512 KiB file you flash with MS4X Flasher. The XDF you last used for its firmware opens with it; otherwise you are asked for one." (existing) | Primary "Open bin…", secondary "Open XDF…" |
| Opening | Reading the bin, then parsing the XDF (3776 items in 65 categories for MS43X001) | Loading block on first run; otherwise the previous bin stays visible, a 2 px bar runs under the header, controls are disabled | "Reading CAR_current_43X001_SP95_STAGE1.bin…" then "Reading Siemens_MS43_MS43X001_512K.xdf…" | none (it finishes or fails) |
| XDF needed | No remembered XDF for this firmware | The file picker opens; if cancelled, the empty block shows the line | "Choose the XDF for firmware 43X001." (existing) | Primary "Open XDF…" |
| Wrong-size file | File is not 524288 bytes | Warning banner; the open bin (if any) stays | "Not opened: backup.bin is 262144 bytes. An MS43 bin is exactly 524288 bytes (512 KiB)." | "Open bin…" |
| Unreadable file or XDF | Read error or XDF does not parse | Warning banner | "Not opened: <file> could not be read (<reason>)." | "Open bin…" or "Open XDF…" |
| Firmware mismatch | XDF title firmware differs from the bin's id | Warning banner above the toolbar, stays while this pair is open (safety) | "This XDF describes <XDF title>, but the bin is firmware <id>. Values may be wrong." (existing) | "Use another XDF…" |
| Nothing selected | Bin open, no item picked | Empty block in the pane | "Pick a map, curve or value on the left" / "Search with Ctrl K. Pinned items stay at the top." (existing) | none |
| Search finds nothing | Query matches no id or title | Rail list replaced by one line | "No map or value matches "ign_x"." | Quiet "Clear search" |
| Changed only, none | Filter on, nothing changed | Rail group header | "No changes since the last save" (existing) | none |
| Read-only item | XDF storage the app cannot write | "Read-only" chip in the title row, toolbar disabled, info line under the title | "'<title>' is read-only: 32-bit elements are not supported." (existing pattern) | none |
| Dragging a value | The slider's thumb is held | Reading and key line show the value under the thumb; the inspector keeps the value the bin holds | "Release to set 4000 rpm. Esc cancels." | Release writes it as one undo step; Esc cancels |
| Edit refused | Bad input or no selection | Inline message under the grid with the warning icon | "Select cells first." / "Type a value first." / "'abc' is not a number." (existing) / "'<title>' is an axis: breakpoint 5 would fall below breakpoint 4. Axis values must not go down." (existing) | none |
| Edit clamped | Value beyond the storable range (-23.6 to 72.0 °CRK here) | Inline message with the info icon | "'ip_iga_ron98_pl__n__maf' cannot hold 80.0; it was set to its limit, 72.0." (existing) | none |
| Single cell, Interpolate | Interpolate needs a range | Interpolate disabled | Tooltip: "Select a range first: Interpolate fills between its corners." | none |
| Unsaved | Items differ from the file | Header chip and enabled Save | "1 item not saved"; key line "2 cells changed since the last save" | "Save" |
| Save failed | Write error | Warning banner | "Not saved: <reason>. The file on disk is unchanged." | "Save as…" |
| Leaving with changes | Open, close or switch bin while dirty | Dialog | Title "Save the changes to CAR_current_43X001_SP95_STAGE1.bin?" (existing) / "Saving keeps a .bak of the file as it was and adds the changes to its edit log." (existing) | Cancel, Don't save, primary Save |
| Live cursor idle | Not connected | Inspector note with an `Ink2` outline dot | "Live input: N (rpm) on rows, MAFIGA (mg/stk) on columns. Connect to see the engine's position on this map." | none (Connect is in the header) |
| Live cursor live | Logging, table bound | Cursor on the grid; key line readout in `NowText` | "Live 2480 rpm · 112.4 mg/stk · map 27.5 °CRK" | none |
| No live input | Table has no binding or the definition does not match | Inspector note, `Ink2` | "No live input: <reason>." (existing pattern, e.g. "the logger definition is not known") | none |

### 7.2 Live

The logger's real rule (`LiveLogger`): a timeout is retried after 50 ms; three timeouts in a row stop the logger; any other error stops it at once. Stopping disconnects and closes the CSV, which keeps every row already written (rows are flushed one by one).

| State | Cause | What shows | Copy | Action |
|---|---|---|---|---|
| Not connected | First launch or after Disconnect | Every value `n/a` in `Ink3`, arcs and bars empty, zero orange; status "Not connected" | Note under the header: "If the ECU refuses the faster baud rate while the engine runs, connect first, then start the engine." | Primary "Connect" (last port preselected) |
| No COM port | Cable unplugged or driver missing | Port picker empty | "No COM port found. Plug in the K-line cable, then rescan ports." | "Rescan ports" |
| Connecting | Opening the port, waking the ECU, switching baud | Values `n/a`; Connect disabled and labelled "Connecting…"; status line | "Connecting to MS43 (ms43x) on COM1…" (existing, with a real ellipsis) | none |
| Connected, no samples yet | First poll not back | Rate "No samples yet", values `n/a`, sparklines empty | "No samples yet" (existing) | none |
| Retrying | One or two timeouts in a row | Values keep their last sample; status line | "The ECU did not answer (1 of 3). Trying again." | none |
| Connection failed | Third timeout in a row, or any other polling error | Values freeze and turn `Ink3`, orange off, rate gone; warning banner in the Live pane | "Logging stopped: the ECU stopped answering on COM1 (3 timeouts in a row). ms43-20261019-2225.csv is closed with 9576 rows." | "Connect"; quiet "Show details" opens the connection log (replaces the all-caps error window) |
| Connect failed | Port in use, ECU silent, refused baud switch | Warning banner | "Not connected: <reason>." Known causes: "COM1 is in use by another program. Close it, then connect." / "The ECU did not answer on COM1. Turn the ignition on, then connect." / "The ECU refused the faster baud rate. Turn the engine off, connect, then start the engine." | "Connect" |
| CSV cannot be created | Path not writable at connect | Connect is refused; warning banner | "Not connected: cannot write D:\logs\ms43-20261019-2225.csv (<reason>). Choose another file, then connect." | "Browse…" |
| CSV write fails mid-session | Disk full, file locked | The write error stops the logger (current behaviour); warning banner | "Logging stopped: writing ms43-20261019-2225.csv failed (<reason>). The rows written before 00:04:12 are kept." | "Browse…" |
| CSV off | "Write CSV log" off | Status bar shows no file | "Not writing a log" | none |
| Wrong definition | ADX cannot be read | Warning banner | "Cannot read the ms43x definition: <reason>." (existing) | Definition picker |
| Channel missing | A channel is absent from the reply or fails to decode | That row, tile or dial shows `n/a` | Tooltip: "No value in the last sample." | none |
| Filter finds nothing | Query matches no title or id | One line in the list | "No channel matches "xyz"." | Quiet "Clear filter" |
| Pinned only, none pinned | Switch on, no pins | One line in the list | "No pinned channels. Use the pin on a row to keep it at the top." | none |
| Out of scale | rpm above 8000, value outside a dial | Mark clamps at the end; the numeral prints the true value | none | none |
| Gear unknown | Clutch in, neutral, standing still | No slot lit, readout `n/a` | none | none |
| No bin open | Limiter unknown | No limiter band | Dial caption: "Rev limit: open a bin to read it" | none |

### 7.3 Compare

| State | Cause | What shows | Copy | Action |
|---|---|---|---|---|
| No A | No bin open on Tune | Empty-state block | "Open a bin on the Tune tab first" / "Compare uses the open bin as A and its XDF for both files. Then pick the bin to compare it with." (existing) | Primary "Open bin…" |
| No B | A open, B not picked | Empty-state block | "Open the bin to compare with" (existing) / "A is CAR_current_43X001_SP95_STAGE1.bin. B can be any 512 KiB MS43 bin; both are read through A's XDF." | Primary "Open B…" |
| Identical | No byte differs through the XDF | Empty-state block | "The two files are identical through this XDF." (existing) / "No bytes differ outside the XDF" (existing) | Quiet "Change…" |
| B wrong size | B is not 524288 bytes | Warning banner; the current B (if any) stays | "Not opened: stock.bin is 262144 bytes. An MS43 bin is exactly 524288 bytes (512 KiB)." | "Open B…" |
| B other firmware | B's id differs from A's | Warning banner above the list, stays while this B is open | "B is firmware <idB>, A is <idA>. Items are compared at the same addresses, where B may hold something else." (existing) | "Change…" |
| Copied | Copy B into A done | Inline message; Save becomes enabled | "Copied <title> from B. Undo with Ctrl Z." (existing) | none |
| Read-only snapshot | Editing the B or B − A grid | Inline message | "This grid is read-only. Edit A on the Tune tab, or copy from B." (existing) | "Edit in Tune" |

---

## 8. Layout

Header 48 px and status bar 28 px on every screen. No horizontal page scroll at any size; only the grid and lists scroll, inside their panes, with sticky headers. Nothing is clipped: text truncates with an ellipsis (middle for file names, start for paths: "…\ms43-20261019-2225.csv") and the full text is in a tooltip.

| Region | 1440 × 900 | 1024 × 680 (minimum) |
|---|---|---|
| Header | wordmark, tabs, file block with second line, rate, Port, Rescan, Definition, Connect | wordmark hidden, XDF line hidden (in tooltip and Details), connection pickers merged into one button |
| Body height | 824 px | 604 px |
| Tune rail | 256 px, `Frame` | 224 px |
| Tune inspector | 312 px docked, `Frame` | Hidden; a quiet "Details" toggle in the title row opens it as a 312 px overlay from the right (popup surface) |
| Tune pane | padding 24; title row 32, toolbar 32, grid, key line 20 | padding 16; same rows |
| Grid | 16 columns about 48 px wide; rows up to 26 px | columns about 44 px; rows shrink to 20 px, then the grid scrolls vertically with sticky headers. Toolbar, title, save state and key line never scroll away |
| Live cluster | 252 px (dials 220 px) | 184 px (dials 160 px) |
| Live channel list | two columns at 1200 px and wider: Pinned left, Not pinned right, each scrolling on its own (5.6) | one column below 1200 px, Pinned then Not pinned; about 11 rows visible |
| Live session | CSV row (switch, path, Browse…) shown only when not logging; while logging the facts live in the status bar | same |
| Compare | list of differing items 280 px, pane with A / B / B − A grid | list 224 px, pane as Tune at 1024 |

Tune pane, top to bottom: title row (kind glyph, map id in Cascadia 13 SemiBold, the XDF description in UI 13 `Ink2`, Pin, Details below 1280), banners, toolbar (cell operations only), grid with its axis line ("MAFIGA load, mg/stk" UI 12 `Ink2` + `ldpm_maf_iga_2` Cascadia `Ink3`), key line. The key line holds, left to right: the selection ("4 rows × 3 columns: 3750 to 4500 rpm, 130.0 to 200.0 mg/stk", or for one cell "4000 rpm × 165.0 mg/stk: 33.4 °CRK, raw 152 (0x98)"), the changed legend with its triangle ("2 cells changed since the last save"), the live readout or idle note, and the latest edit message ("Undo: Set 4 cells of ip_iga_ron98_pl__n__maf to 27.8 °CRK"). Inspector sections, top to bottom: Edit limits ("-23.6 to 72.0 °CRK, step 0.375, 8 bit"), Surface ("Drag to turn, or use the arrow keys"; the plot takes keyboard focus), Details (address, size "20 × 16", units, scaling, rows and columns ids with units and point counts), Colour scale (2.2 caption).

A one-column table (a curve or an axis) is centred horizontally in the pane (owner, 2026-10-20: "for the 1 column only maps, center them in the middle"): its row labels, unit header and cells move as one block, its key line is centred under it, and it stays top-aligned under the toolbar. A value shows its reading in Data 56, the slider of 5.14 under it, then the key line.

Live uses layout B, "readout tiles" (the owner's pick), with a Readouts setting that swaps only the readout band between tiles and dials (owner). Everything else on the Live screen is identical in both modes.

| Readouts | Band at 1440 | At 1024 |
|---|---|---|
| Tiles (default) | rpm tile 2 × 2 (Data 88) with a bar up to the rev limit, load and advance as the tall second tier (Data 56) with slim range bars, gear and speed merged in one cell (Data 24), pinned tiles below on auto rows that scroll | band drops to 3 columns; pinned tiles move to the list |
| Dials | the same band grid and cell sizes: rpm dial 220 px (Data 56 reading) with the rev-limit band, load and advance dials 184 px (Data 40), gear and speed cell unchanged | rpm dial 160 px, load and advance 134 px beside their names; "× 1000 rpm" hidden |

Bar and dial marks that sit on the orange fill (the advance zero mark, the rev-limit tick, a fill end inside the limit zone) carry a 1 px `Pane` casing on each side, so they keep 3:1 against the fill and the hatch (measured 3.50 Day, 7.55 Night).

---

## 9. Copy

### 9.1 Rules

- Sentence case for every label, button, heading and message. No all caps (the old "CONNECTION FAILED", "START TEST", "0-100 KM/H" go).
- No em dash and no en dash anywhere (R-02); ranges are written "3.0 to 39.0".
- No buzzwords (R-16). Labels name the action: "Open bin…", "Use another XDF…", "Save", "Save as…", "Connect", "Disconnect", "Rescan ports", "Browse…", "Copy B into A", "Edit in Tune", "Open B…", "Change…", "Pin" / "Unpin", "Set", "Add", "Scale %", "Interpolate", "Smooth", "Undo", "Redo", "Performance tests".
- The ellipsis character "…" ends a command that opens a dialog or picker, and a status that is in progress. Never three dots.
- Every message: what happened, then what to do. The cause comes from the app (exception text, file name, counts), never a guess.
- Help text appears in the state it helps: the baud-rate advice only when not connected or after a refused baud switch.
- A hint names the control as it is labelled: "Connect to see the engine's position on this map." (not "Start logging").
- Shortcut hints: "Ctrl K", "Ctrl S", "Ctrl Shift S", "Ctrl O", "Ctrl Z", "Ctrl Y".

### 9.2 Units

Number, space, unit: "2480 rpm", "112.4 mg/stk", "27.8 °CRK", "31.5 kPa", "88.5 °C", "77 km/h", "12.4 °TPS", "7.1 %", "3.42 ms", "38 samples/s". Dimensions and positions use "×" with spaces: "20 × 16", "4000 rpm × 165.0 mg/stk". Units are the XDF and ADX units, cleaned only of encoding errors. A unit is shown once per context: not in a title chip when Details already gives it.

### 9.3 One name per thing

| Thing | Name on screen |
|---|---|
| The 512 KiB file | bin |
| The TunerPro definition | XDF |
| Anything in the XDF | item (map, curve, value, switch, flag, axis) |
| One value in a map | cell |
| A value on an axis | breakpoint |
| A logged quantity | channel, named by its ADX title exactly as the definition writes it ("Engine Load Ignition"), with its id beside it (`MAFIGA`). No aliases: the cluster and the list use the same name, and the filter finds both. |
| The ADX variant | definition (`ms43x`, `b22`, `b25`, `b30`) |
| Differs from the last save | changed (items in the header and rail, cells in the key line) |
| Differs between A and B | differs |
| Missing value | n/a |
| The rpm limit | rev limit, "Rev limit 7104 rpm, read from the open bin" |

---

## 10. Dials, motif, focal points, motion

### 10.1 Design Read

Reading this as: a desktop tool for one enthusiast tuning and logging their own E46 325i, in a modern clean language, Day A / Night B, dial **ENERGY 1 / RHYTHM 1 / MOTION 2** (MOTION was 1 until the owner asked on 2026-10-21 to "animate nicely the app, the buttons, the pages").

| Dial | Value | What it means here |
|---|---|---|
| ENERGY | 1 | The data is the loudest thing on screen. Chrome is flat tone and hairlines; one accent; no decoration, gradients, glows or illustrations. |
| RHYTHM | 1 | Every screen uses the same shell, the same spacing steps and the same component shapes. Uniformity is chosen: the user moves between Live, Tune and Compare without relearning. |
| MOTION | 2 | Transitions that answer an action: hover and press fades, the tab underline, screens, dialogs, popups, banners and switches (10.3, `Theme/Motion.axaml`). Live values are never animated: the gauges, numbers and map cursor are data. No pulses, no idle motion, no scroll effects. |

### 10.2 Identity motif and focal points

The motif is **Bahnschrift numerals**: the DIN 1451 lettering of German road signs and dashboards, in tabular figures, on every value the app shows. A second, supporting gesture: limits are drawn from the car's own data, never invented (the rev limit band from `id_n_max_max_mt__gear`, the hatched storage limit in the grid). Swap the name "MS43 Tuner" out and the numerals, the cool map path with its single orange cursor, and the limits read from the bin still say what this is.

| Screen | One focal point | Everything else |
|---|---|---|
| Tune | The map grid (highest-contrast pane, the only coloured area) | Save is the strongest control; rail and inspector sit on Frame |
| Live | The engine-speed reading (rpm dial, hero tile, or the rpm strip with its trace) | Load and advance second tier, channel list third |
| Compare | The B − A grid of the selected item (or the list of differing items when none is selected) | Copy B into A is secondary; A and B views are a switch away |

### 10.3 Motion spec

The transitions live in `Theme/Motion.axaml`, which `App.ApplyMotion` loads only while Windows' "Animation effects" setting is on.

| Motion | Spec | Reduced motion (Windows "Animation effects" off) |
|---|---|---|
| Hover, pressed | Overlay and state fills fade in 120 ms, cubic ease-out (buttons, tabs, segments, list rows, window buttons) | Instant |
| Tab underline | Grows from its centre, 200 ms (`scaleX` 0 to 1) | Shown at once |
| Screen change | The screen coming to the front fades in and rises 6 px, 200 ms | Instant |
| Dialog | Scrim fades in, card grows from 97 %, 180 ms | Instant |
| Menus, picker lists, tooltips | Fade in and drop 4 px, 120 ms | Instant |
| Banners | Fade in and slide down 6 px, 180 ms | Instant |
| Switch knob | Slides 150 ms | Jumps |
| Map selection | The selection box glides from the old cell or range to the new one, 120 ms, cubic ease-out (`MapGrid.SelectionGlide`); typing in a cell does not glide | Jumps |
| Live cursor | Redrawn at most 30 times a second (`LiveCursorController.MaxRedrawsPerSecond`), drawn exactly at the sample, never tweened between samples; stale after 1 s (`StaleAfter`) | trail drawn without width falloff; dot unchanged |
| Just-edited flash | 450 ms (`MapGrid.FlashTime`). Frame 0: the cell fill is the cell's final text colour (black or white per 2.3), so start and end differ by 4.58:1 or more. It eases out (cubic) to the new fill. The text colour is recomputed from the blended fill every frame, so the number stays at 4.58:1 or more throughout. Replaces today's white 50 % overlay, which is invisible on light Day cells | No flash; the changed triangle and the key-line message ("Set 4 cells to 27.8 °CRK") confirm the edit |

---

## 11. Mockup data, open questions, checks

### 11.1 Sample values for Live mockups

Illustrative, physically consistent, and labelled "Sample values" outside the frame of every mockup: 2480 rpm in gear 4 at 77 km/h (E46 325i, 4th 1.22, final drive 3.07, 205/55R16), MAFIGA 112.4 mg/stk, MAFTI 118.0 mg/stk, MAP 31.5 kPa, AMP 101.2 kPa, TPS 12.4 °TPS, TCO 88.5 °C, TIA 31.0 °C, TOIL 96.0 °C, IGAAVG 31.1 °CRK. Rev limit 7104 rpm (real, from the bin). 96 channels (real, ms43x ADX). Trends cover the Trend length setting (10, 30 or 60 s); the label reads "Last 10 s" (or 30, 60) once a rate is measured and "Last N samples" before a rate exists. Tune mockups use the real `ip_iga_ron98_pl__n__maf` values unchanged; the live readout on it is "2480 rpm · 112.4 mg/stk · map 27.5 °CRK" (bilinear between rows 2200 and 2500, columns 100.0 and 130.0; the measured 31.1 °CRK advance is a different number).

One data state for every Tune mockup, Day and Night: range selected rows 3750 to 4500 × columns 130.0 to 200.0 (4 × 3), active cell 4000 × 165.0 (33.4 °CRK, raw 152, 0x98), changed cells 3750 × 130.0 and 3750 × 165.0, header "1 item not saved".

### 11.2 Owner decisions (2026-10-20)

1. App mark: keep the dial mark, redrawn in the icon style (5.1).
2. Live layout: readout tiles (B), with a Tiles / Dials setting for the readout band (8).
3. Trend window: a setting, 10 / 30 / 60 s (5.12). The owner's words: "all of those things like that should be configurable"; the Settings dialog holds the view and behaviour preferences listed in 5.12.
4. Values get a slider with − and + under their reading (owner, 2026-10-20: "maybe add a slider to change it more easily or something like that"); see 5.14.
5. One-column maps are centred in the pane (owner, 2026-10-20: "for the 1 column only maps, center them in the middle"); see 8.

### 11.3 Checks every rebuilt screen must pass

- Both themes rendered from the same layout file with only token values swapped (R-34).
- Every text pair 4.5:1 and every control edge, mark and focus ring 3:1, checked with `contrast-check.py` for any colour not listed here (R-25).
- Tab order follows the visual order; every control shows the focus ring; dialogs close with Esc; the surface plot turns with the arrow keys (R-32).
- Empty, loading and error states from section 7 drawn and reachable (R-27).
- 1024 × 680 frame drawn: no horizontal page scroll, nothing clipped, toolbar and save state always visible (C-4).
- Zero orange on idle screens; orange only on the marks listed in 1.4.
- Every number in Bahnschrift with `+tnum`; ids in Cascadia Mono; no U+2212 inside a number, no em or en dash in any string.
- Built and run, every control clicked in both themes, with the click-through recorded (R-35).

### 11.4 Gate status of this document

This is a specification, not a running UI, so R-35 (run and click through) and R-03 (mobile; replaced here by the 1024 × 680 desktop minimum) are verified by the screens built from it, not by this file. Verified here: all colour pairs through `contrast-check.py` (section 1.2, 2.1, 2.5); font coverage and features read from `bahnschrift.ttf`, `SegUIVar.ttf` and `CascadiaMono.ttf`; state copy and rules traced to `TuneViewModel`, `TableEdits`, `CompareViewModel`, `MainWindowViewModel`, `LiveLogger`, `LiveCursorController`, `MapGrid` and `LiveModels`; no em or en dash in this file; no number without a source (sample values are labelled as samples).
