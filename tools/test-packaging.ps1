#requires -Version 7.0
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'package-common.ps1')
$Root = Split-Path $PSScriptRoot -Parent
$Version = Get-PackageVersion $Root
$Temp = Join-Path ([System.IO.Path]::GetTempPath()) ('poe-packaging-fixtures-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Temp | Out-Null
$Passed = 0
function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    $Rejected = $false
    try { & $Action } catch { $Rejected = $true }
    if (-not $Rejected) { throw "Expected rejection: $Label" }
    $script:Passed++
}
try {
    foreach ($Name in @('../escape', '/root', 'C:/root', 'a\b', 'a/../b', 'a//b', 'a/./b', 'a/NUL.txt', 'a/name.', 'a/name ', 'a/file:stream', 'a/user-data/db', 'a/.git/config')) {
        Assert-Rejected { Assert-PackagePath $Name } $Name
    }
    Assert-PackagePath 'safe path/Юникод.dll'
    $Publish = Join-Path $Temp 'fixture payload'
    New-Item -ItemType Directory -Path $Publish | Out-Null
    # These are explicitly inert test fixture bytes, never runnable application binaries.
    foreach ($Name in @('FlipOverlay.App.exe', 'build-info.json', 'README.md', 'THIRD-PARTY-NOTICES.md', 'Юникод.dll')) {
        Set-Content -LiteralPath (Join-Path $Publish $Name) -Value 'INERT ARCHIVE TEST FIXTURE' -Encoding utf8
    }
    $Archive = Join-Path $Temp 'one.zip'
    $Again = Join-Path $Temp 'two.zip'
    New-PortableArchive $Publish $Archive $Version
    Test-PortableArchive $Archive $Version $Publish
    $Passed++
    New-PortableArchive $Publish $Again $Version
    if ((Get-FileHash $Archive).Hash -ne (Get-FileHash $Again).Hash) { throw 'Identical fixture inputs did not produce identical ZIP bytes.' }
    $Passed++
    Assert-Rejected { New-PortableArchive $Publish $Archive $Version } 'overwrite'
    $Bad = Join-Path $Temp 'bad.zip'
    foreach ($Name in @("PoE-Flip-Overlay-$Version/../escape", "PoE-Flip-Overlay-$Version/README.md", "PoE-Flip-Overlay-$Version/readme.MD", 'wrong-root/file')) {
        Copy-Item $Archive $Bad -Force
        $Zip = [System.IO.Compression.ZipFile]::Open($Bad, [System.IO.Compression.ZipArchiveMode]::Update)
        try { $Zip.CreateEntry($Name) | Out-Null } finally { $Zip.Dispose() }
        Assert-Rejected { Test-PortableArchive $Bad $Version } "malicious archive $Name"
    }
    Copy-Item $Archive $Bad -Force
    $Zip = [System.IO.Compression.ZipFile]::Open($Bad, [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        $Entry = $Zip.CreateEntry("PoE-Flip-Overlay-$Version/link")
        $Entry.ExternalAttributes = (0xA000 -shl 16) # Unix symlink file type.
    } finally { $Zip.Dispose() }
    Assert-Rejected { Test-PortableArchive $Bad $Version } 'Unix symlink'
    Set-Content -LiteralPath (Join-Path $Publish 'README.md') 'changed fixture'
    Assert-Rejected { Test-PortableArchive $Archive $Version $Publish } 'tampered payload'
    New-Item -ItemType Directory -Path (Join-Path $Publish 'user-data') | Out-Null
    Assert-Rejected { New-PortableArchive $Publish (Join-Path $Temp 'forbidden.zip') $Version } 'user-data directory'
    Write-Output "PASS: $Passed packaging fixture checks (not application, installer, or Windows execution tests)."
} finally { Remove-Item -LiteralPath $Temp -Recurse -Force }
