# GIS 数据分析与质检框架

## 设计文档（完善版）

## 基于 C# / .NET 8 技术栈

---

## 一、项目概述

### 1.1 项目背景

本项目旨在基于 C# 和 .NET 8 开发一个 GIS 数据分析框架，该框架同时具备数据质检能力。GIS 数据分析与质检本质上是一致的技术流程——二者均通过定制分析方案、执行分析逻辑，最终获得分析结果。两者的区别主要体现在输出侧重点上：数据分析侧重于最终的输出结果，而质检更侧重于分析的执行过程，需要了解数据在每一步处理中发生了什么变化、是否出现了异常。

基于这一认识，本框架将质检视为数据分析的一种特殊场景，在统一的分析框架中同时支持数据分析和数据质检功能，避免重复建设两套独立的系统。

### 1.2 核心理念

GIS 数据分析的核心特征在于其高度的不确定性和可变性：

- **输入数据不确定**：数据源可以是数据库图层、Shapefile、GDB 文件、GeoJSON API 等多种格式
- **分析方案可变**：不同的业务需求对应不同的分析流程，方案需要灵活组合
- **结果展示和管理方式可变**：根据业务场景灵活配置输出形式

面对这种高度灵活的需求，框架采用 **"方案驱动（Plan-Driven）"** 的设计理念——以分析方案和质检方案为核心单元，通过方案的定制、组合与执行来满足各种复杂的 GIS 数据分析与质检需求。

### 1.3 应用场景

| 场景类别 | 典型场景 | 说明 |
|----------|----------|------|
| 空间分析 | 叠加分析、缓冲区分析、网络分析 | 核心 GIS 空间运算 |
| 数据质检 | 拓扑检查、属性完整性校验、空间一致性验证 | 数据质量保障 |
| 统计报表 | 土地利用统计、人口密度分析、设施覆盖率 | 决策支持 |
| 数据转换 | 格式互转、坐标系转换、数据融合 | 数据治理 |
| 批量处理 | 大批量 Shapefile 质检、自动化数据入库检查 | 自动化流水线 |

### 1.4 技术选型

| 技术要素 | 选型 | 说明 |
|----------|------|------|
| 开发语言 | C# 12 | 充分利用现代语言特性（record、pattern matching 等） |
| 运行框架 | .NET 8 | 跨平台、高性能、长周期支持 |
| GIS 内核 | NetTopologySuite (NTS) | .NET 生态最成熟的空间计算库 |
| 数据格式支持 | PostgreSQL/PostGIS、Shapefile、GDB、GeoJSON | 通过适配器模式扩展 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection | .NET 标准 DI 容器 |
| 序列化 | System.Text.Json | 高性能 JSON 处理 |
| 异步编程 | async/await + System.Threading.Channels | 高吞吐数据处理管道 |
| 日志 | Microsoft.Extensions.Logging | 结构化日志 |
| 扩展支持 | RESTful API、gRPC | 支持远程数据获取与服务调用 |

### 1.5 设计决策与权衡

以下记录框架关键架构决策及其背后的考量：

| 决策 | 选择 | 备选方案 | 考量
|------|------|----------|------|
| 执行模型 | DAG + 拓扑排序 | 工作流引擎（Elsa、Workflow Core） | DAG 模型轻量且天然支持并行度推导；工作流引擎引入额外依赖和复杂度，GIS 场景不需要人工审批等流程特性 |
| 数据处理方式 | 异步流式（IAsyncEnumerable） | 全量加载到内存 | GIS 数据集可达百万级要素，全量加载不可行；流式处理内存可控，且支持 Pipeline 并行 |
| 方案定义格式 | JSON Schema | YAML / DSL | JSON Schema 提供标准校验能力，IDE 智能提示友好，生态系统（代码生成、文档生成）成熟 |
| GIS 内核 | NetTopologySuite (NTS) | GDAL C# 绑定 / 自研 | NTS 是 .NET 生态最成熟的空间库，纯托管代码无原生依赖，API 设计符合 .NET 惯例；GDAL 功能更全但部署复杂 |
| 配置驱动 vs 代码驱动 | 配置驱动为主，代码扩展为辅 | 纯代码（Fluent API） | 目标用户包含非开发人员，JSON 配置可由可视化界面生成；算子开发者使用 C# 接口实现扩展 |
| 依赖管理 | Microsoft.Extensions.DependencyInjection | Autofac / 自研容器 | MS DI 是 .NET 标准，与其他库（Logging、Configuration、HttpClientFactory）无缝集成 |
| 插件隔离 | AssemblyLoadContext | AppDomain（已废弃）/ 进程隔离 | AssemblyLoadContext 是 .NET Core+ 推荐方式，支持程序集热加载和卸载；进程隔离开销大 |

**重要约束：**
- 算子必须是**无状态的**（stateless），所有状态通过 ExecutionContext 传递 — 确保并行安全和可重试
- FeatureSource 和 FeatureSink 的**生命周期由 DI 容器管理**，算子不持有对适配器的直接引用
- 方案配置是**不可变的**（immutable），执行期间不允许修改 — 确保执行过程的可追溯性

---

## 二、核心概念

### 2.1 分析规则（算子 / Operator）

分析规则是整个框架最底层、最基础的核心概念，也称为"算子"。一条完整的分析规则包含三个要素：

- **输入（Input）**：定义分析所需的数据来源，可精确指定到具体图层、文件或数据库表
- **执行（Execute）**：定义具体的分析操作或算法，如叠加分析、相交分析、缓冲区分析等
- **输出（Output）**：定义分析结果的输出形式和输出位置

分析规则作为一个整体，其输入、执行和输出均应是可定制的。从开发角度而言，规则应以标准接口的方式提供，开发者按照接口规范实现具体逻辑即可。框架内置常用算子，同时支持开发者自定义扩展算子。

**内置算子分类：**

| 类别 | 算子示例 | 说明 |
|------|----------|------|
| 空间关系 | Intersect、Contains、Within、Touches、Overlaps | 几何对象的空间关系判断 |
| 空间运算 | Buffer、Union、Difference、Intersection、SymDifference | 几何对象的空间运算 |
| 属性操作 | Filter、Project、Join、Aggregate、Sort | 属性数据的筛选与变换 |
| 空间连接 | SpatialJoin、NearestNeighbor | 基于空间关系的表连接 |
| 统计分析 | ClusterAnalysis、HotSpotAnalysis、DensityAnalysis | 空间统计方法 |
| 格式转换 | Reproject、FormatConvert、GeometrySimplify | 坐标转换与格式处理 |
| 质检专用 | TopologyCheck、AttributeValidate、CompletenessCheck | 数据质量检查 |

### 2.2 分析项（Analysis Item）

分析规则本身是通用的算法或算子，具有笼统的输入输出定义，但缺乏针对具体业务场景的精确配置。当一条分析规则被赋予明确的输入和输出配置后，就形成了一个"分析项"。

**分析项 = 算子 + 具体配置**

| 组件 | 说明 |
|------|------|
| 算子引用 | 指向算子池中某个已注册的规则 |
| 输入配置 | 明确指定数据源类型、连接参数、图层/表名、过滤条件 |
| 参数配置 | 算子的运行时参数（如缓冲区半径、容差值、目标坐标系） |
| 输出配置 | 指定输出目标、输出字段、输出格式 |

