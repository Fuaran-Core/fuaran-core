#Requires -Version 7.0
# build-kit-asset.ps1 — the proof kit's release asset, built reproducibly (Phase 536).
#
# The kit reaches an adopter as ONE archive, `proofs-kit-<version>.tar`, pinned by sha256 in the
# adopter's `proofs/proofs-kit.json`. This script is the only thing that builds it, and three callers
# share it: the reference repository's suite (the `Proofs.Kit` family builds it and holds the sha to
# `copies.json`), the publish workflow (which refuses to upload an asset whose sha is not the
# committed one), and an adopter's offline path (build from a checkout of the reference repository,
# then hand the result to the adopter's `check.ps1 -KitAsset`, which verifies the sha all the same).
#
# REPRODUCIBLE BY CONSTRUCTION. The archive's bytes are a function of the declared files' bytes and
# nothing else: the file list is `asset.json`'s (never a directory walk, so no file system's order
# leaks in), sorted ordinally by archive path; every text file is LF (a CRLF checkout builds the same
# bytes as an LF one); the format is uncompressed ustar (no compressor whose output could move with a
# runtime version); every entry's time is the Unix epoch, its mode 0644, its owner and group 0 with no
# names. The version names the FILE and is not inside it, so a cut that moves no kit file leaves the
# sha where it was.
#
# Exit codes: 0 built (and, under -Check, matching); 1 the built sha is not the committed one
# (-Check); 2 a declaration this script cannot build from (a missing file, a malformed manifest).
[CmdletBinding()]
param(
    # Where the archive is written. Defaults to <proofs>/obj/kit-asset (gitignored).
    [string] $OutDir,
    # The version in the file name. Defaults to Directory.Build.props's <Version> at the repository root.
    [string] $Version,
    # Compare the built sha256 with `copies.json`'s `proofsKit.sha256`; exit 1, naming both, if they differ.
    [switch] $Check,
    # Rewrite `copies.json`'s `proofsKit.sha256` to the built one. The commit that moves a kit file moves it.
    [switch] $Update,
    # The proofs directory the manifest's paths are relative to. Defaults to this script's parent's parent.
    [string] $ProofsDir
)

$ErrorActionPreference = 'Stop'

function Stop-Build([string] $message, [int] $code) {
    Write-Host "==== kit asset: $message" -ForegroundColor Red
    exit $code
}

if (-not $ProofsDir) { $ProofsDir = Split-Path $PSScriptRoot -Parent }
$ProofsDir = [System.IO.Path]::GetFullPath($ProofsDir)
$repoRoot = Split-Path $ProofsDir -Parent
$manifestPath = Join-Path $ProofsDir 'kit/asset.json'
$copiesPath = Join-Path $repoRoot 'copies.json'

if (-not $Version) {
    $props = Join-Path $repoRoot 'Directory.Build.props'
    if (-not (Test-Path $props)) { Stop-Build "no -Version and no Directory.Build.props at $repoRoot to read one from" 2 }
    $node = Select-Xml -Path $props -XPath '//PropertyGroup/Version' | Select-Object -First 1
    if ($null -eq $node) { Stop-Build "$props declares no <Version>" 2 }
    $Version = $node.Node.InnerText.Trim()
}
if (-not $OutDir) { $OutDir = Join-Path $ProofsDir 'obj/kit-asset' }
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

$manifest = try { Get-Content $manifestPath -Raw | ConvertFrom-Json } catch { Stop-Build "the manifest $manifestPath is not JSON: $($_.Exception.Message)" 2 }
if ($manifest.kind -ne 'proofsKitAsset' -or $null -eq $manifest.files) { Stop-Build "$manifestPath is not a proofsKitAsset declaration (kind, files)" 2 }

$entries = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($file in @($manifest.files)) {
    if (-not $file.path -or -not $file.as) { Stop-Build "a manifest entry names no path or no archive path: $($file | ConvertTo-Json -Compress)" 2 }
    if ($file.as -match '\\|^/|\.\.') { Stop-Build "archive path '$($file.as)' is not a relative forward-slash path" 2 }
    if (-not $seen.Add($file.as)) { Stop-Build "archive path '$($file.as)' is declared twice" 2 }
    $source = Join-Path $ProofsDir $file.path
    if (-not (Test-Path $source -PathType Leaf)) { Stop-Build "declared file '$($file.path)' does not exist under $ProofsDir" 2 }
    $text = [System.IO.File]::ReadAllText($source, [System.Text.UTF8Encoding]::new($false, $true))
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($text.Replace("`r`n", "`n"))
    $entries.Add([pscustomobject]@{ As = [string]$file.as; Bytes = $bytes })
}
# Ordinal, never Sort-Object: that is culture-aware, and the order is part of the bytes.
$ordinal = $entries
$ordinal.Sort([System.Comparison[object]] { param($a, $b) [string]::CompareOrdinal($a.As, $b.As) })

New-Item -ItemType Directory -Force $OutDir | Out-Null
$asset = Join-Path $OutDir "proofs-kit-$Version.tar"
$stream = [System.IO.File]::Create($asset)
try {
    $writer = [System.Formats.Tar.TarWriter]::new($stream, [System.Formats.Tar.TarEntryFormat]::Ustar, $true)
    try {
        $mode = [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor
            [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::OtherRead
        foreach ($e in $ordinal) {
            $entry = [System.Formats.Tar.UstarTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, $e.As)
            $entry.ModificationTime = [System.DateTimeOffset]::FromUnixTimeSeconds(0)
            $entry.Mode = $mode
            $entry.Uid = 0
            $entry.Gid = 0
            $entry.UserName = ''
            $entry.GroupName = ''
            $entry.DataStream = [System.IO.MemoryStream]::new($e.Bytes)
            $writer.WriteEntry($entry)
        }
    }
    finally { $writer.Dispose() }
}
finally { $stream.Dispose() }

$sha = (Get-FileHash $asset -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "==== kit asset: $asset ($($ordinal.Count) files)"
Write-Host "==== kit asset: sha256 $sha"

if ($Check -or $Update) {
    if (-not (Test-Path $copiesPath)) { Stop-Build "no copies.json at $repoRoot to hold the committed sha256" 2 }
    $copiesText = [System.IO.File]::ReadAllText($copiesPath)
    $committed = ($copiesText | ConvertFrom-Json).proofsKit.sha256
    if ($Update) {
        $pattern = '("proofsKit"\s*:\s*\{[^{}]*?"sha256"\s*:\s*")[0-9a-f]*(")'
        if ($copiesText -notmatch $pattern) { Stop-Build "copies.json carries no proofsKit.sha256 to rewrite" 2 }
        $rewritten = [regex]::Replace($copiesText, $pattern, "`${1}$sha`${2}")
        [System.IO.File]::WriteAllText($copiesPath, $rewritten, [System.Text.UTF8Encoding]::new($false))
        Write-Host "==== kit asset: copies.json proofsKit.sha256 $committed -> $sha — commit it with the kit change"
    }
    elseif ($committed -ne $sha) {
        Stop-Build ("the kit built to sha256 $sha, but copies.json commits $committed. A kit file moved without the committed sha " +
            "moving with it: run ``pwsh proofs/kit/build-kit-asset.ps1 -Update`` and commit copies.json in the same change.") 1
    }
    else { Write-Host '==== kit asset: matches copies.json proofsKit.sha256' -ForegroundColor Green }
}
exit 0
