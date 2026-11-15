#requires -Version 7.4
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Architecture = 'x64',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Packaged PHI for Windows 11 requires Windows.' }
Get-Command dotnet, winapp -ErrorAction Stop | Out-Null

$workspace = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
if ($env:COPILOT_WORKSPACE_PATH -and
    [IO.Path]::GetFullPath($env:COPILOT_WORKSPACE_PATH).TrimEnd('\') -ine $workspace) {
    throw 'The Copilot workspace does not match this launch script. Run it from the intended worktree.'
}
$project = Join-Path $workspace 'PhiClient\PhiClient.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'PhiClient.csproj was not found.' }

$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
    [Text.Encoding]::UTF8.GetBytes($workspace.ToUpperInvariant()))).Substring(0, 12).ToLowerInvariant()
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if (-not $userProfile) { throw 'The Windows user profile directory is unavailable.' }
$layout = Join-Path $userProfile "PhiClientLayouts\$hash\Debug-$Architecture"
$arguments = @('run', $project, '--arch', $Architecture, '--detach', '--unique-identity',
    '--devtools', 'off', '--output-appx-directory', $layout)
if ($NoBuild) { $arguments += '--no-build' }

if ($PSCmdlet.ShouldProcess($project, 'Build and launch packaged PHI for Windows 11 with a worktree-specific identity')) {
    New-Item -ItemType Directory -Path $layout -Force | Out-Null
    Push-Location -LiteralPath $workspace
    try {
        & winapp @arguments
        if ($LASTEXITCODE -ne 0) { throw "winapp failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}
else {
    [pscustomobject]@{ Executed = $false; Workspace = $workspace; Layout = $layout; Arguments = $arguments }
}