分析项的输入输出配置支持以 JSON 方式进行定义，包括指定数据来源、执行方式以及结果的输出位置和格式等。

### 2.3 分析方案（Analysis Plan）

分析方案是框架的顶层组织单元，对应一组明确的数据和业务需求。一个分析方案由多个分析项组成，通过配置分析项的输入输出来适配当前的数据和业务场景。

方案的核心特性：

#### 2.3.1 方案嵌套

一个方案内部可以包含子方案，通过方案的层层嵌套来构建复杂的分析流程。例如：

```
父方案：土地利用变化分析
├── 子方案 A：数据预处理
│   ├── 分析项 A1：坐标系转换
│   └── 分析项 A2：数据裁剪
├── 子方案 B：变化检测
│   ├── 分析项 B1：叠加分析
│   ├── 分析项 B2：差异提取（并行）
│   └── 分析项 B3：面积统计（并行）
└── 分析项 C：报告生成（依赖 A、B 完成）
```

#### 2.3.2 串并联执行

方案内部的分析项之间存在串并联关系：

- **串行（Serial）**：按依赖顺序逐个执行，上游输出作为下游输入
- **并行（Parallel）**：无依赖关系的分析项可同时执行，提高整体效率

#### 2.3.3 数据流定义

分析项之间通过显式的数据流定义来建立依赖关系。每个分析项声明其输入来源（来自哪个上游分析项的输出，或是外部数据源），调度引擎据此自动推导执行顺序和并行机会。

### 2.4 方案模板与版本管理

- **方案模板**：预定义的通用方案模板，可快速实例化到具体场景
- **方案版本**：方案支持版本化管理，便于追溯和回滚
- **方案导入导出**：方案以 JSON/YAML 格式存储，支持跨环境迁移

---

## 三、框架架构设计

### 3.1 整体架构

```
┌─────────────────────────────────────────────────────┐
│                    API 层                            │
│   REST API / gRPC / CLI / Job Scheduler             │
├─────────────────────────────────────────────────────┤
│              方案管理层 (Plan Manager)               │
│   ┌──────────┬──────────┬──────────┬──────────┐    │
│   │ 方案解析  │ 方案验证  │ 版本管理  │ 模板管理  │    │
│   └──────────┴──────────┴──────────┴──────────┘    │
├─────────────────────────────────────────────────────┤
│             调度引擎层 (Scheduling Engine)           │
│   ┌──────────┬──────────┬──────────┬──────────┐    │
│   │ DAG 构建  │ 拓扑排序  │ 并行调度  │ 状态管理  │    │
│   └──────────┴──────────┴──────────┴──────────┘    │
├─────────────────────────────────────────────────────┤
│               执行引擎层 (Execution Engine)          │
│   ┌──────────┬──────────┬──────────┬──────────┐    │
│   │ 算子执行  │ 上下文管理│ 错误处理  │ 结果收集  │    │
│   └──────────┴──────────┴──────────┴──────────┘    │
├─────────────────────────────────────────────────────┤
│                 算子池 (Operator Pool)               │
│   ┌──────────┬──────────┬──────────┬──────────┐    │
│   │ 空间运算  │ 属性操作  │ 统计分析  │ 质检规则  │ ... │
│   └──────────┴──────────┴──────────┴──────────┘    │
├─────────────────────────────────────────────────────┤
│              数据源适配层 (Data Source Adapter)      │
│   ┌──────┬──────┬──────┬──────┬──────┬──────┐      │
│   │PG/   │Shape-│ GDB  │ Geo- │ REST │ WFS  │ ...  │
│   │PostGIS│file │      │ JSON │ API  │      │      │
│   └──────┴──────┴──────┴──────┴──────┴──────┘      │
├─────────────────────────────────────────────────────┤
│                     基础设施层                       │
│   日志 │ 缓存 │ 配置 │ 监控 │ 安全 │ 序列化          │
└─────────────────────────────────────────────────────┘
```

### 3.2 算子池（Operator Pool）

算子池是框架的基础支撑层，负责管理所有已注册的分析规则（算子）。

**核心职责：**

- 算子注册与发现（通过 DI 自动注册或手动注册）
- 算子元数据管理（输入输出 Schema、参数定义、分类标签）
- 算子版本管理
- 算子能力查询（按分类、标签、能力过滤）

**注册方式：**

```csharp
// 方式一：通过 DI 自动扫描注册
services.AddOperatorsFromAssembly(typeof(BufferOperator).Assembly);

// 方式二：手动注册
services.AddOperator<BufferOperator>("spatial.buffer");
services.AddOperator<IntersectOperator>("spatial.intersect");
```

### 3.3 方案管理层（Plan Manager）

负责方案的完整生命周期管理：

- **方案解析**：将 JSON/YAML 格式的方案配置反序列化为内部模型
- **方案验证**：校验方案的完整性、一致性和合法性（Schema 校验 + 业务规则校验）
- **方案存储**：方案的持久化存储（文件系统或数据库）
- **方案版本**：支持方案的多版本管理和回滚
- **方案模板**：管理预定义方案模板

### 3.4 调度引擎（Scheduling Engine）

调度引擎是框架的执行核心，负责将分析方案转化为可执行的执行计划。

**核心流程：**

1. **DAG 构建**：根据方案中分析项之间的数据依赖关系，构建有向无环图（DAG）
2. **拓扑排序**：对 DAG 进行拓扑排序，确定执行顺序
3. **并行度计算**：识别可并行执行的分析项组
4. **资源分配**：根据系统资源和并行度配置分配执行资源
5. **调度执行**：按照拓扑顺序和并行策略调度分析项执行
6. **状态监控**：跟踪每个分析项的执行状态，处理完成和失败事件

**DAG 模型示例：**

```
        [Item A] ────┐
                      ├──> [Item C] ──> [Item E]
        [Item B] ────┘         │
                                │
        [Item D] ──────────────┘

执行顺序：(A, B, D 并行) → C → E
```

**反环检测**：调度引擎在 DAG 构建阶段进行环路检测，如果检测到循环依赖，立即拒绝方案并给出明确的错误提示，指明形成环路的分析项链。

### 3.5 数据源适配层（Data Source Adapter）

数据源适配层通过适配器模式统一不同数据源的访问接口，使得上层算子无需关心底层数据来源。

**已规划适配器：**

| 适配器 | 数据源 | 实现要点 |
|--------|--------|----------|
| PostGISAdapter | PostgreSQL + PostGIS | Npgsql + NetTopologySuite.IO.PostGIS |
| ShapefileAdapter | .shp 文件 | NetTopologySuite.IO.ShapeFile |
| GdbAdapter | File/Enterprise GDB | FileGDB API / GDAL 绑定 |
| GeoJsonAdapter | GeoJSON 文本/API | System.Text.Json + NTS GeoJsonReader |
| WfsAdapter | OGC WFS 服务 | HTTP Client + GML 解析 |
| InMemoryAdapter | 内存中的 FeatureCollection | 用于中间结果传递 |

**统一数据模型：**

**统一数据模型（详见第四章核心接口定义）：**

所有适配器实现统一的 `IFeatureSource` 接口，确保上层算子无需感知底层数据源的差异。每个 FeatureSource 必须声明其空间参考（CRS），算子执行前自动检测 CRS 一致性并根据需要自动转换。

### 3.6 输出适配层（Output Adapter）

输出适配层负责将分析结果写入不同的目标：

