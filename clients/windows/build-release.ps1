param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$ApiBaseUrl = "",
    [switch]$SelfContained,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientsRoot = Split-Path -Parent $scriptRoot
$repoRoot = Split-Path -Parent $clientsRoot
$projectPath = Join-Path $scriptRoot "VimoVPN.Client\VimoVPN.Client.csproj"
$distRoot = Join-Path $repoRoot "dist\windows"

if (-not (Test-Path $projectPath)) {
    throw "Project not found: $projectPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-Date -Format "yyyyMMdd-HHmmss"
}

$packageName = "VimoVPN.Client-$Runtime-$Version"
$publishDir = Join-Path $distRoot $packageName
$zipPath = Join-Path $distRoot ($packageName + ".zip")
$selfContainedValue = "false"
if ($SelfContained.IsPresent) {
    $selfContainedValue = "true"
}

New-Item -ItemType Directory -Force -Path $distRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $repoRoot ".dotnet-home") | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $repoRoot ".dotnet-home"

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "-p:RestoreIgnoreFailedSources=true",
    "-p:UseAppHost=true",
    "-o", $publishDir
)

Write-Host "Publishing $projectPath"
Write-Host "Output: $publishDir"
dotnet @publishArgs

$publishedSettingsPath = Join-Path $publishDir "appsettings.json"
if (-not [string]::IsNullOrWhiteSpace($ApiBaseUrl) -and (Test-Path $publishedSettingsPath)) {
    $settings = Get-Content $publishedSettingsPath -Raw | ConvertFrom-Json
    $settings.ApiBaseUrl = $ApiBaseUrl
    $settings | ConvertTo-Json -Depth 10 | Set-Content $publishedSettingsPath -Encoding UTF8
}

$startHerePath = Join-Path $publishDir "START-HERE.txt"
$startHereContent = @(
    "VimoVPN Windows Client",
    "",
    "1. Place sing-box.exe and wintun.dll into the runtime folder if they are not already there.",
    "2. Launch VimoVPN.Client.exe.",
    "3. Click Request Code and then Open Telegram.",
    "4. Confirm login in the Telegram bot.",
    "5. Use Connect Best to start the VPN tunnel.",
    "",
    "Current package settings:",
    "ApiBaseUrl = $(if ([string]::IsNullOrWhiteSpace($ApiBaseUrl)) { 'from bundled appsettings.json' } else { $ApiBaseUrl })",
    "Runtime = $Runtime",
    "SelfContained = $($SelfContained.IsPresent)"
)
$startHereContent | Set-Content $startHerePath -Encoding UTF8

if (-not $NoZip.IsPresent) {
    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
    Write-Host "ZIP created: $zipPath"
}

Write-Host "Package ready: $publishDir"
