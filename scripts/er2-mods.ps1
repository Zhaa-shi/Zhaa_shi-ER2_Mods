# ER2 Mod 启停切换器（交互式）
#
# 原理：BepInEx 启动时扫描 <Game>\BepInEx\plugins\ 下所有 *.dll 并加载。
#       "禁用" = 把该插件（及其配套资源目录）移到 plugins_disabled\；
#       "启用" = 移回 plugins\。
#       切换在游戏启动前完成，无需改任何游戏文件。
#
# 安全设计：
#   - 只移动「登记在册」的条目，绝不动 plugins\ 里的未知文件
#   - 先记录再移动；移动失败即回滚，不留半截状态
#   - ER2_ModManager 默认可被禁用，但脚本会警告（禁用后游戏内无法再用它）
#   - 全程不改动 DLL 内容（只移动位置），随时可手工还原
#
# 用法：pwsh -File scripts\er2-mods.ps1
#       pwsh -File scripts\er2-mods.ps1 -List
#       pwsh -File scripts\er2-mods.ps1 -Disable "ER2_RealisticBlood.dll","ER2_MeleeTweaks.dll"
#       pwsh -File scripts\er2-mods.ps1 -OnlyMine
#       pwsh -File scripts\er2-mods.ps1 -All
#       pwsh -File scripts\er2-mods.ps1 -Vanilla

[CmdletBinding()]
param(
    # 只列出状态，不进入交互
    [switch]$List,
    # 非交互式：禁用指定条目
    [string[]]$Disable,
    # 非交互式：启用指定条目
    [string[]]$Enable,
    # 全部启用
    [switch]$All,
    # 全部禁用（= 纯原版）
    [switch]$Vanilla,
    # 启用「自己的 mod」、禁用第三方
    [switch]$OnlyMine,
    # 游戏目录（默认读环境变量或 Steam 默认路径）
    [string]$GameDir
)

$ErrorActionPreference = "Stop"

# ── 游戏目录解析 ─────────────────────────────────────────────
if (-not $GameDir) {
    $GameDir = if ($env:ER2_GAME_DIR) { $env:ER2_GAME_DIR }
               else { "E:\SteamLibrary\steamapps\common\Easy Red 2" }
}
if (-not (Test-Path $GameDir)) {
    Write-Host "找不到游戏目录: $GameDir" -ForegroundColor Red
    Write-Host "请用 -GameDir 指定，或设置环境变量 ER2_GAME_DIR" -ForegroundColor Yellow
    exit 1
}

$pluginsDir  = Join-Path $GameDir "BepInEx\plugins"
$disabledDir = Join-Path $GameDir "BepInEx\plugins_disabled"
if (-not (Test-Path $pluginsDir)) {
    Write-Host "找不到插件目录: $pluginsDir" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $disabledDir)) {
    New-Item -ItemType Directory -Path $disabledDir -Force | Out-Null
}

# ── 归属清单：哪些是本人开发的 mod ───────────────────────────
# 用途：-OnlyMine 一键只启用自己的、关掉第三方。
# 新增自己的 mod 时在这里加一行即可。
$Mine = @(
    'ER2_AIFood.dll'
    'ER2_BattlefieldCommander.dll'          # SquadCommand / Easy Red Gate
    'ER2_CombatTweaks.dll'
    'ER2_InventoryPause.dll'
    'ER2_LimbTweaks.dll'
    'ER2_ModManager.dll'
    'ER2_MorePhysics_UnitCollision.dll'     # UnitCollision
    'ER2_NoInteractionHints_DoneProMaxEnd.dll'  # Hide Anything
    'ER2_ThrowableWheel.dll'
    'ER2_UniversalGeneration.dll'
    'ER2_VeteranHVT.dll'
    'ER2_WeatherControl.dll'
    'ER2_ZoomAnywhere.dll'
    'ER2_UnitInfoOverlay.dll'
)