| 适配器 | 输出目标 |
|--------|----------|
| PostGISWriter | PostgreSQL 表 |
| ShapefileWriter | Shapefile 文件 |
| GeoJsonWriter | GeoJSON 文件或 HTTP 响应 |
| CsvWriter | CSV 统计报表 |
| GeoPackageWriter | GeoPackage 文件 |
| ConsoleWriter | 控制台输出（调试用） |

### 3.7 插件扩展机制

框架支持通过插件方式扩展算子和适配器：

- **算子插件**：开发者实现 `IOperator` 接口并注册即可扩展
- **适配器插件**：实现 `IFeatureSource`（数据源）或 `IFeatureSink`（输出）接口
- **插件加载**：通过程序集扫描（`AssemblyLoadContext`）自动发现和加载插件
- **热加载**：支持运行时动态加载新算子（通过独立的 `AssemblyLoadContext` 实现程序集隔离和卸载）

### 3.8 DI 注册与服务编排

框架基于 .NET 依赖注入容器进行服务编排，提供统一的注册入口：

```csharp
// Program.cs — 框架启动配置
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGisAnalysis(cfg =>
{
    // 自动扫描并注册所有 IOperator 实现
    cfg.AddOperatorsFromAssembly(typeof(Program).Assembly);

    // 注册数据源适配器
    cfg.AddAdapter<PostgisFeatureSource>("PostGIS");
    cfg.AddAdapter<ShapefileFeatureSource>("Shapefile");
    cfg.AddAdapter<GeoJsonFeatureSource>("GeoJSON");
    cfg.AddAdapter<InMemoryFeatureSource>("InMemory");

    // 注册输出适配器
    cfg.AddSink<PostgisFeatureSink>("PostGIS");
    cfg.AddSink<ShapefileFeatureSink>("Shapefile");
    cfg.AddSink<CsvFeatureSink>("Csv");

    // 注册方案存储
    cfg.UsePlanStore<FileSystemPlanStore>(options =>
    {
        options.RootPath = "./plans";
    });

    // 配置日志输出到文件
    cfg.AddExecutionLogging(options =>
    {
        options.OutputPath = "./logs";
        options.RetentionDays = 30;
    });

    // 注册缓存
    cfg.AddFeatureCache<InMemoryFeatureCache>(options =>
    {
        options.MaxEntries = 10000;
        options.DefaultTtl = TimeSpan.FromHours(1);
    });
});
```

**服务生命周期：**

| 服务类型 | 生命周期 | 原因 |
|----------|----------|------|
| IOperator | Singleton | 无状态，可安全共享 |
| IFeatureSource / IFeatureSink | Transient 或 Scoped | 每次执行使用独立实例，避免连接泄漏 |
| IPlanStore | Singleton | 全局方案存储 |
| IFeatureCache | Singleton | 跨执行共享缓存 |
| ExecutionContext | Scoped（每次方案执行） | 隔离不同方案执行的上下文 |

## 四、核心接口定义

### 4.1 算子接口

```csharp
/// <summary>
/// 算子元数据，描述算子的标识、分类和参数定义
/// </summary>
public sealed record OperatorMetadata
{
    public string Id { get; init; }           // 唯一标识，如 "spatial.buffer"
    public string Name { get; init; }         // 显示名称
    public string Category { get; init; }     // 分类：spatial / attribute / statistics / qa
    public string Description { get; init; }  // 功能描述
    public IReadOnlyList<ParameterDefinition> Parameters { get; init; }
    public InputSchema InputSchema { get; init; }
    public OutputSchema OutputSchema { get; init; }
}

/// <summary>
/// 算子参数定义
/// </summary>
public sealed record ParameterDefinition
{
    public string Name { get; init; }
    public string Type { get; init; }         // number, string, boolean, enum, geometry
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string>? AllowedValues { get; init; }
    public (double Min, double Max)? Range { get; init; }
}

/// <summary>
/// 算子接口 - 所有算子必须实现
/// </summary>
public interface IOperator
{
    OperatorMetadata Metadata { get; }

    /// <summary>
    /// 验证输入和参数是否满足算子执行的前置条件
    /// </summary>
    ValidationResult Validate(AnalysisItemConfig config);

    /// <summary>
    /// 执行算子逻辑
    /// </summary>
    Task<ExecutionResult> ExecuteAsync(
        IReadOnlyDictionary<string, IFeatureSource> inputs,
        IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext context,
        CancellationToken cancellationToken);
}
```

### 4.2 分析项接口

```csharp
/// <summary>
/// 分析项运行时表示
/// </summary>
public sealed class AnalysisItem
{
    public string Id { get; init; }
    public string OperatorId { get; init; }              // 引用的算子 ID
    public string? ParentPlanId { get; init; }            // 所属方案 ID
    public IReadOnlyDictionary<string, InputBinding> Inputs { get; init; }
    public IReadOnlyDictionary<string, object?> Parameters { get; init; }
    public OutputBinding Output { get; init; }
    public ItemExecutionPolicy ExecutionPolicy { get; init; }
}

/// <summary>
/// 输入绑定：描述输入数据的来源
/// </summary>
public abstract record InputBinding
{
    /// <summary>从外部数据源获取</summary>
    public sealed record External(string AdapterType, string ConnectionString, string Layer) : InputBinding;

    /// <summary>从上游分析项的输出获取</summary>
    public sealed record FromUpstream(string ItemId, string OutputName) : InputBinding;

    /// <summary>从子方案的汇总输出获取</summary>
    public sealed record FromSubPlan(string SubPlanId) : InputBinding;
}

/// <summary>
/// 输出绑定
/// </summary>
public sealed record OutputBinding
{
    public string AdapterType { get; init; }
    public string Target { get; init; }          // 输出目标路径/表名
    public IReadOnlyList<string>? Fields { get; init; }  // 指定输出字段
    public string? Format { get; init; }         // 输出格式
}

/// <summary>
/// 分析项执行策略
/// </summary>
public sealed record ItemExecutionPolicy
{
    public int MaxRetries { get; init; } = 0;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(30);
    public FailureAction OnFailure { get; init; } = FailureAction.Fail;
}

public enum FailureAction
{
    Fail,           // 失败则整个方案失败
    Skip,           // 跳过继续执行
    Fallback        // 使用降级算子
}
```

### 4.3 方案接口

```csharp
/// <summary>
/// 分析方案
/// </summary>
public sealed class AnalysisPlan
{
    public string Id { get; init; }
    public string Name { get; init; }
    public string Version { get; init; }
    public string? Description { get; init; }
    public PlanConfig Config { get; init; }

    /// <summary>直接包含的分析项</summary>
    public IReadOnlyList<AnalysisItem> Items { get; init; }

    /// <summary>嵌套的子方案</summary>
    public IReadOnlyList<AnalysisPlan> SubPlans { get; init; }

    /// <summary>方案级别的执行策略</summary>
    public PlanExecutionPolicy ExecutionPolicy { get; init; }
}

public sealed record PlanConfig
{
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;
    public bool CollectIntermediateResults { get; init; } = true;  // 质检模式
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;
}

public sealed record PlanExecutionPolicy
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromHours(2);
    public FailureAction OnItemFailure { get; init; } = FailureAction.Fail;
}
```

### 4.4 数据源接口

