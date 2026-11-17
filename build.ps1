# Build the app with the .NET Framework compiler that ships with Windows.
# Usage:  .\build.ps1   (optional: -Out <path>)

param([string]$Out = "MsiKeyboardLight.exe")

$ErrorActionPreference = 'Stop'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw "csc.exe not found - .NET Framework 4.x is required."
}

$src = Join-Path $PSScriptRoot 'src\MsiKeyboardLight.cs'
if (-not (Test-Path $src)) { throw "Source not found: $src" }

& $csc /nologo /optimize+ /target:winexe /out:$Out `
    /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $src

if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }

Write-Host ("Built: " + (Resolve-Path $Out))
