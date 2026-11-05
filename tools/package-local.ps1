#requires -Version 7.0
[CmdletBinding()]
param([string]$Iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'package-common.ps1')
$root = Split-Path $PSScriptRoot -Parent
$version = Get-PackageVersion $root
$output = Join-Path $root 'artifacts'
$publish = Join-Path $output "publish-$version-win-x64"
$zip = Join-Path $output "PoE-Flip-Overlay-$version-portable.zip"
$setup = Join-Path $output "PoE-Flip-Overlay-$version-setup.exe"
foreach ($path in @($zip,$setup,(Join-Path $output 'SHA256SUMS'))) {
    if (Test-Path -LiteralPath $path) { throw "Output exists: $path" }
}
New-PortableArchive $publish $zip $version
Test-PortableArchive $zip $version $publish
& $Iscc "/DAppVersion=$version" "/DNumericVersion=$($version.Split('-')[0]).0" "/DPublishDir=$publish" "/DOutputDir=$output" (Join-Path $PSScriptRoot 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC exit $LASTEXITCODE" }
Test-PortableArchive $zip $version $publish
@($zip,$setup) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_).Hash.ToLowerInvariant(),(Split-Path $_ -Leaf) } |
    Set-Content (Join-Path $output 'SHA256SUMS') -Encoding ascii
Write-Output 'Local packaging completed. Run the full build pipeline in an unrestricted Windows shell before release.'
