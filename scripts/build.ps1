param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("LimbTweaks", "WeatherControl", "AIFood", "NoInteractionHints", "ModManager", "ThrowableWheel", "ZoomAnywhere", "CombatTweaks", "UnitCollision", "UnitInfoOverlay", "HighValueTarget", "InventoryPause", "SquadCommand", "UniversalGeneration", "MorePhysics", "Conquest", "ConquestRecon")]
    [string]$Mod,
    [switch]$SkipDeploy,
    [switch]$SkipPackage,
    [switch]$Cn
)

# ER2 Mod 构建/部署/打包一体化脚本（供 AI harness 调用）
# 用法: .\scripts\build.ps1 -Mod LimbTweaks          # 构建+部署+清cfg
#       .\scripts\build.ps1 -Mod WeatherControl -SkipDeploy
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

# 游戏安装目录：可用环境变量 ER2_GAME_DIR 覆盖，否则用下面的默认值。
# 自定义 Steam 库请改这里，或先执行 $env:ER2_GAME_DIR = "D:\SteamLibrary\steamapps\common\Easy Red 2"
$game = if ($env:ER2_GAME_DIR) { $env:ER2_GAME_DIR } else { "E:\SteamLibrary\steamapps\common\Easy Red 2" }

# 发布包输出目录：可用环境变量 ER2_OUT_DIR 覆盖，默认当前用户的下载目录。
$outDir = if ($env:ER2_OUT_DIR) { $env:ER2_OUT_DIR } else { Join-Path $env:USERPROFILE "Downloads" }
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

switch ($Mod) {
    "LimbTweaks" { $proj = "limbtweaks.csproj"; $dll = "ER2_LimbTweaks.dll"; $cfg = "er2.limbtweaks.cfg"; $pkg = "ER2_LimbTweaks"; $assetsDir = "" }
    "WeatherControl" { $proj = "weather.csproj"; $dll = "ER2_WeatherControl.dll"; $cfg = "er2.weathercontrol.cfg"; $pkg = "ER2_WeatherControl"; $assetsDir = "" }
    "AIFood" { $proj = "aifood.csproj"; $dll = "ER2_AIFood.dll"; $cfg = "er2.aifood.cfg"; $pkg = "ER2_AIFood"; $assetsDir = "" }
    "NoInteractionHints" { $proj = "NoInteractionHints.csproj"; $dll = "ER2_NoInteractionHints_DoneProMaxEnd.dll"; $cfg = ""; $pkg = "ER2_HideAnything"; $assetsDir = "" }
    "ModManager" { $proj = "modmanager.csproj"; $dll = "ER2_ModManager.dll"; $cfg = "er2.modmanager.cfg"; $pkg = "ER2_ModManager"; $assetsDir = "" }
    "ThrowableWheel" { $proj = "ThrowableWheel.csproj"; $dll = "ER2_ThrowableWheel.dll"; $cfg = "er2.throwablewheel.cfg"; $pkg = "ER2_ThrowableWheel"; $assetsDir = "" }
    "ZoomAnywhere" { $proj = "ZoomAnywhere.csproj"; $dll = "ER2_ZoomAnywhere.dll"; $cfg = "er2.zoomanywhere.cfg"; $pkg = "ER2_ZoomAnywhere"; $assetsDir = "" }
    "CombatTweaks" { $proj = "CombatTweaks.csproj"; $dll = "ER2_CombatTweaks.dll"; $cfg = "er2.combattweaks.cfg"; $pkg = "ER2_CombatTweaks"; $assetsDir = "" }
    "UnitCollision" { $proj = "UnitCollision.csproj"; $dll = "ER2_MorePhysics_UnitCollision.dll"; $cfg = "er2.morephysics.unitcollision.cfg"; $pkg = "ER2_MorePhysics_UnitCollision"; $assetsDir = "" }
    "UnitInfoOverlay" { $proj = "UnitInfoOverlay.csproj"; $dll = "ER2_UnitInfoOverlay.dll"; $cfg = "er2.unitinfooverlay.cfg"; $pkg = "ER2_UnitInfoOverlay"; $assetsDir = "" }
    "HighValueTarget" { $proj = "HighValueTarget.csproj"; $dll = "ER2_VeteranHVT.dll"; $cfg = "er2.highvaluetarget.cfg"; $pkg = "ER2_VeteranHVT"; $assetsDir = "ER2_VeteranHVT" }
    "InventoryPause" { $proj = "InventoryPause.csproj"; $dll = "ER2_InventoryPause.dll"; $cfg = "er2.inventorypause.cfg"; $pkg = "ER2_InventoryPause"; $assetsDir = "" }
    "SquadCommand" { $proj = "SquadCommand.csproj"; $dll = "ER2_BattlefieldCommander.dll"; $cfg = "er2.squadcommand.cfg"; $pkg = "EasyRedGate"; $assetsDir = "" }
    "UniversalGeneration" { $proj = "UniversalGeneration.csproj"; $dll = "ER2_UniversalGeneration.dll"; $cfg = "er2.universalgeneration.cfg"; $pkg = "ER2_UniversalGeneration"; $assetsDir = "" }
    "MorePhysics" { $proj = "MorePhysics.csproj"; $dll = "ER2_MorePhysics.dll"; $cfg = "er2.morephysics.cfg"; $pkg = "ER2_MorePhysics"; $assetsDir = "" }
    "Conquest" { $proj = "Conquest.csproj"; $dll = "ER2_Conquest.dll"; $cfg = "er2.conquest.cfg"; $pkg = "ER2_Conquest"; $assetsDir = "" }
    # 内部侦察工具（M0），不发布
    "ConquestRecon" { $proj = "ConquestRecon.csproj"; $dll = "ER2_ConquestRecon.dll"; $cfg = "er2.conquest.recon.cfg"; $pkg = "ER2_ConquestRecon"; $assetsDir = "" }
}

