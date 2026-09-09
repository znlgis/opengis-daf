# OpenGisDAF 示例教程 —— "新星市" 规划分析 Demo

本目录是一个**完全自包含**的示例：数据虚构、开箱即用、不依赖任何真实数据库，
覆盖框架的绝大部分功能点，适合作为学习 OpenGisDAF 的完整教材。

> **数据声明**：示例中的"新星市"、学校、道路、地块、机构与人名均为虚构，
> 坐标位于无人认领的虚构区域。全部数据由 `tools/generate_testdata.py`
> 生成（纯 Python 标准库），可随时重新生成、随意修改。

## 目录结构

```
demo/
├── README.md                    本教程
├── run-demo.ps1 / run-demo.sh   一键运行全部演示（Windows / Linux-macOS）
├── data/                        合成数据（EPSG:4326 GeoJSON）
│   ├── schools.geojson          8 所学校（点）
│   ├── roads.geojson            6 条道路（线）
│   ├── landuse.geojson          5 个地块（面）
│   └── city_boundary.geojson    研究区边界（面）
├── plans/                       分析/质检方案
│   ├── 01-school-service-area.json    主线：6 步 DAG 串联分析
│   ├── 02-data-quality-check.json     质检：5 类 QC 规则 + 评分报告
│   ├── 03-transform-and-shapefile.json 转换：GeoJSON → SHP（.prj）
│   ├── 04-failure-policy.json         容错：失败策略与退出码
│   ├── 05-postgis-output.json         PostGIS 模板（需真实连接，默认不跑）
│   └── invalid-missing-distance.json  非法方案（演示校验拒绝）
├── plugin/                      插件开发示例
│   ├── OpenGisDAF.SamplePlugin/       插件算子项目（length_calculator）
│   ├── OpenGisDAF.PluginHostDemo/     同进程插件宿主（CLI 的替代用法）
│   └── plans/road-length.json         使用插件算子的方案
├── tools/generate_testdata.py   数据生成脚本
└── output/                      运行产物（首次运行后出现）
```

## 快速开始

```bash
# 前置：.NET 10 SDK（仓库根目录 global.json 已锁定版本）

# 方式一：一键运行全部演示（推荐）
./demo/run-demo.sh        # Linux / macOS（Windows Git Bash 同样可用）
.\demo\run-demo.ps1       # Windows PowerShell

# 方式二：逐步执行（下文有每步讲解）
cd demo
dotnet run --project ../src/OpenGisDAF.Cli -- validate --plan plans/01-school-service-area.json
dotnet run --project ../src/OpenGisDAF.Cli -- run --plan plans/01-school-service-area.json
```

一键脚本会依次执行：构建 → 算子清单 → 方案校验 → 非法方案拒绝 →
四个方案执行 → 插件演示，并对每步的**退出码做断言**（0 或 1 均为预期行为，
脚本最后输出"演示完成"即全部通过）。

---

## 数据集说明（刻意注入的"问题"）

学习数据质检最好的方式是有一份"已知答案"的脏数据。生成脚本在每个文件里
注入了精确数量的问题，运行方案 02 后可与质检报告逐一对照：

| 数据文件 | 注入的问题 | 预期被哪条规则发现 |
|---|---|---|
| schools | `s03`、`s06` 的 principal 为 null | `ATTR_MISSING`（Error） |
| schools | `s04` 的 phone 为空字符串 | `ATTR_EMPTY`（Warning） |
| schools | `s07` 带 Z 值（45.5） | 无问题——用于验证三维坐标全程保留 |
| schools | `s08` 在研究区边界外 | 方案 01 的 clip 将其过滤；也可用 containment_check 发现 |
| roads | `r05` 翠柳巷为 8 字自相交线 | `GEOM_NOT_SIMPLE`（Warning） |
| roads | `r06` 云湖联络线完全位于生态保护区 G-004 内 | `WITHIN`（Error） |
| landuse | `p05` 为蝴蝶结多边形（顶点顺序交叉） | `GEOM_INVALID`（Error） |
| landuse | `p01` 与 `p02` 存在一小块重叠 | 集合内 `INTERSECT`（Error） |
| landuse | `p05` 的 owner 为 null | `ATTR_MISSING`；配合 null_value_filler 演示 |

---

## 方案逐讲

### 方案 01：学校服务区分析（核心主线）

`plans/01-school-service-area.json` —— 6 个 item 构成一条 DAG：

