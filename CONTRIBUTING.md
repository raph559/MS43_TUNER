# Contributing to MS43 Tuner

Thanks for helping. MS43 Tuner is a Windows desktop app and a CLI for BMW cars with the Siemens MS43 engine ECU: it logs live data over DS2 on the K-line, reads and edits bins through TunerPro XDF definitions, compares two bins and runs performance tests. Bug reports, fixes, tests, documentation and features are all welcome.

Please read the [rules](#the-rules) before you start: two of them protect people's cars.

## Contents

- [The rules](#the-rules)
- [Build and run](#build-and-run)
- [Test](#test)
- [Report a bug](#report-a-bug)
- [Suggest a feature](#suggest-a-feature)
- [Pull requests](#pull-requests)
- [License of contributions](#license-of-contributions)

## The rules

1. **Never write ECU flash.** The app reads the ECU over DS2 and saves edited bin files on the PC. Flashing a bin, and its checksum, is done with MS4X Flasher. A change that adds flash writing will not be merged.
2. **Never commit bins, XDF files or personal calibration data.** Put your own bins and XDF in `testdata/local/`, which git ignores; `*.xdf`, `*.bin` and `*.bak` are ignored everywhere. A bin saved under another extension is not, so check `git status` before each commit. The same goes for CSV logs, saved results or screenshots that carry your car's data: leave them out unless you mean to publish them.
3. **No absolute local paths** in any committed file, such as a path into your own user folder. Use repository-relative paths such as `docs/firmware/map-use/ve.md`.
4. **Add tests.** A fix comes with a test that fails without it; a feature comes with tests of what it does.
5. **The UI follows the design system** in [docs/design-system.md](docs/design-system.md): colour tokens for Night and Day, type, controls, states and layout. Use the existing tokens and control styles instead of new colours or one-off styles.
6. **UI copy rules** (section 9 of the design system):
   - Sentence case for every label, button, heading and message. No all caps.
   - No em dash and no en dash anywhere. Write ranges in words: "3.0 to 39.0".
   - The ellipsis character "…" (U+2026), never three dots. It ends a command that opens a dialog or picker ("Open bin…") and a status in progress ("Connecting…").
   - Numbers in the data font: Bahnschrift with tabular figures (the `DataFont` resource, `FontFeatures="+tnum"`). The minus inside a number is "-" (U+002D). A missing value is "n/a", never "0" or a dash.
   - Number, space, unit: "2480 rpm", "88.5 °C".
   - One name per thing: bin, XDF, item, cell, breakpoint, channel, definition (section 9.3).
   - Every message says what happened, then what to do.

### Larger changes

For a large change, opening an issue with a short design first saves rework.

Evidence about the firmware lives in `docs/firmware/map-use/`. A change to the map-use rules in `definitions/ms43x001_map_use.json` should record its code addresses and say whether each fact was observed or inferred, as the existing rules do.

## Build and run

You need:

- Windows (the app is developed and released for Windows, win-x64)
- the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- for live features: a K+DCAN USB cable and an MS43 car

Build everything:

```powershell
dotnet build MS43Logger.sln
```

Run the desktop app:

```powershell
dotnet run --project src/MS43Logger.AvaloniaApp/MS43Logger.AvaloniaApp.csproj
```

Run the CLI, for example to list the channels of the bundled B30 definition:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --variant b30 --list-channels
```

Make the release builds (self-contained, single-file win-x64 app and CLI in `artifacts/publish/`):

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Publish-Windows.ps1
```

### Where things are

| Path | What it holds |
|---|---|
| `src/MS43Logger.Core` | DS2 session, ADX definitions, live logger, XDF and bin reading, value codec, edits, compare, performance maths |
| `src/MS43Logger.AvaloniaApp` | the desktop app (Avalonia 11): Live, Tune, Compare and Performance |
| `src/MS43Logger.Cli` | the CLI: logging, `rate`, `show` and `diff` |
| `tests/MS43Logger.Core.Tests` | xUnit tests for Core |
| `tests/MS43Logger.AvaloniaApp.Tests` | UI tests on Avalonia.Headless, including checks for styling, glyphs, accessible names, clipping at 1024 × 680 and keyboard access |
| `definitions/` | bundled ADX logging definitions (see [NOTICE.md](NOTICE.md)) and the project's JSON data |
| `docs/design-system.md` | the UI's design system: tokens, controls and copy rules |
| `docs/firmware/map-use/` | firmware notes behind the map-use rules |
| `scripts/` | release packaging and data generation |

The current live-tuning implementation is in [`src/MS43Logger.Core/LiveTuning`](src/MS43Logger.Core/LiveTuning):

- `OverlayProtocol` handles session framing, patches and version checks; `OverlaySessionClient` handles session state and retries.
- `OverlayMemory` handles RAM allocation and directory status; `OverlayCatalogue` holds map IDs and addresses.
- `OverlaySettings` defines fields and validates settings; `OverlayTriggers` names switch choices; `OverlayWatch` handles watch requests and log replies.

The public `OverlayV3` through `OverlayV10` entry points remain for compatibility. Their historical catalogues and validation rules intentionally differ in places. Current code should use the responsibility-based helpers. Protocol and catalogue changes are covered by `CurrentOverlayTests`, `OverlayAllocationTests`, `OverlayPopsTests` and `OverlayWatchAndTriggerTests`.

## Test

While you work, run only the test classes your change touches, with `--filter`:

```powershell
dotnet test tests/MS43Logger.Core.Tests --filter "FullyQualifiedName~BinComparerTests"
dotnet test tests/MS43Logger.AvaloniaApp.Tests --filter "FullyQualifiedName~MapGridTests|FullyQualifiedName~TuneViewTests"
```

Before you open a pull request, run the full suite once:

```powershell
dotnet test MS43Logger.sln
```

Some tests need real bins and an XDF from the maintainer's car. In `tests/MS43Logger.Core.Tests` they are marked `[RealFileFact]` and look for their files in `testdata/local/` (or in the folder named by the `MS43_TESTDATA` environment variable). Without those files they are **skipped**, and the test run lists them as skipped, not passed. The file names they expect are in `tests/MS43Logger.Core.Tests/TestData.cs`. The UI tests that open the car's bin (through `TuneScreen` and `TuneRailTests.Local`) look in `testdata/local/` only, and without the files they return early and count as **passed**, so a green UI run without those files has not exercised them. A new test should build its own data where it can (see `LivePayloads.cs` and `SyntheticRuns.cs`), so it runs for everyone.

Passing tests are not the same as working in a car. If your change touches the connection, logging, the live cursor or the performance tests, say in the pull request what you checked on a car (cable, firmware, engine running or not), or say that it has not been checked on a car.

## Report a bug

Open an issue with the **Bug report** form. It asks for:

- what happened, word for word for any message, and the steps to get there
- the app version or commit (`git rev-parse --short HEAD` if you built from source) and your Windows version
- the cable and COM port
- the **definition** you used: `ms43x`, `b22`, `b25` or `b30`
- the **firmware id**: a stock MS430069-family id or `MS43X001`, and for Tune or Compare the XDF file name
- the **connection log** for connection problems: on Live, "Show details" on the banner opens it, and its text can be copied
- the **CSV log** if the problem shows in logged values (from Live or the CLI's `--csv`)

**Do not attach a bin unless you want to.** A bin holds your car's calibration, and anything attached to an issue can be seen by everyone who can see this repository. Most Tune and Compare bugs can be shown with the item's name and the values you see.

Problems with flashing, the checksum or the MS43X001 firmware itself belong with the [MS4X project](https://www.ms4x.net). Security problems go through [SECURITY.md](SECURITY.md), not a public issue.

## Suggest a feature

Open an issue with the **Feature request** form: the problem you are trying to solve, what the app could do about it, and which screen it concerns (Live, Tune, Compare, Performance, Settings or the CLI).

## Pull requests

1. Fork the repository and branch from `main`.
2. Keep one change per pull request. Small ones are reviewed faster.
3. Write the test first, then the change.
4. For a UI change, add screenshots of the same view in Night and in Day.
5. Add a line under "Unreleased" in [CHANGELOG.md](CHANGELOG.md) for anything a user would notice.
6. Use `type(scope): summary` for commit subjects and pull request titles, for example `docs(build): clarify prerequisites`. Choose the type that fits the change: `feat`, `fix`, `refactor`, `perf`, `test`, `docs`, `build`, `ci`, `chore` or `revert`. Use the body to explain why and what was checked.

### Commit subjects and PR titles

The scope names the affected subsystem. Common scopes are `logging`, `protocol`, `tuning`, `compare`,
`performance`, `ui` and `theme`. Use `deps` for package updates and `actions` for workflow dependencies.
Other scopes are fine when they describe the change. Use lowercase letters and digits, with hyphens between words.

For example:

- `feat(tuning): add live-map editing`
- `fix(protocol): reject a stale revision`
- `chore(deps): bump System.IO.Ports`
- `ci(actions): bump actions/checkout`

The summary must be nonempty, on one line, without leading or trailing whitespace. Use `!` after the scope
only for a breaking change, such as `feat(protocol)!: require a new session format`.
The [PR title check](.github/workflows/pr-title.yml) validates the format when a PR opens, reopens, changes title
or receives commits. Dependabot uses `chore(deps):` for NuGet updates and `ci(actions):` for Actions updates.
Match the final commit subject to the PR title when squashing.

The pull request template repeats this checklist:

- [ ] Tests were added first and failed before the change
- [ ] The affected test classes pass
- [ ] The full suite passes (`dotnet test MS43Logger.sln`)
- [ ] No bins, XDF files, personal data or absolute local paths in the diff
- [ ] Nothing here writes ECU flash
- [ ] UI and text follow the design system and its copy rules (`docs/design-system.md`, section 9 for copy)

### Commit bodies

Use a body when the subject leaves useful context out. Describe the problem, the resulting behavior and why
the change is needed. For protocol or firmware changes, name the affected version and the byte, address or
register contract when it helps explain the change.

State which checks ran for this change and their limits: missing fixtures, a partial suite, emulator checks
or remaining vehicle validation. Keep results tied to the source version they tested. Record later car results
in the current documentation or release notes.

An import or snapshot should say what it brings in. Preserve credits and validation limits, and leave copied
chat requests and private planning links out of the message. A small, focused change can have an empty body.

## License of contributions

MS43 Tuner's own code and documentation are licensed under the GNU General Public License v3.0 only (`GPL-3.0-only`, see [LICENSE](LICENSE)). By opening a pull request you agree that your contribution is licensed under the same terms.

The exception is the four ADX logging definitions in `definitions/`: they are by the MS4x Dev Team, bundled with credit, have no stated license and are not covered by the GPL (see [NOTICE.md](NOTICE.md)). The MS43X001 XDF is not in the repository for the same reason.

Only submit work you have the right to license under the GPL: do not paste code or definition data from sources whose license does not allow it, including the MS4X ADX and XDF files.

Everyone taking part is expected to follow the [code of conduct](CODE_OF_CONDUCT.md).
