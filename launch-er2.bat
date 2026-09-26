@echo off
REM ================================================================
REM  ER2 Mod Launcher - single file, double-click to run
REM
REM  Structure: the batch header below sets up UTF-8 console, then
REM  hands the rest of THIS FILE to PowerShell as a script. All the
REM  real logic lives in the PowerShell block at the bottom.
REM
REM  The file is saved as UTF-8 with BOM so PowerShell 5.1 reads the
REM  Chinese text correctly. The batch part is pure ASCII, and
REM  "chcp 65001" makes the console render the Chinese properly.
REM ================================================================

setlocal
chcp 65001 >nul 2>&1

REM Strip this batch header and run the remainder as a PowerShell script.
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$self='%~f0';" ^
  "$lines=[IO.File]::ReadAllLines($self,[Text.Encoding]::UTF8);" ^
  "$i=[Array]::IndexOf($lines,'###PS1###');" ^
  "if($i -lt 0){Write-Host 'launcher payload not found';exit 1};" ^
  "$body=$lines[($i+1)..($lines.Count-1)] -join [Environment]::NewLine;" ^
  "$tmp=[IO.Path]::Combine($env:TEMP,'er2-launcher-run.ps1');" ^
  "[IO.File]::WriteAllText($tmp,$body,(New-Object Text.UTF8Encoding($true)));" ^
  "& $tmp -Action '%~1'; exit $LASTEXITCODE"

exit /b %ERRORLEVEL%

###PS1###
param([string]$Action)
$ErrorActionPreference = "Stop"

# ════════════════════════════════════════════════════════════════
#  ER2 Mod 启停切换器
#
#  三个主选项：
#    1. 带 mod 玩          启用全部插件
#    2. 无 mod 玩（纯净）  禁用全部插件 + 关闭 BepInEx 注入器
#    3. 恢复              重新开启 BepInEx（回到可玩 mod 的状态）
#
#  原理：BepInEx 启动时扫描 BepInEx\plugins\ 下所有 *.dll 并加载，
#        没有内置开关。"禁用" = 把插件移到 plugins_disabled\。
#
#        游戏另会检测游戏根目录下的 winhttp.dll（BepInEx 注入器），
#        只要它在就弹「检测到非官方修改」——与加载了哪些插件无关。
#        "纯净"档位会把它连同 doorstop 配置一起移到 bepinex_off\。
# ════════════════════════════════════════════════════════════════

# 命令行动作（bat 传参时用）：mods / pure / restore

# ── 游戏目录 ─────────────────────────────────────────────────────
$GameDir = if ($env:ER2_GAME_DIR) { $env:ER2_GAME_DIR }
           else { "E:\SteamLibrary\steamapps\common\Easy Red 2" }

if (-not (Test-Path $GameDir)) {
    Write-Host ""
    Write-Host "  找不到游戏目录: $GameDir" -ForegroundColor Red
    Write-Host "  请设置环境变量 ER2_GAME_DIR 指向游戏目录。" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "  按回车退出"
    exit 1
}

$pluginsDir  = Join-Path $GameDir "BepInEx\plugins"
$disabledDir = Join-Path $GameDir "BepInEx\plugins_disabled"
$bepRootDir  = Join-Path $GameDir "BepInEx"
$bepOffDir   = Join-Path $GameDir "bepinex_off"
$exePath     = Join-Path $GameDir "Easy Red 2.exe"

# 「纯净」档位要移走的东西。
#
# 依据（2026-09-26 实测 Player.log 取证）：
#   游戏内置 IntegrityGuard，日志打出
#     [IntegrityGuard] BepInEx/Doorstop rilevato (early): .../BepInEx/core
#   即它检测的是 **BepInEx 目录**（core / plugins 等）的存在，
#   而不是（或不只是）winhttp.dll —— 只移 winhttp.dll 仍会弹提示。
#
# 所以「纯净」= 移走下列全部：
$InjectorFiles = @('winhttp.dll', 'doorstop_config.ini', '.doorstop_version')  # 游戏根目录下的注入器
$BepInExDirName = 'BepInEx'   # 整个 BepInEx 目录（约 83 MB，同盘移动是瞬间的）
$DotnetDirName  = 'dotnet'    # BepInEx 6 的 CoreCLR 运行时（doorstop 的 coreclr_path）