```
transform-schools ──┐
                    ├─→ buffer-schools ─→ clip-service-area ─→ label-zones ─→ fill-principal
transform-boundary ─┘
```

| 步骤 | 算子 | 演示的功能点 |
|---|---|---|
| transform-schools | coordinate_transform | 4326 → 3857（Web Mercator）；console 输出可直观对比坐标变化；**Z 值 45.5 保留** |
| transform-boundary | coordinate_transform | 第二条转换链；与前一步**无依赖，DAG 可并行调度** |
| buffer-schools | buffer | 3857 米制坐标下 800 米缓冲；item 级 `timeout` / `maxRetries` 配置 |
| clip-service-area | clip | 上游双输入裁剪；`s08` 界外学校缓冲区与边界不相交，**8 → 7 个要素** |
| label-zones | field_calculator | 表达式 `"服务区-{school_name}"` 的字段引用拼接 |
| fill-principal | null_value_filler | 把 2 个 null 的 principal 填充为"（未登记）" |

运行：

```bash
dotnet run --project ../src/OpenGisDAF.Cli -- run --plan plans/01-school-service-area.json
```

预期关键输出：

```
[Clip] 完成裁剪分析，输出要素数=7
[NullValueFiller] 处理完成: 7 个要素，填充 2 个空值，字段 'principal'
=== 执行完成 === （全部 6 步成功，退出码 0）
```

产物在 `output/demo01/`，其中 `school-buffer-800m.geojson` 的头部带有
`"crs": { ... "urn:ogc:def:crs:EPSG::3857" }` —— 坐标系随数据流一路传播。

### 方案 02：数据质量检查（QC 模式）

`plans/02-data-quality-check.json` —— 5 个 item 全部开启 `qcMode: true`：

| item | 规则 | 发现 |
|---|---|---|
| qc-roads-geometry | geometry_validity_checker | r05 自相交（Warning） |
| qc-landuse-geometry | geometry_validity_checker | p05 蝴蝶结（Error） |
| qc-schools-attributes | attribute_completeness_checker | 2 缺失 + 1 空串 |
| qc-landuse-overlap | intersect_check（use_second_input=false） | 地块两两重叠检查：p01×p02 |
| qc-road-in-ecozone | containment_check（within） | r06 完全位于生态保护区 |

运行后自动生成质检报告 **`plans/02-data-quality-check.qc-report.json`**：

- 每条规则有 `totalChecked / failed / passed / passRate`（按**实际检查要素数**计分）
- 汇总质量评分（本数据集为 **83.67/100**）
- 完整 issue 清单（含 WKT 违规几何）

> **工作流建议**：先跑方案 02 发现数据问题，修复后（如重新生成干净数据）
> 再跑方案 01 做分析。方案 05 演示了未修复的无效几何在 clip 中的表现
> （该要素求交抛 TopologyException 后被跳过）——这正是"先质检后分析"的理由。

### 方案 03：坐标转换与 Shapefile 输出

`plans/03-transform-and-shapefile.json`：

1. `export-landuse-shp`：地块 4326 → 3857 后写出 **SHP 五件套**
   （.shp/.shx/.dbf/**.prj**/.cpg），`.prj` 内容为 Web Mercator；
2. `read-shp-calc`：**把上一步的 SHP 作为新的输入源**再读回来做字段计算
   —— 演示"输出 → 输入"的闭环，以及多数据源混用（GeoJSON 与 SHP）；
3. `fill-owner`：null 值填充演示。

运行时会看到 GDAL 的警告：

```
Warning 6: Normalized/laundered field name: 'landuse_type' to 'landuse_ty'
```

这是 **Shapefile 格式的 10 字符字段名限制**（DBF 规范）：`landuse_type`（12 字符）
被截断为 `landuse_ty`。方案里的表达式刻意只引用 `plot_no`、`area_ha`（≤10 字符）
—— 字段名大小写不敏感（框架已按 OrdinalIgnoreCase 匹配，引用小写、
SHP 返回大写也没问题），但**截断无法自动恢复**，跨数据源方案中应优先引用短字段。

### 方案 04：失败策略与退出码

`plans/04-failure-policy.json`（`failurePolicy: "continueIndependent"`）：

```
step-broken-input      buffer，输入 data/missing-file.geojson（不存在） → 失败
step-independent-ok    field_calculator，与失败项无依赖                → 成功
step-skipped-dependent clip，source 指向上游 step-broken-input          → 跳过
```

