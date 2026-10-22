# Changelog

All notable changes to MS43 Tuner are recorded in this file. The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Nothing has been released yet, so everything is under "Unreleased". Until the first release the entries are grouped by area of the app instead of by kind of change. They summarise the 22 commits from 2026-03-10 to 2026-10-21.

## [Unreleased]

### Logging

- Live data from the MS43 over DS2 on the K-line: the app switches the ECU from 9600 baud 8E1 into the logger session at 125000 baud and polls the custom `0B B0` logging command.
- Bundled ADX logging definitions by the MS4x Dev Team: Extended Log v2.9 for the M54B22, M54B25 and M54B30, and the MS43X001 logging definition (see [NOTICE.md](NOTICE.md)). Channels, math expressions, linked values, lookup flags, status bits and bitmask channels are decoded.
- Live screen: readouts as tiles or dials, pinned tiles, and a two-column channel list with a trend line for each channel, a filter and pins.
- CSV logs from the app (optionally started on connect) and from the CLI (`--csv`), with the logging rate shown in the header, at the end of a CLI run, and by the CLI's `rate PATH.csv` command.
- CLI logging with channel selection, sample count or duration limits, and a custom ADX (`--definition`).
- Connection states with banners; "Show details" opens the full connection log to copy into a report.
- Each sample carries the time of the request that answered it.

### Tune

- Reads a 512 KiB bin through a TunerPro XDF: firmware identification with a warning when the XDF describes other firmware, and raw and physical values from the XDF's scaling. The CLI prints any item with `show --bin PATH --xdf PATH NAME [--raw]`.
- The rail lists every XDF item by category, with search on every word (title, description, category, units and address), pins, hover cards, and sort by name, address, kind or size. It resizes from 200 to 480 px and collapses to a strip (Ctrl B).
- Map grid on a heat ramp, and editors for curves, values, switches and flags, with a value slider.
- Edit tools: set, add, scale %, step, paste, and interpolate and smooth along X, Y or both, as in TunerPro. Clamp and XDF range warnings and axis order checks. + and - step the shown value, also on inverted maps such as the AFR target.
- Undo and redo on every screen (Ctrl Z, Ctrl Y and Ctrl Shift Z), covering edits, values and pins.
- 3D surface: turn it by dragging, zoom towards the pointer, move with Shift-drag, reset with Home, open it large with Enlarge. It stays centred as it turns, with breakpoint and height scales. Edit from the plot by click, Shift-click, + and -, or Ctrl-drag with a preview, with undo and redo in the large view.
- Save and Save as write through a temporary file, keep one `.bak` of the file as it was before the first save in a session, and append a TunerPro-style edit log. Both can be turned off in Settings.
- Live cursor: while logging, the shown map, curve or axis marks where the engine is, found the way the ECU searches its axes, with the surrounding cells weighted, a fading trail and the interpolated value. Gear-indexed tables read the gear from the top bits of the rpm word. Tables whose inputs the logger does not carry say why.
- Map use: next to the live readout, whether the ECU is using the map on screen: "In use", "Not in use", a blend such as "RON98 72 % · RON91 28 %", "Possibly in use" or "Selection unknown". It covers VE 1 to 8, ignition part load, idle and optimum, the full-load AFR target, the VANOS maps (part load told from full load) and the catalyst-heating maps. The rules are data in `definitions/ms43x001_map_use.json`, with their evidence in `docs/firmware/map-use/`.
- Compare with B inside Tune: A, B or B − A on the map, read as units, raw counts or %, and Copy from B. The remembered B opens with A.

### Compare

- Compares the open bin (A) with a second bin (B) through A's XDF: every map, curve, value, switch and flag that differs, with the changed cells, the largest change and any axis that differs, plus the bytes that differ outside every XDF item, by flash area.
- Compare tab: a kind filter, items grouped by category, the B − A grid on a diverging scale and the A and B grids, B − A as units, raw counts or %, "Copy B into A" through the undo history, "Edit in Tune", and a "Differs from B" filter on the Tune rail.
- The list resizes and collapses like the Tune rail and has hover cards. A and B can be swapped after a confirmation.
- The CLI lists the same differences with `diff A.bin B.bin --xdf PATH`.

### Performance

- A Performance tab next to Live, Tune and Compare, fed by the live DS2 samples with no extra requests.
- Tests: full-throttle pull in 2nd, 3rd or 4th up to the bin's rev limit, knock check, roll-ons 60 to 100 and 80 to 120 km/h, standing starts 0 to 100, 0 to 200 and 100 to 200 km/h, speed calibration with an optional GPS correction, car values, pedal step and link test.
- Each run has a ready check, a live view (rpm, progress, knock per cylinder, rpm against time over a baseline) and a result: interpolated band times, wheel power and torque estimates, knock, conditions, flags and the tune it ran on.
- Results are saved with a name, a description and the raw samples, then listed, renamed, deleted and compared: overlaid power, torque, ignition, knock, VANOS and AFR traces, B − A, condition mismatches and the tables that differ, with both runs recomputed from the current car values.
- Warm-up warnings on Live and Performance: a red coolant banner and an amber oil banner while the temperature is below 80 °C. Each clears after 10 s at 80 °C or more and comes back below 75 °C. A cold engine keeps a run from starting.
- Car values: the tyre size is picked from width (135 to 355 mm), profile (25 to 85) and rim (13 to 22 in), and a size saved in another form still selects its parts.
- Removed: the old performance dashboard window, replaced by the tab.

### Look and feel

- Night and Day themes on one design system ([docs/superpowers/specs/2026-10-20-ms43-restyle-system.md](docs/superpowers/specs/2026-10-20-ms43-restyle-system.md)): colour tokens, shared control styles, an ink focus ring and a stroke icon set, with moon and sun buttons and a cross-fade between themes.
- A header with a file menu that is also the window's title bar: drag to move, double-click to maximize, and Minimize, Maximize and Close buttons that stay usable over dialogs. A status bar and in-window dialogs.
- Settings: theme, readouts, trend length, log folder, CSV on connect, map values, cursor trail, reopening the last bin, the `.bak` and the edit log. Changes apply at once and are saved.
- Animations only while Windows' animation effects are on: hover fades, the tab underline, screens, dialogs, popups, banners, switches, the rail, the map selection gliding between cells, and segmented controls whose raised pill slides to the new choice. Live values never animate.
- Filled, soft-edged buttons, pickers and tool groups, slim scroll bars, and an app icon (the header's dial) for the exe, the window and the taskbar.

### Fixes

- 16-bit channels of the `ms43x` definition are read low byte first, as the definition flags them. Before this fix rpm, loads, MAF and the active VE word decoded byte-swapped in Live, CSV logs and the CLI; CSV logs recorded with `ms43x` before it hold swapped 16-bit values. The stock Extended v2.9 definitions decode as before.
- Cars on MS43X001 are logged with that firmware's own definition, the `ms43x` variant, which fixes the wrong ADX file being used.
- The gear readout no longer reads one gear high above about 4100 rpm.
- Clicking a rail category header or a Compare row no longer crashes the app.
- The selection and focus rings are no longer cut on the first row and column of a map.
- A press on the value slider no longer writes a value, a locked edit log no longer reads as a failed save, and a disconnect is no longer reported as a logging failure.
- Trimmed names have tooltips, the arrow keys keep the active cell in view, and the search icon sits inside its field.
- A fix to the theme switch.

[Unreleased]: https://github.com/raph559/MS43_LOGGER/commits/main
