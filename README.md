# OpenGIS Data Analysis Framework (opengis-daf)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-68217A.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)

统一的 GIS 数据分析与数据质检框架 —— 方案驱动、纯配置定义、开源可扩展。

## 核心能力

| 能力 | 说明 |
|------|------|
| 方案驱动 | 通过 JSON 配置定义完整的数据处理流程，可复用、可版本化 |
| 空间分析 | 缓冲区、裁剪、相交检查、包含检查、坐标系转换 |
| 属性操作 | 字段计算器（表达式引擎）、空值填充 |
| 数据质检 | 几何有效性检查、属性完整性检查，原生内置质量评分 |
| 可扩展算子 | 插件式算子体系，通过 `AssemblyLoadContext` 动态加载 DLL |
| 多数据源 | `IFeatureSource` 抽象 — PostGIS、Shapefile、GeoJSON、内存数据集 |
| 多种输出 | `IFeatureSink` 抽象 — 控制台、GeoJSON、Shapefile、PostGIS |
| 方案管理 | 21 条校验规则、版本回退、跨版本 Diff、原子写入 |
| DAG 调度 | Kahn 算法拓扑排序 + 串行/并行调度、超时/重试控制、失败策略 |
| 跨平台 | Windows / Linux / macOS，支持 Docker 容器化部署 |

## 快速开始

### 环境要求

