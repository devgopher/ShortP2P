# Publishes ShortP2P.MessengerServer.Api as a self-contained folder.
# Usage (repo root or scripts/server):
#   .\scripts\server\publish.ps1
#   .\scripts\server\publish.ps1 -Rid win-x64
#   .\scripts\server\publish.ps1 -Rid linux-x64
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'all')]
    [string] $Rid = 'all',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $start = @()
    if ($PSScriptRoot) { $start += $PSScriptRoot }
    $start += (Get-Location).Path

    foreach ($origin in $start) {
        $dir = $origin
        while ($dir) {
            $probe = Join-Path $dir 'src\Server\ShortP2P.MessengerServer.Api\ShortP2P.MessengerServer.Api.csproj'
            if (Test-Path -LiteralPath $probe) {
                return (Resolve-Path -LiteralPath $dir).Path
            }
            $parent = Split-Path -Parent $dir
            if (-not $parent -or $parent -eq $dir) { break }
            $dir = $parent
        }
    }

    throw "Cannot find the ShortP2P repo root (looked for ShortP2P.MessengerServer.Api.csproj)."
}

function Get-ServerVersion {
    param([string] $RepoRoot, [string] $ScriptDir)

    $csproj = Join-Path $RepoRoot 'src\Server\ShortP2P.MessengerServer.Api\ShortP2P.MessengerServer.Api.csproj'
    $text = Get-Content -LiteralPath $csproj -Raw
    if ($text -match '<InformationalVersion>\s*([^<]+?)\s*</InformationalVersion>') {
        return $Matches[1].Trim()
    }
    if ($text -match '<Version>\s*([0-9][^<]*?)\s*</Version>') {
        return $Matches[1].Trim()
    }

    $versionFile = Join-Path $ScriptDir 'VERSION'
    if (Test-Path -LiteralPath $versionFile) {
        return (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }

    return '0.1.0'
}

$repoRoot = Find-RepoRoot
$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Join-Path $repoRoot 'scripts\server' }
$csproj = Join-Path $repoRoot 'src\Server\ShortP2P.MessengerServer.Api\ShortP2P.MessengerServer.Api.csproj'
$outRoot = Join-Path $scriptDir 'out'
$version = Get-ServerVersion -RepoRoot $repoRoot -ScriptDir $scriptDir

$rids = if ($Rid -eq 'all') { @('win-x64', 'linux-x64') } else { @($Rid) }

Write-Host "Repo:    $repoRoot"
Write-Host "Version: $version"
Write-Host "RIDs:    $($rids -join ', ')"

foreach ($runtime in $rids) {
    $dest = Join-Path $outRoot $runtime
    if (Test-Path -LiteralPath $dest) {
        Remove-Item -LiteralPath $dest -Recurse -Force
    }
    New-Item -ItemType Directory -Path $dest -Force | Out-Null

    Write-Host "Publishing $runtime -> $dest"
    & dotnet publish $csproj `
        -c $Configuration `
        -r $runtime `
        --self-contained true `
        -o $dest
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $runtime (exit $LASTEXITCODE)."
    }
}

Write-Host "Publish finished. Output: $outRoot"
