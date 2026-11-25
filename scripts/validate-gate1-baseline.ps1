<#
.SYNOPSIS
    Validates RetailFlow Gate 1 Requirements & Business Baseline deliverables.
.DESCRIPTION
    Performs mechanical validation ONLY: completeness, uniqueness, cross-reference
    integrity, absence of generic placeholders, valid status combinations, and
    resolution of test/source file paths and C# test method names.
    NOTE: Mechanical validation does NOT prove semantic correctness or PO approval.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()
$checksPassed = 0

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if ($Condition) {
        $script:checksPassed++
    }
    else {
        $script:failures.Add($Message)
    }
}

function Split-Ids {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
    return @($Value -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' -and $_ -ne 'None' })
}

$frPath = Join-Path $RepoRoot "docs/09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv"
$brPath = Join-Path $RepoRoot "docs/01-Requirements/Business-Rules-Traceability-Matrix.csv"
$nfrPath = Join-Path $RepoRoot "docs/01-Requirements/NFR-Register.csv"
$acPath = Join-Path $RepoRoot "docs/01-Requirements/Acceptance-Criteria.csv"
$ucPath = Join-Path $RepoRoot "docs/01-Requirements/Use-Case-Specifications.md"
$acRecPath = Join-Path $RepoRoot "docs/01-Requirements/Acceptance-Criteria-Reconciliation.md"
$clOdPath = Join-Path $RepoRoot "docs/08-Engineering-Management/Change-Log-and-Open-Decisions.md"
$ctrlFiles = @(
    (Join-Path $RepoRoot "README.md"),
    (Join-Path $RepoRoot "docs/README.md"),
    (Join-Path $RepoRoot "docs/engineering/control/CURRENT-STATE.md"),
    (Join-Path $RepoRoot "docs/engineering/control/ACTIVE-WORK.md"),
    (Join-Path $RepoRoot "docs/engineering/control/AI-HANDOFF.md"),
    (Join-Path $RepoRoot "docs/engineering/control/WORK-LOG.md")
)

foreach ($p in (@($frPath, $brPath, $nfrPath, $acPath, $ucPath, $acRecPath, $clOdPath) + $ctrlFiles)) {
    Assert-Condition (Test-Path -LiteralPath $p) "Missing required artifact: $p"
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

$frRows = Import-Csv -LiteralPath $frPath
$brRows = Import-Csv -LiteralPath $brPath
$nfrRows = Import-Csv -LiteralPath $nfrPath
$acRows = Import-Csv -LiteralPath $acPath
$ucContent = Get-Content -LiteralPath $ucPath -Raw

# 1. Completeness & Uniqueness checks
$expectedFRs = 1..42 | ForEach-Object { "FR-{0:D3}" -f $_ }
$actualFRs = @($frRows | ForEach-Object { $_.FR_ID.Trim() })
Assert-Condition ($actualFRs.Count -eq 42) "Expected 42 FR rows, found $($actualFRs.Count)"
Assert-Condition ((@($actualFRs | Select-Object -Unique)).Count -eq 42) "Duplicate FR_ID detected"
foreach ($id in $expectedFRs) {
    Assert-Condition ($actualFRs -contains $id) "Missing canonical FR ID: $id"
}

$expectedBRs = 1..12 | ForEach-Object { "BR-{0:D3}" -f $_ }
$actualBRs = @($brRows | ForEach-Object { $_.BR_ID.Trim() })
Assert-Condition ($actualBRs.Count -eq 12) "Expected 12 BR rows, found $($actualBRs.Count)"
Assert-Condition ((@($actualBRs | Select-Object -Unique)).Count -eq 12) "Duplicate BR_ID detected"
foreach ($id in $expectedBRs) {
    Assert-Condition ($actualBRs -contains $id) "Missing canonical BR ID: $id"
}

$expectedNFRs = 1..11 | ForEach-Object { "NFR-{0:D3}" -f $_ }
$actualNFRs = @($nfrRows | ForEach-Object { $_.NFR_ID.Trim() })
Assert-Condition ($actualNFRs.Count -eq 11) "Expected 11 NFR rows (NFR-001..NFR-011), found $($actualNFRs.Count)"
Assert-Condition ((@($actualNFRs | Select-Object -Unique)).Count -eq 11) "Duplicate NFR_ID detected"
foreach ($id in $expectedNFRs) {
    Assert-Condition ($actualNFRs -contains $id) "Missing canonical NFR ID: $id"
}

$expectedACs = 1..8 | ForEach-Object { "AC-{0:D2}" -f $_ }
$actualACs = @($acRows | ForEach-Object { $_.AC_ID.Trim() })
Assert-Condition ($actualACs.Count -eq 8) "Expected 8 AC rows, found $($actualACs.Count)"
Assert-Condition ((@($actualACs | Select-Object -Unique)).Count -eq 8) "Duplicate AC_ID detected"
foreach ($id in $expectedACs) {
    Assert-Condition ($actualACs -contains $id) "Missing canonical AC ID: $id"
}

$expectedUCs = 1..10 | ForEach-Object { "UC-{0:D2}" -f $_ }
$ucMatches = [regex]::Matches($ucContent, '(?m)^### (UC-\d{2})\b') | ForEach-Object { $_.Groups[1].Value }
Assert-Condition ($ucMatches.Count -eq 10) "Expected 10 UC sections in Use-Case-Specifications.md, found $($ucMatches.Count)"
Assert-Condition ((@($ucMatches | Select-Object -Unique)).Count -eq 10) "Duplicate UC heading in Use-Case-Specifications.md"
foreach ($id in $expectedUCs) {
    Assert-Condition ($ucMatches -contains $id) "Missing UC section: $id"
}

# 2. Cross-reference resolution, status consistency, and C# test method verification
$frSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$actualFRs)
$brSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$actualBRs)
$nfrSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$actualNFRs)
$acSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$actualACs)
$ucSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$expectedUCs)