```csharp
/// <summary>
/// GIS 要素（Feature）接口
/// </summary>
public interface IFeature
{
    string Id { get; }
    Geometry Geometry { get; }
    IReadOnlyDictionary<string, object?> Attributes { get; }
}

/// <summary>
/// 要素数据源
/// </summary>
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
    IAsyncEnumerable<IFeature> GetFeaturesAsync(IFeatureCursor cursor, CancellationToken ct = default);
}

/// <summary>
/// 要素数据源元数据
/// </summary>
public sealed record FeatureSourceMetadata
{
    public string Name { get; init; }
    public string SourceType { get; init; }        // PostGIS, Shapefile, GeoJSON, ...
    public GeometryType GeometryType { get; init; }
    public IReadOnlyList<FieldDefinition> Fields { get; init; }
}
```

### 4.5 输出接口

```csharp
public interface IFeatureSink
{
    Task InitializeAsync(OutputSchema schema, CancellationToken cancellationToken);
    Task WriteAsync(IFeature feature, CancellationToken cancellationToken);
    Task WriteBatchAsync(IAsyncEnumerable<IFeature> features, CancellationToken cancellationToken);
    Task CompleteAsync(CancellationToken cancellationToken);
}
```

### 4.6 支撑类型定义

```csharp
/// <summary>
/// 输入 Schema — 定义算子期望的输入端口
/// </summary>
public sealed record InputSchema
{
    public IReadOnlyDictionary<string, InputPortDefinition> Ports { get; init; }
}

public sealed record InputPortDefinition
{
    public string Name { get; init; }
    public string Description { get; init; }
    public GeometryType? ExpectedGeometryType { get; init; }
    public bool Required { get; init; } = true;
    /// <summary>最小/最大要素数约束</summary>
    public (long Min, long Max)? FeatureCountRange { get; init; }
}

/// <summary>
/// 输出 Schema — 定义算子产出的输出端口
/// </summary>
public sealed record OutputSchema
{
    public IReadOnlyDictionary<string, OutputPortDefinition> Ports { get; init; }
}

public sealed record OutputPortDefinition
{
    public string Name { get; init; }
    public string Description { get; init; }
    public GeometryType GeometryType { get; init; }
    public IReadOnlyList<FieldDefinition> Fields { get; init; }
}

/// <summary>
/// 算子执行结果
/// </summary>
public sealed record ExecutionResult
{
    public ExecutionStatus Status { get; init; }
    public IReadOnlyDictionary<string, IFeatureSource> Outputs { get; init; }
    public IReadOnlyList<ExecutionLogEntry> Logs { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

public enum ExecutionStatus
{
    Success,
    PartialSuccess,  // 部分数据成功（如跳过无效要素）
    Failed,
    Canceled
}

/// <summary>
/// 配置校验结果
/// </summary>
public sealed record ValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; }
}

public sealed record ValidationError
{
    public string Code { get; init; }
    public string Message { get; init; }
    public string? PropertyPath { get; init; }  // JSON Pointer to the problematic field
}

/// <summary>
/// 字段定义
/// </summary>
public sealed record FieldDefinition
{
    public string Name { get; init; }
    public string Type { get; init; }         // string, int, double, datetime, geometry
    public bool Nullable { get; init; }
    public int? MaxLength { get; init; }
}

/// <summary>
/// 几何类型枚举
/// </summary>
public enum GeometryType
{
    Unknown,
    Point, MultiPoint,
    LineString, MultiLineString,
    Polygon, MultiPolygon,
    GeometryCollection
}

/// <summary>
/// 要素游标 — 用于分页/增量读取大数据集
/// </summary>
public interface IFeatureCursor
{
    long Offset { get; }
    long? Limit { get; }
    string? ContinuationToken { get; }  // 适配器自定义的分页标记
}

/// <summary>
/// 执行统计信息
/// </summary>
public sealed record ExecutionStatistics
{
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public long TotalItems { get; init; }
    public long CompletedItems { get; init; }
    public long FailedItems { get; init; }
    public long TotalFeaturesProcessed { get; init; }
    public long TotalFeaturesProduced { get; init; }
    public long BytesRead { get; init; }
    public long BytesWritten { get; init; }
}
```

---

## 五、执行模型

### 5.1 DAG 依赖图模型

框架采用有向无环图（DAG）对分析项的依赖关系进行建模：

- **节点（Node）**：每个分析项对应 DAG 中的一个节点
- **边（Edge）**：如果分析项 B 的输入来源于分析项 A 的输出，则存在一条 A → B 的有向边
- **入度为 0 的节点**：可直接从外部数据源开始执行的节点
- **拓扑层（Topological Level）**：同一层内的节点可以并行执行

```
DAG 构建过程：
1. 解析方案中所有分析项的 InputBinding
2. 识别 FromUpstream 绑定 → 建立有向边
3. 识别 FromSubPlan 绑定 → 将子方案展开为虚拟节点并建立边
4. 检测环路（DFS 或 Kahn 算法），如有环路则拒绝方案
5. 计算拓扑排序 → 确定执行层级
```

### 5.2 串并行调度策略

```
┌────────────────────────────────────────────┐
│              Level 0 (入度=0)               │
│  ┌──────┐  ┌──────┐  ┌──────┐             │
│  │Item A│  │Item B│  │Item D│  ← 并行执行   │
│  └──┬───┘  └──┬───┘  └──┬───┘             │
│     │         │         │                   │
├─────┼─────────┼─────────┼───────────────────┤
│     ▼         ▼         │    Level 1        │
│  ┌──────────────┐       │                   │
│  │    Item C    │       │  等待 A,B 完成     │
│  └──────┬───────┘       │                   │
│         │               │                   │
├─────────┼───────────────┼───────────────────┤
│         ▼               ▼    Level 2        │
│  ┌──────────────────────────┐               │
│  │         Item E           │ ← 最终节点     │
│  └──────────────────────────┘               │
└────────────────────────────────────────────┘

规则：
- 同一 Level 内的节点可并行执行（无数据竞争）
- 当前 Level 所有节点完成后，下一 Level 才开始
- 并行度由 PlanConfig.MaxParallelism 控制
- 通过 SemaphoreSlim 或 Channel 实现并发控制
```

### 5.3 执行上下文

```csharp
/// <summary>
/// 执行上下文 - 在方案执行期间共享的状态
/// </summary>
public sealed class ExecutionContext
{
    public string PlanId { get; init; }
    public string ExecutionId { get; init; }   // 每次执行的唯一标识

    /// <summary>中间结果缓存（上游输出供下游消费）</summary>
    public IResultCache ResultCache { get; init; }

    public ILogger Logger { get; init; }
    public IServiceProvider Services { get; init; }
    public CancellationToken CancellationToken { get; init; }

    /// <summary>执行统计信息</summary>
    public ExecutionStatistics Statistics { get; init; }
}

public interface IResultCache
{
    Task StoreAsync(string itemId, string outputName, IFeatureSource features);
    Task<IFeatureSource?> RetrieveAsync(string itemId, string outputName);
    Task<bool> ExistsAsync(string itemId, string outputName);
}
```

### 5.4 状态机

