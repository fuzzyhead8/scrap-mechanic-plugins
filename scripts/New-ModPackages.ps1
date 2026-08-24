#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^mods-v\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$ReleaseTag,

    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string]$MinimumManagerVersion = '0.2.0-preview.12',

    [string[]]$BuildIds = @(),

    [string]$OutputDirectory = '',

    [string]$RepositoryOwner = 'fuzzyhead8',

    [string]$RepositoryName = 'scrap-mechanic-plugins'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = [IO.Path]::GetFullPath((Join-Path $scriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/mod-packages'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

function Get-BytesSha256([byte[]]$Bytes) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-CanonicalTextBytes([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Module source not found: $Path"
    }
    $sourceText = [IO.File]::ReadAllText($Path)
    $canonicalText = $sourceText.Replace("`r`n", "`n").Replace("`r", "`n")
    return ,([Text.UTF8Encoding]::new($false).GetBytes($canonicalText))
}

function Get-ZipEntryBytes([string]$ArchivePath, [string]$EntryName) {
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $entry = $archive.GetEntry($EntryName)
        if ($null -eq $entry -or [string]::IsNullOrEmpty($entry.Name)) {
            throw "ZIP entry not found: $EntryName"
        }
        $entryStream = $entry.Open()
        try {
            $memory = [IO.MemoryStream]::new()
            try {
                $entryStream.CopyTo($memory)
                return ,$memory.ToArray()
            }
            finally {
                $memory.Dispose()
            }
        }
        finally {
            $entryStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function ConvertTo-CanonicalJsonBytes([object]$Value) {
    $json = $Value | ConvertTo-Json -Depth 20
    $canonicalJson = $json.Replace("`r`n", "`n").Replace("`r", "`n")
    return ,([Text.UTF8Encoding]::new($false).GetBytes($canonicalJson))
}

function Write-Utf8Json([string]$Path, [object]$Value) {
    [byte[]]$bytes = ConvertTo-CanonicalJsonBytes $Value
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function New-DeterministicPackage(
    [object]$Definition,
    [object[]]$PayloadEntries,
    [string]$DestinationPath
) {
    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    [byte[]]$moduleJson = ConvertTo-CanonicalJsonBytes $Definition
    $entries = @(
        [pscustomobject][ordered]@{
            Name = 'module.json'
            Bytes = $moduleJson
        }
    ) + @($PayloadEntries | Sort-Object Source | ForEach-Object {
        [pscustomobject][ordered]@{
            Name = $_.Source
            Bytes = [byte[]]$_.Bytes
        }
    })

    $archiveStream = [IO.File]::Open(
        $DestinationPath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($item in $entries) {
                $entry = $archive.CreateEntry(
                    $item.Name,
                    [IO.Compression.CompressionLevel]::NoCompression)
                $entry.LastWriteTime = [DateTimeOffset]::new(
                    1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $entry.ExternalAttributes = 0
                $entryStream = $entry.Open()
                try {
                    [byte[]]$bytes = $item.Bytes
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
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

function Read-LegacyManifest([string]$RelativePath) {
    $path = Join-Path $repoRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Legacy manifest not found: $path"
    }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

$moduleSpecs = @(
    [pscustomobject][ordered]@{
        Manifest = 'distribution/manifest.json'
        DisplayNameHungarian = 'Robot zsákmány'
        DisplayNameEnglish = 'Robot Loot'
        DescriptionHungarian = 'Tesztelt robot zsákmánytáblák.'
        DescriptionEnglish = 'Tested robot loot tables.'
        DefaultSelected = $true
        SourceKind = 'robot-archive'
    },
    [pscustomobject][ordered]@{
        Manifest = 'distribution/manifest-beehive-automation.json'
        DisplayNameHungarian = 'Méhkaptár automatizálás'
        DisplayNameEnglish = 'Beehive Automation'
        DescriptionHungarian = 'A viasztermelést fizikai kimenetté alakítja.'
        DescriptionEnglish = 'Converts wax production into physical output.'
        DefaultSelected = $false
        SourceKind = 'canonical-file'
    },
    [pscustomobject][ordered]@{
        Manifest = 'distribution/manifest-freezer-automation.json'
        DisplayNameHungarian = 'Fagyasztó automatizálás'
        DisplayNameEnglish = 'Freezer Automation'
        DescriptionHungarian = 'A jégtermelést fizikai kimenetté alakítja.'
        DescriptionEnglish = 'Converts ice production into physical output.'
        DefaultSelected = $false
        SourceKind = 'canonical-file'
    }
)

$catalogModules = @()
foreach ($spec in $moduleSpecs) {
    $manifest = Read-LegacyManifest $spec.Manifest
    [string[]]$supportedBuildIds = @(
        if ($BuildIds.Count -gt 0) {
            $BuildIds
        }
        else {
            $manifest.supportedBuildIds
        }
    )
    if ($supportedBuildIds.Count -eq 0) {
        throw "Module $($manifest.modId) has no supported Steam build IDs."
    }

    $payloadEntries = @()
    $definitionFiles = @()
    foreach ($file in @($manifest.files)) {
        [byte[]]$bytes = if ($spec.SourceKind -eq 'robot-archive') {
            Get-ZipEntryBytes (Join-Path $repoRoot 'robots_01.zip') $file.source
        }
        else {
            Get-CanonicalTextBytes (Join-Path $repoRoot ("mods/" + $file.source))
        }
        $actualHash = Get-BytesSha256 $bytes
        if (-not [string]::Equals(
                $actualHash,
                [string]$file.sha256,
                [StringComparison]::OrdinalIgnoreCase)) {
            throw "Canonical source hash mismatch for $($file.source): $actualHash"
        }

        $packageSource = 'payload/' + ([string]$file.source).Replace('\', '/')
        $payloadEntries += [pscustomobject][ordered]@{
            Source = $packageSource
            Bytes = $bytes
        }
        $definitionFiles += [pscustomobject][ordered]@{
            source = $packageSource
            target = $file.target
            sha256 = $actualHash
        }
    }

    $definition = [pscustomobject][ordered]@{
        schemaVersion = 1
        modId = $manifest.modId
        version = $manifest.version
        displayName = [pscustomobject][ordered]@{
            hungarian = $spec.DisplayNameHungarian
            english = $spec.DisplayNameEnglish
        }
        description = [pscustomobject][ordered]@{
            hungarian = $spec.DescriptionHungarian
            english = $spec.DescriptionEnglish
        }
        minimumManagerVersion = $MinimumManagerVersion
        supportedBuildIds = $supportedBuildIds
        files = $definitionFiles
    }

    $packageName = "$($manifest.modId).smmmod"
    $packagePath = Join-Path $OutputDirectory $packageName
    New-DeterministicPackage $definition $payloadEntries $packagePath
    $packageSha256 = Get-FileSha256 $packagePath
    $packageUrl = "https://github.com/$RepositoryOwner/$RepositoryName/releases/download/" +
        "$ReleaseTag/$packageName"
    $catalogModules += [pscustomobject][ordered]@{
        definition = $definition
        packageUrl = $packageUrl
        packageSha256 = $packageSha256
        defaultSelected = [bool]$spec.DefaultSelected
    }
}

$catalog = [pscustomobject][ordered]@{
    schemaVersion = 1
    releaseTag = $ReleaseTag
    modules = $catalogModules
}
$catalogPath = Join-Path $OutputDirectory 'catalog-v1.json'
Write-Utf8Json $catalogPath $catalog

Write-Host "Built $($catalogModules.Count) deterministic .smmmod packages."
Write-Host "Catalog: $catalogPath"
foreach ($module in $catalogModules) {
    Write-Host "$($module.definition.modId): $($module.packageSha256)"
}