# 配套资源目录：禁用插件时必须一起移动
$Companions = @{
    'ER2_VeteranHVT.dll' = @('ER2_VeteranHVT')
}

# 「我的 mod」清单：高级选项「只留我的」靠它区分
$mineList = @(
    'ER2_AIFood.dll'
    'ER2_BattlefieldCommander.dll'              # SquadCommand / Easy Red Gate
    'ER2_CombatTweaks.dll'
    'ER2_InventoryPause.dll'
    'ER2_LimbTweaks.dll'
    'ER2_ModManager.dll'
    'ER2_MorePhysics_UnitCollision.dll'         # UnitCollision
    'ER2_NoInteractionHints_DoneProMaxEnd.dll'  # Hide Anything
    'ER2_ThrowableWheel.dll'
    'ER2_UniversalGeneration.dll'
    'ER2_VeteranHVT.dll'
    'ER2_WeatherControl.dll'
    'ER2_ZoomAnywhere.dll'
    'ER2_UnitInfoOverlay.dll'
)

# 注意：处于「纯净」状态时整个 BepInEx\ 都在 bepinex_off\ 里，
# 此时 $pluginsDir 不存在是正常的 —— 要做的是提示用户恢复，而不是报错退出。
#
# 但 -Action restore 必须能穿过这里（它的职责就是把 BepInEx 搬回来），
# 所以仅在没有指定 Action 时报错。
$bepOffHasBepInEx = Test-Path (Join-Path $bepOffDir $BepInExDirName)
if (-not (Test-Path $pluginsDir)) {
    if ($Action -and $Action.ToLower() -eq 'restore') {
        # 交给后面的 restore 分支处理，这里直接放行
    }
    elseif ($bepOffHasBepInEx) {
        Write-Host ""
        Write-Host "  当前是「纯净」状态（BepInEx 在 bepinex_off\ 里暂存）。" -ForegroundColor Magenta
        Write-Host ""
        Write-Host "  要恢复 mod 功能，请重新运行本启动器并选「3. 恢复」，" -ForegroundColor Yellow
        Write-Host "  或运行: launch-er2.bat restore" -ForegroundColor Yellow
        Write-Host ""
        Read-Host "  按回车退出"
        exit 0
    }
    else {
        Write-Host ""
        Write-Host "  找不到插件目录: $pluginsDir" -ForegroundColor Red
        Write-Host "  这台机器上似乎没装 BepInEx。" -ForegroundColor Yellow
        Write-Host ""
        Read-Host "  按回车退出"
        exit 1
    }
}
if ((-not (Test-Path $disabledDir)) -and (Test-Path $bepRootDir)) {
    New-Item -ItemType Directory -Path $disabledDir -Force -ErrorAction SilentlyContinue | Out-Null
}

# ════════════════════════════════════════════════════════════════
#  基础工具
# ════════════════════════════════════════════════════════════════

function Get-Entries {
    param([string]$Dir)
    if (-not (Test-Path $Dir)) { return @() }
    Get-ChildItem $Dir | Sort-Object Name
}

function Test-IsCompanion {
    param([string]$Name)
    foreach ($k in $Companions.Keys) {
        if ($Companions[$k] -contains $Name) { return $true }
    }
    return $false
}

function Get-TopLevelNames {
    <# 可切换的顶层项（排除配套资源目录与未知资源包） #>
    param([string]$Dir)
    Get-Entries $Dir | Where-Object { -not (Test-IsCompanion $_.Name) } |
        ForEach-Object { $_.Name }
}