- [.NET SDK 10.0](https://dotnet.microsoft.com/)（见 `global.json`）
- Git（用于拉取子模块）

### 获取源码

本项目通过 Git 子模块引用 [`opengis-utils-for-net`](https://github.com/znlgis/opengis-utils-for-net)，克隆时需一并初始化子模块：

```bash
# 克隆并初始化子模块
git clone --recurse-submodules https://github.com/znlgis/opengis-daf.git

# 若已克隆但未拉取子模块
git submodule update --init --recursive
```

### 构建

```bash
dotnet build
```

### 测试

```bash
dotnet test
```

### 运行分析方案

```bash
# 校验方案（不执行）
daf validate --plan plans/my-analysis.json

# 执行分析/质检
daf run --plan plans/my-analysis.json

# 算子管理
daf operator list [--category <分类>]   # 列出已注册算子
daf operator import --dll <path>        # 动态导入算子 DLL

# 方案管理
daf plan list [--group <组>]                       # 列出方案
daf plan create --name <名称> [--group <组>]        # 新建方案
daf plan copy --source <组/名> --target <组/名>      # 复制方案
daf plan export --plan <组/名> [--output <path>]    # 导出方案
```

### 方案示例

```json
{
  "id": "qc-landuse-001",
  "name": "土地利用数据质检",
  "version": "1.0.0",
  "items": [
    {
      "id": "attr-check",
      "operatorId": "attribute_completeness_checker",
      "parameters": { "required_fields": ["land_use", "area"] },
      "inputs": { "source": { "type": "External", "sourceId": "landuse-geojson" } },
      "output": { "adapterType": "ConsoleWriter" }
    }
  ],
  "executionPolicy": { "failurePolicy": "StopOnAny" }
}
```

## 架构概览

```
 ┌───────────────────────────────────────────────────┐
 │             CLI 层 (OpenGisDAF.Cli)                 │
 │             命令路由 · 异常处理 · DI 容器            │
 ├───────────────────────────────────────────────────┤
 │         方案管理层 (OpenGisDAF.PlanManagement)       │
 │         CRUD · JSON序列化 · 版本管理 · 校验          │
 ├───────────────────────────────────────────────────┤
 │          调度引擎层 (OpenGisDAF.Scheduling)           │
 │     DAG 构建 · Kahn 拓扑排序 · 失败策略 · 并发控制    │
 ├───────────────────────────────────────────────────┤
 │          执行引擎层 (OpenGisDAF.Execution)            │
 │       算子执行 · 结果缓存 · 超时重试 · 质量报告       │
 ├───────────────────────────────────────────────────┤
 │             算子池 (OpenGisDAF.Operators)            │
 │       9 个内置算子 · 插件发现 · 动态加载 · 辅助工具    │
 ├───────────────────────────────────────────────────┤
 │          适配器层 (OpenGisDAF.Adapters)               │
 │    IFeatureSource 数据源 (4) · IFeatureSink 输出 (4) │
 │    映射工具: FieldTypeMapper · GeometryTypeMapper    │
 ├───────────────────────────────────────────────────┤
 │         基础设施层 (OpenGisDAF.Infrastructure)        │
 │       配置 · 日志 (Serilog) · 密码加密 (DPAPI)       │
 └───────────────────────────────────────────────────┘
```

数据流方向：`外部数据源 → 适配器读取 → 算子处理 → 适配器写入 → 外部输出`

## 内置算子

| 算子 ID | 分类 | 说明 |
|---------|------|------|
| `buffer` | 空间运算 | 缓冲区分析 |
| `clip` | 空间运算 | 裁剪分析 |
| `intersect_check` | 空间关系 | 相交检查（两集合 / 自相交） |
| `containment_check` | 空间关系 | 包含检查（contains / within） |
| `coordinate_transform` | 格式转换 | 坐标系转换 |
| `field_calculator` | 属性操作 | 字段计算器（表达式 + 算术解析器） |
| `null_value_filler` | 属性操作 | 空值填充 |
| `attribute_completeness_checker` | 质检 | 属性完整性检查 |
| `geometry_validity_checker` | 质检 | 几何有效性检查 |

详见：[算子参考](docs/operator-reference.md)

## 数据源与输出适配器

### 数据源 (`IFeatureSource`)

| 适配器 | 数据格式 | 说明 |
|--------|---------|------|
| `GeoJsonFeatureSource` | `.geojson` / `.json` | 支持属性过滤 |
| `ShapefileFeatureSource` | `.shp` | 自动读取 `.prj` 投影文件 |
| `PostgisFeatureSource` | PostgreSQL/PostGIS | GDAL PG: 驱动，密码 DPAPI 加密 |
| `InMemoryFeatureSource` | 内存数据集 | 算子间传递中间结果 |

### 输出 (`IFeatureSink`)

| 适配器 | 输出格式 | 说明 |
|--------|---------|------|
| `ConsoleFeatureSink` | 控制台 | 调试/交互式查看 |
| `GeoJsonFeatureSink` | GeoJSON 文件 | 包含属性与几何 |
| `ShapefileFeatureSink` | Shapefile | `.shp` + `.dbf` + `.shx` + `.prj` |
| `PostgisFeatureSink` | PostGIS 表 | 自动建表并写入 |

### 适配器工具类

| 工具类 | 说明 |
|--------|------|
| `FieldTypeMapper` | `FieldType` → `FieldDataType` 统一映射 |
| `GeometryTypeMapper` | `Core.GeometryType` → OGU `GeometryType` 统一映射 |
| `PostgisConnectionHelper` | PG 连接串构建 + 值转义 |

## 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 10.0 | 运行框架 |
| C# | 14 | 开发语言 |
| GDAL/OGR | 3.x | GIS 核心（数据读写、坐标转换） |
| NetTopologySuite | 2.6 | 轻量空间计算 |
| Npgsql | 10.0 | PostgreSQL 连接 |
| Serilog | 4.3 | 结构化日志 |
| xUnit v3 | 3.2 | 测试框架 |

## 项目质量

本项目已通过完整的代码审查（2026-07），覆盖 11 个项目、100+ 文件，修复了以下关键问题：

### 并发安全
- `ResultCache` 清除操作与并发计算的信号量竞态条件
- `PlanVersionManager` 备份锁移除导致的竞态条件
- `ExecutionContext.CurrentItemId` 可变共享状态线程安全

### 资源管理
- 所有 `FeatureSource.DisposeAsync` 未释放原生 GDAL/OGR 资源句柄
- `DafApplication` 未实现 `IAsyncDisposable` 导致 `ServiceProvider` 泄漏

### 正确性
- `FieldCalculator` 字段名 `{a}` 与 `{ab}` 重叠时的替换顺序错误
- `ExceptionHandler` 在 Serilog 初始化前配置，结构化日志无效
- `ResultCache.MaxEntries` 未实际强制执行缓存上限
- `ResultCache.InvalidateAsync` 未释放 `IAsyncDisposable` 缓存值
- 全局异常处理器在日志系统初始化前配置，致异常日志落空

### 代码卫生
- 清理死代码：7 个未使用枚举值、4 个未使用属性/方法、冗余注释和无操作 using
- 提取重复代码：3 个适配器映射工具类消除跨文件重复
- 改进防御性编程：5 个算子添加 `ExecuteAsync` 空参数校验
- 改进错误信息：`ContinueIndependent` 策略下上游失败时的下游跳过逻辑

## 文档

| 文档 | 说明 |
|------|------|
| [需求文档](docs/GIS数据分析框架需求文档.md) | 业务目标、功能需求 |
| [设计文档](docs/GIS数据分析框架设计文档.md) | 架构设计、接口定义 |
| [开发计划](docs/GIS数据分析框架开发计划.md) | 阶段规划、里程碑 |
| [快速入门](docs/quickstart.md) | 5 分钟上手指南 |
| [CLI 参考](docs/cli-reference.md) | 命令行接口说明 |
| [方案配置指南](docs/plan-config-guide.md) | plan.json 配置详解 |
| [算子参考](docs/operator-reference.md) | 所有内置算子说明 |
| [贡献指南](CONTRIBUTING.md) | 如何参与贡献 |

## 阶段规划

| 阶段 | 目标 | 状态 |
|------|------|------|
| Phase 1 | MVP：串联执行、9 个核心算子、4 种数据源、4 种输出、方案管理与校验 | 已完成 |
| Phase 2 | 并行调度、20+ 算子、更多适配器、安全增强 | 规划中 |
| Phase 3 | 分布式、可视化编辑器、插件市场 | 远期规划 |

## 许可证

本项目基于 [MIT License](LICENSE) 开源发布。

Copyright (c) 2026 OpenGIS DAF Contributors