# ── 资源包：BepInEx 不加载它们，但属于某个 mod 的资产 ─────────
# 从「纯原版」的彻底性考虑，一并纳入管理（会被一起移到 plugins_disabled\）。
# 若某天确认它属于某个仍在用的 mod，从这份清单里删掉即可。
$Bundles = @(
    'commandmarker'      # UnityFS AssetBundle，无插件引用（2026-09-26 核实）
)

# ── 配套资源目录：禁用插件时必须一起移动 ─────────────────────
# 形如 @{ '插件条目' = @('要一起移动的目录') }
$Companions = @{
    'ER2_VeteranHVT.dll' = @('ER2_VeteranHVT')
}

# ── 帮助函数 ─────────────────────────────────────────────────
function Get-PluginEntries {
    <# 返回当前 plugins\ 下的所有条目（含子目录），已排序 #>
    param([string]$Dir)
    if (-not (Test-Path $Dir)) { return @() }
    Get-ChildItem $Dir | Sort-Object Name
}

function Get-EntryKind {
    <# 判断条目类型：
         dir   = 自包含目录（内部有 dll，BepInEx 会加载）
         res   = 纯资源目录（无 dll；属于某插件，随其一起移动）
         dll   = 插件程序集
         bundle= Unity AssetBundle（UnityFS 头，BepInEx 不加载）
         other = 其它未识别文件 #>
    param($Item)
    if ($Item.PSIsContainer) {
        $hasDll = (Get-ChildItem $Item.FullName -Recurse -Filter *.dll -EA SilentlyContinue).Count -gt 0
        if ($hasDll) { return 'dir' } else { return 'res' }
    }
    if ($Item.Name -like '*.dll') { return 'dll' }
    # 读前 8 字节判断 UnityFS
    try {
        $fs = [System.IO.File]::OpenRead($Item.FullName)
        $buf = New-Object byte[] 8
        $null = $fs.Read($buf, 0, 8)
        $fs.Close()
        $magic = [System.Text.Encoding]::ASCII.GetString($buf)
        if ($magic -like 'UnityFS*') { return 'bundle' }
    } catch { }
    return 'other'
}

function Test-IsCompanion {
    <# 该条目是否是某个插件的配套资源目录（应随主插件移动，不单独列出） #>
    param([string]$Name)
    foreach ($k in $Companions.Keys) {
        if ($Companions[$k] -contains $Name) { return $true }
    }
    return $false
}

function Test-IsMine {
    param([string]$Name)
    return $Mine -contains $Name
}

function Get-Status {
    <# 汇总启用/禁用状态。列表与可切换项保持完全一致（都用 Get-TopLevelNames 过滤） #>
    $enabled  = @()
    $disabled = @()

    # 已启用的
    foreach ($name in (Get-TopLevelNames $pluginsDir)) {
        $it = Get-Item (Join-Path $pluginsDir $name)
        $enabled += [PSCustomObject]@{
            Name = $name
            Kind = Get-EntryKind $it
            Mine = Test-IsMine $name
        }
    }
    # 已禁用的
    foreach ($name in (Get-TopLevelNames $disabledDir)) {
        $it = Get-Item (Join-Path $disabledDir $name)
        $disabled += [PSCustomObject]@{
            Name = $name
            Kind = Get-EntryKind $it
            Mine = Test-IsMine $name
        }
    }
    return [PSCustomObject]@{ Enabled = $enabled; Disabled = $disabled }
}

function Get-TopLevelNames {
    <# 列出目录下的「顶层可切换项」名称。
        排除：配套资源目录（随主插件移动）、未登记为 $Bundles 的未知资源包。 #>
    param([string]$Dir)
    Get-PluginEntries $Dir | Where-Object {
        if (Test-IsCompanion $_.Name) { return $false }
        $k = Get-EntryKind $_
        if ($k -eq 'bundle') { return ($Bundles -contains $_.Name) }
        return $true
    } | ForEach-Object { $_.Name }
}

