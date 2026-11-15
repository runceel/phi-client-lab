#requires -Version 7.4
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw | ConvertFrom-Json
$pixels = 0
foreach ($entry in $manifest.files) {
    $path = Join-Path $PSScriptRoot $entry.path.Replace('/', '\')
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Asset hash mismatch: $($entry.path)"
    }
    $image = [Drawing.Bitmap]::new($path)
    try {
        if ($image.Width -ne $entry.width -or $image.Height -ne $entry.height) {
            throw "Decoded dimensions mismatch: $($entry.path)"
        }
        if ($entry.path.EndsWith('.png')) {
            $opaque = 0
            $transparent = 0
            for ($y = 0; $y -lt $image.Height; $y++) {
                for ($x = 0; $x -lt $image.Width; $x++) {
                    $alpha = $image.GetPixel($x, $y).A
                    if ($alpha -eq 0) { $transparent++ }
                    elseif ($alpha -eq 255) { $opaque++ }
                    else { throw "Nonbinary alpha: $($entry.path) ($x,$y)" }
                }
            }
            if ($opaque -eq 0 -or $transparent -eq 0) { throw "Missing foreground or transparent area: $($entry.path)" }
            $pixels += $opaque + $transparent
        }
    } finally { $image.Dispose() }
}
foreach ($name in @('contact-sheet.png', 'scene-7x7.png', 'scene-7x7-frame1.png', 'scene-7x7-native.png')) {
    $image = [Drawing.Bitmap]::new((Join-Path $PSScriptRoot $name))
    try {
        $expectedWidth = if ($name -eq 'contact-sheet.png') { 768 } elseif ($name -eq 'scene-7x7-native.png') { 224 } else { 672 }
        $expectedHeight = if ($name -eq 'contact-sheet.png') { 672 } elseif ($name -eq 'scene-7x7-native.png') { 240 } else { 720 }
        if ($image.Width -ne $expectedWidth -or $image.Height -ne $expectedHeight) { throw "Preview dimensions: $name" }
    } finally { $image.Dispose() }
}
"PASS: OS decoded all $($manifest.files.Count) asset files; hashes, sizes and $pixels PNG alpha pixels verified; four previews decoded."
