# Legacy compatibility assets for launcher releases.
# New mod content is published independently by New-ModPackages.ps1.
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
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($PayloadPath)) {
    $PayloadPath = Join-Path $repoRoot 'robots_01.zip'
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/release'
}
if ($BuildIds.Count -eq 0) {
    $buildListPath = Join-Path $repoRoot 'distribution/supported-builds.txt'
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

function Get-StreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Stream))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        return Get-StreamSha256 $stream
    }
    finally {
        $stream.Dispose()
    }
}

function Write-JsonFile([string]$Path, $Value) {
    $json = $Value | ConvertTo-Json -Depth 8
    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function New-DeterministicSingleFileZip(
    [string]$SourcePath,
    [string]$EntryName,
    [string]$DestinationPath
) {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Module source not found: $SourcePath"
    }
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    $archiveStream = [IO.File]::Open(
        $DestinationPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try {
        $archive = New-Object IO.Compression.ZipArchive(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $entry = $archive.CreateEntry(
                $EntryName,
                [IO.Compression.CompressionLevel]::NoCompression)
            $entry.LastWriteTime = New-Object DateTimeOffset(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $sourceText = [IO.File]::ReadAllText($SourcePath)
            $canonicalText = $sourceText.Replace("`r`n", "`n").Replace("`r", "`n")
            $utf8NoBom = New-Object Text.UTF8Encoding($false)
            $sourceBytes = $utf8NoBom.GetBytes($canonicalText)
            $entryStream = $entry.Open()
            try {
                $entryStream.Write($sourceBytes, 0, $sourceBytes.Length)
            }
            finally {
                $entryStream.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

function Get-ManifestFiles([string]$ArchivePath, [object[]]$Mapping) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $expectedNames = @($Mapping | ForEach-Object { $_.source })
        $unexpected = @($fileEntries | Where-Object { $_.FullName -notin $expectedNames })
        if ($unexpected.Count -gt 0) {
            throw "Unexpected payload entries: $($unexpected.FullName -join ', ')"
        }

        return @($Mapping | ForEach-Object {
            $item = $_
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
        })
    }
    finally {
        $archive.Dispose()
    }
}

$robotMapping = @(
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

$moduleDefinitions = @(
    [pscustomobject]@{
        modId = 'scrap-mechanic-robot-loot'
        manifestAsset = 'manifest.json'
        payloadAsset = 'robots_01.zip'
        defaultSelected = $true
        mapping = $robotMapping
        sourcePath = $null
    },
    [pscustomobject]@{
        modId = 'scrap-mechanic-beehive-automation'
        manifestAsset = 'manifest-beehive-automation.json'
        payloadAsset = 'beehive-automation.zip'
        defaultSelected = $false
        mapping = @([pscustomobject]@{
            source = 'beehive-automation/InteractableBeehive.lua'
            target = 'Survival/Scripts/game/interactables/InteractableBeehive.lua'
        })
        sourcePath = Join-Path $repoRoot 'mods/beehive-automation/InteractableBeehive.lua'
    },
    [pscustomobject]@{
        modId = 'scrap-mechanic-freezer-automation'
        manifestAsset = 'manifest-freezer-automation.json'
        payloadAsset = 'freezer-automation.zip'
        defaultSelected = $false
        mapping = @([pscustomobject]@{
            source = 'freezer-automation/Freezer.lua'
            target = 'Survival/Scripts/game/interactables/Freezer.lua'
        })
        sourcePath = Join-Path $repoRoot 'mods/freezer-automation/Freezer.lua'
    }
)

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath $PayloadPath -Destination (Join-Path $OutputDirectory 'robots_01.zip') -Force
New-DeterministicSingleFileZip `
    -SourcePath $moduleDefinitions[1].sourcePath `
    -EntryName $moduleDefinitions[1].mapping[0].source `
    -DestinationPath (Join-Path $OutputDirectory $moduleDefinitions[1].payloadAsset)
New-DeterministicSingleFileZip `
    -SourcePath $moduleDefinitions[2].sourcePath `
    -EntryName $moduleDefinitions[2].mapping[0].source `
    -DestinationPath (Join-Path $OutputDirectory $moduleDefinitions[2].payloadAsset)

foreach ($module in $moduleDefinitions) {
    $payloadOutput = Join-Path $OutputDirectory $module.payloadAsset
    $files = Get-ManifestFiles -ArchivePath $payloadOutput -Mapping $module.mapping
    $manifest = [ordered]@{
        schemaVersion = 1
        modId = $module.modId
        version = $Version
        payloadAsset = $module.payloadAsset
        payloadSha256 = Get-FileSha256 $payloadOutput
        supportedBuildIds = @($BuildIds)
        files = @($files)
    }
    Write-JsonFile -Path (Join-Path $OutputDirectory $module.manifestAsset) -Value $manifest
}

$catalog = [ordered]@{
    schemaVersion = 1
    modules = @($moduleDefinitions | ForEach-Object {
        [ordered]@{
            modId = $_.modId
            manifestAsset = $_.manifestAsset
            defaultSelected = $_.defaultSelected
        }
    })
}
Write-JsonFile -Path (Join-Path $OutputDirectory 'modules.json') -Value $catalog

Write-Output "Legacy release payloads created: $OutputDirectory"
Write-Output "Version: $Version"
Write-Output "Build IDs: $($BuildIds -join ', ')"
foreach ($module in $moduleDefinitions) {
    Write-Output "$($module.payloadAsset) SHA256: $(Get-FileSha256 (Join-Path $OutputDirectory $module.payloadAsset))"
}