function Move-Entry {
    <# 移动条目（含配套目录）。失败自动回滚。 #>
    param([string]$Name, [string]$FromDir, [string]$ToDir)
    $moved = @()
    $group = @($Name)
    if ($Companions.ContainsKey($Name)) { $group += $Companions[$Name] }
    try {
        foreach ($g in $group) {
            $src = Join-Path $FromDir $g
            $dst = Join-Path $ToDir $g
            if (-not (Test-Path $src)) {
                if ($g -eq $Name) { throw "源不存在: $src" }
                continue
            }
            if (Test-Path $dst) {
                # 目标可能是上次失败留下的空目录 —— 空的话直接删掉，不算冲突
                $isEmptyDir = (Test-Path $dst -PathType Container) -and
                              (@(Get-ChildItem $dst -Recurse -Force -ErrorAction SilentlyContinue).Count -eq 0)
                if ($isEmptyDir) { Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue }
            }
            if (Test-Path $dst) { throw "目标已存在: $dst" }
            Move-Item -LiteralPath $src -Destination $dst -Force
            $moved += @{ From = $src; To = $dst }
        }
        return $true
    }
    catch {
        Write-Host ("      失败: " + $_.Exception.Message) -ForegroundColor Red
        for ($i = $moved.Count - 1; $i -ge 0; $i--) {
            try { Move-Item -LiteralPath $moved[$i].To -Destination $moved[$i].From -Force } catch { }
        }
        return $false
    }
}

function Set-AllMods {
    <# enabled=$true 启用全部；$false 禁用全部 #>
    param([bool]$Enabled)
    if ($Enabled) {
        $from = $disabledDir; $to = $pluginsDir
        [array]$names = Get-TopLevelNames $disabledDir
    } else {
        $from = $pluginsDir;  $to = $disabledDir
        [array]$names = Get-TopLevelNames $pluginsDir
    }
    $ok = 0; $fail = 0
    foreach ($n in $names) {
        $src = Join-Path $from $n
        if (-not (Test-Path $src)) { continue }
        if (Move-Entry -Name $n -FromDir $from -ToDir $to) { $ok++ } else { $fail++ }
    }
    return @{ Ok = $ok; Fail = $fail }
}

# ════════════════════════════════════════════════════════════════
#  BepInEx 注入器开关
# ════════════════════════════════════════════════════════════════

function Test-BepInExActive {
    <# BepInEx 是否"在场"。
       判据用 winhttp.dll 与 BepInEx 目录两者：
       只要游戏还能看到任何一样，IntegrityGuard 就会报「已修改」。 #>
    $hasDll = Test-Path (Join-Path $GameDir 'winhttp.dll')
    $hasDir = Test-Path $bepRootDir
    $hasDotnet = Test-Path (Join-Path $GameDir $DotnetDirName)
    return ($hasDll -or $hasDir -or $hasDotnet)
}

