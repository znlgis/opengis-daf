# GIS 数据分析与质检框架设计文档

> 对应需求文档：[/docs/GIS数据分析框架需求文档.md](./GIS数据分析框架需求文档.md)

## 1. 文档目标

本文档为 **GIS 数据分析与质检框架** 的技术设计说明，覆盖以下核心内容：

- **总体架构**：七层架构设计与各层职责划分
- **核心模型**：算子、分析项、分析方案等核心对象的定义与关系
- **接口边界**：对内（框架层间契约）与对外（API/CLI）的接口规范
- **执行机制**：DAG 调度、串并行编排、执行上下文与状态管理
- **关键技术决策**：技术栈选型、设计权衡与约束条件

业务目标、功能范围、非功能要求和实施优先级见需求文档。

## 2. 技术设计原则

### 2.1 技术栈

| 技术要素 | 选型 | 说明 |
|----------|------|------|
| 开发语言 | C# 14 | 与 .NET 10 配套，利用最新语言特性 |
| 运行框架 | .NET 10 | 最新 LTS，跨平台，长期支持 |
| GIS 内核 | GDAL/OGR | C++ 内核，跨平台（含 Linux ARM、国产环境），内置坐标转换，原生支持主流矢量/栅格格式；大数据场景性能优于纯 .NET 方案 |
| GIS 补充 | NetTopologySuite (NTS) | 轻量纯 .NET 空间计算库，作为 GDAL 的补充用于简单场景 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | 与 .NET 生态一致 |
| 序列化 | System.Text.Json | 方案配置与元数据处理 |
| 异步与并行 | async/await + Channel + Task Parallel Library | 异步执行 + 并行计算，充分利用多核算力 |
| 日志 | Microsoft.Extensions.Logging | 统一结构化日志 |

> **GIS 内核选型说明**：GDAL 是 GIS 领域事实上的底层标准，其 Java / .NET 接口本质上都是对 C++ 核心的封装。相比纯 .NET 的 NTS，GDAL 优势在于：(1) 跨平台覆盖更广，包括 ARM 和国产 Linux 环境；(2) 内置坐标转换，无需额外依赖 ProjNet；(3) 原生支持 Shapefile、GDB、PostGIS 等主流格式，无需第三方扩展；(4) 大数据量场景下 C++ 内核性能更优。NTS 保留作为轻量场景的补充。所有数据读取、处理和输出遵循异步模式（async/await + Channel）。

### 2.2 关键设计决策

| 决策点 | 选择 | 原因 |
|--------|------|------|
| 执行模型 | DAG + 拓扑排序 | 轻量、易推导并行度、适合数据流场景 |
| 处理方式 | 异步 + 按需并行 | 异步保证 I/O 不阻塞；并行执行独立分析项以充分利用多核算力；流式读取为数据库等场景的自然属性，非强制要求 |
| 方案定义 | JSON | 便于生成、校验和工具链集成；GIS 场景下 JSON 体积开销可忽略 |
| 扩展方式 | 配置驱动为主，代码扩展为辅 | 通过预留配置空间实现外部可配，同时保留接口级的代码扩展能力 |
| 插件隔离 | AssemblyLoadContext + 进程隔离 | `AssemblyLoadContext` 保证 .NET 算子间依赖隔离；未来可扩展到独立进程以支持异构语言运行时 |

### 2.3 设计约束

- 算子必须保持无状态：算子自身只返回成功/失败及失败原因，失败后的重试、跳过或终止由上层框架根据方案配置裁决。
- `IFeatureSource` 与 `IFeatureSink` 生命周期由 DI 容器管理，输入输出的具体绑定由算子外部完成，算子仅声明所需数据类型。
- 方案配置在执行期间保持不可变，保证可追溯性。执行中创建新版本不影响正在运行的旧版本实例；框架仅标记结果所属版本，不裁决不同版本结果的有效性。
- 数据流依赖必须可构建为有向无环图（DAG），否则方案校验阶段直接拒绝。
- 核心框架不依赖任何 GUI 组件，纯控制台/服务化运行。所有交互通过 API、CLI 或配置文件完成。

## 3. 领域模型设计

### 3.1 核心对象

| 对象 | 作用 |
|------|------|
| Operator | 可复用分析能力的最小单元，定义输入/处理逻辑/输出三要素；同一算子在不同场景下输出自适应（质检→问题描述、分析→T/F 或中间数据集、报表→统计值） |
| Analysis Item | 算子在具体场景下的配置实例，绑定具体数据源、参数和输出目标；不支持嵌套 |
| Analysis Plan | 由多个分析项和子方案构成的完整执行单元，定义了分析项之间的串并行关系 |
| FeatureSource | 统一的数据读取抽象（类比 ADO.NET `DbConnection`），屏蔽不同数据源差异 |
| FeatureSink | 统一的数据输出抽象，屏蔽不同输出目标差异 |
| ExecutionContext | 单次执行的上下文与共享状态，承载中间数据和执行状态流转 |

### 3.2 关系说明

- `AnalysisPlan` 包含多个 `AnalysisItem`，也可包含子方案（`SubPlan`），实现分层组合。
- `AnalysisItem` 通过 `OperatorId` 绑定具体算子；通过 `Version` 锁定算子版本。
- 分析项输入可来自外部数据源、上游分析项结果或子方案输出。
- 调度引擎基于输入绑定关系构建 DAG，检测循环依赖，计算拓扑层级，控制并发和状态流转。

## 4. 总体架构

```text
┌─────────────────────────────────────────────────────┐
│              API / CLI 层（对外接口）                  │
├─────────────────────────────────────────────────────┤
│               方案管理层 (Plan Manager)               │
├─────────────────────────────────────────────────────┤
│             调度引擎层 (Scheduling Engine)            │
├─────────────────────────────────────────────────────┤
│               执行引擎层 (Execution Engine)           │
├─────────────────────────────────────────────────────┤
│                 算子池 (Operator Pool)               │
├─────────────────────────────────────────────────────┤
│        数据源/输出适配层 (Source & Sink Adapter)      │
├─────────────────────────────────────────────────────┤
│         基础设施层 (日志、缓存、配置、监控、安全)      │
└─────────────────────────────────────────────────────┘
```

### 4.1 方案管理层

负责方案解析、Schema 校验、业务规则校验、模板管理、版本管理和持久化。方案配置全部以 JSON 格式存储，支持通过配置驱动的方式预留给外部扩展的空间。

### 4.2 调度引擎

负责根据数据依赖构建 DAG、检测循环依赖、计算拓扑层级、控制并发和状态流转。并联分析项全部完成后才可执行下一层级；串联分析项按序执行，涉及中间数据在分析项间的流转管理。

### 4.3 执行引擎

负责驱动算子具体执行、传递 `ExecutionContext`、管理中间数据缓存、收集日志和执行结果。批量分析任务中对独立分析项启用并行执行以充分利用算力。

### 4.4 适配器层

通过统一的 `IFeatureSource` / `IFeatureSink` 接口屏蔽不同数据源和输出目标的差异（类比 ADO.NET 的 Provider 模式：框架定义统一契约，各数据源提供具体实现）。支持 Shapefile、GDB、PostGIS 等常见格式，输出目标同理。

## 5. 组件设计

### 5.1 算子池

职责：

- 注册与发现算子
- 维护算子元数据
- 按分类和能力检索算子
- 管理算子版本

注册方式：

```csharp
services.AddOperatorsFromAssembly(typeof(BufferOperator).Assembly);
services.AddOperator<BufferOperator>("spatial.buffer");
services.AddOperator<IntersectOperator>("spatial.intersect");
```

#### 5.1.1 算子分类体系

所有算子按功能领域划分为以下七个类别，分类信息存储在 `OperatorMetadata.Category` 字段中，用于算子发现和过滤：