function Move-Entry {
    <# 把条目（含配套目录）在 plugins ⇄ plugins_disabled 之间移动。
       返回 $true/$false；失败时自动回滚已移动的部分。 #>
    param(
        [string]$Name,
        [string]$FromDir,
        [string]$ToDir
    )
    $moved = @()   # 已成功移动的（用于回滚）

    # 组成「要一起移动的一组」：条目本身 + 其配套目录
    $group = @($Name)
    if ($Companions.ContainsKey($Name)) { $group += $Companions[$Name] }

    try {
        foreach ($g in $group) {
            $src = Join-Path $FromDir $g
            $dst = Join-Path $ToDir $g
            if (-not (Test-Path $src)) {
                # 配套目录可能单独已被移动过，跳过而不是报错
                if ($g -eq $Name) { throw "源不存在: $src" }
                continue
            }
            if (Test-Path $dst) {
                throw "目标已存在（可能状态不一致，请手工检查）: $dst"
            }
            Move-Item -LiteralPath $src -Destination $dst -Force
            $moved += @{ From = $src; To = $dst }
        }
        return $true
    }
    catch {
        Write-Host ("    移动失败: " + $_.Exception.Message) -ForegroundColor Red
        # 回滚
        for ($i = $moved.Count - 1; $i -ge 0; $i--) {
            $m = $moved[$i]
            try { Move-Item -LiteralPath $m.To -Destination $m.From -Force } catch { }
        }
        if ($moved.Count -gt 0) { Write-Host "    已回滚 $($moved.Count) 个条目" -ForegroundColor Yellow }
        return $false
    }
}

function Disable-Entries {
    param([Parameter(Mandatory=$false)][AllowEmptyCollection()][string[]]$Names = @())
    $ok = 0; $fail = 0
    foreach ($n in $Names) {
        $src = Join-Path $pluginsDir $n
        if (-not (Test-Path $src)) { Write-Host "  跳过（未启用）: $n" -ForegroundColor DarkGray; continue }
        if (Move-Entry -Name $n -FromDir $pluginsDir -ToDir $disabledDir) {
            Write-Host "  已禁用: $n" -ForegroundColor Yellow
            $ok++
        } else { $fail++ }
    }
    return @{ Ok = $ok; Fail = $fail }
}

function Enable-Entries {
    param([Parameter(Mandatory=$false)][AllowEmptyCollection()][string[]]$Names = @())
    $ok = 0; $fail = 0
    foreach ($n in $Names) {
        $src = Join-Path $disabledDir $n
        if (-not (Test-Path $src)) { Write-Host "  跳过（未禁用）: $n" -ForegroundColor DarkGray; continue }
        if (Move-Entry -Name $n -FromDir $disabledDir -ToDir $pluginsDir) {
            Write-Host "  已启用: $n" -ForegroundColor Green
            $ok++
        } else { $fail++ }
    }
    return @{ Ok = $ok; Fail = $fail }
}

function Show-Status {
    param($Status)
    Write-Host ""
    Write-Host "════════ 当前插件状态 ════════" -ForegroundColor Cyan
    Write-Host ""
    # 统一编号：已启用在前、已禁用在后，编号连续，输入编号即切换该项
    $script:_index = @()
    $n = 0

    Write-Host ("  【已启用】 {0} 项" -f $Status.Enabled.Count) -ForegroundColor Green
    foreach ($e in $Status.Enabled) {
        $n++
        $script:_index += $e
        $tag = if ($e.Mine) { "我的" } else { "三方" }
        $kind = switch ($e.Kind) {
            'dll'    { '' }
            'dir'    { ' [目录]' }
            'bundle' { ' [资源包]' }
            default  { '' }
        }
        Write-Host ("   {0,2}. {1,-46} {2}{3}" -f $n, $e.Name, $tag, $kind)
    }
    Write-Host ""
    Write-Host ("  【已禁用】 {0} 项" -f $Status.Disabled.Count) -ForegroundColor DarkYellow
    foreach ($e in $Status.Disabled) {
        $n++
        $script:_index += $e
        $tag = if ($e.Mine) { "我的" } else { "三方" }
        Write-Host ("   {0,2}. {1,-46} {2}" -f $n, $e.Name, $tag) -ForegroundColor DarkGray
    }
    Write-Host ""
}

