# MS43 Tuner restyle: design

Date: 2026-10-20. Status: waiting for the owner's approval.

Labels: **[owner]** decided by the owner in the design conversation; **[call]** decided by the designer, open to veto;
**[ver]** checked against the code, the files or a render.

Companion document, binding for every visual detail: `2026-10-20-ms43-restyle-system.md` (the design system: colour
tokens, map colours, type, spacing, components, icons, states with their copy, layout, motion).

## 1. Intent

The owner rejected the app's whole visual style: "pretty much everything.. the general style". They want "a modern clean
look" and named no reference app **[owner]**. They use the app alone, at a desk and in the car, to log their E46 325i's
MS43 ECU, edit its maps and compare bins.

Four Tune-screen looks were drawn with the car's real ignition map. The owner picked look A ("Studio light") as the
**Day** theme and look B ("Graphite dark") as the **Night** theme **[owner]**. They then asked for the antislop rules to be
applied to the design, in "during" mode, from now on **[owner]**. Every screen was audited and rebuilt on one shared
system, and every screen passed the antislop Delivery Gate (`.superpowers/antislop/delivery-gate-mockups.md`, local).

## 2. Goals, non-goals, anti-goals

Goals:

1. Every screen and window is rebuilt on the companion design system, with one layout and two skins: Day = look A,
   Night = look B **[owner]**. This covers the Live, Tune and Compare screens and the Settings dialog (new). It also
   covers the save prompt (today's `ConfirmDialog`), the connection log (today's `ErrorWindow`, "CONNECTION FAILED")
   and the Performance tests window.
2. Live uses layout B, "readout tiles". A **Readouts** setting swaps only the top readout band between **Tiles**
   (default) and **Dials** **[owner]**.
3. A **Settings** dialog holds the view and behaviour preferences in section 4.3. Its shortcuts in the status bar
   (Readouts, Night / Day) stay **[owner]**: "all of those things like that should be configurable".
4. The header keeps the app's dial mark, redrawn in the new icon style **[owner]**.
5. Every data view has the empty, loading and error states of system section 7, each naming its cause and the next
   action **[call, antislop R-27]**.
6. Every screen holds at the 1024 × 680 minimum window size: nothing clipped and no horizontal page scroll **[call]**.
7. Every screen, as built in Avalonia, passes the antislop Delivery Gate, with the evidence recorded **[owner]**.

Non-goals:

- Changing how logging runs: the 20 ms pause between requests, the stop after three timeouts in a row, the telegrams,
  the CSV columns **[owner]**: "logging timing is fixed".
- Changing what the app computes: decoded values, map values, Compare results, live cursor maths, the save, backup and
  edit-log logic. The only exceptions are the two Saving switches in 4.3 **[call]**.
- Removing or renaming a feature or a keyboard shortcut. Ctrl , is added for Settings **[call]**.
- New NuGet packages (spec 2026-10-19, section 3).
- The other two Live layouts ("modern dials" as a whole screen, "strip charts"). They are dropped **[owner]**.

Anti-goals (would make the restyle a failure even if it builds):

- Logging that gets slower or less reliable than it is today.
- A value shown differently from today, apart from the deliberate number rules in 4.4.
- Orange on anything that is not live, text below WCAG AA (4.5:1, large 3:1), or a control boundary below 3:1.
- A theme in which something breaks, or a control that does nothing.
- Losing edits. The .bak switch defaults to on, and the save prompt never promises a .bak that will not be written.

## 3. Constraints

- C#/.NET 10, Avalonia 11.3.12 with FluentTheme as the base and the app's own styles on top, and the existing solution
  layout. Windows is the owner's platform. Keep Avalonia's cross-platform paths: Segoe UI Variable, Bahnschrift and
  Cascadia Mono ship with Windows; elsewhere Inter (already bundled) is the fallback **[ver]**.
