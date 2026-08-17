[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [string[]]$BuildIds = @(),

    [string]$PayloadPath = '',

    [string]$OutputDirectory = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $scriptRoot '..\robots_01.zip'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $scriptRoot '..\artifacts\release'
}
if ($BuildIds.Count -eq 0) {
    $buildListPath = Join-Path $scriptRoot '..\distribution\supported-builds.txt'
    if (-not (Test-Path -LiteralPath $buildListPath -PathType Leaf)) {
        throw "Supported build list not found: $buildListPath"
    }
    $BuildIds = @(Get-Content -LiteralPath $buildListPath | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}
if ($BuildIds.Count -eq 0) {
    throw 'At least one supported Steam build ID is required.'
}
$PayloadPath = [IO.Path]::GetFullPath($PayloadPath)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $PayloadPath -PathType Leaf)) {
    throw "Payload not found: $PayloadPath"
}

$mapping = @(
    [pscustomobject]@{
        source = 'robots_01/lootsource_haybot.lua'
        target = 'Survival/Scripts/game/loot/lootsources/robots_01/lootsource_haybot.lua'
    },
    [pscustomobject]@{
        source = 'robots_01/lootsource_tapebot.lua'
        target = 'Survival/Scripts/game/loot/lootsources/robots_01/lootsource_tapebot.lua'
    },
    [pscustomobject]@{
        source = 'robots_01/lootsource_totebot_blue.lua'
        target = 'Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_blue.lua'
    },
    [pscustomobject]@{
        source = 'robots_01/lootsource_totebot_green.lua'
        target = 'Survival/Scripts/game/loot/lootsources/robots_01/lootsource_totebot_green.lua'
    }
)

function Get-StreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Stream))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

$archive = [IO.Compression.ZipFile]::OpenRead($PayloadPath)
try {
    $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
    $expectedNames = @($mapping | ForEach-Object { $_.source })
    $unexpected = @($fileEntries | Where-Object { $_.FullName -notin $expectedNames })
    if ($unexpected.Count -gt 0) {
        throw "Unexpected payload entries: $($unexpected.FullName -join ', ')"
    }

    $files = foreach ($item in $mapping) {
        $entry = $archive.GetEntry($item.source)
        if ($null -eq $entry) {
            throw "Missing payload entry: $($item.source)"
        }
        $stream = $entry.Open()
        try {
            $hash = Get-StreamSha256 $stream
        }
        finally {
            $stream.Dispose()
        }
        [pscustomobject]@{
            source = $item.source
            target = $item.target
            sha256 = $hash
        }
    }
}
finally {
    $archive.Dispose()
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$payloadOutput = Join-Path $OutputDirectory 'robots_01.zip'
Copy-Item -LiteralPath $PayloadPath -Destination $payloadOutput -Force
$payloadStream = [IO.File]::OpenRead($payloadOutput)
try {
    $payloadHash = Get-StreamSha256 $payloadStream
}
finally {
    $payloadStream.Dispose()
}

$manifest = [ordered]@{
    schemaVersion = 1
    modId = 'scrap-mechanic-robot-loot'
    version = $Version
    payloadAsset = 'robots_01.zip'
    payloadSha256 = $payloadHash
    supportedBuildIds = @($BuildIds)
    files = @($files)
}
$json = $manifest | ConvertTo-Json -Depth 8
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), $json, $utf8NoBom)

Write-Output "Release payload created: $OutputDirectory"
Write-Output "Version: $Version"
Write-Output "Build IDs: $($BuildIds -join ', ')"
Write-Output "Payload SHA256: $payloadHash"
