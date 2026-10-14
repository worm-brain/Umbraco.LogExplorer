#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails when a NuGet package the solution references has a known vulnerability at or above a threshold.

.DESCRIPTION
    Runs `dotnet list package --vulnerable --format json` in the current directory (which restores
    first), then fails if any reported package has a vulnerability whose severity is at or above
    -Threshold. Advisories listed in the allowlist file are accepted risk and ignored, so an accepted
    finding does not fail later runs. Every reported advisory is printed regardless of severity, so
    nothing is hidden. A restore or evaluation error fails the audit rather than passing it unseen.

    Written in PowerShell (not jq) so it runs the same on the Linux CI runner and on a Windows or
    macOS dev box. MSBuild properties such as UmbracoMajor reach `dotnet list package` only as
    environment variables; see the Security workflow.

.PARAMETER Threshold
    Minimum severity that fails the audit: Low, Moderate, High (default) or Critical.

.PARAMETER AllowlistPath
    Path to the advisory allowlist. Each non-comment line is an advisory URL or a package id to
    ignore; '#' starts a comment. Defaults to security/nuget-audit-allowlist.txt.

.PARAMETER IncludeTransitive
    Audit transitive packages as well as the ones each project references directly. Use it where
    the transitive packages ship (a tool or an application); a library's are resolved by its consumer.

.OUTPUTS
    Exit code 0 when clean (or only allow-listed or below-threshold findings), 1 otherwise.
#>
[CmdletBinding()]
param(
    [ValidateSet('Low', 'Moderate', 'High', 'Critical')]
    [string]$Threshold = 'High',

    [string]$AllowlistPath = "$PSScriptRoot/../../security/nuget-audit-allowlist.txt",

    [switch]$IncludeTransitive
)

$ErrorActionPreference = 'Stop'

# Ordinal severity so we can compare against the threshold.
$rank = @{ Low = 1; Moderate = 2; High = 3; Critical = 4 }
$minRank = $rank[$Threshold]

$listArgs = @('list', 'package', '--vulnerable', '--format', 'json')
if ($IncludeTransitive) { $listArgs += '--include-transitive' }

# `dotnet list ... --format json` can emit a leading human line before the JSON on some SDKs; trim to
# the first '{' so ConvertFrom-Json always gets clean input.
$raw = (dotnet @listArgs 2>&1) | Out-String
$start = $raw.IndexOf('{')
if ($start -lt 0) {
    Write-Host "::error::'dotnet list package' emitted no JSON:"
    Write-Host $raw
    exit 1
}
$report = $raw.Substring($start) | ConvertFrom-Json

# A failed restore reports only a `problems` array and no projects, which would otherwise read as
# "no vulnerable packages". Fail on any error-level problem instead.
$errors = @($report.problems | Where-Object { $_.level -eq 'error' })
if ($errors.Count -gt 0) {
    Write-Host "::error::'dotnet list package' could not audit the solution:"
    $errors | ForEach-Object { Write-Host "  $($_.project) $($_.text)" }
    exit 1
}

# Load the allowlist (advisory URLs or package ids), stripping comments and blanks.
$allow = @()
if (Test-Path $AllowlistPath) {
    $allow = Get-Content $AllowlistPath |
        ForEach-Object { ($_ -replace '#.*$', '').Trim() } |
        Where-Object { $_ }
}

$all = [System.Collections.Generic.List[object]]::new()
$violations = [System.Collections.Generic.List[object]]::new()

foreach ($project in $report.projects) {
    foreach ($framework in $project.frameworks) {
        # Top-level and transitive packages have the same shape; audit whichever were listed.
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -eq $package) { continue }
            foreach ($vuln in $package.vulnerabilities) {
                $row = [pscustomobject]@{
                    Project  = Split-Path -Leaf $project.path
                    Package  = $package.id
                    Version  = $package.resolvedVersion
                    Severity = $vuln.severity
                    Advisory = $vuln.advisoryurl
                }
                $all.Add($row)

                $ignored = ($allow -contains $vuln.advisoryurl) -or ($allow -contains $package.id)
                $sev = $rank[[string]$vuln.severity]
                if (-not $ignored -and $sev -ge $minRank) {
                    $violations.Add($row)
                }
            }
        }
    }
}

if ($all.Count -gt 0) {
    Write-Host "All reported advisories:"
    ($all | Format-Table -AutoSize | Out-String -Width 400) | Write-Host
}
else {
    Write-Host "No vulnerable packages reported."
}

if ($violations.Count -gt 0) {
    Write-Host "::error::$($violations.Count) vulnerable package(s) at or above $Threshold and not allow-listed."
    ($violations | Format-Table -AutoSize | Out-String -Width 400) | Write-Host
    exit 1
}

Write-Host "PASS: no vulnerabilities at or above $Threshold (allow-listed advisories excluded)."
exit 0
