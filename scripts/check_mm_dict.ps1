# Extract ModManager dictionaries and report coverage gaps (ASCII only - PS5.1 GBK pitfall)
$repoRoot = Split-Path -Parent $PSScriptRoot
$src = [System.IO.File]::ReadAllText((Join-Path $repoRoot "ModManager\Plugin.cs"))
function Extract-Dict([string]$name) {
    $m = [regex]::Match($src, 'Dictionary<string, string> ' + $name + ' = new Dictionary<string, string>\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?<body>.*?)\n\s*\};', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) { return @{} }
    $dict = @{}
    foreach ($e in [regex]::Matches($m.Groups['body'].Value, '\{\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\}')) {
        $dict[$e.Groups[1].Value] = $e.Groups[2].Value
    }
    return $dict
}
$keys = Extract-Dict "keys"
$desc = Extract-Dict "descriptions"
$mods = Extract-Dict "modNames"
$sects = Extract-Dict "sections"
Write-Host ("keys=" + $keys.Count + " desc=" + $desc.Count + " mods=" + $mods.Count + " sects=" + $sects.Count)
Write-Host "=== keys without descriptions ==="
foreach ($k in ($keys.Keys | Sort-Object)) { if (-not $desc.ContainsKey($k)) { Write-Host ("  " + $k) } }
Write-Host "=== duplicate keys (OrdinalIgnoreCase) ==="
$all = @{}; $dup = @()
foreach ($d in @($keys, $desc, $mods, $sects)) { foreach ($k in $d.Keys) { $lk = $k.ToLowerInvariant(); if ($all.ContainsKey($lk)) { $dup += $k } else { $all[$lk] = $true } } }
if ($dup.Count -eq 0) { Write-Host "  none" } else { $dup | ForEach-Object { Write-Host ("  " + $_) } }
Write-Host "=== installed cfg keys missing from keys dict ==="
$cfgDir = Join-Path $(if ($env:ER2_GAME_DIR) { $env:ER2_GAME_DIR } else { "E:\SteamLibrary\steamapps\common\Easy Red 2" }) "BepInEx\config"
if (-not (Test-Path $cfgDir)) { Write-Host "  (config dir not found, skipped): $cfgDir"; return }
Get-ChildItem $cfgDir -Filter *.cfg | ForEach-Object {
    $missing = @()
    foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
        if ($line -match '^\s*([A-Za-z0-9_.\-]+)\s*=') { $k = $Matches[1]; if (-not $keys.ContainsKey($k)) { $missing += $k } }
    }
    if ($missing.Count -gt 0) { Write-Host ("  " + $_.Name + " missing: " + ($missing -join ", ")) }
}
Write-Host "=== installed mod display names vs modNames dict ==="
foreach ($n in @("Easy Red 2 World HUD","ER2_Fire_Coaxial_Spacebar","Easy Red 2 Melee Tweaks","ER2 Realistic Effects","Easy Red 2 Aim Over Cover","Push It To The Limit","Realistic Blood","Easy Red 2 FPS Body Shadows Fix","Easy Red 2 Death Screen Effect","Decals & Shells")) {
    if (-not $mods.ContainsKey($n)) { Write-Host ("  missing: " + $n) }
}