function Show-Menu {
    Write-Host "════════ 操作 ════════" -ForegroundColor Cyan
    Write-Host "  数字           切换该项（逗号分隔，如 1,3,5）"
    Write-Host "  a              全部启用"
    Write-Host "  n              全部禁用（= 纯原版）"
    Write-Host "  m              只启用「我的」，禁用「三方」"
    Write-Host "  t              只启用「三方」，禁用「我的」"
    Write-Host "  s / 回车       只显示状态，不改动"
    Write-Host "  q              退出"
    Write-Host ""
}

# ── 非交互式模式 ─────────────────────────────────────────────
if ($Vanilla) {
    Write-Host "→ 全部禁用（纯原版）" -ForegroundColor Cyan
    [array]$all = Get-TopLevelNames $pluginsDir
    $r = Disable-Entries -Names $all
    Write-Host ("`n完成: 禁用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
    exit 0
}
if ($All) {
    Write-Host "→ 全部启用" -ForegroundColor Cyan
    [array]$all = Get-TopLevelNames $disabledDir
    $r = Enable-Entries -Names $all
    Write-Host ("`n完成: 启用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
    exit 0
}
if ($OnlyMine) {
    Write-Host "→ 只启用「我的」，禁用「三方」" -ForegroundColor Cyan
    [array]$en = @(Get-TopLevelNames $pluginsDir)  | Where-Object { -not (Test-IsMine $_) }
    [array]$dis = @(Get-TopLevelNames $disabledDir) | Where-Object { Test-IsMine $_ }
    $r1 = Disable-Entries -Names $en
    $r2 = Enable-Entries -Names $dis
    Write-Host ("`n完成: 禁用 {0}，启用 {1}，失败 {2}" -f $r1.Ok, $r2.Ok, ($r1.Fail + $r2.Fail)) -ForegroundColor Cyan
    exit 0
}
if ($Disable) {
    $r = Disable-Entries -Names $Disable
    Write-Host ("`n完成: 禁用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
    exit 0
}
if ($Enable) {
    $r = Enable-Entries -Names $Enable
    Write-Host ("`n完成: 启用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
    exit 0
}

$status = Get-Status
if ($List) {
    Show-Status $status
    exit 0
}

# ── 交互式主循环 ─────────────────────────────────────────────
Write-Host ""
Write-Host "  ER2 Mod 启停切换器" -ForegroundColor White
Write-Host "  游戏: $GameDir" -ForegroundColor DarkGray
Write-Host "  禁用 = 移到 plugins_disabled\  （游戏启动前生效，需重启游戏）" -ForegroundColor DarkGray

while ($true) {
    $status = Get-Status
    Show-Status $status
    Show-Menu

    $ans = Read-Host "  选择"
    if ($null -eq $ans) { break }
    $ans = $ans.Trim()

    if ($ans -eq 'q') { break }
    if ($ans -eq '' -or $ans -eq 's') { continue }

    if ($ans -eq 'a') {
        $r = Enable-Entries  -Names @(Get-TopLevelNames $disabledDir)
        Write-Host ("`n完成: 启用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
        Start-Sleep -Seconds 1
        continue
    }
    if ($ans -eq 'n') {
        # 保护：禁用 ModManager 前提醒
        $willDisableMM = (Get-TopLevelNames $pluginsDir) -contains 'ER2_ModManager.dll'
        if ($willDisableMM) {
            Write-Host "`n  注意：ER2_ModManager.dll 也会被禁用 —— 游戏内将无法再用 MODS 页面。" -ForegroundColor Yellow
            $c = Read-Host "  继续？(y/N)"
            if ($c -notmatch '^[yY]') { continue }
        }
        $r = Disable-Entries -Names @(Get-TopLevelNames $pluginsDir)
        Write-Host ("`n完成: 禁用 {0} 项，失败 {1} 项" -f $r.Ok, $r.Fail) -ForegroundColor Cyan
        Start-Sleep -Seconds 1
        continue
    }
    if ($ans -eq 'm') {
        $en  = (Get-TopLevelNames $pluginsDir)  | Where-Object { -not (Test-IsMine $_) }
        $dis = (Get-TopLevelNames $disabledDir) | Where-Object { Test-IsMine $_ }
        $r1 = Disable-Entries -Names $en
        $r2 = Enable-Entries -Names $dis
        Write-Host ("`n完成: 禁用 {0}，启用 {1}" -f $r1.Ok, $r2.Ok) -ForegroundColor Cyan
        Start-Sleep -Seconds 1
        continue
    }
    if ($ans -eq 't') {
        $en  = (Get-TopLevelNames $pluginsDir)  | Where-Object { Test-IsMine $_ }
        $dis = (Get-TopLevelNames $disabledDir) | Where-Object { -not (Test-IsMine $_) }
        $r1 = Disable-Entries -Names $en
        $r2 = Enable-Entries -Names $dis
        Write-Host ("`n完成: 禁用 {0}，启用 {1}" -f $r1.Ok, $r2.Ok) -ForegroundColor Cyan
        Start-Sleep -Seconds 1
        continue
    }

    # 数字选择：按统一编号切换该项（已启用→禁用，已禁用→启用）
    $nums = $ans -split '[,\s]+' | Where-Object { $_ -match '^\d+$' } | ForEach-Object { [int]$_ }
    if ($nums.Count -eq 0) {
        Write-Host "`n  无法识别的输入: $ans" -ForegroundColor Red
        Start-Sleep -Seconds 1
        continue
    }

    $total = $script:_index.Count
    $enabledNames  = @($status.Enabled  | ForEach-Object { $_.Name })
    $toDisable = @()
    $toEnable  = @()
    foreach ($n in $nums) {
        if ($n -lt 1 -or $n -gt $total) {
            Write-Host "  编号 $n 超出范围（1-$total）" -ForegroundColor Red
            continue
        }
        $entry = $script:_index[$n - 1]
        if ($enabledNames -contains $entry.Name) { $toDisable += $entry.Name }
        else { $toEnable += $entry.Name }
    }

    # 保护：禁用 ModManager 前提醒
    if ($toDisable -contains 'ER2_ModManager.dll') {
        Write-Host "`n  注意：ER2_ModManager.dll 将被禁用 —— 游戏内将无法再用 MODS 页面。" -ForegroundColor Yellow
        $c = Read-Host "  继续？(y/N)"
        if ($c -notmatch '^[yY]') { continue }
    }

    $r1 = @{ Ok = 0; Fail = 0 }; $r2 = @{ Ok = 0; Fail = 0 }
    if ($toDisable.Count -gt 0) { $r1 = Disable-Entries -Names $toDisable }
    if ($toEnable.Count  -gt 0) { $r2 = Enable-Entries  -Names $toEnable  }
    if ($toDisable.Count -gt 0 -or $toEnable.Count -gt 0) {
        Write-Host ("`n完成: 禁用 {0}，启用 {1}，失败 {2}" -f $r1.Ok, $r2.Ok, ($r1.Fail + $r2.Fail)) -ForegroundColor Cyan
        Start-Sleep -Seconds 1
    }
}

Write-Host "`n已退出。" -ForegroundColor DarkGray