| 分类标识 | 名称 | 说明 | 示例算子 |
|----------|------|------|----------|
| `spatial.relation` | 空间关系 | 判断要素间的空间拓扑关系 | 相交判断、包含判断、相邻判断、距离判断 |
| `spatial.computation` | 空间运算 | 对要素执行空间几何计算 | 缓冲区分析、叠加分析、裁剪、合并、差集 |
| `attribute` | 属性操作 | 对要素属性执行计算或变换 | 字段计算、属性映射、条件赋值、空值填充 |
| `spatial.join` | 空间连接 | 按空间关系关联两个数据集 | 空间关联、最近邻连接 |
| `statistics` | 统计分析 | 对数据集执行统计聚合 | 分组统计、密度分析、热点分析 |
| `conversion` | 格式转换 | 在不同格式或坐标系之间转换 | 格式互转、坐标系转换、几何类型转换 |
| `qc` | 质检规则 | 数据质量检查专用规则 | 拓扑检查、属性完整性、几何有效性 |

算子池提供按分类检索的能力：

```csharp
public interface IOperatorPool
{
    IReadOnlyList<OperatorMetadata> GetByCategory(string category);
    IReadOnlyList<OperatorMetadata> Search(string keyword, string? category = null, string[]? tags = null);
    OperatorMetadata? GetById(string operatorId);
}
```

#### 5.1.2 算子元数据详细设计

`OperatorMetadata` 扩展版本管理、参数校验和兼容性声明：

```csharp
public sealed record OperatorMetadata
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Category { get; init; }
    public string Description { get; init; }
    public string[] Tags { get; init; }
    public string Version { get; init; }             // 语义化版本号，如 "1.2.0"
    public string? MinFrameworkVersion { get; init; } // 最低框架版本要求
    public string? CompatibilityNotes { get; init; }  // 兼容性说明
    public bool SupportsIncremental { get; init; } = false; // 是否支持增量计算
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; }
    public InputSchema InputSchema { get; init; }
    public OutputSchema OutputSchema { get; init; }
}

public sealed record ParameterDefinition
{
    public string Name { get; init; }
    public string Type { get; init; }          // "string" | "int" | "double" | "bool" | "geometry" | "enum"
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public string? Description { get; init; }
    public ParameterConstraint? Constraint { get; init; }
}

public sealed record ParameterConstraint
{
    public double? MinValue { get; init; }
    public double? MaxValue { get; init; }
    public string? Pattern { get; init; }           // 字符串正则约束
    public string[]? AllowedValues { get; init; }   // 枚举允许值列表
}
```

参数约束在校验阶段由框架自动执行，算子无需自行实现校验逻辑。

#### 5.1.3 插件版本管理

**版本共存机制：** 同一算子的多个版本可通过 DLL 文件名后缀区分，并存放在同一插件目录：

```text
plugins/spatial/
  MyPlugin.V1.dll
  MyPlugin.V2.dll
  MyPlugin.V3.dll
```

插件加载时通过文件名或 Assembly 版本号识别版本，不同版本以不同 `OperatorId` 注册到算子池（如 `myplugin:v1`、`myplugin:v2`）。

**方案中的版本指定：** 方案配置通过 `OperatorId` + `Version` 组合精确锁定算子版本；未指定版本时框架默认使用已注册的最高版本。

**兼容性声明：** 每个算子通过 `OperatorMetadata.MinFrameworkVersion` 和 `CompatibilityNotes` 声明兼容性要求。框架加载插件时校验兼容性，不满足要求时给出警告并拒绝加载。

#### 5.1.4 插件组织管理

**目录结构：** 插件按功能领域分目录存放，支持两级以内嵌套：

```text
plugins/
  spatial/           # 空间分析算子
  qc/                # 质检规则算子
  conversion/        # 格式转换算子
  custom/            # 自定义/第三方算子
```

框架启动时递归扫描 `plugins/` 目录及子目录，自动发现并注册符合规范的 DLL。

**插件查询 API：**

```csharp
public interface IPluginManager
{
    IReadOnlyList<PluginInfo> SearchPlugins(string? name = null, string? category = null, string[]? tags = null);
    PluginInfo? GetPlugin(string pluginId);
    IReadOnlyList<string> GetPluginVersions(string pluginId);
    Task ReloadPluginsAsync(CancellationToken cancellationToken = default);
}
```

**分组管理：** 插件按功能领域标记逻辑分组标签（如 `"spatial-analysis"`、`"qc-rules"`、`"data-conversion"`），与目录组织互不冲突——目录用于物理组织，标签用于逻辑分类和检索。

### 5.2 方案管理器

职责：

- 解析 JSON 方案配置
- 执行 Schema 与业务规则校验
- 管理方案模板和版本
- 将方案转换为运行时模型

#### 5.2.1 方案基本操作

方案管理器提供完整的 CRUD 操作，均以 JSON 序列化/反序列化为核心，Schema 校验在加载和保存时自动执行：

| 操作 | 接口 | 说明 |
|------|------|------|
| 创建 | `CreateAsync(config)` | 从零创建空白方案，或基于模板生成初始配置 |
| 加载 | `LoadAsync(planId)` | 从持久化存储（JSON 文件）反序列化方案 |
| 保存 | `SaveAsync(plan)` | 将方案序列化为 JSON 并持久化 |
| 更新 | `UpdateAsync(planId, patch)` | 修改方案中的分析项、参数或执行策略 |
| 复制 | `CopyAsync(planId, newName)` | 克隆已有方案并赋予新标识，支持增量修改 |
| 导入 | `ImportAsync(filePath)` | 从外部 JSON 文件导入方案配置 |
| 导出 | `ExportAsync(planId, filePath)` | 将方案配置导出为独立可交换的配置文件 |

#### 5.2.2 方案组织管理

**文件夹层级：** 方案以 JSON 文件存储于文件夹层级结构中，建议控制在 3 层以内以避免管理复杂度过高。文件夹本身仅作为组织手段，不承载业务语义。

```text
plans/
  项目A/
    质检方案/
      基础拓扑检查.json
      属性完整性检查.json
    分析方案/
      缓冲区分析.json
  项目B/
    数据融合方案.json
```

**分组管理：** 支持按项目、业务方向或使用场景将方案归入不同分组，分组信息存储于方案元数据中，与文件目录解耦：

```csharp
public sealed class PlanGroup
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> PlanIds { get; init; }
}
```

**命名规范：** 方案名称支持中文命名，建议包含业务场景和版本信息（如 `"基础拓扑检查_V2"`、`"土地利用统计_20240604"`）。

#### 5.2.3 方案版本管理

**版本标识：** 方案版本通过 `AnalysisPlan.Version` 字段标识，支持语义化版本号（如 `V1`、`V2`）或日期版本号（如 `20240604`）。

**版本回退机制：** 方案修改保存时自动生成带版本后缀的历史副本（如 `plan.json.V1.bak`），回退操作通过复制历史副本覆盖当前文件实现：

```csharp
public interface IPlanVersionManager
{
    Task<IReadOnlyList<PlanVersionInfo>> GetVersionHistoryAsync(string planId);
    Task<AnalysisPlan> RollbackAsync(string planId, string targetVersion);
    Task<PlanDiffResult> DiffAsync(string planId, string versionA, string versionB);
}
```

**版本差异对比：** `PlanDiffResult` 记录两版本间的新增/删除/修改差异：

```csharp
public sealed record PlanDiffResult
{
    public string PlanId { get; init; }
    public string VersionA { get; init; }
    public string VersionB { get; init; }
    public IReadOnlyList<PlanDiffItem> Changes { get; init; }
}

public sealed record PlanDiffItem
{
    public string ItemId { get; init; }
    public DiffType Type { get; init; } // Added / Removed / Modified
    public string? FieldPath { get; init; }
    public object? OldValue { get; init; }
    public object? NewValue { get; init; }
}
```