预期输出与退出码：

```
  buffer (step-broken-input): 失败
  field_calculator (step-independent-ok): 成功
  clip (step-skipped-dependent): 跳过
退出码: 1        ← 只要有失败或跳过，CLI 即返回 1，便于 CI/脚本判断
```

若把策略改为 `stopOnAny`（默认），失败后独立项也不会执行。`maxRetries`
仅对执行期 `ERR_RT_*` 错误生效，输入不存在这类 `SCH_*` 错误在调度层直接失败。

### 非法方案：校验规则演示

```bash
dotnet run --project ../src/OpenGisDAF.Cli -- validate --plan plans/invalid-missing-distance.json
```

```
方案校验失败:
  [ERR_CFG_PARAM_OUT_OF_RANGE] 缺少必需参数 'distance'。 (item.buffer-without-distance)
退出码: 1
```

框架内置 21 条方案校验规则（参数存在性/类型/范围、绑定完整性、DAG 环检测等），
`validate` 与 `run` 都会在执行前拦截。

---

## 插件开发示例

`plugin/` 目录演示从零编写一个算子插件并在方案中真实使用：

```
length_calculator —— 计算线要素长度（公里），写入 length_km 字段
```

### 为什么需要"插件宿主"？

CLI 的 `daf operator import --dll` 在**当前 CLI 进程内**加载插件；
而 `daf run` 每次都是新进程，导入的算子不会跨进程保留。
要让插件算子参与方案执行，需要一个同进程编程宿主：

```bash
# 构建（一键脚本会自动做）
dotnet build plugin/OpenGisDAF.SamplePlugin/OpenGisDAF.SamplePlugin.csproj -c Release
dotnet build plugin/OpenGisDAF.PluginHostDemo/OpenGisDAF.PluginHostDemo.csproj -c Release

# 同进程：导入插件 → 校验 → 执行（方案里 length_calculator 与内置算子同台协作）
dotnet plugin/OpenGisDAF.PluginHostDemo/bin/Release/net10.0/OpenGisDAF.PluginHostDemo.dll \
    --plugin plugin/OpenGisDAF.SamplePlugin/bin/Release/net10.0/OpenGisDAF.SamplePlugin.dll \
    --plan   plugin/plans/road-length.json
```

方案 `plugin/plans/road-length.json` 中，`coordinate_transform`（内置）的输出
通过 `upstream` 绑定流入 `length_calculator`（插件）——插件算子与内置算子在
同一个 DAG 中没有区别。

### 编写插件的三个关键点

1. **最小依赖面**：插件只引用 `OpenGisDAF.Core` 的公共契约
   （`IOperator` / `IFeature` / `IFeatureSource`），自带
   `PluginFeature` / `PluginFeatureSource` 简单实现，不引用任何实现层程序集。

2. **不要复制宿主程序集**（最常见的坑）：
   `OpenGisDAF.SamplePlugin.csproj` 中：

   ```xml
   <ProjectReference Include="...\OpenGisDAF.Core.csproj"
                     Private="false" ExcludeAssets="runtime" />
   ```

   若把 `OpenGisDAF.Core.dll` 复制进插件目录，`PluginLoadContext` 会加载
   **自己的副本**，导致插件里的 `IOperator` 与宿主的不是同一个 Type，
   插件算子被静默跳过（日志显示 "No IOperator implementations found"）。
   保持零复制，运行时才会回退到宿主已加载的程序集。

3. **schema 可选**：插件的输出源不实现 `IFeatureSchemaProvider` 也能工作
   —— 写出时框架会自动物化并推断字段并集、几何类型与 CRS。需要精确
   schema 控制时再实现该接口。

---

## PostGIS 输出（可选）

`plans/05-postgis-output.json` 是一个**模板**：把研究区内的地块裁剪后写入
PostGIS 表。默认不运行（占位连接信息），相关行为：

- `encryptedPassword` 必须是当前用户上下文生成的 DPAPI/AES 密文，
  **不能填写明文密码**；占位密文运行会立即失败（`ERR_DS_CONNECTION_FAILED`
  - "EncryptedPassword 不是有效密文"），且错误信息不回显密文内容。
- 框架对已存在的目标表**不会静默覆盖**（`Layer already exists` 报错），
  重新写入前请先删除目标表或换表名——这是保护生产数据的有意设计。
