param([string]$Dotnet = "dotnet")
$ErrorActionPreference = "Stop"
# 兼容旧入口：统一代理到标准工作流 scripts/build.ps1，避免 src/ 双源漂移
# 新标准： powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Mod SquadCommand
$root = Split-Path -Parent $PSScriptRoot
if (-not $root) { $root = $PSScriptRoot }
$std = Join-Path $root "scripts\build.ps1"
if (Test-Path $std) {
  Write-Host "[BUILD_0.7.10] 代理到标准工作流: scripts/build.ps1 -Mod SquadCommand"
  & powershell -ExecutionPolicy Bypass -File $std -Mod SquadCommand
  exit $LASTEXITCODE
}
# 兜底：直接构建扁平结构
$proj = Join-Path $PSScriptRoot "SquadCommand.csproj"
Write-Host "Building ER2 Squad Command 0.7.10 (fallback)..."
& $Dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
$out = Join-Path $PSScriptRoot "bin\Release\net6.0\ER2_SquadCommand.dll"
if (-not (Test-Path $out)) { throw "Build reported success but DLL was not found: $out" }
Write-Host "Built: $out"