#### 5.2.4 模板化管理

**模板存储：** 模板以与方案相同的 JSON 格式存储，存放于独立目录（`templates/`）中，与普通方案物理隔离。

**模板结构特征：** 模板定义处理逻辑和参数约束，但数据源绑定信息以占位符形式存在，由使用者在实例化时填充：

```json
{
  "id": "tpl.qc.topology.basic",
  "name": "基础拓扑检查模板",
  "type": "template",
  "items": [
    {
      "operatorId": "qc.overlap.check",
      "inputs": {
        "source": { "type": "placeholder", "key": "target_dataset" }
      },
      "parameters": { "tolerance": 0.001 }
    }
  ]
}
```

**基于模板创建方案流程：**
1. 用户选择模板并指定数据源绑定
2. 系统将模板中的占位符替换为具体数据源引用
3. 生成新的可执行方案实例并持久化

```csharp
public interface IPlanTemplateManager
{
    Task<AnalysisPlan> CreateFromTemplateAsync(string templateId, IReadOnlyDictionary<string, object> bindings);
    Task SaveAsTemplateAsync(AnalysisPlan plan, string templateName);
    Task ImportTemplateAsync(string filePath);
    Task ExportTemplateAsync(string templateId, string filePath);
}
```

模板支持导入导出，便于团队内部分享和复用成熟的分析方案。

> 模板与方案使用相同的 `AnalysisPlan` 模型，通过存储位置（模板目录 vs 方案目录）区分类型，无需在模型中增加 `PlanType` 字段。

#### 5.2.5 方案校验

方案校验分为两层——Schema 校验和业务规则校验，在校验通过后方能进入执行阶段。

**Schema 校验：** 基于 JSON Schema 验证配置文件的结构合法性，包括必填字段检查、类型匹配、枚举值合法性等。

**业务规则校验检查清单：**

| 校验项 | 说明 | 错误级别 |
|--------|------|----------|
| 算子存在性检查 | 方案引用的每个 `OperatorId` 必须在算子池中存在 | Error |
| 输入绑定完整性 | 每个分析项的 `Inputs` 绑定不得有空引用或悬挂引用 | Error |
| 输出绑定完整性 | 每个分析项必须指定 `Output` 目标 | Warning |
| DAG 无环检查 | 数据依赖关系必须构成有向无环图，严禁循环依赖 | Error |
| 参数边界校验 | 每个参数值必须在 `ParameterConstraint` 定义的范围内 | Error |
| 子方案引用校验 | `SubPlans` 引用的方案必须存在且可加载 | Error |
| CRS 一致性预检 | 输入数据源的 CRS 不一致且已配置转换策略 | Warning |
| CRS 一致性预检 | 输入数据源的 CRS 不一致且未配置转换策略 | Error |

**预执行校验（Dry-Run）：** 支持在执行前进行轻量级预校验，模拟数据源连接测试和算子初始化，提前发现运行时问题：

```csharp
public sealed record DryRunResult
{
    public bool Passed { get; init; }
    public IReadOnlyList<DryRunCheck> Checks { get; init; }
}

public sealed record DryRunCheck
{
    public string CheckType { get; init; }
    public bool Success { get; init; }
    public string? Message { get; init; }
}
```

### 5.3 数据源适配器

已规划适配器：

| 适配器 | 数据源 | 说明 |
|--------|--------|------|
| PostGISAdapter | PostgreSQL + PostGIS | 主力空间数据库，支持完整空间运算下推 |
| OracleSpatialAdapter | Oracle Spatial | 存量系统适配，使用率逐步降低 |
| MySqlAdapter | MySQL 8.0+ | 基础空间数据读取，利用 InnoDB R-tree 索引 |
| SqlServerAdapter | SQL Server | 通过 geometry/geography 类型支持空间数据 |
| ShapefileAdapter | Shapefile | 处理字段名长度限制、多几何类型等固有限制 |
| GdbAdapter | File / Enterprise GDB | File GDB 必须支持；Enterprise GDB（SDE）为可选扩展 |
| GeoJsonAdapter | GeoJSON | Web 端常用交换格式，对前端友好 |
| WfsAdapter | OGC WFS | 优先支持读取，写入由上层业务系统控制 |
| InMemoryAdapter | 中间结果缓存 | 内存缓存，支持溢出到临时文件 |
| RestApiAdapter | REST API / Web API | 外部 HTTP 接口适配，注意可控性较低 |

统一要求：

- 所有适配器实现 `IFeatureSource`
- 必须提供空间参考信息
- 支持分页或流式读取
- 允许将过滤条件尽可能下推到数据源

#### 5.3.1 数据源差异屏蔽机制

**统一抽象：** 所有适配器实现 `IFeatureSource`，向上层暴露一致的要素读取能力。算子仅与 `IFeatureSource` 交互，无需感知底层数据来自数据库、文件还是服务。

**CRS 一致性检查：** 执行前框架自动检查所有输入数据源的 CRS。流程如下：
1. 收集方案中所有数据源的 CRS 声明
2. 若所有 CRS 一致，直接进入执行
3. 若存在不一致，检查方案是否配置了转换目标 CRS
4. 若已配置，通过 ProjNet 自动执行投影转换（生成转换后的 `InMemoryAdapter`）
5. 若未配置，方案校验时给出 Warning 提示

**字段映射机制：** 不同数据源的字段命名和类型体系各不相同。适配器内部维护原始字段到框架统一字段的映射表：

```csharp
public sealed class FieldMapping
{
    public string SourceField { get; init; }       // 数据源原始字段名
    public string TargetField { get; init; }       // 框架统一字段名
    public FieldType SourceType { get; init; }
    public FieldType TargetType { get; init; }
    public Func<object?, object?>? Converter { get; init; } // 自定义转换器
}
```

字段映射由适配器在初始化时建立，上层算子通过统一字段名访问属性，无需处理不同数据源的命名差异。

**过滤条件下推策略：**

| 过滤类型 | 下推方式 | 适用数据源 |
|----------|----------|-----------|
| 空间范围（BBox） | 转换为 SQL WHERE（ST_Intersects 等） | PostGIS、Oracle Spatial、SQL Server |
| 空间范围（BBox） | 通过 `.qix` 索引 + 内存过滤 | Shapefile |
| 属性过滤（表达式） | 转换为 SQL WHERE 子句 | 所有关系型数据库 |
| 属性过滤（表达式） | LINQ 表达式树遍历 + 内存过滤 | 文件型、服务型数据源 |

过滤条件下推通过 `IFeatureSource.GetFeaturesAsync` 的 `boundingBox` 和 `filterExpression` 参数声明，适配器根据自身能力决定是否支持原生下推——不支持的过滤条件由框架在内存中回退执行。

#### 5.3.2 大数据量读取设计

**分页读取：** 支持按要素数量分页和按空间范围分页两种模式：

```csharp
public interface IPageableFeatureSource : IFeatureSource
{
    IAsyncEnumerable<IFeature> GetFeaturesByPageAsync(
        int pageSize,
        int pageIndex,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<IFeature> GetFeaturesByExtentAsync(
        Envelope extent,
        CancellationToken cancellationToken = default);
}
```

**流式读取：** `IFeatureSource.GetFeaturesAsync` 返回 `IAsyncEnumerable<IFeature>`，数据按需逐条消费（yield return），框架内部通过 `Channel<T>` 实现流水线传递，不在内存中预缓存全部结果。

**空间索引利用：**

| 数据源 | 索引类型 | 利用方式 |
|--------|----------|----------|
| PostGIS | GiST 索引 | SQL WHERE 子句自动走索引 |
| MySQL 8.0+ | InnoDB R-tree | MBRContains / ST_Within 查询 |
| SQL Server | Spatial Index | geometry::STIntersects 走空间索引 |
| Shapefile | .qix / .shp 空间索引 | 优先读取索引文件缩小扫描范围 |
| 内存数据 | NTS STRtree | 算子按需创建临时索引 |

