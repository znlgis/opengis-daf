# OpenGisDAF demo 一键运行脚本（Windows PowerShell）
# 用法：在仓库根目录执行  .\demo\run-demo.ps1
# 前置条件：.NET 10 SDK；脚本会自动构建 CLI 与插件。

$ErrorActionPreference = "Stop"

# 切换到 demo 目录（所有方案内的相对路径以 demo 为基准）
$demoDir = SplitPath -Parent $MyInvocation.MyCommand.Path
Set-Location $demoDir

$repoRoot = Resolve-Path (Join-Path $demoDir "..")
$cliDll = Join-Path $repoRoot "src/OpenGisDAF.Cli/bin/Release/net10.0/OpenGisDAF.Cli.dll"
$pluginDll = Join-Path $demoDir "plugin/OpenGisDAF.SamplePlugin/bin/Release/net10.0/OpenGisDAF.SamplePlugin.dll"
$hostDll = Join-Path $demoDir "plugin/OpenGisDAF.PluginHostDemo/bin/Release/net10.0/OpenGisDAF.PluginHostDemo.dll"

function Step($title) {
    Write-Host ""
    Write-Host "==================================================" -ForegroundColor Cyan
    Write-Host "  $title" -ForegroundColor Cyan
    Write-Host "==================================================" -ForegroundColor Cyan
}

# ---- 0. 构建 ----
Step "0. 构建 CLI 与插件（已构建则跳过）"
if (-not (Test-Path $cliDll)) {
    dotnet build (Join-Path $repoRoot "OpenGisDAF.slnx") -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "构建失败"; exit 1 }
}
if (-not (Test-Path $pluginDll)) {
    dotnet build (Join-Path $demoDir "plugin/OpenGisDAF.SamplePlugin/OpenGisDAF.SamplePlugin.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "插件构建失败"; exit 1 }
}
if (-not (Test-Path $hostDll)) {
    dotnet build (Join-Path $demoDir "plugin/OpenGisDAF.PluginHostDemo/OpenGisDAF.PluginHostDemo.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { Write-Error "插件宿主构建失败"; exit 1 }
}

# ---- 1. 算子清单 ----
Step "1. 查看已注册算子（daf operator list）"
dotnet $cliDll operator list
if ($LASTEXITCODE -ne 0) { Write-Error "命令失败"; exit 1 }

# ---- 2. 方案校验 ----
Step "2. 校验方案 01（daf validate）"
dotnet $cliDll validate --plan plans/01-school-service-area.json
if ($LASTEXITCODE -ne 0) { Write-Error "校验失败"; exit 1 }

# ---- 3. 非法方案演示 ----
Step "3. 非法方案演示（buffer 缺少必需参数，预期校验失败、退出码 1）"
dotnet $cliDll validate --plan plans/invalid-missing-distance.json
if ($LASTEXITCODE -eq 0) { Write-Error "非法方案竟然通过了校验——这不应该发生"; exit 1 }
Write-Host ">>> 如预期被拒绝（退出码 1）" -ForegroundColor Green

# ---- 4. 主线分析 ----
Step "4. 执行方案 01：学校服务区（坐标转换/缓冲/裁剪/字段计算/空值填充，预期退出码 0）"
dotnet $cliDll run --plan plans/01-school-service-area.json | Select-Object -Last 12
if ($LASTEXITCODE -ne 0) { Write-Error "执行失败"; exit 1 }

# ---- 5. 质检 ----
Step "5. 执行方案 02：数据质量检查（5 类 QC 规则 + 评分报告，预期退出码 0）"
dotnet $cliDll run --plan plans/02-data-quality-check.json | Select-Object -Last 20
if ($LASTEXITCODE -ne 0) { Write-Error "执行失败"; exit 1 }
Write-Host ">>> 质检报告: plans/02-data-quality-check.qc-report.json" -ForegroundColor Green

# ---- 6. 转换与 SHP ----
Step "6. 执行方案 03：坐标转换 + Shapefile 输出（.prj 生成，预期退出码 0）"
dotnet $cliDll run --plan plans/03-transform-and-shapefile.json | Select-Object -Last 8
if ($LASTEXITCODE -ne 0) { Write-Error "执行失败"; exit 1 }
Write-Host ">>> 检查输出: output/demo03/ 下的 .shp/.prj/.dbf 五件套" -ForegroundColor Green

# ---- 7. 失败策略 ----
Step "7. 执行方案 04：失败策略（失败 + 独立成功 + 跳过，预期退出码 1）"
dotnet $cliDll run --plan plans/04-failure-policy.json | Select-Object -Last 8
if ($LASTEXITCODE -eq 0) { Write-Error "包含失败项的方案不应返回 0"; exit 1 }
Write-Host ">>> 如预期返回退出码 1（失败/跳过即失败，与 CLI 文档一致）" -ForegroundColor Green

# ---- 8. 插件演示 ----
Step "8. 插件演示：同进程导入 length_calculator 并执行（预期退出码 0）"
dotnet $hostDll --plugin $pluginDll --plan plugin/plans/road-length.json | Select-Object -Last 12
if ($LASTEXITCODE -ne 0) { Write-Error "插件演示失败"; exit 1 }
Write-Host ">>> 插件算子输出: output/plugin-demo/roads-with-length.geojson" -ForegroundColor Green

# ---- 9. PostGIS 模板说明 ----
Step "9. PostGIS 模板（plans/05-postgis-output.json）"
Write-Host "该方案需要真实数据库连接，默认不运行。"
Write-Host "占位密文运行会快速失败（ERR_DS_CONNECTION_FAILED），不会泄露信息——"
Write-Host "这是框架的安全设计。配置方法见 demo/README.md 第 7 节。"

Step "演示完成"
Write-Host "输出产物: output/demo01/ output/demo03/ output/plugin-demo/"
Write-Host "质检报告: plans/02-data-quality-check.qc-report.json"
Write-Host "详细讲解: demo/README.md"
