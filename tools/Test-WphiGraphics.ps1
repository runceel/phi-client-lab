#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($ArtifactDirectory)
if (Test-Path -LiteralPath $root) { throw 'Choose a new test artifact directory.' }
New-Item -ItemType Directory -Path $root | Out-Null
$output = Join-Path $root 'graphics'
$results = [Collections.Generic.List[object]]::new()
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{ name = $Name; status = 'PASS' }) }
    catch { $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" }) }
}
Check 'Unchanged originals and all license files, without server configuration' {
    & (Join-Path $PSScriptRoot 'Get-WphiGraphics.ps1') -ArchivePath $ArchivePath -OutputDirectory $output
    $archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ArchivePath))
    try {
        $manifest = Get-Content -LiteralPath (Join-Path $output 'source-manifest.json') -Raw | ConvertFrom-Json
        foreach ($file in $manifest.files) {
            $entry = $archive.GetEntry("wphi-$($manifest.revision)/resources/phi/" + $file.path.Replace('\', '/'))
            $stream = $entry.Open()
            try {
                $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
                $actual = (Get-FileHash -LiteralPath (Join-Path $output $file.path) -Algorithm SHA256).Hash
                if ($actual -ne $expected) { throw "Original file was changed: $($file.path)" }
            } finally { $stream.Dispose() }
        }
        foreach ($name in @('PCGL.txt', 'PCGL.utf8.txt', 'README.md', 'WphiGraphics-NOTICE.md',
            'Sushi-MIT.txt', 'Skins\Mine\制作者.txt')) {
            if (-not (Test-Path -LiteralPath (Join-Path $output $name))) { throw "Missing notice: $name" }
        }
        if (Test-Path -LiteralPath (Join-Path $output 'Config')) { throw 'Server configuration was imported.' }
        if (Get-ChildItem -LiteralPath $output -Recurse -Filter 'LANU_*') { throw 'Excluded graphic was imported.' }
    } finally { $archive.Dispose() }
}
Check 'Existing graphics and notices are never overwritten' {
    $before = (Get-FileHash -LiteralPath (Join-Path $output 'PCGL.txt')).Hash
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Get-WphiGraphics.ps1') -ArchivePath $ArchivePath -OutputDirectory $output }
    catch { if ("$_" -notlike '*never overwritten*') { throw }; $rejected = $true }
    if (-not $rejected -or (Get-FileHash -LiteralPath (Join-Path $output 'PCGL.txt')).Hash -ne $before) {
        throw 'Existing collection was not preserved.'
    }
}
Check 'Unreviewed archives are rejected before creating an output folder' {
    $invalid = Join-Path $root 'invalid.zip'
    [IO.File]::WriteAllBytes($invalid, [byte[]]@(1, 2, 3))
    $target = Join-Path $root 'rejected'
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Get-WphiGraphics.ps1') -ArchivePath $invalid -OutputDirectory $target }
    catch { if ("$_" -notlike '*does not match*') { throw }; $rejected = $true }
    if (-not $rejected -or (Test-Path -LiteralPath $target)) { throw 'Unreviewed files were imported.' }
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ results = @($results.ToArray()); passed = $results.Count - $failed; failed = $failed } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
if ($failed) { throw "Graphics importer failed $failed checks. See $root\results.json" }
Write-Output "Passed $($results.Count) importer checks: $root\results.json"
