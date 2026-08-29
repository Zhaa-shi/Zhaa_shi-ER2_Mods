# Per-dictionary duplicate check (OrdinalIgnoreCase) - ASCII only
$src = [System.IO.File]::ReadAllText("D:\Users\71011\Documents\ER2_Mods\ModManager\Plugin.cs")
function Extract-Dict([string]$name) {
    $m = [regex]::Match($src, 'Dictionary<string, string> ' + $name + ' = new Dictionary<string, string>\(StringComparer\.OrdinalIgnoreCase\)\s*\{(?<body>.*?)\n\s*\};', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $m.Success) { return @{} }
    $dict = @{}
    foreach ($e in [regex]::Matches($m.Groups['body'].Value, '\{\s*"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\}')) {
        $dict[$e.Groups[1].Value] = $e.Groups[2].Value
    }
    return $dict
}
$ok = $true
foreach ($dn in @("modNames", "keys", "descriptions", "sections")) {
    $d = Extract-Dict $dn
    $seen = @{}
    foreach ($k in $d.Keys) {
        $lk = $k.ToLowerInvariant()
        if ($seen.ContainsKey($lk)) { Write-Host ("DUP in " + $dn + ": " + $k); $ok = $false }
        else { $seen[$lk] = $true }
    }
    Write-Host ($dn + ": " + $d.Count + " entries, internal dup check done")
}
if ($ok) { Write-Host "ALL DICTS OK - no internal duplicates" } else { Write-Host "DUPLICATES FOUND" }
