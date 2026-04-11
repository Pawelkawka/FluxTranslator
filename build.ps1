param(
    [string]$OutputDir     = (Join-Path $PSScriptRoot "build"),
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Error "dotnet SDK not found. Download it from https://dotnet.microsoft.com/download"
}

$sdkVersion = (& dotnet --version 2>&1)
Write-Host "Using .NET SDK $sdkVersion" -ForegroundColor Cyan

$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    Write-Error ("Python not found.`n" +
        "Download and install Python 3.14 from:`n" +
        "https://www.python.org/ftp/python/3.14.4/python-3.14.4-amd64.exe / https://www.python.org/downloads/release/python-3144/`n" +
        "Make sure to check 'Add Python to PATH' during installation.")
}

$pyVersion = (& python --version 2>&1)
Write-Host "Using $pyVersion" -ForegroundColor Cyan

$requirementsFile = Join-Path $PSScriptRoot "src\FluxHelper\requirements.txt"
if (Test-Path $requirementsFile) {
    Write-Host ""
    Write-Host "Installing Python requirements..." -ForegroundColor Cyan
    & python -m pip install -r $requirementsFile
    if ($LASTEXITCODE -ne 0) {
        Write-Error "pip install failed with exit code $LASTEXITCODE."
    }
    Write-Host "Python requirements installed." -ForegroundColor Green
} else {
    Write-Warning "requirements.txt not found at $requirementsFile - skipping pip install."
}

$projectFile = Join-Path $PSScriptRoot "src\FluxTranslator.csproj"
if (-not (Test-Path $projectFile)) {
    Write-Error "Project file not found: $projectFile"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-Host ""
Write-Host "Building FluxTranslator..." -ForegroundColor Cyan
Write-Host "  Configuration : $Configuration"
Write-Host "  Output        : $OutputDir"
Write-Host ""

& dotnet publish $projectFile `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $OutputDir `
    /p:Platform=x64

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE."
}

$backendSrc = Join-Path $PSScriptRoot "src\FluxHelper"
$backendDst = Join-Path $OutputDir "FluxHelper"

if (Test-Path $backendSrc) {
    Write-Host ""
    Write-Host "Copying Python backend..." -ForegroundColor Cyan
    if (Test-Path $backendDst) {
        Remove-Item -Path $backendDst -Recurse -Force
    }
    Copy-Item -Path $backendSrc -Destination $backendDst -Recurse -Force
}

$sourceBinDir = Join-Path $PSScriptRoot "src\bin"
if (Test-Path $sourceBinDir) {
    Write-Host ""
    Write-Host "Removing src/bin folder..." -ForegroundColor Cyan
    Remove-Item -Path $sourceBinDir -Recurse -Force
}

Write-Host ""
Write-Host "Build complete." -ForegroundColor Green
Write-Host "Output folder: $OutputDir" -ForegroundColor Green
Write-Host ""
Write-Host "Run FluxTranslator.exe from the output folder to start the app."