foreach ($row in $frRows) {
    foreach ($uc in (Split-Ids $row.RelatedUCs)) { Assert-Condition ($ucSet.Contains($uc)) "$($row.FR_ID): unresolved UC '$uc'" }
    foreach ($br in (Split-Ids $row.RelatedBRs)) { Assert-Condition ($brSet.Contains($br)) "$($row.FR_ID): unresolved BR '$br'" }
    foreach ($nfr in (Split-Ids $row.RelatedNFRs)) { Assert-Condition ($nfrSet.Contains($nfr)) "$($row.FR_ID): unresolved NFR '$nfr'" }
    foreach ($ac in (Split-Ids $row.RelatedACs)) { Assert-Condition ($acSet.Contains($ac)) "$($row.FR_ID): unresolved AC '$ac'" }

    Assert-Condition (-not [string]::IsNullOrWhiteSpace($row.LifecycleStatus)) "$($row.FR_ID): missing LifecycleStatus"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($row.ImplementationStatus)) "$($row.FR_ID): missing ImplementationStatus"
    Assert-Condition (-not [string]::IsNullOrWhiteSpace($row.VerificationStatus)) "$($row.FR_ID): missing VerificationStatus"

    if ($row.ImplementationStatus -in @("Not Implemented", "PoC Only")) {
        Assert-Condition ($row.VerificationStatus -notin @("Passed", "Partially Verified")) "$($row.FR_ID): $($row.ImplementationStatus) cannot claim VerificationStatus=$($row.VerificationStatus)"
    }
    if ($row.ImplementationStatus -eq "Partially Implemented") {
        Assert-Condition ($row.VerificationStatus -ne "Passed") "$($row.FR_ID): Partially Implemented cannot claim full VerificationStatus=Passed (use Partially Verified or Not Verified)"
    }

    if ($row.ImplementationStatus -eq "Implemented") {
        Assert-Condition ($row.CommitSHA -and $row.CommitSHA -ne "N/A") "$($row.FR_ID): Implemented row missing CommitSHA"
        Assert-Condition ($row.CIRunOrEvidence -and $row.CIRunOrEvidence -ne "N/A") "$($row.FR_ID): Implemented row missing CIRunOrEvidence"
        foreach ($srcRel in (Split-Ids $row.ImplementationSourcePath)) {
            $fullSrc = Join-Path $RepoRoot $srcRel
            Assert-Condition (Test-Path -LiteralPath $fullSrc) "$($row.FR_ID): ImplementationSourcePath does not exist: $srcRel"
        }
    }

    if ($row.VerificationStatus -in @("Passed", "Partially Verified")) {
        Assert-Condition ($row.TestSourcePath -and $row.TestSourcePath -ne "Test Planned") "$($row.FR_ID): $($row.VerificationStatus) row missing TestSourcePath"
        $testFile = (($row.TestSourcePath -split '\(')[0] -split '#')[0].Trim()
        $fullTestPath = Join-Path $RepoRoot $testFile
        Assert-Condition (Test-Path -LiteralPath $fullTestPath) "$($row.FR_ID): TestSourcePath file does not exist: $testFile"
        if ((Test-Path -LiteralPath $fullTestPath) -and $row.TestCaseID -match '^([A-Za-z0-9_]+)\.([A-Za-z0-9_]+)$') {
            $methodName = $Matches[2]
            $testRaw = Get-Content -LiteralPath $fullTestPath -Raw
            Assert-Condition ($testRaw -match "\b$([regex]::Escape($methodName))\s*\(") "$($row.FR_ID): Test method '$methodName' not found in $testFile"
        }
    }
}

