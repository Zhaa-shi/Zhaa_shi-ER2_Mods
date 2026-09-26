# 把纯文本 mod 的 README.txt 机械转换为 README.md（GitHub 可渲染）
# 原则：只做格式转换，绝不改写/精简/翻译任何说明文字。
#       原 README.txt 保持不变（build.ps1 打包依赖它）。
#
# 安全策略：只处理「纯文本」README —— 若原文已含 Markdown 语法（反引号或 **粗体**），
#           说明作者已按 Markdown 书写，任何机械转换都可能破坏内容，一律跳过并报告。
#
# 注意：本脚本含中文，必须以 UTF-8 BOM 保存，否则 PS 5.1 会按 GBK 读取而报错。

param(
    [string[]]$Only,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

function Convert-ReadmeToMd {
    param([string]$Text, [string]$ModName)

    $lines = $Text -split "`r?`n"
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    $n = $lines.Count

    while ($i -lt $n -and $lines[$i].Trim() -eq "") { $i++ }
    if ($i -ge $n) { return "# $ModName`n" }

    $title = $lines[$i].Trim()
    $i++
    if ($i -lt $n -and $lines[$i] -match '^\s*[=]{3,}\s*$') { $i++ }

    $titleNoVer = $title -replace '\s+v?\d+\.\d+(\.\d+)*\s*$', ''
    if ($titleNoVer.Trim() -eq "") { $titleNoVer = $title }

    $out.Add("# " + $titleNoVer.Trim())
    $out.Add("")

    while ($i -lt $n -and $lines[$i].Trim() -eq "") { $i++ }
    if ($i -lt $n -and $lines[$i] -match '^\s*(Version|v)\s+[\d.]') {
        $out.Add("*" + $lines[$i].Trim() + "*")
        $out.Add("")
        $i++
    }
    $out.Add("> Part of the [ER2 Mods](../README.md) collection.")
    $out.Add("")

    $inCode = $false
    while ($i -lt $n) {
        $line = $lines[$i]
        $trimmed = $line.TrimEnd()

        if ($trimmed -match '^\s*```') {
            $inCode = -not $inCode
            $out.Add($trimmed); $i++; continue
        }
        if ($inCode) { $out.Add($line); $i++; continue }

        # 分节标题：本行非空 + 下一行是 ---/=== 下划线
        if ($trimmed.Trim() -ne "" -and ($i + 1) -lt $n -and
            $lines[$i + 1] -match '^\s*[-=]{3,}\s*$') {

            $head = $trimmed.Trim()
            $level = if ($lines[$i + 1] -match '=') { "#" } else { "##" }

            if ($head -cmatch '^[^a-z]*$' -and $head -match '[A-Z]') {
                $words = $head -split '\s+'
                $keepLower = @('AND','OR','OF','THE','IN','ON','TO','FOR','A','AN','AT','BY','WITH')
                $tc = @()
                for ($w = 0; $w -lt $words.Count; $w++) {
                    $word = $words[$w]
                    if ($keepLower -contains $word -and $w -gt 0) {
                        $tc += $word.ToLower()
                    } elseif ($word -match '^[A-Za-z]') {
                        $tc += $word.Substring(0,1).ToUpper() + $word.Substring(1).ToLower()
                    } else {
                        $tc += $word
                    }
                }
                $head = ($tc -join ' ')
            }

            $out.Add("")
            $out.Add("$level $head")
            $out.Add("")
            $i += 2
            continue
        }

        # 配置节名 [General]
        if ($trimmed -match '^\s*\[[A-Za-z][A-Za-z0-9_.\-]*\]\s*$') {
            $out.Add("")
            $out.Add("**" + $trimmed.Trim() + "**")
            $out.Add("")
            $i++
            continue
        }

        # 配置项（多空格分隔）
        if ($trimmed -match '^\s{2,}([A-Za-z][A-Za-z0-9_.]*)\s{2,}(\S.*)$') {
            $key = $Matches[1]
            $desc = $Matches[2].Trim()
            $i++
            while ($i -lt $n -and $lines[$i] -match '^\s{3,}\S' -and
                   $lines[$i] -notmatch '^\s{2,}[A-Za-z][A-Za-z0-9_.]*\s{2,}\S') {
                $desc += " " + $lines[$i].Trim()
                $i++
            }
            $out.Add("- ``" + $key + "`` — " + $desc)
            continue
        }

        # 配置项（冒号分隔）：仅限缩进形式，且排除盘符路径
        if ($trimmed -match '^\s{2,}([A-Za-z][A-Za-z0-9_.]*)\s*:\s+(\S.*)$' -and
            $trimmed -notmatch '`' -and
            $trimmed -notmatch '^\s*[A-Za-z]:[\\/]') {
            $ck = $Matches[1]
            $cd = $Matches[2].Trim()
            $i++
            while ($i -lt $n -and $lines[$i] -match '^\s{3,}\S' -and
                   $lines[$i] -notmatch '^\s*[A-Za-z][A-Za-z0-9_.]*\s*:\s+\S') {
                $cd += " " + $lines[$i].Trim()
                $i++
            }
            $out.Add("- ``" + $ck + "`` — " + $cd)
            continue
        }

        # 列表项
        if ($trimmed -match '^(\s*)[-*]\s+(\S.*)$') {
            $out.Add($Matches[1] + "- " + $Matches[2])
            $i++
            continue
        }

        # 数字列表
        if ($trimmed -match '^\s*\d+\.\s+\S') {
            $out.Add(($trimmed -replace '^\s*', ''))
            $i++
            continue
        }

        # 普通段落：把 <...> 路径包进反引号，防止被当 HTML 吞掉
        $safe = $trimmed
        if ($safe -match '<[^>]*>' -and $safe -notmatch '`') {
            $safe = [regex]::Replace($safe, '(<[^>]*>\S*)', '`$1`')
        }
        $out.Add($safe)
        $i++
    }

    $result = New-Object System.Collections.Generic.List[string]
    $prevBlank = $false
    foreach ($l in $out) {
        $isBlank = ($l.Trim() -eq "")
        if ($isBlank -and $prevBlank) { continue }
        $result.Add($l)
        $prevBlank = $isBlank
    }
    while ($result.Count -gt 0 -and $result[$result.Count-1].Trim() -eq "") {
        $result.RemoveAt($result.Count-1)
    }
    return ($result -join "`n") + "`n"
}

$mods = Get-ChildItem -Directory -Path $root | Where-Object {
    (Test-Path (Join-Path $_.FullName "README.txt")) -and
    (Test-Path (Join-Path $_.FullName "*.csproj"))
}
if ($Only) { $mods = $mods | Where-Object { $Only -contains $_.Name } }

Write-Host ("扫描 {0} 个 mod`n" -f $mods.Count)
$done = @(); $skipped = @()
foreach ($m in $mods) {
    $src = Join-Path $m.FullName "README.txt"
    $dst = Join-Path $m.FullName "README.md"
    $raw = [System.IO.File]::ReadAllText($src, [System.Text.Encoding]::UTF8)

    # 安全闸门：原文已含 Markdown 语法则跳过
    if ($raw -match '`' -or $raw -match '\*\*') {
        $skipped += $m.Name
        continue
    }

    $md = Convert-ReadmeToMd -Text $raw -ModName $m.Name
    $srcLines = ($raw -split "`r?`n" | Where-Object { $_.Trim() -ne "" }).Count
    $mdLines  = ($md  -split "`r?`n" | Where-Object { $_.Trim() -ne "" }).Count

    if (-not $DryRun) {
        [System.IO.File]::WriteAllText($dst, $md, (New-Object System.Text.UTF8Encoding($false)))
    }
    $done += [PSCustomObject]@{
        Mod = $m.Name; 原文行 = $srcLines; 转换后行 = $mdLines
        比 = [math]::Round($mdLines / [Math]::Max($srcLines,1), 2)
    }
}
Write-Host "已转换:"
$done | Format-Table -AutoSize
Write-Host ("跳过（原文已含 Markdown 语法，需人工处理）: {0}" -f ($skipped -join ', '))
