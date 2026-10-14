#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Scans the full commit history for committed secrets using a pinned gitleaks release.

.DESCRIPTION
    Downloads a pinned gitleaks binary, verifies it against the release's own SHA-256
    checksums file, then runs `gitleaks detect` over git history with secret values redacted.
    gitleaks reads .gitleaks.toml (default rules plus whole-path allowlists) and .gitleaksignore
    (accepted findings by fingerprint) from the repository root, so an accepted finding does not
    re-fail later runs.

    Cross-platform: selects the correct release asset for Linux (CI), Windows and macOS, so
    it runs identically in the Security workflow and on a dev box. Requires a full clone
    (the workflow checks out with fetch-depth: 0).

    Hardening note: verifying against the release's own checksums file guards download
    integrity, not a compromised upstream release. For stronger supply-chain assurance, pin
    the expected digest here.

.PARAMETER Version
    The gitleaks release version to pin.
#>
[CmdletBinding()]
param(
    [string]$Version = '8.21.2'
)

$ErrorActionPreference = 'Stop'

# Select the release asset and binary name for this OS.
if ($IsWindows) {
    $asset = "gitleaks_${Version}_windows_x64.zip"
    $binName = 'gitleaks.exe'
}
elseif ($IsMacOS) {
    $asset = "gitleaks_${Version}_darwin_x64.tar.gz"
    $binName = 'gitleaks'
}
else {
    $asset = "gitleaks_${Version}_linux_x64.tar.gz"
    $binName = 'gitleaks'
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) ("gitleaks-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
$baseUrl = "https://github.com/gitleaks/gitleaks/releases/download/v$Version"
$archive = Join-Path $work $asset
$checksums = Join-Path $work 'checksums.txt'

Write-Host "Downloading gitleaks $Version ($asset) ..."
Invoke-WebRequest -Uri "$baseUrl/$asset" -OutFile $archive
Invoke-WebRequest -Uri "$baseUrl/gitleaks_${Version}_checksums.txt" -OutFile $checksums

# Verify the download against the release checksums file.
$expected = Get-Content $checksums |
    Where-Object { $_ -match [regex]::Escape($asset) } |
    ForEach-Object { ($_ -split '\s+')[0] } |
    Select-Object -First 1
if (-not $expected) {
    Write-Host "::error::no checksum listed for $asset"
    exit 1
}
$actual = (Get-FileHash -Algorithm SHA256 -Path $archive).Hash.ToLower()
if ($actual -ne $expected.ToLower()) {
    Write-Host "::error::checksum mismatch for $asset (expected $expected, got $actual)"
    exit 1
}

# Extract just the binary.
if ($asset.EndsWith('.zip')) {
    Expand-Archive -Path $archive -DestinationPath $work -Force
}
else {
    tar -xzf $archive -C $work
}
$gitleaks = Join-Path $work $binName

# detect: scans git history (default). --redact hides secret values in output;
# .gitleaksignore (repo root) suppresses accepted fingerprints; --exit-code 1 fails on any
# unignored finding.
Write-Host "Running gitleaks detect over history ..."
& $gitleaks detect --source . --redact --no-banner --exit-code 1
exit $LASTEXITCODE
