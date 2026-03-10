param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopOut = Join-Path $repoRoot "artifacts\publish\$Runtime\desktop"
$cliOut = Join-Path $repoRoot "artifacts\publish\$Runtime\cli"

Write-Host "Publishing Avalonia desktop app to $desktopOut"
dotnet publish (Join-Path $repoRoot "src\MS43Logger.AvaloniaApp\MS43Logger.AvaloniaApp.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    /p:PublishDir="$desktopOut\"

Write-Host "Publishing CLI to $cliOut"
dotnet publish (Join-Path $repoRoot "src\MS43Logger.Cli\MS43Logger.Cli.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:PublishDir="$cliOut\"

Write-Host ""
Write-Host "Desktop app:"
Write-Host "  $desktopOut"
Write-Host "CLI:"
Write-Host "  $cliOut"