```
每个分析项在生命周期中经历以下状态：

     ┌──────┐
     │ Pending│
     └───┬──┘
         │ 调度器分配
         ▼
     ┌──────┐
     │Queued │
     └───┬──┘
         │ 工作线程拾取
         ▼
     ┌──────────┐
     │Executing  │◄──── 重试 ────┐
     └───┬──────┘               │
         │                      │
    ┌────┼────┐                 │
    ▼    ▼    ▼                 │
┌──────┐ ┌──────┐ ┌────────┐   │
│Success│ │Failed│ │Canceled│   │
└──────┘ └──┬───┘ └────────┘   │
            │                   │
            ▼                   │
       ┌─────────┐              │
       │ Retrying├──────────────┘
       └─────────┘
            │ (超过最大重试次数)
            ▼
       ┌─────────┐
       │  Failed  │
       └─────────┘
```

### 5.5 并发控制与背压

GIS 分析任务的特点是数据密集型，需要精细的并发控制来平衡吞吐量和资源消耗：

```csharp
/// <summary>
/// 并发控制器 — 管理并行度和资源配额
/// </summary>
public sealed class ConcurrencyController
{
    private readonly SemaphoreSlim _parallelismGate;     // 并行度上限
    private readonly Channel<WorkItem> _workChannel;     // 有界通道实现背压
    private readonly MemoryPressureMonitor _memoryMonitor;

    /// <summary>
    /// 提交工作项（当通道满时阻塞调用方，实现背压）
    /// </summary>
    public async ValueTask EnqueueAsync(WorkItem item, CancellationToken ct)
    {
        await _workChannel.Writer.WriteAsync(item, ct);
    }
}
```

**背压（Backpressure）策略：**

| 场景 | 策略 | 说明 |
|------|------|------|
| 生产者快于消费者 | 有界 Channel 阻塞写入 | 防止中间结果无限堆积导致 OOM |
| 内存压力高 | 暂停上游算子提交 | 通过 MemoryPressureMonitor 监控 GC 压力 |
| 下游算子失败 | 取消上游并传播取消令牌 | 避免无效计算和资源浪费 |
| 单个算子超时 | 取消该算子，根据策略决定方案行为 | 避免整体方案被单个慢算子阻塞 |

**Pipeline 流水线模式：**

```
┌─────────┐   Channel(32)   ┌─────────┐   Channel(32)   ┌─────────┐
│ 读取数据  │ ───────────────> │ 空间运算  │ ───────────────> │ 写入结果  │
│ (Producer)│                 │(Processor)│                 │(Consumer)│
└─────────┘                 └─────────┘                 └─────────┘
     ↑                                                     │
     │                    结果缓存                          │
     └─────────────────────────────────────────────────────┘
```

上下游通过有界 `Channel<T>` 连接，通道容量（如 32）限制中间缓冲区大小，实现自然的背压控制。

### 5.6 优雅关闭

对于长时间运行的方案，框架支持优雅关闭：

```csharp
public sealed class GracefulShutdown
{
    private readonly CancellationTokenSource _cts;
    private readonly TimeSpan _drainTimeout;  // 等待完成的最大时间

    /// <summary>
    /// 触发优雅关闭：不再接受新工作，等待进行中的工作完成
    /// </summary>
    public async Task ShutdownAsync()
    {
        _cts.Cancel();  // 通知所有算子停止
        // 等待当前执行中的所有分析项完成（不超过 drainTimeout）
        await WaitForDrainingAsync(_drainTimeout);
        // 超时后强制终止并保存中间状态
        if (HasPendingItems)
        {
            await SaveCheckpointAsync();
        }
    }
}
```

**关闭行为：**
- 停止接受新的分析项调度
- 当前执行中的算子收到 `CancellationToken`，在下一个检查点安全退出
- 已完成的中间结果写入持久化存储作为检查点（Checkpoint）
- 超时后强制终止，下次执行可从检查点恢复

---

## 六、配置数据模型

