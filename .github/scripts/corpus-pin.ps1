#Requires -Version 7.0
# THE CORPUS PIN — Phase 394.
#
#     pwsh ./.github/scripts/corpus-pin.ps1                          # read ./copies.json's `corpus` record
#     pwsh ./.github/scripts/corpus-pin.ps1 -CopiesJson <path>       # read another copies.json
#
# Every workflow that checks out the wire-format conformance corpus runs this first and passes what it
# prints as the checkout's `repository:` and `ref:`. Until Phase 394 those checkouts named no `ref:`, so
# they took whatever the corpus's default branch held at that moment, and a corpus push could red this
# repository's `main` with no commit here. The pin is the `corpus` record of `copies.json`: the
# repository, the commit, the day of the last bump and why it moved. docs/conformance-corpus.md,
# "Bumping the pin", is the procedure for moving it.
#
# The record is held to the same four rules the suite applies (`SiblingCorpus.readPin`), and the suite
# runs this script against both a valid and a malformed record to keep the two readers one reader
# (`SiblingCorpusTests`, "the workflow pin step and the suite read copies.json alike"):
#   repository  exactly `fuaran-ui/fuaran-ui-specification`;
#   sha         a full commit SHA, 40 lowercase hex — never a branch, never an abbreviation;
#   date        yyyy-MM-dd;
#   reason      non-blank.
#
# Output: `repository=<owner/name>` and `sha=<40 hex>`, one per line, to stdout and — under GitHub
# Actions — to $GITHUB_OUTPUT, so a step with `id: corpus-pin` exposes
# `steps.corpus-pin.outputs.repository` and `steps.corpus-pin.outputs.sha`.
# Exit 0: the pin is well-formed. Exit 1: it is absent or malformed; every defect is named, and nothing
# is written to $GITHUB_OUTPUT, so the checkout that would have used it never runs.
[CmdletBinding()]
param(
    [string] $CopiesJson = (Join-Path (Get-Location) 'copies.json')
)

$ErrorActionPreference = 'Stop'

$expectedRepository = 'fuaran-ui/fuaran-ui-specification'

function Fail([string[]] $Defects) {
    foreach ($d in $Defects) { Write-Host "corpus pin: $d" }
    Write-Host "corpus pin: REFUSED — $CopiesJson must carry a well-formed ``corpus`` record (docs/conformance-corpus.md, `"Bumping the pin`")."
    exit 1
}

if (-not (Test-Path -LiteralPath $CopiesJson -PathType Leaf)) {
    Fail @("no copies.json at '$CopiesJson'")
}

try {
    $doc = Get-Content -LiteralPath $CopiesJson -Raw | ConvertFrom-Json -AsHashtable
}
catch {
    Fail @("copies.json is not JSON ($($_.Exception.Message))")
}

if ($doc -isnot [System.Collections.IDictionary]) {
    Fail @('copies.json is not a JSON object')
}

if (-not $doc.ContainsKey('corpus')) {
    Fail @('copies.json declares no `corpus` record — the corpus pin (repository, sha, date, reason) every corpus checkout reads')
}

$pin = $doc['corpus']
if ($pin -isnot [System.Collections.IDictionary]) {
    Fail @('copies.json''s `corpus` member is not an object')
}

$defects = @()

function Read-Member([string] $Name, [scriptblock] $Valid, [string] $Rule) {
    $value = $pin[$Name]
    if ($value -isnot [string]) {
        $script:defects += "``corpus.$Name`` is missing or not a string ($Rule)"
        return $null
    }
    if (-not (& $Valid $value)) {
        $script:defects += "``corpus.$Name`` is '$value', which is not $Rule"
        return $null
    }
    return $value
}

$repository = Read-Member 'repository' { param($v) $v -ceq $expectedRepository } "'$expectedRepository'"
$sha = Read-Member 'sha' { param($v) $v -cmatch '^[0-9a-f]{40}$' } 'a full commit SHA, 40 lowercase hex'
$null = Read-Member 'date' {
    param($v)
    $parsed = [datetime]::MinValue
    [datetime]::TryParseExact($v, 'yyyy-MM-dd', [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None, [ref] $parsed)
} 'a yyyy-MM-dd date'
$null = Read-Member 'reason' { param($v) -not [string]::IsNullOrWhiteSpace($v) } 'a non-blank account of the last bump'

if ($defects.Count -gt 0) {
    Fail $defects
}

$lines = @("repository=$repository", "sha=$sha")
$lines | ForEach-Object { Write-Host $_ }

if ($env:GITHUB_OUTPUT) {
    $lines | Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8
}

exit 0
