#requires -Version 7.4
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\.local\wphi-graphics'),
    [string]$ArchivePath
)

$ErrorActionPreference = 'Stop'
$revision = 'd8f11bf60c95d470fa14ca83bcc4c02d5c83fecc'
$archiveHash = '92E09A35945B8103AF93AD7C1B92C74B4964654FF6E1F0A6B08D15A94F87A379'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ($output.StartsWith('\\') -or (Test-Path -LiteralPath $output)) {
    throw 'Choose a new local output directory. Existing graphics are never overwritten.'
}

$temporaryArchive = $null
$archive = $null
try {
    if (-not $ArchivePath) {
        $temporaryArchive = [IO.Path]::GetTempFileName()
        Invoke-WebRequest "https://codeload.github.com/s7k/wphi/zip/$revision" -OutFile $temporaryArchive
        $ArchivePath = $temporaryArchive
    }
    if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash -ne $archiveHash) {
        throw 'The archive does not match the reviewed wphi revision. No graphics were imported.'
    }
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
    $prefix = "wphi-$revision/resources/phi/"
    $entries = @($archive.Entries | Where-Object {
        if (-not $_.FullName.StartsWith($prefix, [StringComparison]::Ordinal)) { return $false }
        $relative = $_.FullName.Substring($prefix.Length)
        $relative -match '^chara/[^/]+\.bmp$' -or
        $relative -match '^Skins/Mine/chara/[^/]+\.(bmp|txt)$' -or
        $relative -in @('README.md', 'PCGL.txt', 'PCGL.utf8.txt',
            'Config/MasterMagicMenu.xml',
            'Skins/Mine/chip/Default.bmp', 'Skins/Mine/Images/Items.bmp',
            'Skins/Mine/制作者.txt', 'Skins/Mine/説明.txt')
    })
    if ($entries.Count -ne 349) {
        throw "Unexpected original graphics inventory ($($entries.Count)). No graphics were imported."
    }
    New-Item -ItemType Directory -Path $output | Out-Null
    $files = foreach ($entry in $entries) {
        $relative = $entry.FullName.Substring($prefix.Length).Replace('/', '\')
        $destination = [IO.Path]::GetFullPath((Join-Path $output $relative))
        if (-not $destination.StartsWith($output.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe archive entry: $relative"
        }
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination)) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $destination, $false)
        [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash }
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'WphiGraphics-NOTICE.md') -Destination $output
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Sushi-MIT.txt') -Destination $output
    [ordered]@{
        source = 'https://github.com/s7k/wphi'
        revision = $revision
        archiveSha256 = $archiveHash
        files = @($files)
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-manifest.json') -Encoding utf8
    Write-Output "Original wphi graphics imported: $output"
    Write-Output 'Choose View > Graphics folder in PHI Client Lab, enter this directory, and Load.'
} finally {
    if ($archive) { $archive.Dispose() }
    if ($temporaryArchive) { Remove-Item -LiteralPath $temporaryArchive }
}