function Set-BepInEx {
    <# $true 恢复；$false 关闭（游戏不再弹「已修改」提示）。
       关闭时移走：注入器 3 个文件 + BepInEx\ + dotnet\。
       恢复时全部搬回。同盘移动是元数据操作，瞬间完成。

       注意：这里**不做"已是某状态"的早退判断** —— 因为可能出现
       部分状态（例：BepInEx 已移走但 dotnet 还在），早退会漏搬。
       搬运时源不存在的条目自动跳过，所以幂等。 #>
    param([bool]$Enable)

    if (-not (Test-Path $bepOffDir)) {
        if ($Enable) {
            Write-Host ""
            Write-Host "  找不到 bepinex_off\ 备份，无法自动恢复。" -ForegroundColor Red
            Write-Host "  请手工把 BepInEx 目录与 winhttp.dll / doorstop_config.ini /" -ForegroundColor Yellow
            Write-Host "  .doorstop_version 放回游戏目录。" -ForegroundColor Yellow
            return $false
        }
        New-Item -ItemType Directory -Path $bepOffDir -Force | Out-Null
    }

    # 要搬运的条目：3 个注入器文件 + BepInEx 目录 + dotnet 目录
    $items = @($InjectorFiles) + @($BepInExDirName) + @($DotnetDirName)

    if ($Enable) { $from = $bepOffDir; $to = $GameDir }
    else         { $from = $GameDir;   $to = $bepOffDir }

    $moved = @()
    try {
        foreach ($f in $items) {
            $src = Join-Path $from $f
            $dst = Join-Path $to $f
            if (-not (Test-Path $src)) { continue }
            if (Test-Path $dst) {
                # 目标可能是上次失败留下的空目录 —— 空的话直接删掉，不算冲突
                $isEmptyDir = (Test-Path $dst -PathType Container) -and
                              (@(Get-ChildItem $dst -Recurse -Force -ErrorAction SilentlyContinue).Count -eq 0)
                if ($isEmptyDir) { Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue }
            }
            if (Test-Path $dst) {
                # 目标已存在：关闭时覆盖旧备份，恢复时不该发生
                if (-not $Enable) { Remove-Item $dst -Recurse -Force -ErrorAction Stop }
                else { throw "目标已存在，拒绝覆盖: $dst" }
            }
            if ($f -eq $BepInExDirName -or $f -eq $DotnetDirName) {
                Write-Host "    正在移动 BepInEx 目录（约 83 MB，同盘瞬间完成）..." -ForegroundColor DarkGray
            }
            Move-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop
            $moved += @{ From = $src; To = $dst; Name = $f }
            if ($f -ne $BepInExDirName) { Write-Host "    移走: $f" -ForegroundColor DarkGray }
        }
    }
    catch {
        Write-Host ("  移动失败: " + $_.Exception.Message) -ForegroundColor Red
        for ($i = $moved.Count - 1; $i -ge 0; $i--) {
            try { Move-Item -LiteralPath $moved[$i].To -Destination $moved[$i].From -Force } catch { }
        }
        Write-Host "  已回滚，状态未改变。" -ForegroundColor Yellow
        return $false
    }

    # 校验：目标状态必须真的达成，否则回滚（防止 BepInEx 半死导致 mod 全废）
    $nowActive = Test-BepInExActive
    if ($nowActive -ne $Enable) {
        Write-Host "  校验失败（状态未达成），正在回滚..." -ForegroundColor Red
        for ($i = $moved.Count - 1; $i -ge 0; $i--) {
            try { Move-Item -LiteralPath $moved[$i].To -Destination $moved[$i].From -Force } catch { }
        }
        return $false
    }
    return $true
}

# ════════════════════════════════════════════════════════════════
#  状态显示
# ════════════════════════════════════════════════════════════════

function Get-Counts {
    $on  = @(Get-TopLevelNames $pluginsDir).Count
    $off = @(Get-TopLevelNames $disabledDir).Count
    return @{ On = $on; Off = $off; BepInEx = (Test-BepInExActive) }
}

function Show-Header {
    $c = Get-Counts
    Write-Host ""
    Write-Host "  ╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "  ║           ER2 Mod 启动器                         ║" -ForegroundColor Cyan
    Write-Host "  ╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""
    Write-Host ("     已启用 mod：{0} 个      已禁用：{1} 个" -f $c.On, $c.Off) -ForegroundColor Gray
    if ($c.BepInEx) {
        Write-Host "     BepInEx：开启（游戏会提示「检测到非官方修改」）" -ForegroundColor Gray
    } else {
        Write-Host "     BepInEx：关闭（完全纯净）" -ForegroundColor Magenta
    }
    Write-Host ""
}

function Show-MainMenu {
    Write-Host "  ──────────────────────────────────────────────────" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    1.  带 mod 玩" -ForegroundColor Green
    Write-Host "        启用全部插件，正常启动游戏" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    2.  无 mod 玩（纯净）" -ForegroundColor Magenta
    Write-Host "        禁用全部插件并关闭 BepInEx，不弹「已修改」提示" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    3.  恢复" -ForegroundColor Yellow
    Write-Host "        重新开启 BepInEx（不启动游戏）" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "    d.  高级选项" -ForegroundColor DarkGray
    Write-Host "    q.  退出" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "  ──────────────────────────────────────────────────" -ForegroundColor DarkGray
}

