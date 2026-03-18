# MS43 Logger

`MS43Logger` is a C#/.NET 10 logger for BMW MS43 ECUs running the MS4X custom firmware extended logger.

It uses the official MS4X ADX definitions to:

- switch the ECU from `9600 8E1` into the logger session
- poll the custom `0B B0` logging command at `125000` baud
- decode ADX channels, math expressions, linked values, and lookup-backed flags
- show a live desktop dashboard
- print live values to the console
- write CSV logs

## Requirements

- an MS43 ECU with the MS4X extended logging patch
- a K-line interface that can handle `125000` baud reliably
- .NET 10 SDK

Some firmware and adapter combinations only allow the baud-switch command before engine startup. If the ECU rejects the switch while the engine is running, connect first and then start the engine.

## Quick Start

Build everything:

```powershell
dotnet build MS43Logger.sln
```

Run the desktop app:

```powershell
dotnet run --project src/MS43Logger.AvaloniaApp/MS43Logger.AvaloniaApp.csproj
```

The desktop app lets you:

- pick the COM port and ADX variant
- switch between dark and light themes
- connect/disconnect from the ECU
- view all decoded channels live
- pin favorite channels so they stay at the top
- filter channels by name/ID
- optionally write a CSV log directly from the GUI
- choose the CSV path with a file picker

List channels from the bundled B30 definition from the CLI:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --variant b30 --list-channels
```

Log a few channels on `COM3`:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --port COM3 --variant b30 --channel RPM --channel MAF --channel COOLANTTEMP
```

For MS4X `MS43X` custom firmware, use the dedicated bundled definition:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --port COM3 --variant ms43x
```

Capture everything to CSV:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --port COM3 --variant b30 --csv logs\session.csv
```

Stop after a fixed sample count:

```powershell
dotnet run --project src/MS43Logger.Cli/MS43Logger.Cli.csproj -- --port COM3 --variant b22 --count 500 --csv logs\b22-run.csv
```

## Desktop Publish

If you want a standalone Windows build of the desktop app:

```powershell
dotnet publish src/MS43Logger.AvaloniaApp/MS43Logger.AvaloniaApp.csproj -c Release -r win-x64 --self-contained true
```

The published app will be under:

```text
src\MS43Logger.AvaloniaApp\bin\Release\net10.0\win-x64\publish\
```

There is also a helper script that publishes both the desktop app and the CLI into an `artifacts\publish` folder:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Publish-Windows.ps1
```

## CLI Options

- `--port COMx`: serial port for the K-line adapter
- `--variant {b22,b25,b30,ms43x}`: bundled ADX definition variant to use
- `--definition PATH`: use a custom ADX instead of a bundled variant
- `--channel NAME`: repeat to choose channels by ID or title match
- `--list-channels`: print the matched channels and exit
- `--count N`: stop after `N` samples
- `--duration SECONDS`: stop after a time limit
- `--interval-ms N`: optional delay between polls
- `--csv PATH`: write a CSV file
- `--no-console`: suppress the live console output

## Tests

```powershell
dotnet test MS43Logger.sln
```

## Sources

The bundled ADX files and logger behavior are based on these primary sources:

- [MS4X Logger Definition Files](https://www.ms4x.net/index.php?title=Logger_Definition_Files)
- [MS4X TunerPro Data Logging](https://www.ms4x.net/index.php?title=TunerPro_Data_Logging)
- [MS4X Logger.S](https://www.ms4x.net/index.php?title=Logger.S)
- [handmade0octopus/ds2](https://github.com/handmade0octopus/ds2)
