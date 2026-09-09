#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
新星市（虚构）demo 数据生成器
================================

为 OpenGisDAF demo 生成全部合成测试数据。数据中的城市、学校、道路、
地块、机构与人名均为虚构，坐标位于无人认领的虚构区域，不含任何真实
用户或真实业务数据。

生成文件（EPSG:4326 GeoJSON）：
  schools.geojson       8 所学校点 —— 含 2 个 null 字段、1 个空字符串、
                       1 个带 Z 值的点、1 个位于研究区边界外
  roads.geojson         6 条道路线 —— 含 1 条自相交线（GEOM_NOT_SIMPLE）、
                       1 条完全位于生态保护区内（containment_check 可发现）
  landuse.geojson       5 个地块面 —— 含 1 个蝴蝶结无效面（GEOM_INVALID）、
                       1 处地块重叠（intersect_check 集合内检查可发现）、
                       1 个 null 属性（配合 null_value_filler）
  city_boundary.geojson 研究区边界面 —— 配合 clip / within 检查

用法：
  python demo/tools/generate_testdata.py [--out demo/data]
"""

import argparse
import json
import os


def feature(fid, geometry, properties):
    return {"type": "Feature", "id": fid, "geometry": geometry, "properties": properties}


def point(coord):
    return {"type": "Point", "coordinates": coord}


def line(coords):
    return {"type": "LineString", "coordinates": coords}


def polygon(coords):
    return {"type": "Polygon", "coordinates": [coords]}


def collection(features):
    return {"type": "FeatureCollection", "features": features}


def schools():
    """8 所虚构学校。问题注入见各行注释。"""
    rows = [
        # (坐标, 名称, 校长, 电话, 学位, 建校年份, 注入的问题)
        ((110.24, 30.18), "新星市第一小学", "王建国", "0717-6600001", 1200, 1998, None),
        ((110.28, 30.20), "新星市第二小学", "李秀兰", "0717-6600002", 960, 2005, None),
        ((110.33, 30.17), "新星市实验中学", None, "0717-6600003", 1800, 2001, "principal=null → ATTR_MISSING"),
        ((110.35, 30.25), "新星市外国语学校", "陈静", "", 1500, 2012, "phone=\"\" → ATTR_EMPTY"),
        ((110.21, 30.24), "新星市第九中学", "周强", "0717-6600005", 2100, 1985, None),
        ((110.29, 30.16), "明德国际学校", None, "0717-6600006", 800, 2018, "principal=null → ATTR_MISSING"),
        ((110.32, 30.22, 45.5), "启航科技高中", "孙梅", "0717-6600007", 1600, 2015, "带 Z 值 → 验证三维保留"),
        ((110.15, 30.25), "云湖小学", "赵磊", "0717-6600008", 700, 2020, "边界外 → containment/clip 演示"),
    ]
    feats = []
    for i, (coord, name, principal, phone, capacity, year, _note) in enumerate(rows, start=1):
        feats.append(feature(f"s{i:02d}", point(list(coord)), {
            "school_name": name,
            "principal": principal,
            "phone": phone,
            "capacity": capacity,
            "built_year": year,
        }))
    return collection(feats)


def roads():
    """6 条虚构道路。问题注入见各行注释。"""
    rows = [
        # (坐标串, 名称, 等级, 车道数, 注入的问题)
        ([(110.20, 30.19), (110.30, 30.19), (110.38, 30.19)], "明珠大道", "主干道", 6, None),
        ([(110.24, 30.15), (110.24, 30.25)], "学府路", "次干道", 4, None),
        ([(110.34, 30.14), (110.40, 30.20)], "滨江路", "次干道", 4, None),
        ([(110.19, 30.27), (110.28, 30.27), (110.36, 30.27)], "环城北路", "主干道", 6, None),
        # 8 字形自相交线 → GEOM_NOT_SIMPLE (Warning)
        ([(110.22, 30.16), (110.26, 30.20), (110.22, 30.20), (110.26, 30.16)], "翠柳巷", "支路", 2,
         "自相交 → GEOM_NOT_SIMPLE"),
        # 完全位于生态保护区 G-004 内 → containment_check(within) 可发现
        ([(110.34, 30.23), (110.39, 30.26)], "云湖联络线", "支路", 2,
         "完全在生态保护区内 → WITHIN 违规"),
    ]
    feats = []
    for i, (coords, name, road_class, lanes, _note) in enumerate(rows, start=1):
        feats.append(feature(f"r{i:02d}", line([list(c) for c in coords]), {
            "road_name": name,
            "road_class": road_class,
            "lanes": lanes,
        }))
    return collection(feats)


def landuse():
    """5 个虚构地块。问题注入见各行注释。"""
    rows = [
        # (环坐标, 用地类型, 编号, 面积ha, 权属, 注入的问题)
        ([(110.22, 30.17), (110.27, 30.17), (110.27, 30.21), (110.22, 30.21)],
         "居住用地", "R-001", 25.6, "新星城建集团", None),
        # 与 R-001 在 x∈[110.26,110.27] 重叠 → 集合内 intersect_check 可发现
        ([(110.26, 30.18), (110.30, 30.18), (110.30, 30.21), (110.26, 30.21)],
         "商业用地", "B-002", 12.3, "星汇商业", "与 R-001 重叠"),
        ([(110.31, 30.15), (110.36, 30.15), (110.36, 30.19), (110.31, 30.19)],
         "工业用地", "M-003", 48.9, "新星工业园", None),
        # 生态保护区：云湖联络线完全位于其中
        ([(110.33, 30.22), (110.40, 30.22), (110.40, 30.27), (110.33, 30.27)],
         "生态保护区", "G-004", 35.0, "市林业局", "道路越界的检查目标"),
        # 蝴蝶结多边形（顶点顺序交叉）→ GEOM_INVALID (Error)；owner=null → ATTR_MISSING
        ([(110.23, 30.22), (110.27, 30.22), (110.23, 30.25), (110.27, 30.25)],
         "待平整地块", "X-005", 8.8, None, "蝴蝶结 → GEOM_INVALID；owner=null"),
    ]
    feats = []
    for i, (ring, landuse_type, plot_no, area_ha, owner, _note) in enumerate(rows, start=1):
        closed = list(ring) + [ring[0]]
        props = {
            "landuse_type": landuse_type,
            "plot_no": plot_no,
            "area_ha": area_ha,
        }
        if owner is not None:
            props["owner"] = owner
        else:
            props["owner"] = None
        feats.append(feature(f"p{i:02d}", polygon([list(c) for c in closed]), props))
    return collection(feats)


def city_boundary():
    """研究区边界（不规则五边形）。"""
    ring = [
        (110.20, 30.14), (110.38, 30.12), (110.42, 30.24),
        (110.30, 30.30), (110.18, 30.26),
    ]
    closed = list(ring) + [ring[0]]
    return collection([
        feature("boundary", polygon([list(c) for c in closed]), {
            "name": "新星市研究区",
            "code": "XXS-STUDY-001",
        }),
    ])


def write_json(path, obj):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)
    print(f"已生成 {path} ({len(obj['features'])} 个要素)")


def main():
    parser = argparse.ArgumentParser(description="生成 OpenGisDAF demo 合成数据")
    parser.add_argument("--out", default=os.path.join(os.path.dirname(__file__), "..", "data"),
                        help="输出目录（默认 demo/data）")
    args = parser.parse_args()

    out_dir = os.path.abspath(args.out)
    write_json(os.path.join(out_dir, "schools.geojson"), schools())
    write_json(os.path.join(out_dir, "roads.geojson"), roads())
    write_json(os.path.join(out_dir, "landuse.geojson"), landuse())
    write_json(os.path.join(out_dir, "city_boundary.geojson"), city_boundary())
    print("全部合成数据生成完毕。")


if __name__ == "__main__":
    main()
