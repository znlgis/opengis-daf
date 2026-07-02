# OpenGIS Utils for .NET — 预评估报告

> 评估日期：2026-07-02
> 评估阶段：M1（项目工程搭建与基础设施）
> 评估目的：验证 M2 阶段数据源/输出适配器的技术依赖可行性

## 1. 结论

**✅ 可用** — `OpenGIS.Utils` v1.0.2（2026-06-09 发布）可以满足 M2 阶段 Shapefile/PostGIS/GeoJSON 读写需求。

## 2. API 覆盖矩阵

| 能力 | 支持 | 说明 |
|------|:---:|------|
| Shapefile 读取 | ✅ | `OguLayerUtil.ReadLayer(SHP, path)`，支持空间过滤 |
| Shapefile 写入 | ✅ | `OguLayerUtil.WriteLayer(SHP, ...)`，含编码检测 |
| PostGIS 读取 | ✅ | `DataFormatType.POSTGIS`，通过 GDAL OGR PostgreSQL 驱动 |
| PostGIS 写入 | ✅ | 同上 |
| GeoJSON 读取 | ✅ | `DataFormatType.GEOJSON` |
| GeoJSON 写入 | ✅ | `OguLayerUtil.WriteLayer(GEOJSON, ...)` |
| CRS 转换 | ✅ | `CrsUtil.Transform(wkt, srcEpsg, dstEpsg)`，含 CGCS2000 支持 |
| 几何运算 | ✅ | `GeometryUtil.*`（缓冲区、交并差、拓扑验证） |
| FileGDB | ✅ | `DataFormatType.FileGDB` |

## 3. 平台兼容性

全平台支持（Windows x64 / Linux x64+arm64 / macOS x64+arm64），通过 `MaxRev.Gdal.Universal`（3.12.0.427+）自动携带 GDAL 原生库，无需手动安装。

## 4. 风险评估

| 风险项 | 等级 | 说明 |
|--------|:----:|------|
| 社区活跃度 | 🔴 高 | 单维护者，仅 969 下载，3 GitHub Stars |
| 底层依赖 | 🟢 低 | MaxRev.Gdal.Core 1.9M+ 下载，频繁更新 |
| 依赖版本 | 🟡 中 | 要求 `System.Text.Json` ≥ 10.0.0，本项目使用 .NET 10，兼容 |
| 维护持续性 | 🟡 中 | 发布频率高但社区极小，存在断更风险 |
| LGPL 许可证 | 🟢 低 | 商业友好 |

## 5. Plan B：NTS 全栈方案

如果 OpenGIS.Utils 出现重大兼容性问题或停止维护，切换到 NetTopologySuite 全栈方案：

| 包 | 用途 | 下载量 |
|----|------|--------|
| `NetTopologySuite` 2.6.0 | 核心几何运算 | 213M+ |
| `NetTopologySuite.IO.Esri.Shapefile` 1.2.0 | Shapefile 读写 | 1.3M |
| `NetTopologySuite.IO.GeoJSON` 4.0.0 | GeoJSON 读写 | 18.9M |
| `Npgsql.NetTopologySuite` 10.0.3 | PostGIS 读写 | 29.3M |
| `ProjNet` | CRS 转换 | — |

**代价**：无统一图层抽象，适配器代码量更大，不支持 FileGDB。

## 6. 风险缓释

1. 通过 `IFeatureSource` / `IFeatureSink` 接口抽象，不直接依赖 OpenGIS.Utils
2. OpenGIS.Utils 源码开源（LGPL），可在紧急情况下 Fork 自维护
3. NTS 已作为 Plan B 验证通过，切换成本可控（约 2-3 天）

## 7. 引入方式

**以 git 子模块方式引入，跟踪 `main` 分支**：

```bash
git submodule add -b main https://github.com/znlgis/opengis-utils-for-net.git extern/opengis-utils-for-net
```

子模块路径：`extern/opengis-utils-for-net/`，当前最新提交 `dac5896`。

**采用子模块而非 NuGet 包的原因**：
1. 源码级引用，便于调试和修改（LGPL 许可允许）
2. 直接控制依赖版本，不受 NuGet 发布周期影响
3. 可在紧急情况下直接修改源码（Fork → 推送到自有仓库 → 更新子模块 URL）
4. 与项目一起构建，CI 流程更简单

M2 阶段适配器项目将直接引用子模块中的 `extern/opengis-utils-for-net/src/OpenGIS.Utils/OpenGIS.Utils.csproj`。

## 8. 推荐

**M2 阶段使用子模块引入的 OpenGIS.Utils v1.0.2 作为主要方案**，同时在适配器设计中保持对 NTS 方案的兼容性（通过接口隔离）。