foreach ($row in $brRows) {
    foreach ($fr in (Split-Ids $row.RelatedFRs)) { Assert-Condition ($frSet.Contains($fr)) "$($row.BR_ID): unresolved FR '$fr'" }
    foreach ($uc in (Split-Ids $row.RelatedUCs)) { Assert-Condition ($ucSet.Contains($uc)) "$($row.BR_ID): unresolved UC '$uc'" }
    foreach ($ac in (Split-Ids $row.RelatedACs)) { Assert-Condition ($acSet.Contains($ac)) "$($row.BR_ID): unresolved AC '$ac'" }
    foreach ($nfr in (Split-Ids $row.RelatedNFRs)) { Assert-Condition ($nfrSet.Contains($nfr)) "$($row.BR_ID): unresolved NFR '$nfr'" }
}

foreach ($row in $acRows) {
    foreach ($fr in (Split-Ids $row.RelatedFRs)) { Assert-Condition ($frSet.Contains($fr)) "$($row.AC_ID): unresolved FR '$fr'" }
    foreach ($br in (Split-Ids $row.RelatedBRs)) { Assert-Condition ($brSet.Contains($br)) "$($row.AC_ID): unresolved BR '$br'" }
    foreach ($nfr in (Split-Ids $row.RelatedNFRs)) { Assert-Condition ($nfrSet.Contains($nfr)) "$($row.AC_ID): unresolved NFR '$nfr'" }
    foreach ($uc in (Split-Ids $row.RelatedUCs)) { Assert-Condition ($ucSet.Contains($uc)) "$($row.AC_ID): unresolved UC '$uc'" }
    if ($row.ImplementationStatus -eq "Not Implemented") {
        Assert-Condition ($row.VerificationStatus -notin @("Passed", "Partially Verified")) "$($row.AC_ID): Not Implemented cannot claim $($row.VerificationStatus)"
    }
}

# 3. Placeholder ban check across Gate 1 files
$forbiddenPatterns = @('TO BE MAPPED', 'MISSING - add', 'See relevant AC')
foreach ($file in @($frPath, $brPath, $nfrPath, $acPath, $ucPath, $acRecPath, $clOdPath)) {
    $raw = Get-Content -LiteralPath $file -Raw
    foreach ($pat in $forbiddenPatterns) {
        Assert-Condition (-not $raw.Contains($pat)) "Forbidden placeholder '$pat' found in $file"
    }
}

Write-Output "Gate 1 Baseline Validation Summary (Mechanical Checks Only)"
Write-Output "Repository Root : $RepoRoot"
Write-Output "FR Rows         : $($frRows.Count) / 42"
Write-Output "BR Rows         : $($brRows.Count) / 12"
Write-Output "NFR Rows        : $($nfrRows.Count) / 11"
Write-Output "AC Rows         : $($acRows.Count) / 8"
Write-Output "UC Sections     : $($ucMatches.Count) / 10"
Write-Output "Checks Passed   : $checksPassed"
Write-Output "Failures        : $($failures.Count)"

if ($failures.Count -gt 0) {
    Write-Output "Failure Details :"
    foreach ($f in $failures) {
        Write-Output " - $f"
    }
    exit 1
}

Write-Output "RESULT: PASS (Mechanical integrity verified; semantic review & PO approval remain separate gates)"
exit 0
