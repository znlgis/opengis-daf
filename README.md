# OpenGIS Data Analysis Framework (opengis-daf)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-68217A.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)

统一的 GIS 数据分析与数据质检框架 —— 方案驱动、纯配置定义、开源可扩展。

## 核心能力

| 能力 | 说明 |
|------|------|
| 📋 方案驱动 | 通过 JSON 配置定义完整的数据处理流程，可复用、可版本化 |
| 📐 空间分析 | 缓冲区、裁剪、相交检查、包含检查、坐标系转换 |
| 📏 属性操作 | 字段计算器（表达式引擎）、空值填充 |
| ✅ 数据质检 | 几何有效性检查、属性完整性检查，原生内置质量评分 |
| 🔌 可扩展算子 | 插件式算子体系，通过 `AssemblyLoadContext` 动态加载 DLL |
| 🗄️ 多数据源 | `IFeatureSource` 抽象 — PostGIS、Shapefile、GeoJSON、内存数据集 |
| 📊 多种输出 | `IFeatureSink` 抽象 — 控制台、GeoJSON、Shapefile、PostGIS |
| 📐 方案管理 | 21 条校验规则、版本回退、跨版本 Diff、原子写入 |
| ⚡ DAG 调度 | Kahn 算法拓扑排序 + 串行/并行调度、超时/重试控制 |
| 🌍 跨平台 | Windows / Linux / macOS，支持 Docker 容器化部署 |

## 快速开始

### 构建

```bash
dotnet build
```

### 运行分析方案

```bash
# 校验方案
daf validate --plan plans/my-analysis.json

# 执行分析/质检
daf run --plan plans/my-analysis.json

# 列出已注册算子
daf operator list
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
 ┌────────────────────────────────────────────┐
 │              CLI 层 (OpenGisDAF.Cli)         │
 ├────────────────────────────────────────────┤
 │           方案管理层 (PlanManagement)         │
 ├────────────────────────────────────────────┤
 │            调度引擎层 (Scheduling)             │
 ├────────────────────────────────────────────┤
 │            执行引擎层 (Execution)              │
 ├────────────────────────────────────────────┤
 │              算子池 (Operators)               │
 ├────────────────────────────────────────────┤
 │      数据源适配层 / 输出适配层 (Adapters)       │
 ├────────────────────────────────────────────┤
 │         基础设施层 (Infrastructure)            │
 └────────────────────────────────────────────┘
```

详细设计见：[设计文档](docs/GIS数据分析框架设计文档.md)

## 内置算子

| 算子 ID | 分类 | 说明 |
|---------|------|------|
| `buffer` | 空间运算 | 缓冲区分析 |
| `clip` | 空间运算 | 裁剪分析 |
| `intersect_check` | 空间关系 | 相交检查（支持两集合和自相交） |
| `containment_check` | 空间关系 | 包含检查（contains / within） |
| `coordinate_transform` | 格式转换 | 坐标系转换 |
| `field_calculator` | 属性操作 | 字段计算器（表达式 + 算术解析器） |
| `null_value_filler` | 属性操作 | 空值填充 |
| `attribute_completeness_checker` | 质检 | 属性完整性检查 |
| `geometry_validity_checker` | 质检 | 几何有效性检查 |

详见：[算子参考](docs/operator-reference.md)

## 数据源与输出适配器

**数据源（IFeatureSource）：**
- `GeoJsonFeatureSource` — `.geojson` / `.json`
- `ShapefileFeatureSource` — `.shp`
- `PostgisFeatureSource` — PostgreSQL/PostGIS
- `InMemoryFeatureSource` — 内存数据集

**输出（IFeatureSink）：**
- `ConsoleFeatureSink` — 控制台
- `GeoJsonFeatureSink` — GeoJSON 文件
- `ShapefileFeatureSink` — Shapefile
- `PostgisFeatureSink` — PostGIS 表

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

## 文档

| 文档 | 说明 |
|------|------|
| [需求文档](docs/GIS数据分析框架需求文档.md) | 业务目标、功能需求 |
| [设计文档](docs/GIS数据分析框架设计文档.md) | 架构设计、接口定义 |
| [快速入门](docs/quickstart.md) | 5 分钟上手指南 |
| [CLI 参考](docs/cli-reference.md) | 命令行接口说明 |
| [方案配置指南](docs/plan-config-guide.md) | plan.json 配置详解 |
| [算子参考](docs/operator-reference.md) | 所有内置算子说明 |
| [贡献指南](CONTRIBUTING.md) | 如何参与贡献 |

## 阶段规划

| 阶段 | 目标 | 状态 |
|------|------|------|
| Phase 1 | MVP：串联执行、10 个核心算子、4 种数据源、4 种输出 | ✅ 基本完成 |
| Phase 2 | 并行调度、20+ 算子、更多适配器、安全增强 | 📅 规划中 |
| Phase 3 | 分布式、可视化编辑器、插件市场 | 📅 远期规划 |

## 许可证

本项目基于 [MIT License](LICENSE) 开源发布。

Copyright (c) 2026 OpenGIS DAF Contributors
