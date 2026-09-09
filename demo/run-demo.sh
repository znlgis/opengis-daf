#!/usr/bin/env bash
# OpenGisDAF demo 一键运行脚本（Linux / macOS）
# 用法：在仓库根目录执行  ./demo/run-demo.sh
# 前置条件：.NET 10 SDK；脚本会自动构建 CLI 与插件。

set -uo pipefail

# 切换到 demo 目录（所有方案内的相对路径以 demo 为基准）
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CLI_DLL="$REPO_ROOT/src/OpenGisDAF.Cli/bin/Release/net10.0/OpenGisDAF.Cli.dll"
PLUGIN_DLL="$SCRIPT_DIR/plugin/OpenGisDAF.SamplePlugin/bin/Release/net10.0/OpenGisDAF.SamplePlugin.dll"
HOST_DLL="$SCRIPT_DIR/plugin/OpenGisDAF.PluginHostDemo/bin/Release/net10.0/OpenGisDAF.PluginHostDemo.dll"

step() {
  echo ""
  echo "=================================================="
  echo "  $1"
  echo "=================================================="
}

fail() { echo "错误: $1" >&2; exit 1; }

# ---- 0. 构建 ----
step "0. 构建 CLI 与插件（已构建则跳过）"
[ -f "$CLI_DLL" ]   || dotnet build "$REPO_ROOT/OpenGisDAF.slnx" -c Release || fail "构建失败"
[ -f "$PLUGIN_DLL" ] || dotnet build "$SCRIPT_DIR/plugin/OpenGisDAF.SamplePlugin/OpenGisDAF.SamplePlugin.csproj" -c Release || fail "插件构建失败"
[ -f "$HOST_DLL" ]   || dotnet build "$SCRIPT_DIR/plugin/OpenGisDAF.PluginHostDemo/OpenGisDAF.PluginHostDemo.csproj" -c Release || fail "插件宿主构建失败"

# ---- 1. 算子清单 ----
step "1. 查看已注册算子（daf operator list）"
dotnet "$CLI_DLL" operator list || fail "命令失败"

# ---- 2. 方案校验 ----
step "2. 校验方案 01（daf validate）"
dotnet "$CLI_DLL" validate --plan plans/01-school-service-area.json || fail "校验失败"

# ---- 3. 非法方案演示 ----
step "3. 非法方案演示（buffer 缺少必需参数，预期校验失败、退出码 1）"
dotnet "$CLI_DLL" validate --plan plans/invalid-missing-distance.json
if [ $? -eq 0 ]; then fail "非法方案竟然通过了校验——这不应该发生"; fi
echo ">>> 如预期被拒绝（退出码 1）"

# ---- 4. 主线分析 ----
step "4. 执行方案 01：学校服务区（坐标转换/缓冲/裁剪/字段计算/空值填充，预期退出码 0）"
dotnet "$CLI_DLL" run --plan plans/01-school-service-area.json | tail -12 || fail "执行失败"

# ---- 5. 质检 ----
step "5. 执行方案 02：数据质量检查（5 类 QC 规则 + 评分报告，预期退出码 0）"
dotnet "$CLI_DLL" run --plan plans/02-data-quality-check.json | tail -20 || fail "执行失败"
echo ">>> 质检报告: plans/02-data-quality-check.qc-report.json"

# ---- 6. 转换与 SHP ----
step "6. 执行方案 03：坐标转换 + Shapefile 输出（.prj 生成，预期退出码 0）"
dotnet "$CLI_DLL" run --plan plans/03-transform-and-shapefile.json | tail -8 || fail "执行失败"
echo ">>> 检查输出: output/demo03/ 下的 .shp/.prj/.dbf 五件套"

# ---- 7. 失败策略 ----
step "7. 执行方案 04：失败策略（失败 + 独立成功 + 跳过，预期退出码 1）"
dotnet "$CLI_DLL" run --plan plans/04-failure-policy.json | tail -8
if [ $? -eq 0 ]; then fail "包含失败项的方案不应返回 0"; fi
echo ">>> 如预期返回退出码 1（失败/跳过即失败，与 CLI 文档一致）"

# ---- 8. 插件演示 ----
step "8. 插件演示：同进程导入 length_calculator 并执行（预期退出码 0）"
dotnet "$HOST_DLL" --plugin "$PLUGIN_DLL" --plan plugin/plans/road-length.json | tail -12 || fail "插件演示失败"
echo ">>> 插件算子输出: output/plugin-demo/roads-with-length.geojson"

# ---- 9. PostGIS 模板说明 ----
step "9. PostGIS 模板（plans/05-postgis-output.json）"
cat <<'EOF'
该方案需要真实数据库连接，默认不运行。
占位密文运行会快速失败（ERR_DS_CONNECTION_FAILED），不会泄露信息——
这是框架的安全设计。配置方法见 demo/README.md 第 7 节。
EOF

step "演示完成"
echo "输出产物: output/demo01/ output/demo03/ output/plugin-demo/"
echo "质检报告: plans/02-data-quality-check.qc-report.json"
echo "详细讲解: demo/README.md"