### 6.1 方案配置 JSON Schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "required": ["id", "name", "version", "items"],
  "properties": {
    "id": { "type": "string", "description": "方案唯一标识" },
    "name": { "type": "string", "description": "方案名称" },
    "version": { "type": "string", "description": "语义化版本号" },
    "description": { "type": "string" },
    "config": {
      "type": "object",
      "properties": {
        "maxParallelism": { "type": "integer", "default": 4 },
        "collectIntermediateResults": { "type": "boolean", "default": true },
        "minimumLogLevel": {
          "type": "string",
          "enum": ["Trace", "Debug", "Information", "Warning", "Error"]
        }
      }
    },
    "executionPolicy": {
      "type": "object",
      "properties": {
        "timeout": { "type": "string", "format": "duration", "default": "02:00:00" },
        "onItemFailure": { "type": "string", "enum": ["Fail", "Skip", "Fallback"] }
      }
    },
    "subPlans": {
      "type": "array",
      "items": { "$ref": "#" }
    },
    "items": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "operatorId", "inputs", "output"],
        "properties": {
          "id": { "type": "string" },
          "operatorId": { "type": "string" },
          "inputs": {
            "type": "object",
            "additionalProperties": {
              "oneOf": [
                {
                  "type": "object",
                  "required": ["type", "adapterType", "connectionString", "layer"],
                  "properties": {
                    "type": { "const": "external" },
                    "adapterType": { "type": "string" },
                    "connectionString": { "type": "string" },
                    "layer": { "type": "string" },
                    "filter": { "type": "string" }
                  }
                },
                {
                  "type": "object",
                  "required": ["type", "itemId", "outputName"],
                  "properties": {
                    "type": { "const": "upstream" },
                    "itemId": { "type": "string" },
                    "outputName": { "type": "string" }
                  }
                },
                {
                  "type": "object",
                  "required": ["type", "subPlanId"],
                  "properties": {
                    "type": { "const": "subPlan" },
                    "subPlanId": { "type": "string" }
                  }
                }
              ]
            }
          },
          "parameters": { "type": "object" },
          "output": {
            "type": "object",
            "required": ["adapterType", "target"],
            "properties": {
              "adapterType": { "type": "string" },
              "target": { "type": "string" },
              "fields": { "type": "array", "items": { "type": "string" } },
              "format": { "type": "string" }
            }
          },
          "executionPolicy": {
            "type": "object",
            "properties": {
              "maxRetries": { "type": "integer", "default": 0 },
              "timeout": { "type": "string", "format": "duration" },
              "onFailure": { "type": "string", "enum": ["Fail", "Skip", "Fallback"] }
            }
          }
        }
      }
    }
  }
}
```

### 6.2 配置示例

```json
{
  "id": "land-use-change-detection",
  "name": "土地利用变化检测",
  "version": "1.0.0",
  "description": "对比两期土地利用数据，检测变化区域并统计面积",
  "config": {
    "maxParallelism": 4,
    "collectIntermediateResults": true
  },
  "items": [
    {
      "id": "reproject-2020",
      "operatorId": "spatial.reproject",
      "inputs": {
        "source": {
          "type": "external",
          "adapterType": "Shapefile",
          "connectionString": "./data/landuse_2020.shp",
          "layer": "landuse_2020"
        }
      },
      "parameters": { "targetCrs": "EPSG:3857" },
      "output": {
        "adapterType": "InMemory",
        "target": "memory://reprojected_2020"
      }
    },
    {
      "id": "reproject-2024",
      "operatorId": "spatial.reproject",
      "inputs": {
        "source": {
          "type": "external",
          "adapterType": "Shapefile",
          "connectionString": "./data/landuse_2024.shp",
          "layer": "landuse_2024"
        }
      },
      "parameters": { "targetCrs": "EPSG:3857" },
      "output": {
        "adapterType": "InMemory",
        "target": "memory://reprojected_2024"
      }
    },
    {
      "id": "intersect-diff",
      "operatorId": "spatial.difference",
      "inputs": {
        "base": { "type": "upstream", "itemId": "reproject-2024", "outputName": "result" },
        "subtract": { "type": "upstream", "itemId": "reproject-2020", "outputName": "result" }
      },
      "parameters": {},
      "output": {
        "adapterType": "Shapefile",
        "target": "./output/landuse_change.shp",
        "format": "ESRI Shapefile"
      }
    },
    {
      "id": "area-statistics",
      "operatorId": "attribute.aggregate",
      "inputs": {
        "source": { "type": "upstream", "itemId": "intersect-diff", "outputName": "result" }
      },
      "parameters": {
        "groupBy": "landuse_type",
        "aggregations": [{ "field": "area", "function": "sum", "alias": "total_area" }]
      },
      "output": {
        "adapterType": "Csv",
        "target": "./output/change_statistics.csv",
        "fields": ["landuse_type", "total_area"]
      }
    }
  ]
}
```

---

## 七、质检支持

### 7.1 质检与分析的关系

| 维度 | 数据分析 | 数据质检 |
|------|----------|----------|
| 关注点 | 最终结果 | 过程与结果并重 |
| 中间结果 | 通常不保留 | 全部保留，便于追溯 |
| 日志粒度 | 概要级别 | 详细到每条记录 |
| 失败处理 | 允许部分失败 | 记录所有异常 |
| 输出 | 分析报告 | 质检报告 + 问题清单 |

### 7.2 质检规则

质检规则是算子的一种特殊类别（`Category = "qa"`），内置质检规则包括：

| 质检规则 | 说明 |
|----------|------|
| 拓扑检查 (TopologyCheck) | 自相交、悬挂线、面重叠、缝隙检查 |
| 属性完整性 (AttributeCompleteness) | 必填字段检查、值域范围检查 |
| 属性一致性 (AttributeConsistency) | 字段间逻辑关系校验（如面积与几何面积一致） |
| 空间一致性 (SpatialConsistency) | 跨图层空间关系校验 |
| 几何有效性 (GeometryValidity) | 几何合法性检查（OGC Simple Features 规范） |
| 重复检查 (DuplicateCheck) | 几何重复、属性重复检测 |
| 精度检查 (PrecisionCheck) | 坐标精度、面积精度检查 |

### 7.3 日志与追溯

质检模式下，框架记录结构化日志：

```csharp
public sealed record ExecutionLogEntry
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public LogLevel Level { get; init; }
    public string Message { get; init; }

    /// <summary>关联的要素 ID（用于精确定位问题数据）</summary>
    public string? FeatureId { get; init; }

    /// <summary>执行耗时</summary>
    public TimeSpan? Elapsed { get; init; }

    /// <summary>输入要素数量</summary>
    public long? InputFeatureCount { get; init; }

    /// <summary>输出要素数量</summary>
    public long? OutputFeatureCount { get; init; }

    /// <summary>异常详情</summary>
    public string? ExceptionDetail { get; init; }
}
```

### 7.4 差异对比

质检支持对不同分析批次的结果进行差异对比，是数据版本管理和变化追踪的核心能力：

**对比维度：**

| 维度 | 方法 | 说明 |
|------|------|------|
| 结果集对比 | 全集哈希 + 差分枚举 | 识别新增（仅存在于新结果）、删除（仅存在于旧结果）、修改（两版均存在但内容不同）的要素 |
| 属性对比 | 字段级 MD5 / SHA256 | 对每条要素的属性集计算哈希，快速定位字段级别的值变更 |
| 几何对比 | Hausdorff 距离 / 面积差 / 重心偏移 | 量化几何形变程度，可设置阈值过滤微小变化 |
| 拓扑对比 | 空间关系一致性检查 | 检测相邻关系、包含关系是否发生变化 |

**对比结果模型：**

```csharp
public sealed record DiffResult
{
    public string ItemId { get; init; }
    public string BaseExecutionId { get; init; }    // 基线执行
    public string CompareExecutionId { get; init; }  // 比较执行
    public IReadOnlyList<DiffEntry> Entries { get; init; }
    public DiffSummary Summary { get; init; }
}

public sealed record DiffEntry
{
    public DiffType Type { get; init; }           // Added, Removed, Modified
    public string FeatureId { get; init; }
    public IReadOnlyList<FieldChange>? FieldChanges { get; init; }
    public double? GeometryDifference { get; init; }  // Hausdorff 距离
}

public enum DiffType { Added, Removed, Modified }
```

### 7.5 质检报告

质检执行完成后，自动生成质检报告：

```
质检报告
========================
方案：土地利用数据质检
执行时间：2026-05-30 14:30:00 - 14:35:23
耗时：5分23秒

检查项：
  ✓ 几何有效性检查 ── 通过（检查 15,234 条，0 异常）
  ✓ 属性完整性检查 ── 通过（检查 15,234 条，0 异常）
  ✗ 拓扑检查 ── 发现问题（检查 15,234 条，发现 47 处自相交）

问题清单：
  1. [FID:1234] 面要素自相交 (坐标: 120.5, 30.2)
  2. [FID:5678] 相邻面之间缝隙 (间隙面积: 0.05m²)
  ...

汇总：
  总检查项：3
  通过：2
  发现问题：1 项（共 47 处）
```

### 7.6 数据血缘与溯源

数据血缘（Data Lineage）记录了每一条数据从输入到输出的完整流转路径，是质检可追溯性的核心保障：

```csharp
/// <summary>
/// 数据血缘记录
/// </summary>
public sealed record LineageRecord
{
    public string ExecutionId { get; init; }
    public string ItemId { get; init; }
    public string OperatorId { get; init; }
    /// <summary>输入要素 ID 列表（来源追溯）</summary>
    public IReadOnlyList<string> SourceFeatureIds { get; init; }
    /// <summary>输出要素 ID（产出物）</summary>
    public string OutputFeatureId { get; init; }
    /// <summary>变换类型：Create, Modify, Delete, PassThrough, Aggregate</summary>
    public string TransformType { get; init; }
    public DateTimeOffset Timestamp { get; init; }
}
```

**血缘查询能力：**
- **正向追溯**：给定输入要素，查询经过哪些算子、最终产出了什么
- **反向追溯**：给定输出问题要素，回溯其上游输入和执行链路
- **影响分析**：修改某个算子后，识别哪些方案和输出会受影响
- **血缘可视化**：生成 DAG 血缘图，直观展示数据流转路径

### 7.7 质量评分

框架支持对质检结果进行量化评分，便于跨批次、跨方案的数据质量比较：

```csharp
public sealed record QualityScore
{
    /// <summary>综合得分 (0-100)</summary>
    public double Overall { get; init; }
    /// <summary>各维度得分</summary>
    public IReadOnlyDictionary<string, DimensionScore> Dimensions { get; init; }
}

