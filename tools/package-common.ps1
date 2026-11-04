#requires -Version 7.0
Set-StrictMode -Version Latest

function Get-PackageVersion([string]$Root) {
    [xml]$Props = Get-Content (Join-Path $Root 'Directory.Build.props') -Raw
    $Version = [string]$Props.Project.PropertyGroup.Version
    if ($Version -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') { throw 'Invalid package version in Directory.Build.props.' }
    return $Version
}

function Assert-PackagePath([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or $Name.Contains('\') -or $Name.Contains(':') -or $Name.StartsWith('/')) { throw "Unsafe archive member: $Name" }
    foreach ($Part in $Name.Split('/')) {
        if ($Part -in @('', '.', '..') -or $Part -match '[\x00-\x1f<>"|?*]' -or $Part -match '[. ]$' -or
            $Part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' -or $Part -in @('user-data', '.git')) { throw "Unsafe archive member: $Name" }
    }
}

function New-PortableArchive([string]$Publish, [string]$Archive, [string]$Version) {
    if (Test-Path -LiteralPath $Archive) { throw "Archive already exists: $Archive" }
    $Files = @(Get-ChildItem -LiteralPath $Publish -Recurse -Force)
    $Names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($File in $Files) {
        if (($File.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Links are forbidden: $($File.FullName)" }
        $Relative = [System.IO.Path]::GetRelativePath($Publish, $File.FullName).Replace('\', '/')
        Assert-PackagePath $Relative
        if (-not $Names.Add($Relative)) { throw "Case-insensitive duplicate: $Relative" }
        if ($Relative.Split('/') -icontains 'portable.flag') { throw 'The publish payload must not contain portable.flag.' }
    }
    $Stream = [System.IO.File]::Open($Archive, [System.IO.FileMode]::CreateNew)
    $Zip = [System.IO.Compression.ZipArchive]::new($Stream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        # Stable name ordering and timestamps; never inherit filesystem mtime.
        [string[]]$Paths = @($Files | Where-Object { -not $_.PSIsContainer } | ForEach-Object { [System.IO.Path]::GetRelativePath($Publish, $_.FullName).Replace('\', '/') })
        [System.Array]::Sort($Paths, [System.StringComparer]::Ordinal)
        foreach ($Relative in $Paths) {
            $Entry = $Zip.CreateEntry("PoE-Flip-Overlay-$Version/$Relative", [System.IO.Compression.CompressionLevel]::Optimal)
            $Entry.LastWriteTime = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $InputStream = [System.IO.File]::OpenRead((Join-Path $Publish $Relative))
            $OutputStream = $Entry.Open()
            try { $InputStream.CopyTo($OutputStream) } finally { $InputStream.Dispose(); $OutputStream.Dispose() }
        }
        $Marker = $Zip.CreateEntry("PoE-Flip-Overlay-$Version/portable.flag")
        $Marker.LastWriteTime = [System.DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    } finally { $Zip.Dispose(); $Stream.Dispose() }
}

function Test-PortableArchive([string]$Archive, [string]$Version, [string]$Publish = '') {
    $Zip = [System.IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        $Top = "PoE-Flip-Overlay-$Version/"
        $Names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($Entry in $Zip.Entries) {
            Assert-PackagePath $Entry.FullName
            if (-not $Entry.FullName.StartsWith($Top, [System.StringComparison]::Ordinal)) { throw "Wrong archive root: $($Entry.FullName)" }
            if (-not $Names.Add($Entry.FullName)) { throw "Duplicate archive member: $($Entry.FullName)" }
            # Reject Unix symlinks and all other non-regular Unix file types.
            $UnixType = ($Entry.ExternalAttributes -shr 16) -band 0xF000
            if ($UnixType -notin @(0, 0x8000)) { throw "Non-regular archive member: $($Entry.FullName)" }
            if ($Entry.FullName -ceq ($Top + 'portable.flag')) {
                if ($Entry.Length -ne 0) { throw 'portable.flag must be empty.' }
                continue
            }
            if ($Publish) {
                $Path = Join-Path $Publish $Entry.FullName.Substring($Top.Length)
                if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Unexpected archive member: $($Entry.FullName)" }
                $Stream = $Entry.Open()
                try { $Hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($Stream)) } finally { $Stream.Dispose() }
                if ($Hash -ne (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash) { throw "Payload hash mismatch: $Path" }
            }
        }
        foreach ($Required in @('portable.flag', 'FlipOverlay.App.exe', 'build-info.json', 'README.md', 'THIRD-PARTY-NOTICES.md')) {
            if (-not $Names.Contains($Top + $Required)) { throw "Missing archive member: $Required" }
        }
        if ($Publish) {
            $ExpectedCount = @(Get-ChildItem -LiteralPath $Publish -File -Recurse -Force).Count + 1
            if ($Names.Count -ne $ExpectedCount) { throw 'Archive does not contain the exact publish payload plus portable.flag.' }
        }
    } finally { $Zip.Dispose() }
}
