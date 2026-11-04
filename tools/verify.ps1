#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Archive, [string]$ExpectedVersion = '')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'This smoke test must run on Windows (PowerShell 7). A macOS run is not Windows verification.' }
$Root = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'package-common.ps1')
$PropsVersion = Get-PackageVersion $Root
if ($ExpectedVersion -and $ExpectedVersion -cne $PropsVersion) { throw 'ExpectedVersion must match Directory.Build.props.' }
$ExpectedVersion = $PropsVersion
$Archive = (Resolve-Path $Archive).Path
$Destination = Join-Path $Root ('artifacts/verify path Юникод ' + [guid]::NewGuid().ToString('N'))
New-Item $Destination -ItemType Directory | Out-Null
Test-PortableArchive $Archive $ExpectedVersion
# Validate the declared checksum before extracting or executing any member.
$ChecksumPath = Join-Path (Split-Path $Archive -Parent) 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $ChecksumPath)) { throw 'Missing adjacent SHA256SUMS.' }
$Leaf = Split-Path $Archive -Leaf
$Lines = @(Get-Content -LiteralPath $ChecksumPath | Where-Object { $_ -cmatch ('^[0-9a-f]{64}  ' + [regex]::Escape($Leaf) + '$') })
if ($Lines.Count -ne 1 -or $Lines[0].Substring(0, 64) -cne (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Archive checksum mismatch or ambiguous checksum entry.' }
[System.IO.Compression.ZipFile]::ExtractToDirectory($Archive, $Destination)
$AppDir = Join-Path $Destination "PoE-Flip-Overlay-$ExpectedVersion"
$Executable = Join-Path $AppDir 'FlipOverlay.App.exe'
if (-not (Test-Path (Join-Path $AppDir 'portable.flag'))) { throw 'Missing explicit portable.flag.' }
if (Test-Path (Join-Path $AppDir 'user-data')) { throw 'Archive unexpectedly includes user data.' }
function Run-App([string]$Argument, [string]$Label) {
    $Stdout = Join-Path $Destination "$Label.stdout.txt"
    $Stderr = Join-Path $Destination "$Label.stderr.txt"
    $Process = Start-Process -FilePath $Executable -ArgumentList $Argument -WorkingDirectory $AppDir -PassThru -RedirectStandardOutput $Stdout -RedirectStandardError $Stderr
    if (-not $Process.WaitForExit(60000)) { $Process.Kill(); throw "$Label timed out." }
    $Process.WaitForExit()
    if ($Process.ExitCode -ne 0) { throw "$Label failed ($($Process.ExitCode)). See $Stderr" }
    return (Get-Content $Stdout -Raw).Trim()
}
$ActualVersion = Run-App '--version' 'version'
if ($ActualVersion -ne $ExpectedVersion) { throw "Version mismatch: $ActualVersion != $ExpectedVersion" }
$Selftest = Run-App '--selftest' 'selftest'
$Report = [ordered]@{
    os = [System.Environment]::OSVersion.VersionString
    version = $ActualVersion
    archive = $Archive
    sha256 = (Get-FileHash $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
    unicodeSpacePath = $Destination
    offlineSelftest = $Selftest
    result = 'PASS: archive structure, version and offline selftest ONLY'
    notVerified = @('Installer clean-VM install/uninstall/upgrade', 'Window interaction on PoE1', 'GPU and frame times', 'DPI/monitors', 'Read-only portable directory and second-instance UI', 'Absence of AppData writes during GUI usage')
}
$Report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Destination 'verification.json') -Encoding utf8
$Report | ConvertTo-Json -Depth 4