public sealed record DimensionScore
{
    public string Name { get; init; }             // 完整性 / 一致性 / 精度 / 拓扑
    public double Score { get; init; }             // 0-100
    public double Weight { get; init; }            // 权重
    public long TotalChecked { get; init; }
    public long IssuesFound { get; init; }
}
```

**评分维度与权重示例：**

| 维度 | 默认权重 | 计算方法 |
|------|----------|----------|
| 几何有效性 | 25% | 有效要素数 / 总要素数 * 100 |
| 属性完整性 | 25% | 必填字段完整率 |
| 拓扑正确性 | 20% | 1 - (拓扑异常数 / 总要素数) * 100 |
| 空间精度 | 15% | 基于精度阈值的达标率 |
| 属性一致性 | 15% | 一致性规则通过率 |

质量评分可与 CI/CD 流程集成，设置通过阈值（如 `overall ≥ 80`）作为数据入库门禁。

---

## 八、空间参考与几何处理

### 8.1 坐标参考系统（CRS）

框架支持多 CRS 管理：

```csharp
public interface ISpatialReference
{
    int Srid { get; }              // EPSG 代码
    string Authority { get; }      // "EPSG"
    string Wkt { get; }            // WKT 格式定义
    bool IsGeographic { get; }     // 是否为地理坐标系
    bool IsProjected { get; }      // 是否为投影坐标系
    string Unit { get; }           // 单位：degree, meter, foot, ...
}
```

- 内置常用 CRS 定义（EPSG:4326, EPSG:3857, EPSG:4490, EPSG:4547 等）
- 通过 ProjNet 库进行坐标系转换
- 每个 FeatureSource 必须声明其 CRS
- 算子执行前自动检测 CRS 一致性，必要时自动转换

### 8.2 几何类型支持

基于 NetTopologySuite 的几何类型体系：

- **Point / MultiPoint**：点 / 多点
- **LineString / MultiLineString**：线 / 多线
- **Polygon / MultiPolygon**：面 / 多面
- **GeometryCollection**：混合几何集合
- **CircularString / CurvePolygon**：曲线几何（PostGIS 扩展）

### 8.3 空间索引

对于大型数据集的空间查询，框架内置空间索引支持：

- **R-Tree**：基于 NTS 的 STRtree 实现
- **网格索引**：适用于等面积网格场景
- **自动索引**：算子在执行前根据数据量自动决定是否建立空间索引

---

## 九、性能与扩展

### 9.1 大数据集处理策略

| 策略 | 说明 | 适用场景 |
|------|------|----------|
| 流式处理 | 使用 `IAsyncEnumerable` 逐条处理，避免全量加载 | 内存敏感的大数据集 |
| 分块处理 | 将数据集按空间范围或属性分块 | 可独立处理的子区域 |
| 空间过滤 | 先通过 Envelope 粗筛再精确判断 | 空间关系算子 |
| 属性下推 | 将过滤条件下推到数据源层执行 | PostGIS 查询优化 |
| 分批提交 | 批量写入输出，减少 I/O 次数 | 大量输出场景 |

### 9.2 缓存机制

```csharp
public interface IFeatureCache
{
    /// <summary>基于空间范围的缓存键</summary>
    Task<IFeatureSource?> GetOrComputeAsync(
        string cacheKey,
        Envelope boundingBox,
        Func<Task<IFeatureSource>> factory,
        TimeSpan? ttl = null);
}
```

- **结果缓存**：同一输入+同一算子的结果可复用
- **空间缓存**：按空间瓦片缓存，后续查询命中瓦片则直接返回
- **TTL 管理**：可配置过期时间，平衡性能与数据新鲜度

### 9.3 增量计算

对于重复执行的方案，支持增量计算：

- **变更检测**：识别输入数据的变化部分
- **增量传播**：仅重新计算受影响的 DAG 子图
- **缓存失效**：输入变化自动使下游缓存失效

### 9.4 并行处理优化

- **数据分区并行**：将大数据集分区，每个分区由一个工作线程独立处理
- **Pipeline 并行**：上下游分析项在数据可用时即开始处理（生产者-消费者模式）
- **自适应并行度**：根据系统 CPU 和内存使用率动态调整并行度

---

## 十、错误处理与容错

### 10.1 错误分类

| 错误类别 | 说明 | 处理策略 |
|----------|------|----------|
| 配置错误 | Schema 不合法、引用不存在的算子、循环依赖 | 方案加载阶段拒绝 |
| 数据源错误 | 连接失败、文件不存在、权限不足 | 重试或降级 |
| 运行时错误 | 算子执行异常、内存不足、超时 | 根据策略重试/跳过/失败 |
| 数据错误 | 无效几何、坐标系不匹配、字段类型错误 | 记录并跳过单条 |

### 10.2 重试策略

```csharp
public sealed record RetryPolicy
{
    public int MaxRetries { get; init; } = 3;
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public double BackoffMultiplier { get; init; } = 2.0;  // 指数退避
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>可重试的异常类型白名单</summary>
    public IReadOnlySet<Type>? RetryableExceptions { get; init; }
}
```

### 10.3 降级处理

当某个算子的最优实现不可用时，可配置降级算子：

```json
{
  "id": "spatial-intersect",
  "operatorId": "spatial.intersect.gpu",
  "fallbackOperatorId": "spatial.intersect.cpu",
  "...": "..."
}
```

### 10.4 异常传播

- **算子级**：算子内部异常向上抛至调度引擎
- **方案级**：根据 `onItemFailure` 策略决定方案整体行为
- **用户可见**：所有异常通过结构化日志记录，质检模式下详细记录异常数据

### 10.5 错误码体系

框架定义标准化的错误码，便于日志检索、告警规则匹配和自动化处理：

```csharp
public static class ErrorCodes
{
    // 配置类 (CFG)
    public const string SchemaInvalid        = "CFG-001";
    public const string OperatorNotFound     = "CFG-002";
    public const string CircularDependency   = "CFG-003";
    public const string MissingRequiredParam = "CFG-004";

    // 数据源类 (DS)
    public const string ConnectionFailed     = "DS-001";
    public const string FileNotFound         = "DS-002";
    public const string AccessDenied         = "DS-003";
    public const string CrsMismatch          = "DS-004";

    // 运行时类 (RT)
    public const string OperatorTimeout      = "RT-001";
    public const string OutOfMemory          = "RT-002";
    public const string InvalidGeometry      = "RT-003";
    public const string SpatialIndexFailure  = "RT-004";

