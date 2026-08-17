[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($PublishDirectory)
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$executablePath = Join-Path $resolvedPublishDirectory "ScrapMechanicModManager"
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Linux launcher executable not found: $executablePath"
}

$packageName = "ScrapMechanicModManager-linux-x64"
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "smmm-linux-$([Guid]::NewGuid().ToString('N'))"
$stagingDirectory = Join-Path $temporaryRoot $packageName
$archivePath = Join-Path $resolvedOutputDirectory "$packageName.tar.gz"

try {
    New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $resolvedPublishDirectory "*") `
        -Destination $stagingDirectory `
        -Recurse `
        -Force
    Get-ChildItem -Path $stagingDirectory -Filter "*.pdb" -Recurse |
        Remove-Item -Force

    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "distribution/linux/scrap-mechanic-mod-manager") `
        -Destination $stagingDirectory `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "distribution/linux/scrap-mechanic-mod-manager.desktop") `
        -Destination $stagingDirectory `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "distribution/linux/README-Linux.txt") `
        -Destination $stagingDirectory `
        -Force
    Copy-Item `
        -LiteralPath (Join-Path $repoRoot "src/ScrapMechanicModManager.Desktop/Assets/ScrapMechanicModManager.png") `
        -Destination $stagingDirectory `
        -Force
    [System.IO.File]::WriteAllText(
        (Join-Path $stagingDirectory "VERSION"),
        "$Version`n",
        [System.Text.UTF8Encoding]::new($false))

    $isRunningOnWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    if (-not $isRunningOnWindows) {
        & chmod +x `
            (Join-Path $stagingDirectory "ScrapMechanicModManager") `
            (Join-Path $stagingDirectory "scrap-mechanic-mod-manager")
        if ($LASTEXITCODE -ne 0) {
            throw "chmod failed with exit code $LASTEXITCODE."
        }
    }

    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    $temporaryArchiveName = "$packageName.tar.gz"
    $temporaryArchivePath = Join-Path $temporaryRoot $temporaryArchiveName
    Push-Location $temporaryRoot
    try {
        & tar -czf $temporaryArchiveName $packageName
        if ($LASTEXITCODE -ne 0) {
            throw "tar failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
    Move-Item -LiteralPath $temporaryArchivePath -Destination $archivePath -Force

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    $archiveStream = [System.IO.File]::OpenRead($archivePath)
    try {
        $hash = [System.BitConverter]::ToString(
            $sha256.ComputeHash($archiveStream)).Replace("-", "")
    }
    finally {
        $archiveStream.Dispose()
        $sha256.Dispose()
    }
    Write-Host "Version: $Version"
    Write-Host "Archive: $archivePath"
    Write-Host "SHA-256: $hash"
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
