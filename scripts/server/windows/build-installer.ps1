# Publishes win-x64 (if needed) and compiles the Inno Setup installer.
# Usage (repo root or scripts/server or scripts/server/windows):
#   powershell -File scripts\server\windows\build-installer.ps1
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $SkipPublish
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
    param([string] $RepoRoot, [string] $ServerScripts)

    $csproj = Join-Path $RepoRoot 'src\Server\ShortP2P.MessengerServer.Api\ShortP2P.MessengerServer.Api.csproj'
    $text = Get-Content -LiteralPath $csproj -Raw
    if ($text -match '<InformationalVersion>\s*([^<]+?)\s*</InformationalVersion>') {
        return $Matches[1].Trim()
    }
    if ($text -match '<Version>\s*([0-9][^<]*?)\s*</Version>') {
        return $Matches[1].Trim()
    }

    $versionFile = Join-Path $ServerScripts 'VERSION'
    if (Test-Path -LiteralPath $versionFile) {
        return (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }

    return '0.1.0'
}

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path -LiteralPath $path)) { return $path }
    }
    return $null
}

$repoRoot = Find-RepoRoot
$serverScripts = Join-Path $repoRoot 'scripts\server'
$publishScript = Join-Path $serverScripts 'publish.ps1'
$iss = Join-Path $serverScripts 'windows\shortp2p-messengerserver.iss'
$publishDir = Join-Path $serverScripts 'out\win-x64'
$version = Get-ServerVersion -RepoRoot $repoRoot -ServerScripts $serverScripts

if (-not $SkipPublish) {
    & $publishScript -Rid win-x64 -Configuration $Configuration
}

if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'ShortP2P.MessengerServer.Api.exe'))) {
    throw "Publish output not found: $publishDir. Run publish.ps1 -Rid win-x64 first."
}

$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host "Inno Setup compiler (ISCC.exe) is not installed."
    Write-Host "Publish is ready at: $publishDir"
    Write-Host "Install Inno Setup 6, then run:"
    Write-Host "  & `"<path-to-ISCC.exe>`" `"/DMyAppVersion=$version`" `"$iss`""
    exit 0
}

$installerOut = Join-Path $serverScripts 'out\installer'
New-Item -ItemType Directory -Path $installerOut -Force | Out-Null

Write-Host "Compiling installer version $version"
& $iscc "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit $LASTEXITCODE)."
}

Write-Host "Installer output: $installerOut"
