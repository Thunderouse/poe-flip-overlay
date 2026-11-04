#requires -Version 7.0
[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Iscc = '', [switch]$SkipInstaller)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'package-common.ps1')
$Root = Split-Path $PSScriptRoot -Parent
Push-Location $Root
try {
    function Invoke-Checked([string]$Executable, [string[]]$Arguments) {
        & $Executable @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Executable failed with exit code $LASTEXITCODE" }
    }
    $Version = Get-PackageVersion $Root
    $Sdk = (Get-Content (Join-Path $Root 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $ActualSdk = (& $Dotnet --version)
    if ($LASTEXITCODE -ne 0 -or $ActualSdk.Trim() -ne $Sdk) { throw "Expected SDK $Sdk; got $ActualSdk" }
    $Output = Join-Path $Root 'artifacts'
    $Publish = Join-Path $Output "publish-$Version-win-x64"
    $Setup = Join-Path $Output "PoE-Flip-Overlay-$Version-setup.exe"
    $ZipPath = Join-Path $Output "PoE-Flip-Overlay-$Version-portable.zip"
    foreach ($Path in @($Publish, $Setup, $ZipPath, (Join-Path $Output 'SHA256SUMS'))) {
        if (Test-Path -LiteralPath $Path) { throw "Output already exists; archive/remove it explicitly before rebuilding: $Path" }
    }
    if (-not $SkipInstaller) {
        if (-not $IsWindows) { throw 'Installer compilation requires Windows and Inno Setup. Use -SkipInstaller explicitly for partial cross-build validation.' }
        if (-not $Iscc) {
            $Command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
            if ($Command) { $Iscc = $Command.Source }
            elseif (${env:ProgramFiles(x86)}) { $Iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe' }
        }
        if (-not $Iscc -or -not (Test-Path -LiteralPath $Iscc -PathType Leaf)) { throw 'Inno Setup compiler missing. Supply -Iscc or use -SkipInstaller (partial output only).' }
    }
    # Every project must have an existing lock; locked mode must never bootstrap one.
    $Locks = @{}
    foreach ($Project in Get-ChildItem src, tests, tools -Filter '*.csproj' -Recurse) {
        $Lock = Join-Path $Project.DirectoryName 'packages.lock.json'
        if (-not (Test-Path -LiteralPath $Lock)) { throw "Missing dependency lock: $Lock" }
        $Locks[$Lock] = (Get-FileHash -LiteralPath $Lock -Algorithm SHA256).Hash
    }
    function Assert-LocksUnchanged {
        foreach ($Lock in $Locks.Keys) {
            if ((Get-FileHash -LiteralPath $Lock -Algorithm SHA256).Hash -ne $Locks[$Lock]) { throw "Dependency lock changed: $Lock" }
        }
    }
    & (Join-Path $PSScriptRoot 'check-safety.ps1')
    & (Join-Path $PSScriptRoot 'test-packaging.ps1')
    Invoke-Checked $Dotnet @('restore', 'FlipOverlay.slnx', '--locked-mode')
    Assert-LocksUnchanged
    Invoke-Checked $Dotnet @('build', 'FlipOverlay.slnx', '-c', 'Release', '--no-restore')
    Invoke-Checked $Dotnet @('test', 'FlipOverlay.slnx', '-c', 'Release', '--no-build', '--no-restore')
    Invoke-Checked $Dotnet @('run', '--project', 'src/FlipOverlay.App', '-c', 'Release', '--no-build', '--no-restore', '--', '--selftest')
    Invoke-Checked $Dotnet @('format', 'FlipOverlay.slnx', '--verify-no-changes', '--no-restore')
    # Publish never performs an implicit restore with a different dependency graph.
    Invoke-Checked $Dotnet @('restore', 'src/FlipOverlay.App/FlipOverlay.App.csproj', '--locked-mode', '-p:RuntimeIdentifier=win-x64', '-p:SelfContained=true')
    Assert-LocksUnchanged
    Invoke-Checked $Dotnet @('publish', 'src/FlipOverlay.App/FlipOverlay.App.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore', '-o', $Publish, '-p:ContinuousIntegrationBuild=true', '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:PublishAot=false', '-p:PublishReadyToRun=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:CopyOutputSymbolsToPublishDirectory=false')
    # Re-prove ordinary locked restore after the RID-specific operation.
    Invoke-Checked $Dotnet @('restore', 'FlipOverlay.slnx', '--locked-mode')
    Assert-LocksUnchanged
    foreach ($Required in @('FlipOverlay.App.exe', 'FlipOverlay.App.dll', 'coreclr.dll', 'hostfxr.dll', 'System.Private.CoreLib.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Publish $Required) -PathType Leaf)) { throw "Incomplete self-contained payload: $Required" }
    }
    $Commit = (& git rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) { $Commit = 'uncommitted' }
    $Dirty = @(& git status --porcelain).Count -gt 0
    [ordered]@{ version = $Version; commit = $Commit; dirty = $Dirty; sdk = $Sdk; channel = 'local-validation'; liveScanner = $false } |
        ConvertTo-Json | Set-Content (Join-Path $Publish 'build-info.json') -Encoding utf8
    Copy-Item (Join-Path $Root 'docs/USER-GUIDE.md') (Join-Path $Publish 'README.md')
    Copy-Item (Join-Path $Root 'docs/THIRD-PARTY-NOTICES.md') $Publish
    $BundledDocs = Join-Path $Publish 'docs'
    New-Item -ItemType Directory -Path $BundledDocs | Out-Null
    foreach ($Name in @('STATUS.md', 'USER-RECIPES.md', 'PERFORMANCE.md', 'capability-matrix.md')) {
        Copy-Item (Join-Path (Join-Path $Root 'docs') $Name) $BundledDocs
    }
    # ZIP read-back compares every file hash with this exact installer input directory.
    New-PortableArchive $Publish $ZipPath $Version
    Test-PortableArchive $ZipPath $Version $Publish
    $Artifacts = [System.Collections.Generic.List[string]]::new()
    $Artifacts.Add($ZipPath)
    if (-not $SkipInstaller) {
        $NumericVersion = $Version.Split('-')[0] + '.0'
        Invoke-Checked $Iscc @("/DAppVersion=$Version", "/DNumericVersion=$NumericVersion", "/DPublishDir=$Publish", "/DOutputDir=$Output", (Join-Path $PSScriptRoot 'installer.iss'))
        if (-not (Test-Path -LiteralPath $Setup -PathType Leaf) -or (Get-Item -LiteralPath $Setup).Length -eq 0) { throw 'Setup compiler did not produce a nonempty installer.' }
        Test-PortableArchive $ZipPath $Version $Publish
        $Artifacts.Add($Setup)
    }
    $Hashes = foreach ($File in $Artifacts) { '{0}  {1}' -f (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $File -Leaf) }
    $Hashes | Set-Content (Join-Path $Output 'SHA256SUMS') -Encoding ascii
    $Artifacts | ForEach-Object { Get-Item -LiteralPath $_ | Select-Object Name, Length }
    if ($SkipInstaller) { Write-Warning 'PARTIAL BUILD: installer explicitly skipped; not a completed release.' }
    Write-Host 'Local artifacts only. Nothing uploaded or released. Run tools/verify.ps1 on Windows.'
} finally { Pop-Location }