Write-Host "[1/4] Building $Mod ..."
Push-Location "$root\$Mod"
try {
    if ($Cn) { dotnet build -c Release $proj -p:DefineConstants=CN_BUILD | Out-Null }
    else { dotnet build -c Release $proj | Out-Null }
} finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

if (-not $SkipDeploy) {
    Write-Host "[2/4] Deploying $dll (polling if game is running) ..."
    $src = "$root\$Mod\bin\Release\net6.0\$dll"
    $dst = "$game\BepInEx\plugins\$dll"
    $deadline = (Get-Date).AddMinutes(10)
    $ok = $false
    while ((Get-Date) -lt $deadline) {
        try { Copy-Item $src $dst -Force -ErrorAction Stop; $ok = $true; break } catch { Start-Sleep -Seconds 10 }
    }
    if (-not $ok) { throw "Deploy failed: file locked" }
    if ($cfg -ne "") { Remove-Item "$game\BepInEx\config\$cfg" -Force -ErrorAction SilentlyContinue }
    if ($assetsDir -ne "") {
        $assetsSrc = "$root\$Mod\Assets"
        $assetsDst = "$game\BepInEx\plugins\$assetsDir"
        if (Test-Path $assetsSrc) {
            if (Test-Path $assetsDst) { Remove-Item $assetsDst -Recurse -Force }
            Copy-Item $assetsSrc $assetsDst -Recurse -Force
            Write-Host "Deployed assets: $assetsDst"
        } else {
            Write-Host "WARNING: Assets folder missing at $assetsSrc"
        }
    }
    Write-Host "Deployed: $(Get-Item $dst | Select-Object -ExpandProperty LastWriteTime)"
}

if (-not $SkipPackage) {
    Write-Host "[3/4] Packaging ..."
    $src = "$root\$Mod\bin\Release\net6.0\$dll"
    $tag = if ($Cn) { "_CN" } else { "" }
    $pkgDir = Join-Path $outDir "$pkg$tag"
    if (Test-Path $pkgDir) { Remove-Item $pkgDir -Recurse -Force }
    New-Item -ItemType Directory -Path $pkgDir -Force | Out-Null
    Copy-Item $src "$pkgDir\$dll" -Force
    if ($assetsDir -ne "" -and (Test-Path "$root\$Mod\Assets")) {
        Copy-Item "$root\$Mod\Assets" "$pkgDir\$assetsDir" -Recurse -Force
    }
    foreach ($doc in @("README.txt", "Nexus_description.md")) {
        # 双语发布包：README/Nexus 描述按包语言取文件——默认包=英文，-Cn 包=中文
        $docSrc = "$root\$Mod\$doc"
        if ($doc -eq "README.txt") {
            $localized = if ($Cn) { "README_CN.txt" } else { "README.txt" }
            if (Test-Path "$root\$Mod\$localized") { $docSrc = "$root\$Mod\$localized" }
        }
        if ($doc -eq "Nexus_description.md") {
            $localized = if ($Cn) { "Nexus_description_CN.md" } else { "Nexus_description.md" }
            if (Test-Path "$root\$Mod\$localized") { $docSrc = "$root\$Mod\$localized" }
        }
        if (Test-Path $docSrc) { Copy-Item $docSrc "$pkgDir\$doc" -Force }
    }
    $zipName = "$pkg" + $tag + "_v" + ((Select-String -Path "$root\$Mod\Plugin.cs" -Pattern '"(\d+)\.(\d+)\.(\d+)"' | Select-Object -First 1).Matches.Value.Trim('"')) + ".zip"
    $zipPath = Join-Path $outDir $zipName
    Compress-Archive -Path "$pkgDir\*" -DestinationPath $zipPath -Force
    Write-Host "Packaged: $zipPath"
}

Write-Host "[4/4] Done. Ask user to restart the game and test."