- 连接密码的加密机制与生成方式参见仓库文档
  `docs/plan-config-guide.md` 与 `src/OpenGisDAF.Adapters/Utilities/ConnectionEncryption.cs`。

---

## 功能点覆盖矩阵

| 框架能力 | 本 demo 的位置 |
|---|---|
| 方案驱动（纯 JSON 配置） | 全部 plans/ |
| 9 个内置算子 | 01（5 种）、02（4 种 QC 用法）、03、04 |
| 多数据源输入（GeoJSON + Shapefile 混用） | 03 的 read-shp-calc |
| 三种文件输出 console / geojson / shapefile | 01、02、03 |
| 坐标系转换 + CRS 全链路传播 | 01、03（GeoJSON crs 字段 / SHP .prj） |
| Z 值（三维坐标）保留 | 01 的 s07（45.5m） |
| QC 模式 + 质量评分报告 | 02 + qc-report.json |
| DAG 上下游串联（upstream 绑定） | 01 六步链、插件方案两步链 |
| 失败策略 + 跳过传播 + CLI 退出码 | 04（continueIndependent） |
| item 级 timeout / maxRetries / 重试间隔 | 01 的 buffer-schools |
| isIntermediate 中间结果标记 | 01 的前五步 |
| 方案校验（21 条规则） | invalid-missing-distance.json |
| 算子清单 / 分类筛选 | 一键脚本第 1 步（operator list） |
| 方案管理（list/create/copy/export） | 见下方"动手练习" |
| 插件算子（AssemblyLoadContext 动态加载） | plugin/ 全部 |
| 字段名大小写不敏感匹配 | 03 的 read-shp-calc |
| PostGIS 输出（加密连接、无覆盖策略） | 05 模板 |

## 动手练习（建议按序）

1. **改参数**：把方案 01 的缓冲距离从 800 改成 2000，观察 `s08`（界外学校）
   的缓冲区是否开始与边界相交、clip 输出从 7 变 8。
2. **加规则**：给方案 02 增加一个 `attribute_completeness_checker` 检查
   landuse 的 `owner` 字段（应发现 p05 的 null）。
3. **造数据**：修改 `tools/generate_testdata.py`（比如新增一条穿越保护区的
   道路），重新生成后验证质检报告的 issue 数量随之变化。
4. **写插件**：仿照 `LengthCalculatorOperator` 写一个 `area_calculator`
   （提示：面要素面积为 `feature.Geometry.Area`，同样假定米制坐标）。
5. **方案管理**：执行 `dotnet run --project ../src/OpenGisDAF.Cli -- plan list`
   查看仓库；`plan create` / `plan copy` / `plan export` 的用法见
   `docs/cli-reference.md`。注意这些命令操作的是用户数据目录
   （`%APPDATA%\opengis-daf\plans`），练习后可按组名清理。

## 常见问题（来自真实数据测试的经验）

- **SHP 字段名为什么变了？** DBF 规范限制 10 字符，超长字段名会被 GDAL
  截断（如 `landuse_type` → `landuse_ty`）。引用字段时优先用短名；大小写
  不一致没关系（框架按 OrdinalIgnoreCase 匹配），但截断无法自动恢复。
- **质检报告里的 FeatureId 是什么？** 它是底层驱动分配的**要素 FID**，
  当前 GDAL 通常从 0 开始编号，不保证跨驱动/跨版本稳定，不能当作业务主键。
  需要稳定标识时请在数据里自带 id 字段（本 demo 的 s01/p01/r01 即为此设计）。
- **clip 的语义**：逐面对源要素求交，一个源要素与多个裁剪面相交会输出多条
  （可能要素分裂）。与 QGIS"先联合裁剪面再裁剪"的语义不同，详见
  `docs/operator-reference.md` 的 clip 说明。
- **无效几何在空间运算中的表现**：蝴蝶结多边形参与 clip 求交时会抛
  `TopologyException`，该要素被跳过并记录错误。先质检修复，再空间分析。

## 下一步

- [方案配置指南](../docs/plan-config-guide.md) —— 方案 JSON 的全部字段
- [算子参考](../docs/operator-reference.md) —— 各算子的参数与输入输出契约
- [CLI 命令参考](../docs/cli-reference.md) —— 全部命令与退出码
- [快速开始](../docs/quickstart.md) —— 从零构建本框架
