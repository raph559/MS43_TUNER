# Security policy

## Supported versions

| Version | Supported |
|---|---|
| `main` (the latest commit) | Yes |
| Anything older | No |

Fixes land on `main`. If you run an older build, update to the latest one before you report.

## Reporting a vulnerability

Report it privately through GitHub:

1. Open the **Security** tab of this repository.
2. Choose **Report a vulnerability**.
3. Describe the problem, the commit or release you tested, the steps or file that show it, and what an attacker could do with it.

Only the maintainer, [@raph559](https://github.com/raph559), and you can read the report. You will get an answer there, and the fix, its timing and any credit are agreed in the report before anything is made public.

Please do not open a public issue, pull request or discussion for a vulnerability.

## Scope

What the app does, so you can tell what counts:

- **It reads the ECU and never writes its flash.** It talks to the ECU over DS2 on the K-line through a K+DCAN USB cable, to log live data. Flashing a bin, and its checksum, is done with MS4X Flasher, outside this project.
- **Your files stay on your PC.** Edited bins are saved where you choose, with a `.bak` copy and an edit log (`CAR.log` for `CAR.bin`) beside them while those settings are on. The app keeps its settings and saved performance results in `%AppData%\MS43Tuner`, and its CSV logs in `Documents\MS43 Logs` unless you choose another folder; the CLI writes a CSV only where `--csv` says.
- **It does not use the network.** No accounts, no telemetry, no update checks, no online services.

In scope, for example:

- any way to make the app or the CLI send a request that writes ECU flash or changes the ECU's calibration
- a bin, XDF, ADX, CSV or saved result file that makes the app run code, or write or delete files other than the ones it is asked to save
- a save that can lose the original bin, such as overwriting it without the `.bak` when the setting is on
- a vulnerable dependency that is actually reachable in the Windows app or CLI

Out of scope:

- MS4X Flasher, the MS43X001 firmware, and the ADX and XDF definitions themselves: report those to the [MS4X project](https://www.ms4x.net)
- the cable, its drivers and Windows
- driving safety: performance tests belong on a closed road or a track, and wheel power is an estimate, not a measurement

## What not to post publicly

Until a fix is out, keep these out of public issues, pull requests, discussions and chats:

- the details of the vulnerability and how to trigger it
- proof-of-concept files or code
- your bins, logs, results, or anything that identifies you or your car
