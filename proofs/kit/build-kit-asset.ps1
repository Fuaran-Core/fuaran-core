#Requires -Version 7.0
# build-kit-asset.ps1 — a proofs release asset, built reproducibly (Phase 536; any producer since Phase 606).
#
# The kit reaches an adopter as ONE archive, `proofs-kit-<version>.tar`, pinned by sha256 in the
# adopter's `proofs/proofs-kit.json`. This script is the only thing that builds it, and three callers
# share it: the reference repository's suite (the `Proofs.Kit` family builds it and holds the sha to
# `copies.json`), the publish workflow (which refuses to upload an asset whose sha is not the
# committed one), and an adopter's offline path (build from a checkout of the reference repository,
# then hand the result to the adopter's `check.ps1 -KitAsset`, which verifies the sha all the same).
#
# A SECOND PRODUCER (Phase 606). Any repository whose models another repository's proof leg re-checks
# ships them the same way, with this same script, from its own declaration: `-Manifest` names the
# declaration (kind `proofsAsset`, which must carry its own `assetPrefix`, so the archive is
# `<assetPrefix>-<version>.tar`), and `-ShaFile` / `-ShaPath` name where its committed sha lives. With
# none of the three the script builds the kit exactly as before: `proofs/kit/asset.json`, kind
# `proofsKitAsset`, `proofs-kit-<version>.tar`, `copies.json` → `proofsKit.sha256`. A producer that
# does not adopt the kit obtains this script by pinning the kit asset, which carries it
# (`kit/build-kit-asset.ps1`; DECISIONS.md D245).
#
# REPRODUCIBLE BY CONSTRUCTION. The archive's bytes are a function of the declared files' bytes and
# nothing else: the file list is the manifest's (never a directory walk, so no file system's order
# leaks in), sorted ordinally by archive path; every text file is LF (a CRLF checkout builds the same
# bytes as an LF one); the format is uncompressed ustar (no compressor whose output could move with a
# runtime version); every entry's time is the Unix epoch, its mode 0644, its owner and group 0 with no
# names. The version names the FILE and is not inside it, so a cut that moves no declared file leaves
# the sha where it was.
#
# Exit codes: 0 built (and, under -Check, matching); 1 the built sha is not the committed one
# (-Check); 2 a declaration this script cannot build from (a missing file, a malformed manifest, a sha
# location that does not exist or cannot be rewritten in place).
[CmdletBinding()]
param(
    # Where the archive is written. Defaults to <proofs>/obj/kit-asset (gitignored).
    [string] $OutDir,
    # The version in the file name. Defaults to Directory.Build.props's <Version> at the repository root.
    [string] $Version,
    # Compare the built sha256 with the committed one (-ShaFile / -ShaPath); exit 1, naming both, if they differ.
    [switch] $Check,
    # Rewrite the committed sha256 (-ShaFile / -ShaPath) to the built one. The commit that moves a declared file moves it.
    [switch] $Update,
    # The directory the manifest's `path`s are relative to; its parent is the repository root. Defaults to
    # the manifest's own directory when -Manifest is given, and to this script's parent's parent otherwise.
    [string] $ProofsDir,
    # The declaration to build from. Defaults to <proofs>/kit/asset.json, the kit's own.
    [string] $Manifest,
    # The JSON file that holds the committed sha256. Defaults to copies.json at the repository root.
    [string] $ShaFile,
    # Where in -ShaFile the sha256 is, as dot-separated property names. Defaults to proofsKit.sha256.
    [string] $ShaPath = 'proofsKit.sha256'
)

$ErrorActionPreference = 'Stop'

function Stop-Build([string] $message, [int] $code) {
    Write-Host "==== proofs asset: $message" -ForegroundColor Red
    exit $code
}

# Relative paths resolve against the session's location, never the process's working directory
# (which is what [IO.Path]::GetFullPath alone would read when this script is dot-sourced or `&`-called).
function Resolve-Full([string] $path) {
    [System.IO.Path]::GetFullPath($path, (Get-Location -PSProvider FileSystem).ProviderPath)
}

if ($Manifest) { $Manifest = Resolve-Full $Manifest }
if (-not $ProofsDir) { $ProofsDir = if ($Manifest) { Split-Path $Manifest -Parent } else { Split-Path $PSScriptRoot -Parent } }
$ProofsDir = Resolve-Full $ProofsDir
$repoRoot = Split-Path $ProofsDir -Parent
if (-not $Manifest) { $Manifest = Join-Path $ProofsDir 'kit/asset.json' }
$ShaFile = if ($ShaFile) { Resolve-Full $ShaFile } else { Join-Path $repoRoot 'copies.json' }
$shaKeys = @($ShaPath -split '\.')
if ($shaKeys | Where-Object { -not $_ }) { Stop-Build "-ShaPath '$ShaPath' is not a dot-separated list of property names" 2 }

if (-not $Version) {
    $props = Join-Path $repoRoot 'Directory.Build.props'
    if (-not (Test-Path $props)) { Stop-Build "no -Version and no Directory.Build.props at $repoRoot to read one from" 2 }
    $node = Select-Xml -Path $props -XPath '//PropertyGroup/Version' | Select-Object -First 1
    if ($null -eq $node) { Stop-Build "$props declares no <Version>" 2 }
    $Version = $node.Node.InnerText.Trim()
}
if (-not $OutDir) { $OutDir = Join-Path $ProofsDir 'obj/kit-asset' }
$OutDir = Resolve-Full $OutDir