**内存控制策略：** 通过有界 `Channel<T>` 实现背压控制——当消费端处理速度低于生产端时，Channel 容量达到上限自动阻塞生产端，防止内存失控。单分析项默认 Channel 容量为 4096 条要素，可通过 `ItemExecutionPolicy` 配置调整。

#### 5.3.3 中间数据格式

**WKT（Well-Known Text）：** 作为几何对象在算子间传递的轻量中间格式。WKT 以纯文本形式表示几何信息（如 `"POINT (116.4 39.9)"`），无需依赖特定二进制协议，适合日志输出和跨进程传递。WKT 仅承载几何信息，不包含属性和坐标系。

**WktConverter 工具类设计：**

```csharp
public static class WktConverter
{
    public static string ToWkt(Geometry geometry);
    public static Geometry FromWkt(string wkt);
    public static bool TryParse(string wkt, out Geometry? geometry);
}
```

对于需要传递完整要素（几何 + 属性）的场景，统一使用 `IFeature` 模型通过 `InMemoryAdapter` 传递。

#### 5.3.4 几何类型支持与降级

**支持的 OGC 几何类型：**

| 类型 | 英文名 | 说明 |
|------|--------|------|
| 点 | Point | 0 维单点 |
| 多点 | MultiPoint | 多个 Point 的集合 |
| 线 | LineString | 1 维线要素 |
| 多线 | MultiLineString | 多个 LineString 的集合 |
| 面 | Polygon | 2 维面要素（含环） |
| 多面 | MultiPolygon | 多个 Polygon 的集合 |
| 几何集合 | GeometryCollection | 任意几何类型的混合集合 |

**降级策略：** 对于不直接支持的几何类型，适配器在读取时执行降级转换：

| 原始类型 | 降级目标 | 降级方式 |
|----------|----------|----------|
| Curve / CircularString | LineString | 按容差进行线性插值逼近 |
| CompoundCurve | LineString | 拆分为各段并线性化 |
| Surface | Polygon | 提取外边界构造等价 Polygon |
| CurvePolygon | Polygon | 对所有环执行线性化 |
| TIN / PolyhedralSurface | GeometryCollection | 拆分为子三角形集合 |

降级转换由适配器层自动完成，上层算子无需感知降级逻辑。若降级可能引起精度损失，适配器在日志中输出 Warning。

#### 5.3.5 连接配置管理

**连接字符串结构：** 连接信息统一通过连接配置模型管理：

```csharp
public sealed class ConnectionConfig
{
    public string DataSourceId { get; init; }
    public string AdapterType { get; init; }
    public string Host { get; init; }
    public int Port { get; init; }
    public string Database { get; init; }
    public string UserName { get; init; }
    public string? EncryptedPassword { get; init; }
    public IReadOnlyDictionary<string, string> AdditionalOptions { get; init; }
}
```

> `EncryptedPassword` 字段应在反序列化或构造时校验——不接受明文值。可考虑通过专用的 `SensitiveString` 封装类型强制加密语义，或通过 `ISecretProvider` 在构造时自动加密。

**敏感字段加密存储：** 密码等敏感字段通过 .NET Data Protection API（DPAPI）在本地加密，或通过 Azure Key Vault / HashiCorp Vault 在服务端解密。连接字符串仅在运行时解密到内存，持久化存储中始终保持密文。

```csharp
public interface IConnectionEncryption
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}
```

### 5.4 输出适配器

| 适配器 | 输出目标 | 说明 |
|--------|----------|------|
| PostGISWriter | PostgreSQL 表 | 持久化分析结果，供下游系统消费 |
| ShapefileWriter | Shapefile 文件 | 数据交换与分发，注意字段名和几何类型限制 |
| GeoJsonWriter | GeoJSON 文件或 HTTP 响应 | Web 端最友好的输出格式 |
| CsvWriter | CSV 报表 | 属性数据导出，不含几何信息 |
| GeoPackageWriter | GeoPackage | 标准化单文件多图层交换格式 |
| ObsS3Writer | 对象存储（OBS / S3） | 生产环境大文件存储，分布式高可用，自动容灾 |
| ConsoleWriter | 调试输出 | 轻量级文本输出，用于开发调试 |

#### 5.4.1 输出配置能力

**字段子集选择：** 通过 `OutputBinding.FieldSelection` 指定输出字段白名单，非白名单字段不写入目标，减少输出体积并加速写入：

```csharp
public sealed class OutputBinding
{
    public string AdapterType { get; init; }
    public string TargetPath { get; init; }
    public ConnectionConfig? ConnectionConfig { get; init; }
    public IReadOnlyList<string>? FieldSelection { get; init; } // null = 全量输出
    public bool IsIntermediate { get; init; } = false;           // 标记为中间结果输出
    public OutputFormatOptions? FormatOptions { get; init; }
}
```

**数值精度控制：** `OutputFormatOptions` 提供输出格式细节配置：

```csharp
public sealed class OutputFormatOptions
{
    public int? DecimalPlaces { get; init; }        // 数值小数位精度
    public string? DateFormat { get; init; }         // 日期格式化字符串，如 "yyyy-MM-dd"
    public string? Encoding { get; init; }           // 字符编码，默认 UTF-8
    public bool WriteHeader { get; init; } = true;   // CSV/表格类是否写入表头
}
```

**中间结果输出：** 质检场景中，中间计算结果对问题溯源具有重要价值。`OutputBinding` 支持标记 `IsIntermediate = true` 以将中间结果输出到指定位置：

- 中间结果默认输出到独立子路径（如 `intermediate/{planId}/{itemId}/`），不与最终结果混淆
- 中间结果的保留与否由 `ItemExecutionPolicy.RetainIntermediateResults` 控制
- QC 模式下默认保留所有中间结果，Analysis 模式下默认不保留

### 5.5 插件扩展

- 算子插件实现 `IOperator`
- 数据源插件实现 `IFeatureSource`
- 输出插件实现 `IFeatureSink`
- 通过 `AssemblyLoadContext` 实现插件发现、隔离和卸载

## 6. 核心接口设计

### 6.1 算子接口

`OperatorMetadata` 的完整定义见 §5.1.2，此处仅列出算子执行接口：

```csharp
public interface IOperator
{
    OperatorMetadata Metadata { get; }
    ValidationResult Validate(AnalysisItem config);
    Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken);
}
```

### 6.2 分析项与方案

```csharp
public sealed class AnalysisItem
{
    public string Id { get; init; }
    public string OperatorId { get; init; }
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
    public OutputBinding Output { get; init; }
    public ItemExecutionPolicy ExecutionPolicy { get; init; }
}

public sealed class AnalysisPlan
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public PlanConfig Config { get; init; }
    public IReadOnlyList<AnalysisItem> Items { get; init; }
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; }
    public PlanExecutionPolicy ExecutionPolicy { get; init; }
}
```

### 6.3 数据源与输出

```csharp
public interface IFeature
{
    string Id { get; }
    Geometry Geometry { get; }
    IReadOnlyDictionary<string, object?> Attributes { get; }
}

public interface IFeatureSource
{
    FeatureSourceMetadata Metadata { get; }
    Envelope BoundingBox { get; }
    ISpatialReference SpatialReference { get; }
    Task<long> GetFeatureCountAsync();
    IAsyncEnumerable<IFeature> GetFeaturesAsync(
        Envelope? boundingBox = null,
        string? filterExpression = null,
        CancellationToken cancellationToken = default);
}

public interface IFeatureSink
{
    Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken);
    Task WriteAsync(IFeature feature, CancellationToken cancellationToken);
    Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
```

