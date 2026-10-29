# Install script for the VectorDrawable Shell Extension (per-user / HKCU).
# Writes the COM registration directly via PowerShell registry APIs (avoids reg.exe
# encoding/path issues) and restarts Explorer.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File install.ps1

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$deployDir = Join-Path $scriptDir "deploy"

if (-not (Test-Path $deployDir)) {
    Write-Error "deploy/ directory not found: $deployDir"
    exit 1
}

$dllPath = (Join-Path $deployDir "VectorDrawableShellExt.dll")
if (-not (Test-Path $dllPath)) {
    Write-Error "VectorDrawableShellExt.dll not found in deploy/"
    exit 1
}

$dll = (Resolve-Path $dllPath).Path

$thumbClsid = "{2D063192-2936-47B0-9029-EEC429B56CAD}"
$previewClsid = "{7DE97D1F-66CA-415B-9C8C-4D4D57504687}"

function Set-DefaultValue([string]$Path, [string]$Value) {
    # Sets the "(default)" value of a registry key.
    New-Item -Path $Path -Force | Out-Null
    (Get-Item -Path $Path).OpenSubKey("", $true).SetValue("", $Value)
}

function Set-StringValue([string]$Path, [string]$Name, [string]$Value) {
    New-Item -Path $Path -Force | Out-Null
    New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
}

# ---- Thumbnail provider ----
$t = "HKCU:\Software\Classes\CLSID\$thumbClsid"
Set-DefaultValue $t "VectorDrawable Thumbnail Provider"
Set-DefaultValue "$t\InprocServer32" $dll
Set-StringValue "$t\InprocServer32" "ThreadingModel" "Apartment"

# ---- Preview handler ----
$p = "HKCU:\Software\Classes\CLSID\$previewClsid"
Set-DefaultValue $p "VectorDrawable Preview Handler"
Set-DefaultValue "$p\InprocServer32" $dll
Set-StringValue "$p\InprocServer32" "ThreadingModel" "Apartment"
Set-StringValue $p "AppID" "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}"
Set-StringValue $p "DisplayName" "VectorDrawable Preview Handler"

# ---- PreviewHandlers list ----
$ph = "HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers"
New-Item -Path $ph -Force | Out-Null
New-ItemProperty -Path $ph -Name $previewClsid -Value "VectorDrawable Preview Handler" -PropertyType String -Force | Out-Null

# ---- Associate with .xml ----
Set-DefaultValue "HKCU:\Software\Classes\.xml\shellex\{e357fccd-a995-4576-b01f-234630154e96}" $thumbClsid
Set-DefaultValue "HKCU:\Software\Classes\.xml\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}" $previewClsid

Write-Host "Registry registration complete. DLL = $dll"

# Restart Explorer to pick up the new handlers.
Write-Host "Restarting Explorer..."
& taskkill /f /im explorer.exe 2>$null | Out-Null
Start-Sleep -Seconds 1
& explorer.exe
Start-Sleep -Seconds 2

Write-Host "Done. VectorDrawable Shell Extension installed."
