# Builds the Snipper installer end-to-end:
#   1. self-contained win-x64 publish  ->  ..\publish
#   2. Inno Setup compile of Snipper.iss  ->  ..\Snipper-<ver>-setup.exe
#
# Run from a normal PowerShell on Windows (needs .NET SDK 8 + Inno Setup 6):
#   powershell -ExecutionPolicy Bypass -File packaging\build-installer.ps1

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host "==> Publishing self-contained win-x64 build..." -ForegroundColor Cyan
    # Clean prior output so removed files don't linger in the installer.
    if (Test-Path "publish") { Remove-Item -Recurse -Force "publish" }
    dotnet publish Snipper.csproj `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=false `
        -o publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    # Sanity check: ffmpeg must have come along.
    if (-not (Test-Path "publish\ffmpeg\ffmpeg.exe")) {
        Write-Warning "publish\ffmpeg\ffmpeg.exe is missing - the app will rely on a PATH ffmpeg. Make sure ffmpeg\ffmpeg.exe exists in the repo."
    }

    Write-Host "==> Compiling installer with Inno Setup..." -ForegroundColor Cyan
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        $iscc = "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    }
    if (-not (Test-Path $iscc)) {
        throw "ISCC.exe (Inno Setup 6) not found. Install it from https://jrsoftware.org/isdl.php"
    }
    & $iscc "packaging\Snipper.iss"
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

    $setup = Get-ChildItem -Path . -Filter "Snipper-*-setup.exe" | Select-Object -First 1
    Write-Host "`n==> Done. Installer: $($setup.FullName)" -ForegroundColor Green
}
finally {
    Pop-Location
}