- No new packages.
- No windows opened on the owner's desktop while building. Verify through the headless test project
  (`tests/MS43Logger.AvaloniaApp.Tests`, `Screens.Save` writes PNGs to `artifacts/screens/`).
- Commit only when the owner asks, with no AI co-author trailer.
- The mockups hold the owner's real calibration data. They stay local and untracked in `.superpowers/design/`. Committed
  documents refer to them by path only.
- The existing tests keep passing. Tests that pin old styling, such as `ShellTests.ThemeSwitchChangesPaneBrush`
  (expects `#141920` / `#F5F6F8`), are updated to the new tokens, not deleted.

## 4. The design

### 4.1 System

The companion document is binding. These are its main rules **[call, built on the owner's looks]**:

- **Orange means now.** Orange marks only what moves with the engine and the state of the link: the live cursor, value
  arcs and bars, the active gear, the Connected dot and word, the logging rate. Live numbers stay ink. An idle screen
  shows no orange at all.
- **One map colour path in both themes**, from sea teal to ultramarine. Day runs light to dark and Night dark to light,
  so a map keeps its look across themes. Cells at the storage limit are hatched and kept out of the colour range. Cell
  text is black or white, whichever gives 4.58:1 or more on any fill.
- **Bahnschrift with tabular figures for every number.** This is the identity motif. UI text is Segoe UI Variable, and
  raw ids, addresses and scalings use Cascadia Mono.
- **Keyboard focus is a 2 px ink ring with a gap.** It is never orange.
- **Shapes, not colour alone, mark cell states.** A selection rectangle, a changed triangle, a differs ring and a live
  dot each have their own shape.
- Dials are set to ENERGY 1, RHYTHM 1, MOTION 1. The only motion is hover, the live cursor, and a 450 ms flash on cells
  just edited.

### 4.2 Screens and reference mockups

The mockups are static HTML with renders, local in `.superpowers/design/2026-10-20-restyle/clean/`. Every one passed the
Delivery Gate **[ver]**.

| Screen | Mockups (Day / Night) | Renders in `clean/png/` |
|---|---|---|
| Tune | `tune-day.html`, `tune-night.html` | `tune-{day,night}-{1440,1024}.png` |
| Live, Tiles | `live-b-day.html`, `live-b-night.html` (each with a board of the off, connecting, failed and lost frames) | `live-b-{day,night}-1440.png`, `-1024.png`, `-f1..f4` |
| Live, Dials | `live-b-dials-day.html`, `live-b-dials-night.html` | `live-b-dials-{day,night}-{1440,1024}.png`, `-off`, `-lost` |
| States | `states-day.html`, `states-night.html` (Tune, Live, Compare: empty, loading, error) | `states-{day,night}-1440.png` |
| Settings | `settings-day.html`, `settings-night.html` (the dialog over the Tune screen, with a board holding the save-failure state) | `settings-{day,night}-{1440,1024}.png`, `-notsaved-1440.png` |
| App mark and gear | in every header above | `mark-zoom.png`, `gear-zoom.png` |
| Compare | `compare-day.html`, `compare-night.html` (B − A map; board: value item, bytes outside the XDF, after a copy) | `compare-{day,night}-{1440,1024}.png`, `-value`, `-bytes`, `-copied` |
| Tune items | `tune-items-day.html`, `tune-items-night.html` (a value with its slider; board: dragging, a value with an XDF range, switch, flag, curve, axis) | `tune-items-{day,night}-{1440,1024}.png` and `-{drag,range,switch,flag,curve,axis}-{1440,1024}.png` |

Compare and every Tune item kind (value, switch, flag, curve, axis) have their own mockups, drawn with the real car
and release data **[call]**. The panes are specified in system 5.13 to 5.15. The MS43X001 XDF has no read-only item,
so that state appears only in the states sheet **[ver]**.

The save prompt, connection log and Performance tests window are restyled with the system's dialog, button and copy
rules, with no separate mockup **[call]**. Examples: "Connection failed" in sentence case instead of "CONNECTION FAILED",
"0 to 100 km/h" instead of "0-100 KM/H", and "Start test" instead of "START TEST".

One small fix rides along **[call]**. Today the Performance tests window reads only a channel named `VEHICLESPEED`,
but the owner's ms43x definition calls it `VS` (`MainWindowViewModel.ShowSample`) **[ver]**. As a result "Start test"
waits forever with the definition the app picks for their bin. The window will accept `VS` as well, the way the gauges
accept `N` or `RPM`, so its main control does something.

### 4.3 Settings

The dialog opens from the gear button at the right end of the header, or with Ctrl , on any screen **[owner]**. Every
change applies at once and is saved to `settings.json` (`AppSettings`). There are no Apply or Cancel buttons
**[owner]**. The layout and copy are in system 5.12.

| Setting | Values (default first) | What it does |
|---|---|---|
| Theme | Night, Day, Follow Windows | Follow Windows tracks the Windows app theme while the app runs. Night / Day in the status bar is a shortcut and shows the same state. |
| Live readouts | Tiles, Dials | Swaps the Live readout band (system 8). Tiles / Dials in the Live status bar is a shortcut. |
| Trend length | 10 s, 30 s, 60 s | Sets how much history every channel keeps for its trend line. The history holds at least that long at the measured rate (60 s at 38 samples/s is about 2300 samples per channel), and the 80 px sparklines draw from it. The column label follows ("Last 30 s"). |
| Log folder | Documents\MS43 Logs | The folder for new, automatically named CSV logs. Browse… in the Live CSV row still picks one file for one session. |
| Start writing the CSV log when connected | off | The "Write CSV log" switch starts in this state when you connect. |
| Open maps showing | Physical values, Raw values | Whether a map opens in physical units or raw numbers. Today the app has no on-screen raw toggle (`TableEditorViewModel.ShowRaw` has no control) **[ver]**, so this setting is how raw values become visible. |
| Live cursor trail | Short, Off, Long | Short is today's trail (32 points, 1.2 s, `CursorTrail`) **[ver]**; Long keeps about 3.6 s **[call]**; Off draws no trail. |
| Reopen the last bin when the app starts | on | New behaviour: today the app starts with nothing open **[ver]**. If the last bin or its XDF is missing, the app starts on the empty Tune state with a banner naming the missing file **[call]**. |
| Keep a .bak copy before the first save | on | When off, Save writes no `.bak`, and the save prompt drops its sentence about the .bak. |
| Add changes to the edit log next to the bin | on | When off, Save does not append to the TunerPro-format `<bin>.log`. |

Kept fixed and named in the dialog: the logging timing **[owner]**. The meaning of orange stays fixed too, but needs no
setting.

Two behaviours the build adds **[call]**:

- `AppSettings.Save` reports a failed write instead of ignoring it. The dialog then shows "Not saved: <reason>" with
  "Try again", and the choices still apply until the app closes.
- With Theme set to Follow Windows, the status bar's Night / Day shows the theme in effect. Pressing one of them sets
  a fixed theme.

### 4.4 Values and one-column maps

- A value gets a slider with − and + under its reading, for quick changes by dragging and one raw step at a time
  **[owner]**: "maybe add a slider to change it more easily or something like that". The Value field and Set stay for an
  exact number. A drag is one undo step, and Esc cancels it (system 5.14).
- One-column maps (curves and axes) are centred in the pane **[owner]**: "for the 1 column only maps, center them in the
  middle" (system 8).

### 4.5 Number rules you will see

These rules come from Bahnschrift and the system (3.3) **[call]**:

- No digit grouping ("2480 rpm").
- The plain "-" for minus.
- "n/a" for a missing value instead of today's dash.
- Decimals: the Live channel list and the pinned tiles keep the definition's precision, as today (112.41 mg/stk). The
  readout band rounds for a glance: rpm and speed whole, load and advance one decimal. Today's gauges round to 0, 0
  and 1 decimals, so only the load reading gains a decimal **[call]**. Map values keep the XDF's decimals.

The rev limit on Live stays as it is today: read from the open bin.

## 5. Verification and acceptance

Automated, headless, for every screen:

- Renders in both themes at 1440 × 900 and 1024 × 680, saved to `artifacts/screens/` and compared against the mockups.
- Every colour pair not already measured is checked with the antislop contrast script.
- The full test suite stays green.

New tests cover:

- Settings: defaults, persistence across a restart, every setting's effect, Follow Windows mapping to the Windows
  theme, and the status-bar shortcuts staying in sync with the dialog.
- Ctrl , opens Settings and Esc closes it.
- Tiles and Dials show the right band.
- Trend length changes how much history is kept.
- Trail Off / Short / Long.
- Reopening the last bin, both when the file is present and when it is missing.
- No `.bak` when that switch is off, and no edit-log line when that switch is off.
- The slider's span rule; one undo step per arrow key, per − or + and per drag; Esc cancels a drag; Page Up, Page Down,
  Home and End.
- Every state in system section 7 is reachable.

The Delivery Gate runs per screen. Its click-through (R-35) is a headless run that clicks every control and records what
happened.

Real-world acceptance (only the owner can do these, and they stay "not done" until they do):

- Run the app on their PC and look at Live, Tune, Compare and Settings in both themes.
- Log in the car with Tiles and with Dials, and watch the cursor move on a map.
- Change each setting, then restart the app and see it kept.

## 6. Decisions marked [call] (for the owner to check)

- No change to what the app computes or to its shortcuts.
- Ctrl , opens Settings.
- The three dialogs are built from the system without their own mockup.
- The Long trail is about 3.6 s.
- When the last bin is missing at start, the empty state shows with a banner.
- The number rules in 4.4.
- A storage-limit cell is hatched only when it is an outlier (system 2.2), so ordinary zeros and neutral factors stay
  normal cells.
- On Live, pinned channels sit in the left column and the rest in the right, and each column scrolls on its own
  (system 5.6).
- The Performance tests window accepts `VS` as the speed channel.
- A switch shows its choices as an always-visible list, not a dropdown.
- A curve's chart uses square points; the round dot stays reserved for the live cursor.
- Curves and axes show the single column's unit, which today is not shown.
- The Compare list gets a search field (a small text filter added to `CompareViewModel`).
- The slider track beyond the value is Edge, because Track measured 1.10:1 (Day) and 1.25:1 (Night) on the pane, so the
  span's end vanished.
- A tick marks every XDF end on the slider, also when the span is the XDF range.
- A value counts as outside its XDF range only beyond half a raw step.
- − sits at the low end of the slider and + at the high end.
- Axis rows are numbered 1 to n, as the refusal message counts breakpoints.
- Compare and the non-map Tune panes get mockups before the build.
- The rules in 4.1, which the companion system details:
  - The single map colour path replaces look B's green-to-yellow and look A's violet.
  - Orange replaces look B's pink.
  - Live numbers are ink, not orange.
  - The focus ring is ink.
  - Pins replace stars.
  - The file actions sit in the header's file menu.

## 7. Left to the builder

- Control internals and Avalonia styling structure: resource dictionaries per token, one `FocusAdorner`, the icon
  geometry file.
- How Follow Windows is wired.
- The sample-history sizing.
- The Performance tests window layout, within the system.
- Test structure and the order of work.

The plan comes next, through writing-plans.

## 8. Local references

- Design system working copy and mockup generators: `.superpowers/design/2026-10-20-restyle/clean/` (`system.md`,
  `gen/`, `tools/`).
- Gate reports: `.superpowers/antislop/delivery-gate-mockups.md`.
- First drafts (the four looks, the three Live layouts): `.superpowers/design/2026-10-20-restyle/first-drafts/`.
- Status note: `.superpowers/design/2026-10-20-restyle/README.md`.