if (-not (Test-Path $Manifest -PathType Leaf)) { Stop-Build "no manifest at $Manifest" 2 }
$declaration = try { Get-Content $Manifest -Raw | ConvertFrom-Json } catch { Stop-Build "the manifest $Manifest is not JSON: $($_.Exception.Message)" 2 }
if ($null -eq $declaration.files) { Stop-Build "$Manifest declares no files" 2 }
# The kind says whose asset it is, so neither producer's declaration has to borrow the other's: the
# kit's is `proofsKitAsset` and builds `proofs-kit`; any other producer's is `proofsAsset` and names
# its own prefix, which may not be the kit's.
$prefix = switch ($declaration.kind) {
    'proofsKitAsset' {
        if ($declaration.assetPrefix -and $declaration.assetPrefix -ne 'proofs-kit') { Stop-Build "$Manifest is a proofsKitAsset declaration but names assetPrefix '$($declaration.assetPrefix)'; the kit's asset is proofs-kit" 2 }
        'proofs-kit'
    }
    'proofsAsset' {
        if (-not $declaration.assetPrefix) { Stop-Build "$Manifest is a proofsAsset declaration with no assetPrefix: a second producer names its own archive" 2 }
        if ($declaration.assetPrefix -eq 'proofs-kit') { Stop-Build "$Manifest is a proofsAsset declaration naming assetPrefix 'proofs-kit', which is the kit's archive" 2 }
        [string]$declaration.assetPrefix
    }
    default { Stop-Build "$Manifest is neither a proofsKitAsset nor a proofsAsset declaration (kind '$($declaration.kind)')" 2 }
}
if ($prefix -cnotmatch '^[a-z0-9][a-z0-9.-]*$') { Stop-Build "assetPrefix '$prefix' is not a lower-case file-name stem ([a-z0-9][a-z0-9.-]*)" 2 }

$entries = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($file in @($declaration.files)) {
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
$asset = Join-Path $OutDir "$prefix-$Version.tar"
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
Write-Host "==== proofs asset: $asset ($($ordinal.Count) files)"
Write-Host "==== proofs asset: sha256 $sha"

# The value at -ShaPath in the parsed file, or $null when any step of the path is absent.
function Read-ShaAt([string] $json) {
    $node = $json | ConvertFrom-Json
    foreach ($key in $shaKeys) {
        if ($null -eq $node -or $node -isnot [System.Management.Automation.PSCustomObject] -or
            $node.PSObject.Properties.Name -cnotcontains $key) { return $null }
        $node = $node.$key
    }
    $node
}

if ($Check -or $Update) {
    $where = "$(Split-Path $ShaFile -Leaf) $ShaPath"
    if (-not (Test-Path $ShaFile -PathType Leaf)) { Stop-Build "no $ShaFile to hold the committed sha256" 2 }
    $shaText = [System.IO.File]::ReadAllText($ShaFile)
    $committed = try { Read-ShaAt $shaText } catch { Stop-Build "$ShaFile is not JSON: $($_.Exception.Message)" 2 }
    if ($committed -isnot [string]) { Stop-Build "$ShaFile carries no string at $ShaPath to hold the sha256" 2 }
    if ($Update) {
        # Rewritten in place, so the file's layout and every other byte survive: the value's key,
        # inside its parent's object when the path has one. One match or none is rewritten; anything
        # else is refused rather than guessed at, and the rewrite is read back through the path.
        $key = [regex]::Escape($shaKeys[-1])
        $pattern = if ($shaKeys.Count -gt 1) {
            '("' + [regex]::Escape($shaKeys[-2]) + '"\s*:\s*\{[^{}]*?"' + $key + '"\s*:\s*")[0-9a-f]*(")'
        }
        else { '("' + $key + '"\s*:\s*")[0-9a-f]*(")' }
        $found = [regex]::Matches($shaText, $pattern).Count
        if ($found -ne 1) { Stop-Build "$where is matched $found times by the in-place rewrite, not once; rewrite it by hand to $sha" 2 }
        $rewritten = [regex]::Replace($shaText, $pattern, "`${1}$sha`${2}")
        if ((Read-ShaAt $rewritten) -ne $sha) { Stop-Build "the in-place rewrite of $where did not land on that path; rewrite it by hand to $sha" 2 }
        [System.IO.File]::WriteAllText($ShaFile, $rewritten, [System.Text.UTF8Encoding]::new($false))
        Write-Host "==== proofs asset: $where $committed -> $sha — commit it with the change that moved the asset"
    }
    elseif ($committed -ne $sha) {
        Stop-Build ("$prefix built to sha256 $sha, but $where commits $committed. A declared file moved without the committed sha " +
            "moving with it: run this script again with -Update and commit $(Split-Path $ShaFile -Leaf) in the same change.") 1
    }
    else { Write-Host "==== proofs asset: matches $where" -ForegroundColor Green }
}
exit 0
