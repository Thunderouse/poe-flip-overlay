[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
# Production source only: never scan this deny-list, documentation, fixtures or generated obj files.
$Forbidden = '\b(ReadProcessMemory|WriteProcessMemory|OpenProcess|SendInput|mouse_event|keybd_event|SetWindowsHookEx\w*|NtReadVirtualMemory|VirtualAllocEx|CreateRemoteThread|HwndSource|WebView2)\b'
$Violations = @()
foreach ($File in Get-ChildItem (Join-Path $Root 'src') -Filter '*.cs' -File -Recurse) {
    if ($File.FullName -match '[/\\](bin|obj)[/\\]') { continue }
    $MatchesFound = Select-String -Path $File.FullName -Pattern $Forbidden
    foreach ($Match in $MatchesFound) { $Violations += "$($File.FullName):$($Match.LineNumber): $($Match.Line.Trim())" }
}
if ($Violations.Count -gt 0) { $Violations | Write-Output; throw 'Forbidden production API reference found.' }
Write-Output 'PASS: deny-list production source scan. This is not a ban-safety guarantee or a substitute for review.'