### 6.4 执行结果与校验结果

```csharp
public sealed record ExecutionResult
{
    public ExecutionStatus Status { get; init; }
    public IReadOnlyDictionary<string, IFeatureSource> Outputs { get; init; }
    public IReadOnlyList<ExecutionLogEntry> Logs { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; }
}
```

### 6.5 基础类型定义

```csharp
// Input/Output schemas for operator compatibility
public sealed record InputSchema
{
    public IReadOnlyList<FieldDefinition> RequiredFields { get; init; }
    public GeometryType? RequiredGeometryType { get; init; }
    public string? Description { get; init; }
}

public sealed record OutputSchema
{
    public IReadOnlyList<FieldDefinition> ProducedFields { get; init; }
    public GeometryType? ProducedGeometryType { get; init; }
    public string? Description { get; init; }
}

public sealed record FieldDefinition
{
    public string Name { get; init; }
    public FieldType Type { get; init; }
    public bool Required { get; init; }
}

public enum FieldType { String, Integer, Double, DateTime, Boolean, Geometry }

public enum GeometryType { Point, MultiPoint, LineString, MultiLineString, Polygon, MultiPolygon, GeometryCollection }

// Input binding (used in DAG construction)
public sealed record InputBinding
{
    public BindingType Type { get; init; }
    public string SourceId { get; init; }          // DataSourceId, upstream ItemId, or sub-plan PlanId
    public string? OutputKey { get; init; }         // which output of the upstream source to consume
}

public enum BindingType { External, Upstream, SubPlan }

// Execution status
public enum ExecutionStatus { Pending, Queued, Executing, Success, Failed, Canceled, Retrying }

// Validation error
public sealed record ValidationError
{
    public string Code { get; init; }
    public string Message { get; init; }
    public string? Location { get; init; }  // which field/path in the config
}

// Feature source metadata
public sealed record FeatureSourceMetadata
{
    public string SourceId { get; init; }
    public string SourceType { get; init; }
    public long? FeatureCount { get; init; }
    public string? Description { get; init; }
}

// Spatial reference (simplified)
public interface ISpatialReference
{
    string Authority { get; }     // e.g., "EPSG"
    int Code { get; }             // e.g., 4326
    string Wkt { get; }           // full WKT representation
}
```

### 6.6 QC 相关模型

```csharp
public sealed class IssueRecord
{
    public string IssueId { get; init; }        // 问题唯一 ID
    public string ItemId { get; init; }          // 所属分析项 ID
    public string FeatureId { get; init; }       // 违规要素 ID
    public string IssueType { get; init; }       // 问题类型，如 "TopologyOverlap"
    public IssueSeverity Severity { get; init; } // 严重级别
    public string Description { get; init; }     // 问题描述
    public IReadOnlyDictionary<string, object?> ContextData { get; init; } // 上下文数据
    public Geometry? ViolationGeometry { get; init; } // 违规位置（可选）
}

public enum IssueSeverity
{
    Error,      // 必须修复
    Warning,    // 建议修复
    Info        // 提示信息
}

public sealed record QualityReport
{
    public double TotalScore { get; init; }                           // 综合评分 0-100
    public IReadOnlyDictionary<string, RuleLevelStats> RuleStats { get; init; } // 各规则统计
    public IReadOnlyList<IssueRecord> Issues { get; init; }           // 问题清单
    public ExecutionMetadata Metadata { get; init; }                  // 执行元数据
}

public sealed record RuleLevelStats
{
    public string RuleId { get; init; }
    public long TotalChecked { get; init; }
    public long Passed { get; init; }
    public long Failed { get; init; }
    public double PassRate { get; init; }
}

public sealed record ExecutionMetadata
{
    public string PlanId { get; init; }
    public string PlanVersion { get; init; }
    public string OperatorVersion { get; init; }
    public string DataSourceVersion { get; init; }
    public DateTimeOffset ExecutionTime { get; init; }
}
```

### 6.7 执行统计模型

```csharp
public sealed record PlanExecutionStatistics
{
    public DateTimeOffset StartTime { get; init; }
    public DateTimeOffset EndTime { get; init; }
    public TimeSpan TotalElapsed { get; init; }
    public IReadOnlyList<PerItemStats> ItemStats { get; init; }
    public QcStatistics? QcStats { get; init; }
    public ResourceUsage? ResourceUsage { get; init; }
}

public sealed record PerItemStats
{
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public TimeSpan Elapsed { get; init; }
    public long FeaturesProcessed { get; init; }
    public long SuccessCount { get; init; }
    public long FailedCount { get; init; }
    public long SkippedCount { get; init; }
}

public sealed record QcStatistics
{
    public int TotalIssues { get; init; }
    public IReadOnlyDictionary<string, int> IssuesBySeverity { get; init; }  // "Error": 12, "Warning": 5
    public IReadOnlyDictionary<string, int> IssuesByCategory { get; init; }  // "Topology": 8, "Attribute": 4
}

public sealed record ResourceUsage
{
    public long PeakMemoryBytes { get; init; }
    public double? AvgCpuPercent { get; init; }
    public long DataReadBytes { get; init; }
}
```

### 6.8 错误码定义

```csharp
public static class ErrorCode
{
    // 配置错误
    public const string CfgSchemaInvalid     = "ERR_CFG_SCHEMA_INVALID";
    public const string CfgOperatorNotFound  = "ERR_CFG_OPERATOR_NOT_FOUND";
    public const string CfgParamOutOfRange   = "ERR_CFG_PARAM_OUT_OF_RANGE";
    public const string CfgDagCycle          = "ERR_CFG_DAG_CYCLE";
    public const string CfgBindingIncomplete = "ERR_CFG_BINDING_INCOMPLETE";

    // 数据源错误
    public const string DsConnectionFailed   = "ERR_DS_CONNECTION_FAILED";
    public const string DsPermissionDenied   = "ERR_DS_PERMISSION_DENIED";
    public const string DsFormatInvalid      = "ERR_DS_FORMAT_INVALID";
    public const string DsCrsNotDeclared     = "ERR_DS_CRS_NOT_DECLARED";

    // 运行时错误
    public const string RtTimeout            = "ERR_RT_TIMEOUT";
    public const string RtOutOfMemory        = "ERR_RT_OUT_OF_MEMORY";
    public const string RtUnexpected         = "ERR_RT_UNEXPECTED";
    public const string RtCancelled          = "ERR_RT_CANCELLED";

    // 数据错误
    public const string DataGeometryInvalid  = "ERR_DATA_GEOMETRY_INVALID";
    public const string DataFieldMissing     = "ERR_DATA_FIELD_MISSING";
    public const string DataValueOutOfRange  = "ERR_DATA_VALUE_OUT_OF_RANGE";
}
```

### 6.9 执行策略模型

```csharp
public sealed class ItemExecutionPolicy
{
    public int MaxRetries { get; init; } = 0;           // 最大重试次数
    public TimeSpan RetryInterval { get; init; } = TimeSpan.FromSeconds(5);
    public bool ExponentialBackoff { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30); // 单分析项超时
    public LogGranularity LogGranularity { get; init; } = LogGranularity.Item;
    public bool RetainIntermediateResults { get; init; } = false;
    public bool QcMode { get; init; } = false;          // QC 模式开关
}

public sealed class PlanExecutionPolicy
{
    public int MaxParallelism { get; init; } = 4;
    public GlobalConcurrencyPolicy? GlobalConcurrency { get; init; }
    public FailurePolicy FailurePolicy { get; init; } = FailurePolicy.StopOnAny;
    public bool EnablePartitioning { get; init; } = false;
    public int PartitionCount { get; init; } = 8;
}

public enum LogGranularity
{
    Plan,   // 方案级：仅记录方案开始/结束/统计
    Item,   // 分析项级（默认）：记录每个分析项的执行情况
    Feature // 要素级（QC 模式默认）：记录每个要素的处理结果
}

public enum FailurePolicy
{
    StopOnAny,          // 任一分析项失败即终止方案
    ContinueIndependent // 继续执行无依赖的分析项
}

public sealed class GlobalConcurrencyPolicy
{
    public int MaxGlobalParallelism { get; init; } = 16;  // 全局最大并行分析项数
    public bool Enabled { get; init; } = true;
}
```

