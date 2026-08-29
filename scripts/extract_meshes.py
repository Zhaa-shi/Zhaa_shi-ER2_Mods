# -*- coding: utf-8 -*-
"""从 er2bundle 按 prefab 引用精确提取道具网格 → OBJ 缓存（ShatterFX 运行时加载）。

v2（prefab 驱动）：解析目标 prefab 的 m_Objects 里的 MeshFilter → m_Mesh 引用，
导出该道具真正使用的网格（不猜名字，Cube.007 / ammoboxes 这类怪名也能抓到）。
文件名：<Prefab基名>__<网格名>.obj —— 运行时按物体名前缀匹配。

用法: python scripts/extract_meshes.py
输出: ShatterFX/Assets/meshes/
"""
import UnityPy
import os
import sys

BUNDLE = r"E:\SteamLibrary\steamapps\common\Easy Red 2\Easy Red 2_Data\StreamingAssets\CorvoBundles\er2bundle"
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "ShatterFX", "Assets", "meshes")

# 目标道具（manifest 里的 prefab 路径，小写比较）
TARGETS = [
    "assets/er2 assets/prefabs/props and buildings/fornitures/table_01.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/sidetable02.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/table_side.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/furniture_kitchen_table.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/church_bench.prefab",
    "assets/er2 assets/prefabs/props and buildings/stalingrad props/sovietbench01.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/couchsmall_1.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/furniture_wood_drawers.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/japanesetable.prefab",
    "assets/er2 assets/prefabs/props and buildings/panzerfaustcrate_g_lod.prefab",
    "assets/er2 assets/models/props/vidovicarts/prefabs/intact/crate_tiny1.prefab",
    "assets/er2 assets/prefabs/props and buildings/ammobox_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/woodenstorage.prefab",
    "assets/er2 assets/prefabs/props and buildings/ammocrate.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/woodenbarrels.prefab",
    "assets/er2 assets/models/props/vidovicarts/prefabs/intact/wood_barrel.prefab",
    "assets/er2 assets/prefabs/props and buildings/medkit_open_prefab.prefab",
    "assets/er2 assets/prefabs/props and buildings/cratehuge01.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxjap_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxfr_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxeng_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxrus_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxger_lw_refill.prefab",
    "assets/er2 assets/prefabs/props and buildings/grenadeboxger_refill.prefab",
    "assets/er2 assets/models/props/vidovicarts/prefabs/intact/crate_large_grm.prefab",
    "assets/er2 assets/models/props/vidovicarts/prefabs/intact/crate_small_grm.prefab",
    "assets/er2 assets/prefabs/props and buildings/panzershreck crate.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/japmetaltable.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/table_02.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/coffee_table.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/old sidetable 1.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/furniture_bedroom_cupboard.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/furniture_bedroom_cupboard_open.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/couchfancy.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/couchfancysmall.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/couchbig_1.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/military_bed_01.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/military_bed_03.prefab",
    "assets/er2 assets/prefabs/props and buildings/fornitures/old bed 3.prefab",
]

MAX_EXPORTS = 200
MIN_VERTS = 20
MAX_VERTS = 8000


def pptr(d):
    """从 PPtr 字典取 pathID——兼容两种结构：直接 {'m_FileID','m_PathID'} 或
    包裹 {'component'/'transform'/'m_GameObject': {PPtr}}。"""
    if not isinstance(d, dict):
        return None
    if "m_PathID" in d:
        return d["m_PathID"]
    for k in ("component", "transform", "m_GameObject", "gameObject"):
        if k in d and isinstance(d[k], dict):
            return d[k].get("m_PathID")
    return None


def walk_go(af, go_pid, visited, depth, out):
    """从 prefab 根 GameObject 沿 Transform 子树递归，收集所有 MeshFilter 引用的网格 pathID。"""
    if go_pid is None or go_pid in visited or depth > 6 or len(visited) > 60:
        return
    visited.add(go_pid)
    go = af.objects.get(go_pid)
    if go is None or go.type.name != "GameObject":
        return
    tt = go.read_typetree()
    child_gos = []
    for cref in tt.get("m_Component") or []:
        cpid = pptr(cref)
        co = af.objects.get(cpid) if cpid is not None else None
        if co is None:
            continue
        if co.type.name == "MeshFilter":
            try:
                mf = co.read_typetree()
                mref = mf.get("m_Mesh") or {}
                mpid = mref.get("m_PathID")
                if mpid is not None and mref.get("m_FileID", 0) in (0, -1):
                    out.append(mpid)
            except Exception:
                pass
        elif co.type.name == "Transform":
            try:
                tr = co.read_typetree()
                for ch in tr.get("m_Children") or []:
                    cpid2 = pptr(ch)
                    ctr = af.objects.get(cpid2) if cpid2 is not None else None
                    if ctr is not None and ctr.type.name == "Transform":
                        try:
                            cgo = pptr(ctr.read_typetree().get("m_GameObject"))
                            child_gos.append(cgo)
                        except Exception:
                            pass
            except Exception:
                pass
    for cg in child_gos:
        walk_go(af, cg, visited, depth + 1, out)


def main():
    os.makedirs(OUT, exist_ok=True)
    print("loading bundle (large, be patient)...")
    env = UnityPy.load(BUNDLE)
    print("container entries:", len(env.container))

    targets_lower = {t.lower() for t in TARGETS}
    target_objs = []
    for path, obj in env.container.items():
        if path.lower() in targets_lower:
            target_objs.append((path, obj))
            print("target found:", path, "path_id", obj.path_id, "file", obj.assetsfile.name)
    if not target_objs:
        print("NO TARGET FOUND in container")
        sys.exit(1)

    exported = 0
    for path, tobj in target_objs:
        base = os.path.splitext(os.path.basename(path))[0]  # "Table_01"
        af = tobj.assetsfile
        mesh_pids = []
        walk_go(af, tobj.path_id, set(), 0, mesh_pids)
        mesh_pids = list(dict.fromkeys(mesh_pids))
        if not mesh_pids:
            print("  no mesh refs:", base)
            continue

        for mpid in mesh_pids:
            mo = af.objects.get(mpid)
            if mo is None or mo.type.name != "Mesh":
                continue
            try:
                m = mo.read()
                mname = getattr(m, "m_Name", "mesh") or "mesh"
                data = m.export()  # Wavefront OBJ 文本
                if not data or len(data) < 400:
                    print("  skip (empty):", base, mname)
                    continue
                vert_count = data.count("\nv ") + (1 if data.startswith("v ") else 0)
                tri_count = data.count("\nf ") + (1 if data.startswith("f ") else 0)
                if vert_count < MIN_VERTS or vert_count > MAX_VERTS:
                    print("  skip (size):", base, mname, vert_count, "v", tri_count, "t")
                    continue
                safe_base = "".join(c for c in base if c.isalnum() or c in "_-").strip()
                safe_mesh = "".join(c for c in mname if c.isalnum() or c in "_-").strip()
                fname = "%s__%s.obj" % (safe_base, safe_mesh) if safe_mesh else safe_base + ".obj"
                with open(os.path.join(OUT, fname), "w", encoding="utf-8") as f:
                    f.write(data)
                print("  exported:", fname, vert_count, "v,", tri_count, "t")
                exported += 1
                if exported >= MAX_EXPORTS:
                    print("reached MAX_EXPORTS")
                    print("DONE. exported:", exported)
                    return
            except Exception as e:
                print("  mesh fail:", base, mpid, e)
    print("DONE. exported:", exported)


if __name__ == "__main__":
    main()
