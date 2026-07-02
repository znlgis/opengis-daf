# OpenGIS Data Analysis Framework (opengis-daf)

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-14-68217A.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)

统一的 GIS 数据分析与数据质检框架 —— 方案驱动、纯配置定义、开源可扩展。

## 核心能力

| 能力 | 说明 |
|------|------|
| 📋 方案驱动 | 通过 JSON 配置定义完整的数据处理流程，可复用、可版本化 |
| 📐 空间分析 | 缓冲区分析、叠加分析、空间连接、网络分析等 |
| ✅ 数据质检 | 拓扑检查、属性完整性、空间一致性验证，原生内置质量评分 |
| 🔌 可扩展算子 | 插件式算子体系，第三方可通过 DLL 扩展自定义处理能力 |
| 🗄️ 多数据源 | 统一 `IFeatureSource` 抽象，支持 PostGIS、Shapefile、GeoJSON、GDB 等格式 |
| 📊 多种输出 | 支持写入数据库、文件、CSV、GeoPackage 及生成质检报告 |
| 🌍 跨平台 | Windows / Linux / macOS（x64 + ARM64），支持 Docker 容器化部署 |
| 🔒 安全可控 | 主动导入机制、敏感信息加密、审计日志、API 认证 |

## 快速开始

> 项目当前处于设计完成、即将进入编码阶段。以下为规划的使用方式。

### CLI 执行方案

```bash
# 校验方案配置
daf validate --plan ./plans/my-analysis.json

# 执行分析/质检方案
daf run --plan ./plans/my-analysis.json

# 列出已注册算子
daf operator list

# 导入算子插件
daf operator import --dll ./plugins/MyOperator.dll
```

### 方案示例

```json
{
  "id": "qc-landuse-001",
  "name": "土地利用数据质检",
  "version": "1.0.0",
  "items": [
    {
      "id": "item-1",
      "operatorId": "qc.topology.overlap",
      "parameters": { "tolerance": 0.001 },
      "inputs": { "data": { "type": "External", "sourceId": "landuse-shp" } },
      "output": { "adapterType": "ConsoleWriter" }
    }
  ],
  "executionPolicy": { "maxParallelism": 4 }
}
```

## 架构概览

```
 ┌────────────────────────────────────────────┐
 │              API / CLI 层                    │
 ├────────────────────────────────────────────┤
 │              方案管理层                       │
 ├────────────────────────────────────────────┤
 │              调度引擎层                       │
 ├────────────────────────────────────────────┤
 │              执行引擎层                       │
 ├────────────────────────────────────────────┤
 │                算子池                         │
 ├────────────────────────────────────────────┤
 │         数据源适配层 │ 输出适配层              │
 ├────────────────────────────────────────────┤
 │           基础设施（日志/安全/DI）             │
 └────────────────────────────────────────────┘
```

详细设计见 [设计文档](docs/GIS数据分析框架设计文档.md)。

## 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 10 LTS | 运行框架 |
| C# | 14 | 开发语言 |
| GDAL/OGR | 3.x | GIS 核心（数据读写、坐标转换） |
| NetTopologySuite | 2.x | 轻量空间计算补充 |
| System.Text.Json | - | 序列化 |
| Microsoft.Extensions.* | - | DI、日志、配置 |

## 文档

- [需求文档](docs/GIS数据分析框架需求文档.md) —— 业务目标、功能需求、阶段规划
- [设计文档](docs/GIS数据分析框架设计文档.md) —— 架构设计、接口定义、执行机制
- [贡献指南](CONTRIBUTING.md) —— 如何参与贡献

## 阶段规划

| 阶段 | 目标 | 状态 |
|------|------|------|
| Phase 1 | MVP：串联执行、5-8 个核心算子、3-5 个数据源 | 🔜 即将开始 |
| Phase 2 | 并行调度、20+ 算子、更多适配器、安全 | 📅 规划中 |
| Phase 3 | 分布式、可视化编辑器、插件市场 | 📅 远期规划 |

## 许可证

本项目基于 [MIT License](LICENSE) 开源发布。

Copyright (c) 2026 OpenGIS DAF Contributors