    // 输出类 (OUT)
    public const string WriteFailed          = "OUT-001";
    public const string DiskFull             = "OUT-002";
    public const string SchemaChanged        = "OUT-003";
}
```

| 错误码前缀 | 类别 | 示例场景 |
|-----------|------|----------|
| CFG | 配置 | Schema 不合法、算子不存在、循环依赖 |
| DS | 数据源 | 连接失败、文件缺失、CRS 不匹配 |
| RT | 运行时 | 超时、OOM、无效几何、索引失败 |
| OUT | 输出 | 写入失败、磁盘满、Schema 变更 |

错误码嵌入到 `ExecutionResult.ErrorCode` 中，监控系统可基于错误码配置告警规则（如 `DS-001` 触发数据库连接告警）。

---

## 十一、安全与权限

### 11.1 数据源访问控制

- 数据源连接字符串加密存储
- 支持基于角色的数据源访问权限
- Shapefile/GDB 文件路径访问白名单

### 11.2 API 安全

- REST API 支持 JWT 认证
- API 限流（Rate Limiting）
- 输入数据的 Schema 校验，防止注入攻击

### 11.3 审计日志

所有方案执行操作记录审计日志：

- 谁（用户标识）
- 何时（时间戳）
- 做了什么（方案 ID、执行结果）
- 访问了哪些数据（数据源清单）

---

## 十二、测试策略

### 12.1 测试层次

| 层次 | 范围 | 工具 | 说明 |
|------|------|------|------|
| 单元测试 | 单个算子 | xUnit + NSubstitute | 验证算子逻辑正确性 |
| 集成测试 | 算子 + 适配器 | xUnit + Testcontainers (PostGIS) | 验证端到端数据流 |
| 方案测试 | 完整方案 | xUnit + 测试夹具 | 验证方案配置和执行 |
| 质检验证 | 质检规则 | 预置测试数据 | 验证质检规则准确性 |
| 性能测试 | 大数据集 | BenchmarkDotNet | 验证处理性能达标 |

### 12.2 测试数据管理

- 使用小规模标准化测试数据集
- PostGIS 集成测试使用 Testcontainers 启动临时容器
- 测试数据版本化管理，与代码一同提交

---

## 十三、部署与运维

### 13.1 容器化部署

```dockerfile
FROM mcr.microsoft.com/dotnet/runtime:8.0
COPY --from=build /app /app
WORKDIR /app
ENTRYPOINT ["dotnet", "GisAnalysis.Host.dll"]
```

- 支持 Docker 容器化部署
- Kubernetes 编排支持水平扩展
- 通过环境变量和配置文件管理运行参数

### 13.2 配置管理

- 方案配置：JSON/YAML 文件
- 运行配置：`appsettings.json` + 环境变量覆盖
- 敏感配置：Secret Manager / Azure Key Vault
- 方案存储：文件系统 / PostgreSQL

### 13.3 监控与告警

| 指标 | 说明 |
|------|------|
| 方案执行耗时 | 按方案、按算子粒度统计 |
| 成功率 | 方案/分析项的执行成功率 |
| 数据吞吐量 | 单位时间处理的要素数量 |
| 资源使用 | CPU、内存、磁盘 I/O |
| 队列深度 | 等待执行的分析项数量 |

- 通过 OpenTelemetry 导出指标到 Prometheus
- Grafana 仪表盘展示

### 13.4 健康检查与诊断

框架内置健康检查端点，支持 Kubernetes 探针和运维诊断：

```csharp
// 注册健康检查
builder.Services.AddHealthChecks()
    .AddCheck<PostgisHealthCheck>("postgis")
    .AddCheck<DiskSpaceHealthCheck>("disk")
    .AddCheck<OperatorPoolHealthCheck>("operator_pool");

// 映射端点
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => true  // 存活探针：最基础检查
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("readiness")  // 就绪探针
});
```

| 健康检查项 | 探针类型 | 检查内容 |
|-----------|----------|----------|
| PostgisHealthCheck | Readiness | 数据库连接和基础查询 |
| DiskSpaceHealthCheck | Readiness | 输出目录剩余空间 |
| OperatorPoolHealthCheck | Liveness | 算子注册状态 |
| MemoryHealthCheck | Readiness | 可用内存是否充足 |

**诊断端点（仅内网）：**
- `/diagnostics/active-plans` — 当前执行中的方案列表
- `/diagnostics/operator-stats` — 各算子执行统计（调用次数、平均耗时、失败率）
- `/diagnostics/cache-stats` — 缓存命中率和使用量

---

## 十四、实现路线图

### Phase 1：核心框架（MVP）

| 序号 | 任务 | 预估工期 |
|------|------|----------|
| 1 | 核心接口定义（IOperator, IFeatureSource, IFeatureSink） | 3 天 |
| 2 | 算子池（注册、发现、元数据管理） | 3 天 |
| 3 | 方案解析与验证（JSON Schema） | 3 天 |
| 4 | DAG 构建与拓扑排序 | 3 天 |
| 5 | 调度引擎（串并行执行） | 5 天 |
| 6 | PostGIS 适配器 | 3 天 |
| 7 | Shapefile 适配器 | 2 天 |
| 8 | 内置基础算子（Buffer, Intersect, Filter, Aggregate） | 5 天 |
| 9 | 日志与执行追溯 | 3 天 |

### Phase 2：质检增强

| 序号 | 任务 | 预估工期 |
|------|------|----------|
| 1 | 质检规则算子（拓扑、属性、几何） | 5 天 |
| 2 | 质检报告生成 | 3 天 |
| 3 | 差异对比引擎 | 3 天 |
| 4 | 中间结果完整保留 | 2 天 |

### Phase 3：扩展与优化

| 序号 | 任务 | 预估工期 |
|------|------|----------|
| 1 | GDB / GeoJSON / WFS 适配器 | 5 天 |
| 2 | 空间索引集成 | 3 天 |
| 3 | 增量计算引擎 | 5 天 |
| 4 | REST API 对外服务 | 5 天 |
| 5 | 插件热加载 | 3 天 |

### Phase 4：生产就绪

| 序号 | 任务 | 预估工期 |
|------|------|----------|
| 1 | 性能测试与优化 | 5 天 |
| 2 | 容器化与 K8s 部署 | 3 天 |
| 3 | 监控与告警集成 | 3 天 |
| 4 | 安全加固 | 3 天 |
| 5 | 文档与使用手册 | 5 天 |

### 验收标准

**Phase 1 验收标准：**
- PostGIS 和 Shapefile 适配器可正常读取 10 万+要素数据集
- 单方案 5 个分析项的 DAG 正确构建并执行
- 串并行调度正确且无资源泄漏
- 所有内置算子通过单元测试覆盖率 > 80%

**Phase 2 验收标准：**
- 7 类质检规则各具备至少 3 个正例和 3 个负例的测试数据集
- 质检报告格式通过人工评审
- 差异对比在 10 万要素下 30 秒内完成

**Phase 3 验收标准：**
- GDB/GeoJSON 适配器支持常用编码和几何类型
- 增量计算模式下 10% 数据变化时重新计算时间 < 全量计算时间的 30%
- REST API 单节点支持 50 并发请求

**Phase 4 验收标准：**
- 100 万要素叠加分析在 8 核机器上 5 分钟内完成
- K8s 环境可用性 > 99.9%
- Grafana 仪表盘覆盖全部 5 项监控指标
- 文档包含 Quick Start、API Reference 和 Operator 开发指南

---

## 附录 A：术语表

| 术语 | 英文 | 说明 |
|------|------|------|
| 算子（分析规则） | Operator / Analysis Rule | 可复用的分析算法单元 |
| 分析项 | Analysis Item | 配置了输入输出的算子实例 |
| 分析方案 | Analysis Plan | 由分析项组成的执行单元 |
| DAG | Directed Acyclic Graph | 有向无环图，用于表达依赖关系 |
| 要素 | Feature | GIS 中的最小数据单元（几何 + 属性） |
| 适配器 | Adapter | 统一不同数据源访问接口的组件 |
| CRS | Coordinate Reference System | 坐标参考系统 |
| NTS | NetTopologySuite | .NET 空间计算核心库 |

## 附录 B：参考资源

- [NetTopologySuite](https://github.com/NetTopologySuite/NetTopologySuite) — .NET 空间计算库
- [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) — 坐标系转换库
- [Npgsql](https://www.npgsql.org/) — .NET PostgreSQL 驱动
- [OGC Simple Features](https://www.ogc.org/standards/sfa) — 简单要素访问标准
- [Testcontainers](https://dotnet.testcontainers.org/) — 集成测试容器管理
