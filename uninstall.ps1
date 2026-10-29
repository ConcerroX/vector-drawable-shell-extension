# Uninstall script for the VectorDrawable Shell Extension (per-user / HKCU).
# Removes the registry entries created by install.ps1.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File uninstall.ps1

$ErrorActionPreference = "Stop"

$root = "HKEY_CURRENT_USER\Software\Classes"

# Thumbnail provider
reg.exe delete "$root\CLSID\{2D063192-2936-47B0-9029-EEC429B56CAD}" /f 2>$null | Out-Null
# Preview handler
reg.exe delete "$root\CLSID\{7DE97D1F-66CA-415B-9C8C-4D4D57504687}" /f 2>$null | Out-Null
# PreviewHandlers list entry
reg.exe delete "HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers" /v "{7DE97D1F-66CA-415B-9C8C-4D4D57504687}" /f 2>$null | Out-Null
# .xml shellex entries
reg.exe delete "$root\.xml\shellex\{e357fccd-a995-4576-b01f-234630154e96}" /f 2>$null | Out-Null
reg.exe delete "$root\.xml\shellex\{8895b1c6-b41f-4c1c-a562-0d564250836f}" /f 2>$null | Out-Null

Write-Host "Registry entries removed."

Write-Host "Restarting Explorer..."
& taskkill /f /im explorer.exe 2>$null | Out-Null
Start-Sleep -Seconds 1
& explorer.exe

Write-Host "Done. VectorDrawable Shell Extension uninstalled."