> `TimeSpan` 类型在 System.Text.Json 中需自定义转换器或使用 ISO 8601 持续时间格式（如 `"PT30M"` 表示 30 分钟）。方案配置文件中的时间配置推荐使用秒数为单位的整数或 ISO 8601 格式。

## 7. 执行模型设计

### 7.1 DAG 构建

执行流程：

1. 解析方案中的输入绑定
2. 基于 `FromUpstream` 和 `FromSubPlan` 建立依赖边
3. 检测循环依赖
4. 计算拓扑排序和执行层级
5. 生成可调度执行计划

### 7.2 串并行调度

- 同一拓扑层中的分析项可并行执行
- 不同层按依赖顺序执行
- 并行度由 `PlanConfig.MaxParallelism` 控制
- 调度器负责资源分配、状态更新和失败传播

### 7.3 执行上下文

```csharp
public sealed class ExecutionContext
{
    public string PlanId { get; init; }
    public string ExecutionId { get; init; }
    public IResultCache ResultCache { get; init; }
    public ILogger Logger { get; init; }
    public IServiceProvider Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
    public PlanExecutionStatistics Statistics { get; init; }
}
```

### 7.4 状态机

分析项生命周期：

`Pending -> Queued -> Executing -> Success/Failed/Canceled`

失败后可依据重试策略回到 `Retrying -> Executing`，超过阈值后进入最终失败状态。

### 7.5 并发与背压

- 使用 `SemaphoreSlim` 控制执行并发度
- 使用有界 `Channel<T>` 承接流水线数据，避免内存失控
- 当下游阻塞或资源压力过高时，对上游形成自然背压

### 7.6 优雅关闭

- 停止接受新的调度任务
- 向执行中的算子传播取消令牌
- 持久化必要的中间结果和检查点
- 支持后续恢复执行

### 7.7 超时控制

每个分析项可配置独立超时时间（`ItemExecutionPolicy.Timeout`），实现如下：

```csharp
public sealed class TimeoutController
{
    public static async Task<ExecutionResult> ExecuteWithTimeoutAsync(
        Func<CancellationToken, Task<ExecutionResult>> execution,
        TimeSpan timeout,
        CancellationToken externalCancellation)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellation);
        cts.CancelAfter(timeout);

        try
        {
            return await execution(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !externalCancellation.IsCancellationRequested)
        {
            return new ExecutionResult
            {
                Status = ExecutionStatus.Failed,
                ErrorCode = ErrorCode.RtTimeout,
                ErrorMessage = $"分析项执行超时（{timeout.TotalSeconds:F0}s）"
            };
        }
    }
}
```

超时触发后自动终止算子并标记为 Failed。超时取消与外部手动取消通过 `CancellationTokenSource.CreateLinkedTokenSource` 统一处理，二者行为一致。

### 7.8 全局并发控制

使用框架级 `SemaphoreSlim` 限制所有方案的并行执行总数，防止资源争抢：

```csharp
public sealed class GlobalConcurrencyController
{
    private readonly SemaphoreSlim _semaphore;

    public GlobalConcurrencyController(GlobalConcurrencyPolicy policy)
    {
        _semaphore = new SemaphoreSlim(policy.MaxGlobalParallelism);
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new SemaphoreReleaser(_semaphore);
    }

    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _disposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (!_disposed)
            {
                _semaphore.Release();
                _disposed = true;
            }
        }
    }
}
```

执行引擎在启动分析项前通过 `AcquireAsync` 获取全局槽位，执行完成后自动释放。

### 7.9 失败重试机制

重试策略集成进状态机，失败后按配置自动重试：

**状态流转：**
`Pending → Queued → Executing → (Failed → Retrying → Executing) × N → FinalFailed`

**实现要点：**
- 重试次数通过 `ItemExecutionPolicy.MaxRetries` 控制，最大重试次数内进入 `Retrying` 状态
- 重试间隔由 `RetryInterval` 设定，开启 `ExponentialBackoff` 时每次重试等待时间翻倍
- 超过最大重试次数后进入 `FinalFailed`，传播至方案级由 `FailurePolicy` 决定是否终止整个方案
- 仅系统运行时错误（`ERR_RT_*`）触发重试；配置错误（`ERR_CFG_*`）和数据源错误（`ERR_DS_*`）不重试，直接失败

### 7.10 错误码体系

**统一格式：** `ERR_CATEGORY_SPECIFIC`，如 `ERR_CFG_OPERATOR_NOT_FOUND`。

**错误分类与传播策略：**

| 类别 | 前缀 | 传播策略 | 是否重试 |
|------|------|----------|----------|
| 配置错误 | `ERR_CFG_` | 方案预校验阶段即拒绝，不进入执行 | 否 |
| 数据源错误 | `ERR_DS_` | 终止整个方案执行 | 否 |
| 运行时错误 | `ERR_RT_` | 按重试策略处理，超阈值后终止方案 | 是 |
| 数据错误 | `ERR_DATA_` | 仅跳过当前要素，继续处理后续（QC 场景记录到问题清单） | 否 |

错误码定义详见 §6.8。

### 7.11 性能优化设计

**结果缓存：** 支持对中间计算结果进行缓存，避免相同输入条件下重复计算。缓存键由输入数据哈希和参数组合生成：

```csharp
public interface IResultCache
{
    Task<T?> GetOrComputeAsync<T>(string cacheKey, Func<Task<T>> factory, TimeSpan? ttl = null);
    Task InvalidateAsync(string cacheKeyPrefix);
}
```

**数据分区并行：** 将大数据集按空间范围或属性分组，在各分区上并行执行同一分析项：

```csharp
public interface IPartitionStrategy
{
    IReadOnlyList<Envelope> Partition(Envelope extent, int partitionCount);
}
```

分区由 `PlanExecutionPolicy` 配置（如 `EnablePartitioning = true`, `PartitionCount = 8`），调度引擎自动拆分数据集并合并分区结果。

**增量计算（可选）：** 对于仅部分数据变更的场景，通过比较输入数据的变更标记（如时间戳、版本号），仅对变更部分重新计算。增量计算为可选能力，需算子声明支持 `SupportsIncremental = true` 方可启用。

## 8. 配置模型设计

### 8.1 方案配置结构

方案配置至少包含以下部分：

- 基本信息：`id`、`name`、`version`
- 运行配置：`config`
- 执行策略：`executionPolicy`
- 子方案：`subPlans`
- 分析项集合：`items`

### 8.2 输入绑定模型

| 类型 | 说明 |
|------|------|
| external | 从外部数据源读取 |
| upstream | 引用上游分析项输出 |
| subPlan | 引用子方案输出 |

### 8.3 输出绑定模型

输出定义至少包含：

- 输出适配器类型
- 输出目标位置
- 可选字段裁剪
- 可选格式说明

## 9. 质检能力设计

### 9.1 质检模式

质检模式是普通分析模式的增强版本，增加：

- 更细粒度日志
- 中间结果保留
- 差异对比
- 数据血缘
- 质量评分

### 9.2 关键模型

```csharp
public sealed record ExecutionLogEntry
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }
    public string? FeatureId { get; init; }
}

public sealed record LineageRecord
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public IReadOnlyList<string> SourceFeatureIds { get; init; }
    public string OutputFeatureId { get; init; }
    public string TransformType { get; init; }
}
```

### 9.3 质检规则覆盖范围