function Show-AdvancedMenu {
    $c = Get-Counts
    Write-Host ""
    Write-Host "  ──────────── 高级选项 ────────────" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "    1.  只启用「我的 mod」，禁用第三方"
    Write-Host "    2.  只启用「第三方」，禁用我的"
    Write-Host "    3.  查看完整插件清单"
    Write-Host "    4.  不改动，直接启动游戏"
    Write-Host "    b.  返回主菜单"
    Write-Host ""
}

function Show-PluginList {
    Write-Host ""
    Write-Host "  ══════ 插件清单 ══════" -ForegroundColor Cyan
    Write-Host ""
    $n = 0
    Write-Host "  【已启用】" -ForegroundColor Green
    foreach ($name in (Get-TopLevelNames $pluginsDir)) {
        $n++
        $tag = if ($mineList -contains $name) { "我" } else { "三" }
        Write-Host ("   {0,2}. [{1}] {2}" -f $n, $tag, $name)
    }
    if ($n -eq 0) { Write-Host "    （无）" -ForegroundColor DarkGray }
    Write-Host ""
    Write-Host "  【已禁用】" -ForegroundColor DarkYellow
    foreach ($name in (Get-TopLevelNames $disabledDir)) {
        $n++
        $tag = if ($mineList -contains $name) { "我" } else { "三" }
        Write-Host ("   {0,2}. [{1}] {2}" -f $n, $tag, $name) -ForegroundColor DarkGray
    }
    Write-Host ""
    Write-Host "  （[我] = 你自己开发的 mod，[三] = 第三方）" -ForegroundColor DarkGray
    Write-Host ""
}

# ════════════════════════════════════════════════════════════════
#  动作
# ════════════════════════════════════════════════════════════════

function Start-Game {
    if (-not (Test-Path $exePath)) {
        Write-Host ""
        Write-Host "  找不到游戏程序: $exePath" -ForegroundColor Red
        return
    }
    Write-Host ""
    Write-Host "  正在启动游戏..." -ForegroundColor Cyan
    Start-Process -FilePath $exePath
}

function Invoke-WithMods {
    Write-Host ""
    Write-Host "  [1/2] 启用全部插件" -ForegroundColor White
    $r = Set-AllMods -Enabled $true
    Write-Host ("        启用 {0} 个，失败 {1} 个" -f $r.Ok, $r.Fail) -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [2/2] 开启 BepInEx 注入器" -ForegroundColor White
    [void](Set-BepInEx -Enable $true)
    Start-Game
}

function Invoke-Pure {
    Write-Host ""
    Write-Host "  [1/2] 禁用全部插件" -ForegroundColor White
    $r = Set-AllMods -Enabled $false
    Write-Host ("        禁用 {0} 个，失败 {1} 个" -f $r.Ok, $r.Fail) -ForegroundColor Gray
    Write-Host ""
    Write-Host "  [2/2] 关闭 BepInEx 注入器" -ForegroundColor White
    $ok = Set-BepInEx -Enable $false
    if ($ok) {
        Write-Host ""
        Write-Host "  游戏将以完全纯净状态启动（不再弹「已修改」提示）" -ForegroundColor Green
        Write-Host "  想恢复 mod，重新运行本启动器选「3. 恢复」" -ForegroundColor DarkGray
    } else {
        Write-Host ""
        Write-Host "  插件已禁用，但注入器关闭失败 —— 仍会弹提示。" -ForegroundColor Yellow
    }
    Start-Game
}

function Invoke-Restore {
    Write-Host ""
    Write-Host "  正在恢复 BepInEx 注入器..." -ForegroundColor White
    $ok = Set-BepInEx -Enable $true
    Write-Host ""
    if ($ok) {
        Write-Host "  已恢复。mod 功能可用。" -ForegroundColor Green
        Write-Host "  （插件是否启用由「带 mod 玩」控制）" -ForegroundColor DarkGray
    } else {
        Write-Host "  恢复失败，请检查游戏目录。" -ForegroundColor Red
    }
}

