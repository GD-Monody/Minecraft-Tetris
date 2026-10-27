# 扫描 26.2.jar 里所有"完整立方体"方块模型，导出候选清单。
# 判断标准：父级链最终落在 cube 类根模型上，且中间没有任何模型自己写 elements
# （写了 elements 说明是楼梯/台阶/交叉植物之类，不是完整立方体）。
# 用法：python tools/scan_full_blocks.py
import json
import os
import re
import zipfile

JAR = os.path.expandvars(r'%APPDATA%\.minecraft\versions\26.2\26.2.jar')
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "tools", "full_blocks.json")

# 特殊方块：模型里的“平台/推头”面不在 up 上，手动指定六面，避免启发式填错。
FACE_OVERRIDES = {
    'piston': {
        'up': 'piston_top', 'down': 'piston_bottom',
        'north': 'piston_side', 'south': 'piston_side',
        'east': 'piston_side', 'west': 'piston_side',
    },
    'sticky_piston': {
        'up': 'piston_top_sticky', 'down': 'piston_bottom',
        'north': 'piston_side', 'south': 'piston_side',
        'east': 'piston_side', 'west': 'piston_side',
    },
    # 雪地草方块：资源包给模型写的 top 还是 grass_block_top（草绿），
    # 游戏里是客户端在 snowy 状态下把顶面换成雪，所以这里手动改。
    # 南瓜类：MC 的模型继承 cube_column / orientable，down 也用的是 pumpkin_top（带梗的顶面）。
    # 原版看不到底面，但这里的方块底面朝外，露着梗不像话，所以底面改用侧面的竖条纹。
    'pumpkin': {
        'up': 'pumpkin_top', 'down': 'pumpkin_side',
        'north': 'pumpkin_side', 'south': 'pumpkin_side',
        'east': 'pumpkin_side', 'west': 'pumpkin_side',
    },
    'carved_pumpkin': {
        'up': 'pumpkin_top', 'down': 'pumpkin_side',
        'north': 'carved_pumpkin', 'south': 'pumpkin_side',
        'east': 'pumpkin_side', 'west': 'pumpkin_side',
    },
    'jack_o_lantern': {
        'up': 'pumpkin_top', 'down': 'pumpkin_side',
        'north': 'jack_o_lantern', 'south': 'pumpkin_side',
        'east': 'pumpkin_side', 'west': 'pumpkin_side',
    },
    'grass_block_snow': {
        'up': 'snow', 'down': 'dirt',
        'north': 'grass_block_snow', 'south': 'grass_block_snow',
        'east': 'grass_block_snow', 'west': 'grass_block_snow',
    },
}

# 流体方块：MC 里没有 cube 模型（model 里只写了个粒子纹理），
# 也没法从模型推六面，所以整条链路手动指定，直接进候选。
MANUAL_BLOCKS = {
    'water': {
        'up': 'water_still', 'down': 'water_still',
        'north': 'water_still', 'south': 'water_still',
        'east': 'water_still', 'west': 'water_still',
    },
    'lava': {
        'up': 'lava_still', 'down': 'lava_still',
        'north': 'lava_still', 'south': 'lava_still',
        'east': 'lava_still', 'west': 'lava_still',
    },
}

CUBE_ROOTS = {
    'cube_all', 'cube', 'cube_column', 'cube_column_horizontal',
    'cube_bottom_top', 'cube_top', 'cube_directional',
    'cube_mirrored', 'cube_mirrored_all', 'block',
}

# 透明/发光/特殊渲染，或者不是普通方块
EXCLUDE_WORDS = (
    'glass', 'leaves', 'ice', 'water', 'lava', 'slime', 'honey',
    'froglight', 'sea_lantern', 'shroomlight', 'glowstone', 'magma',
    'barrier', 'light', 'structure_void', 'bedrock',
)