| 规则类别 | 检查目标 | 典型规则示例 |
|----------|----------|--------------|
| 拓扑检查 | 要素间空间关系是否正确 | 面不能重叠、线必须闭合、点必须在面内 |
| 属性完整性 | 必填字段是否有值 | 必填字段非空检查、字段值域范围检查 |
| 属性一致性 | 字段间逻辑是否一致 | 行政区划代码与名称匹配、面积与周长合理性 |
| 空间一致性 | 空间位置与属性描述是否一致 | 坐标落点是否在声称的行政区内 |
| 几何有效性 | 几何对象本身是否合法 | 自相交检查、环方向检查、退化几何检查 |
| 重复检查 | 是否存在重复要素 | 完全重复、几何重复（属性不同）、属性重复（几何不同） |
| 精度检查 | 数据精度是否满足要求 | 坐标精度、面积精度、几何节点密度 |

### 9.4 问题清单结构

质检发现的问题以结构化清单输出，每条问题包含：

| 字段 | 类型 | 说明 |
|------|------|------|
| IssueId | string | 问题唯一编号 |
| ItemId | string | 所属分析项 ID |
| FeatureId | string | 违规要素 ID |
| IssueType | string | 问题类型分类，如 `"TopologyOverlap"` |
| Severity | IssueSeverity | 严重级别（Error / Warning / Info） |
| Description | string | 人类可读的问题描述 |
| ContextData | Dictionary | 相关上下文数据（如阈值、实际值、期望值） |
| ViolationGeometry | Geometry? | 违规发生的几何位置（可选） |

对应模型定义详见 §6.6 `IssueRecord`。

### 9.5 质量评分算法

基于加权扣分法计算综合质量评分（0-100 分）：

```
Score = max(0, 100 - Σ(Weight_i × ViolationCount_i / (TotalChecked_i + ε)))
```

其中：
- `Weight_i`：第 i 条规则的权重，可配置，默认所有规则等权（Weight = 100 / RuleCount）
- `ViolationCount_i`：该规则发现的问题数
- `TotalChecked_i`：该规则检查的要素总数
- `ε`：极小正值，避免除零

规则权重可通过 `QualityReportConfig` 配置调整，以体现不同规则的重要性差异：

```csharp
public sealed class QualityReportConfig
{
    public IReadOnlyDictionary<string, double> RuleWeights { get; init; }
    public double MinPassRate { get; init; } = 0.95; // 最低合格率阈值
}
```

### 9.6 版本追溯设计

**追溯链：** 每次执行记录四要素形成可追溯的版本链：

```text
PlanVersion + OperatorVersion + DataSourceVersion + ExecutionTime
```

**追溯模型：**

```csharp
public sealed record VersionTraceRecord
{
    public string ExecutionId { get; init; }
    public string PlanId { get; init; }
    public string PlanVersion { get; init; }
    public IReadOnlyDictionary<string, string> OperatorVersions { get; init; } // ItemId → Version
    public IReadOnlyDictionary<string, string> DataSourceVersions { get; init; } // DataSourceId → Version
    public DateTimeOffset ExecutionTime { get; init; }
}
```

**跨版本结果对比：** 支持同一方案对不同版本数据执行质检的结果对比，用于数据质量趋势分析和变化检测：

```csharp
public interface IVersionDiffService
{
    Task<QualityReportDiff> CompareAsync(string executionA, string executionB);
}
```

### 9.7 质检与分析统一

同一算子在方案配置中可按需在两种模式下运行，无需为同一能力开发两套算子：

| 模式 | `QcMode` 标志 | 行为差异 |
|------|--------------|----------|
| 分析模式（默认） | `false` | 默认分析项级日志，不保留中间结果，产出分析结果集 |
| 质检模式 | `true` | 启用要素级细粒度日志，保留中间结果，产出问题清单 + 质量报告 |

通过 `ItemExecutionPolicy.QcMode` 标志切换模式。框架运行时检测该标志：

- `QcMode = true`：自动启用要素级日志（`LogGranularity = Feature`），启用中间结果保留（`RetainIntermediateResults = true`），执行结束后自动生成 `QualityReport`
- `QcMode = false`：沿用默认执行策略

核心执行引擎不区分分析/质检——仅根据 Policy 配置调整运行行为，保证框架统一性。

## 10. 空间参考与几何处理

### 10.1 空间参考

- 每个 `FeatureSource` 必须声明 CRS
- 执行前检查输入数据 CRS 一致性
- 必要时通过 ProjNet 执行自动转换

### 10.2 几何支持

基于 NTS 支持：

- Point / MultiPoint
- LineString / MultiLineString
- Polygon / MultiPolygon
- GeometryCollection

### 10.3 空间索引

- 大数据集查询优先使用空间索引
- 支持 STRtree 等索引结构
- 索引建立可由算子按需触发

## 11. 性能、容错与安全设计

### 11.1 性能设计

- 基于 `IAsyncEnumerable` 做流式处理
- 支持数据分块和属性下推
- 支持结果缓存和增量计算
- 支持数据分区并行和流水线并行

### 11.2 错误处理

错误分为配置错误、数据源错误、运行时错误、数据错误四类。  
框架使用统一错误码体系，便于日志检索、告警和自动化处理。

### 11.3 安全设计

- 敏感连接信息加密存储
- 数据源访问受权限控制
- API 层支持认证、限流和输入校验
- 执行记录纳入审计日志

### 11.4 报告生成设计

报告生成子系统支持三种输出格式，满足不同消费场景：

| 格式 | 用途 | 生成方式 |
|------|------|----------|
| JSON | 结构化数据，供下游系统编程消费 | 直接序列化 `QualityReport` / `PlanExecutionStatistics` |
| HTML | 可视化展示，支持浏览器查看 | 基于 Razor/DotLiquid 模板引擎渲染 |
| PDF | 可打印的正式报告 | 通过 PuppeteerSharp 或 Playwright 将 HTML 转为 PDF |

> PuppeteerSharp/Playwright 引入完整 Chromium 实例，部署体积较大。对于轻量部署场景，可替换为 QuestPDF 或 iTextSharp 等纯 .NET PDF 库。框架通过 `IReportGenerator` 接口抽象报告生成，具体实现可替换。

**报告内容结构：**

```csharp
public interface IReportGenerator
{
    Task<Stream> GenerateJsonAsync(QualityReport report, CancellationToken ct);
    Task<Stream> GenerateHtmlAsync(QualityReport report, CancellationToken ct);
    Task<Stream> GeneratePdfAsync(QualityReport report, CancellationToken ct);
}
```

**报告内容要素：** 综述（总体评分、执行概况）、规则级统计（各规则通过率、问题分布图表）、问题清单（可筛选、可排序）、执行摘要（耗时、资源使用、数据量）。

### 11.5 审计日志设计

审计日志与执行日志分离存储，记录关键管理操作，不可篡改：

```csharp
public sealed record AuditLogEntry
{
    public string EntryId { get; init; }
    public string Operator { get; init; }         // 操作者
    public DateTimeOffset Timestamp { get; init; } // 操作时间
    public AuditOperationType OperationType { get; init; } // 操作类型
    public string TargetId { get; init; }          // 操作目标（PlanId / DataSourceId 等）
    public string? TargetName { get; init; }
    public AuditResult Result { get; init; }       // 成功/失败
    public string? Details { get; init; }          // 操作详情
}

public enum AuditOperationType
{
    PlanCreated, PlanModified, PlanDeleted, PlanCopied,
    ExecutionStarted, ExecutionCancelled, ExecutionCompleted,
    DataSourceConnected, DataSourceDisconnected,
    TemplateCreated, TemplateImported, TemplateExported,
    PluginLoaded, PluginUnloaded
}

public enum AuditResult { Success, Failure }

public interface IAuditLogger
{
    Task LogAsync(AuditLogEntry entry, CancellationToken ct);
    IAsyncEnumerable<AuditLogEntry> QueryAsync(AuditQueryFilter filter, CancellationToken ct);
}
```