# ════════════════════════════════════════════════════════════════
#  入口
# ════════════════════════════════════════════════════════════════

# 支持命令行直接指定动作（bat 传参或直接跑 ps1）
if ($Action) {
    switch ($Action.ToLower()) {
        'mods'    { Invoke-WithMods; exit 0 }
        'pure'    { Invoke-Pure;     exit 0 }
        'restore' { Invoke-Restore;  exit 0 }
    }
}

# 交互菜单
while ($true) {
    Show-Header
    Show-MainMenu
    $ans = Read-Host "  请选择"
    if ($null -eq $ans) { break }
    $ans = $ans.Trim().ToLower()

    switch ($ans) {
        '1' { Invoke-WithMods; break }
        '2' { Invoke-Pure;     break }
        '3' { Invoke-Restore;  Write-Host ""; Read-Host "  按回车返回"; continue }
        'd' {
            while ($true) {
                Show-Header
                Show-AdvancedMenu
                $a2 = Read-Host "  请选择"
                if ($null -eq $a2) { break }
                $a2 = $a2.Trim().ToLower()
                if ($a2 -eq 'b' -or $a2 -eq '') { break }
                if ($a2 -eq '1') {
                    Write-Host ""
                    Write-Host "  正在只保留「我的 mod」..." -ForegroundColor White
                    [array]$off = Get-TopLevelNames $pluginsDir  | Where-Object { $mineList -notcontains $_ }
                    [array]$on  = Get-TopLevelNames $disabledDir | Where-Object { $mineList -contains $_ }
                    $c1 = 0; $c2 = 0; $f1 = 0; $f2 = 0
                    foreach ($x in $off) { if (Move-Entry -Name $x -FromDir $pluginsDir  -ToDir $disabledDir) { $c1++ } else { $f1++ } }
                    foreach ($x in $on)  { if (Move-Entry -Name $x -FromDir $disabledDir -ToDir $pluginsDir)  { $c2++ } else { $f2++ } }
                    Write-Host ("        禁用第三方 {0} 个，启用我的 {1} 个，失败 {2} 个" -f $c1, $c2, ($f1 + $f2)) -ForegroundColor Gray
                    Write-Host ""
                    Read-Host "  按回车返回"
                    continue
                }
                if ($a2 -eq '2') {
                    Write-Host ""
                    Write-Host "  正在只保留「第三方 mod」..." -ForegroundColor White
                    [array]$off = Get-TopLevelNames $pluginsDir  | Where-Object { $mineList -contains $_ }
                    [array]$on  = Get-TopLevelNames $disabledDir | Where-Object { $mineList -notcontains $_ }
                    $c1 = 0; $c2 = 0; $f1 = 0; $f2 = 0
                    foreach ($x in $off) { if (Move-Entry -Name $x -FromDir $pluginsDir  -ToDir $disabledDir) { $c1++ } else { $f1++ } }
                    foreach ($x in $on)  { if (Move-Entry -Name $x -FromDir $disabledDir -ToDir $pluginsDir)  { $c2++ } else { $f2++ } }
                    Write-Host ("        禁用我的 {0} 个，启用第三方 {1} 个，失败 {2} 个" -f $c1, $c2, ($f1 + $f2)) -ForegroundColor Gray
                    Write-Host ""
                    Read-Host "  按回车返回"
                    continue
                }
                if ($a2 -eq '3') { Show-PluginList; Read-Host "  按回车返回"; continue }
                if ($a2 -eq '4') { Start-Game; break }
            }
            continue
        }
        'q' { break }
        ''  { continue }
        default {
            Write-Host "  无效选择: $ans" -ForegroundColor Red
            Start-Sleep -Milliseconds 800
            continue
        }
    }
}

Write-Host ""
Write-Host "  已退出。" -ForegroundColor DarkGray