def main():
    z = zipfile.ZipFile(JAR)
    names = set(z.namelist())

    def read_json(path):
        if path not in names:
            return None
        try:
            return json.loads(z.read(path).decode('utf-8'))
        except Exception:
            return None

    def model_path(name):
        return 'assets/minecraft/models/block/%s.json' % name

    def chain(name):
        seq = []
        cur = name
        while cur and len(seq) < 12:
            j = read_json(model_path(cur))
            if not j:
                return None
            seq.append((cur, bool(j.get('elements'))))
            parent = j.get('parent', '')
            if not parent:
                break
            cur = parent.replace('minecraft:', '')
            if cur.startswith('block/'):
                cur = cur[6:]
        return seq

    def collect(name, depth=0):
        if depth > 10:
            return {}
        j = read_json(model_path(name))
        if not j:
            return {}
        tex = {}
        parent = j.get('parent', '')
        if parent:
            p = parent.replace('minecraft:', '')
            if p.startswith('block/'):
                p = p[6:]
            tex.update(collect(p, depth + 1))
        tex.update(j.get('textures', {}))
        return tex

    def resolve(value, tex, depth=0):
        if isinstance(value, dict):
            value = value.get('sprite', '')
        while isinstance(value, str) and value.startswith('#') and depth < 8:
            value = tex.get(value[1:], '')
            if isinstance(value, dict):
                value = value.get('sprite', '')
            depth += 1
        if not isinstance(value, str):
            return ''
        value = value.replace('minecraft:', '')
        if value.startswith('block/'):
            value = value[6:]
        return value

    def is_full_cube(name):
        """沿父级链找第一个自己写 elements 的模型，全部是 16³ 立方体才算完整方块。"""
        seq = chain(name)
        if not seq:
            return False
        for cur, has_elements in seq:
            if not has_elements:
                continue
            j = read_json(model_path(cur)) or {}
            elements = j.get('elements') or []
            if not elements:
                return False
            for el in elements:
                if el.get('from') != [0, 0, 0] or el.get('to') != [16, 16, 16]:
                    return False
            return True
        return False

    def first_elements(name):
        """沿父级链找第一个自己写 elements 的模型，返回它的 faces：{面: (贴图引用, tintindex)}。

        为什么要看 elements：像草方块这种模型自己写了 elements，贴图槽名是
        bottom / top / side（不是 down / up / north…），按槽名猜会猜错 ——
        底面会退回 side，结果底面用了草边图。按 faces 里的 "#bottom" 这类引用
        顺着贴图表解析才准。
        """
        cur = name
        for _ in range(12):
            j = read_json(model_path(cur))
            if not j:
                return None
            els = j.get('elements')
            if els:
                faces = {}
                for el in els:
                    for face, data in (el.get('faces') or {}).items():
                        if face in faces or not isinstance(data, dict):
                            continue
                        ref = data.get('texture')
                        if not ref:
                            continue
                        faces[face] = (ref, data.get('tintindex'))
                return faces
            parent = j.get('parent', '')
            if not parent:
                return None
            cur = parent.replace('minecraft:', '')
            if cur.startswith('block/'):
                cur = cur[6:]
        return None

    def drop_variant(name):
        """同一个方块的不同朝向/开关状态/镜像变体只留一个，避免池子被重复占满。"""
        if re.search(r'_(horizontal|[xyz]|mirrored|inventory|on|lit|powered|conditional|triggered|awake|dormant|bloom|crafting|open|empty)$', name):
            return True
        if name.startswith(('test_', 'structure_block_', 'jigsaw', 'test_instance_block')):
            return True
        if re.match(r'^respawn_anchor_[1-4]$', name):
            return True
        if re.match(r'^suspicious_(gravel|sand)_[1-3]$', name):
            return True
        return False

    # 中文名：方块名到 lang 里的显示名
    lang = {}
    if 'assets/minecraft/lang/zh_cn.json' in names:
        try:
            lang = json.loads(z.read('assets/minecraft/lang/zh_cn.json').decode('utf-8'))
        except Exception:
            lang = {}

    def zh_name(name):
        return lang.get('block.minecraft.%s' % name, name)

    models = sorted(
        n[len('assets/minecraft/models/block/'):-len('.json')]
        for n in names
        if n.startswith('assets/minecraft/models/block/') and n.endswith('.json')
    )

    candidates = []
    for name in models:
        if name in MANUAL_BLOCKS:
            candidates.append({
                'name': name, 'zh': zh_name(name), 'root': 'manual',
                'faces': dict(MANUAL_BLOCKS[name]), 'tint': [],
            })
            continue
        if any(w in name for w in EXCLUDE_WORDS):
            continue
        if drop_variant(name):
            continue
        if not is_full_cube(name):
            continue

        tex = collect(name)
        if not tex:
            continue

        element_faces = first_elements(name) or {}
        faces = {}
        tint_faces = []

        # 先按模型 elements 里的面引用取（草方块这类靠这个才对）
        for face in ('up', 'down', 'north', 'south', 'east', 'west'):
            entry = element_faces.get(face)
            if not entry:
                continue
            ref, tint = entry
            png = resolve(ref, tex)
            if png and ('assets/minecraft/textures/block/%s.png' % png) in names:
                faces[face] = png
                if tint is not None:
                    tint_faces.append(face)

        # 模型里没写这一面时，退回按贴图槽名猜（cube_all / cube_column 之类）
        for face in ('up', 'down', 'north', 'south', 'east', 'west'):
            if face in faces:
                continue
            key = 'all' if 'all' in tex else face
            raw = tex.get(key) or tex.get('side') or tex.get('all') or tex.get('top') or tex.get('bottom')
            if not raw:
                continue
            png = resolve(raw, tex)
            if png and ('assets/minecraft/textures/block/%s.png' % png) in names:
                faces[face] = png
        if not faces:
            continue
        if name in FACE_OVERRIDES:
            faces = dict(FACE_OVERRIDES[name])
            tint_faces = []
        seq = chain(name) or []
        candidates.append({'name': name, 'zh': zh_name(name), 'root': seq[-1][0] if seq else '', 'faces': faces, 'tint': tint_faces})

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, 'w', encoding='utf-8') as f:
        json.dump(candidates, f, ensure_ascii=False, indent=1)

    print('完整立方体候选: %d' % len(candidates))
    roots = {}
    for c in candidates:
        roots[c['root']] = roots.get(c['root'], 0) + 1
    for r, c in sorted(roots.items(), key=lambda kv: -kv[1]):
        print('  %-24s %d' % (r, c))


if __name__ == '__main__':
    main()