审计日志持久化到独立的存储（数据库或专用日志文件），与执行日志物理隔离。日志写入采用追加模式，不支持修改或删除。

### 11.6 凭据加密方案

**本地部署：** 使用 .NET Data Protection API（DPAPI）对敏感字段（密码、Token）进行加密。DPAPI 基于当前机器/用户上下文自动管理密钥，适合单机部署。

**服务化部署：** 集成 Azure Key Vault 或 HashiCorp Vault：

```csharp
public interface ISecretProvider
{
    Task<string> GetSecretAsync(string secretId, CancellationToken ct);
    Task<string> EncryptAsync(string plainText, CancellationToken ct);
    Task<string> DecryptAsync(string cipherText, CancellationToken ct);
}
```

**加密流程：**
1. 存储阶段：明文 → `ISecretProvider.EncryptAsync` → 密文持久化到配置文件
2. 运行时：从配置文件读取密文 → `ISecretProvider.DecryptAsync` → 明文仅存于内存
3. 日志/序列化输出中自动脱敏，密码字段替换为 `"***"`

### 11.7 API 安全设计

**认证方式：** API 层支持多种认证机制，通过中间件链式处理：

| 认证方式 | 适用场景 | 实现 |
|----------|----------|------|
| API Key | 内部服务调用、脚本集成 | Header `X-Api-Key` 校验 |
| JWT | 用户登录、前端集成 | Bearer Token 校验，支持过期和刷新 |
| OAuth2 | 第三方授权 | 委托给 Identity Provider（如 Azure AD、Keycloak） |

**限流机制：** 基于令牌桶算法（Token Bucket）实现，通过中间件在每个请求到达时检查桶内令牌数：

```csharp
public sealed class RateLimitConfig
{
    public int TokensPerSecond { get; init; } = 100;
    public int BucketSize { get; init; } = 200;
    public IReadOnlyDictionary<string, int> PerEndpointLimits { get; init; } // 不同端点的独立限流
}
```

**输入校验：** 所有 API 入参经过验证管道，包括：
- JSON Schema 校验（方案配置）
- 参数类型与范围校验
- SQL/脚本注入防护（过滤 `';--`、`<script>` 等模式）
- 文件路径穿越防护

### 11.8 数据源权限模型

框架通过 `IDataSourceAccessControl` 接口抽象数据源访问权限，支持对接外部权限系统：

```csharp
public interface IDataSourceAccessControl
{
    Task<bool> CanReadAsync(string userId, string dataSourceId, CancellationToken ct);
    Task<bool> CanWriteAsync(string userId, string dataSourceId, CancellationToken ct);
    Task<IReadOnlyList<string>> GetAccessibleDataSourcesAsync(string userId, CancellationToken ct);
    Task<DataSourcePermission> GetPermissionAsync(string userId, string dataSourceId, CancellationToken ct);
}

[Flags]
public enum DataSourcePermission
{
    None  = 0,
    Read  = 1,
    Write = 2,
    Admin = 4
}
```

每个角色可对同一数据源拥有不同读写权限。权限校验在执行启动时强制进行，不具备读取权限的数据源在方案校验阶段即被拒绝。

## 12. 运维设计

### 12.1 部署形态

- 支持控制台、服务化和容器化部署
- 可接入 Kubernetes 做扩缩容与健康探针管理

### 12.2 监控与诊断

- 导出执行耗时、成功率、吞吐量、资源占用等指标
- 提供健康检查端点和诊断端点
- 支持接入 OpenTelemetry、Prometheus、Grafana

### 12.3 跨平台部署

**平台兼容性：** 框架及所有核心依赖（.NET 8、NTS、GDAL 绑定）支持以下平台：

| 平台 | 架构 | 说明 |
|------|------|------|
| Windows | x64 | 完整支持 |
| Linux | x64 | 完整支持，推荐 Ubuntu 22.04 / Debian 12 |
| Linux | ARM64 | 支持，适合 ARM 服务器和树莓派等 |
| macOS | x64 / ARM64 | 开发环境支持 |

**Docker 容器化：** 提供官方 Dockerfile 模板：

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app
RUN apt-get update && apt-get install -y libgdal-dev

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "OpenGis.Daf.dll"]
```

**Kubernetes 集成：** 框架暴露标准健康端点，支持 K8s 探针：

```csharp
// GET /health/live  → 返回 200 表示进程存活
// GET /health/ready → 返回 200 表示可接受请求（算子池已初始化、DB 可连接）
// GET /health/startup → 返回 200 表示启动完成
```

### 12.4 日志粒度与输出

**三级日志粒度：**

| 级别 | 触发时机 | 典型内容 |
|------|----------|----------|
| 方案级 | 方案开始/结束 | 方案 ID、版本、总耗时、总体状态 |
| 分析项级（默认） | 每个分析项的执行开始/结束 | ItemId、算子 ID、耗时、处理要素数、状态 |
| 要素级（QC 默认） | 每个要素的处理结果 | FeatureId、检查结果、异常详情 |

日志粒度由 `ItemExecutionPolicy.LogGranularity` 控制，分析场景默认 `Item` 级，质检场景默认 `Feature` 级。

**结构化日志格式：** 日志采用 JSON 格式输出，每条日志携带上下文标签：

```json
{
  "timestamp": "2024-06-04T10:30:00.000Z",
  "level": "Information",
  "planId": "plan.qc.topo.001",
  "executionId": "exec.20240604.001",
  "itemId": "item.overlap.check",
  "featureId": "F_10042",
  "message": "要素 F_10042 拓扑重叠检查通过",
  "context": { "overlapArea": 0.0 }
}
```

**日志输出目标：** 通过 `Microsoft.Extensions.Logging` 的 Provider 机制灵活配置：

| Provider | 适用场景 |
|----------|----------|
| Console | 开发调试 |
| File | 单机部署、持久化存储 |
| Seq | 结构化日志查询与分析 |
| Elasticsearch | 大规模集群日志聚合 |
| Application Insights | Azure 云环境 |

### 12.5 可观测性集成

框架通过 OpenTelemetry 实现统一可观测性，支持 Traces、Metrics、Logs 三种信号的导出：

```csharp
// 初始化 OpenTelemetry
services.AddOpenTelemetry()
    .WithTracing(builder => builder
        .AddSource("OpenGis.Daf")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(builder => builder
        .AddMeter("OpenGis.Daf")
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());
```

**关键指标（Metrics）：**

| 指标 | 类型 | 说明 |
|------|------|------|
| `plan_execution_duration_seconds` | Histogram | 方案执行总耗时分布 |
| `item_execution_success_rate` | Gauge | 分析项执行成功率 |
| `features_processed_total` | Counter | 已处理要素总数 |
| `active_executions` | Gauge | 当前并行执行数 |
| `memory_usage_bytes` | Gauge | 内存占用 |
| `data_read_bytes_total` | Counter | 数据读取总量 |

**导出目标：** Prometheus（Metrics）、Jaeger / Zipkin（Traces）、Grafana（可视化仪表盘）。所有这些外部系统为可选项，框架不强制依赖任何特定可观测性平台。

## 13. 术语表

| 术语 | 英文 | 说明 |
|------|------|------|
| 算子 | Operator | 可复用分析算法单元 |
| 分析项 | Analysis Item | 配置了输入输出的算子实例 |
| 分析方案 | Analysis Plan | 由分析项组成的执行单元 |
| DAG | Directed Acyclic Graph | 用于表达执行依赖关系 |
| 要素 | Feature | GIS 中的最小数据单元 |
| CRS | Coordinate Reference System | 坐标参考系统 |
