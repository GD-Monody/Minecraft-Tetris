# -*- coding: utf-8 -*-
"""按 MC 原版分组，把每个方块的「放置音效」抽出来，并生成 BlockSfxData.cs。

MC 里的放置音是 **按方块材质分组** 的（SoundType），比如木头是 dig/wood1..4，
石头是 dig/stone1..4，羊毛是 dig/cloth1..4。分组定义在 sounds.json 的
`block.<组>.place` 事件里（assets/minecraft/sounds.json），每组的 sounds 数组
就是随机变体，个别条目带 volume。

所以这个脚本干三件事：
1. 读最新一份 assets 索引里的 sounds.json，拿到本脚本用到的每个组的变体文件列表；
2. 把这些 ogg 从 assets/objects 拷到 Assets/Resources/Sfx/Place/（文件名扁平化，
   dig/stone1 -> dig_stone1），同一批次被多个组共用只拷一份；
3. 按下面的「方块名 -> 组」规则表，给 342 个方块各分一组，生成
   Assets/Scripts/Core/BlockSfxData.cs。

用法：
    python tools/build_place_sfx.py --dry      # 只打印分组结果，不写任何文件
    python tools/build_place_sfx.py            # 真正提取 + 生成

要改某个方块的音效，就在 RULES 里加一条更靠前的规则，然后重跑。
"""

import glob
import json
import os
import re
import shutil
import sys

ASSETS = os.path.join(os.environ.get("APPDATA", ""), ".minecraft", "assets")
PROJ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SFX_DIR = os.path.join(PROJ, "Assets", "Resources", "Sfx", "Place")
CS_PATH = os.path.join(PROJ, "Assets", "Scripts", "Core", "BlockSfxData.cs")
BLOCKS_JSON = os.path.join(PROJ, "tools", "full_blocks.json")

# --- 用到的音效组：组名 -> sounds.json 里的事件名 -------------------------
# 组名大多等于事件名去掉 block. 前缀和 .place 后缀，方便和 MC 文档对照；
# 水和岩浆例外，原版没有「水方块」，放下液体时响的是桶的音效，所以用 item.bucket.*。
GROUPS = [
    ("stone", "block.stone.place"),
    ("wood", "block.wood.place"),
    ("gravel", "block.gravel.place"),
    ("grass", "block.grass.place"),
    ("sand", "block.sand.place"),
    ("wool", "block.wool.place"),
    ("metal", "block.metal.place"),
    ("iron", "block.iron.place"),
    ("netherite_block", "block.netherite_block.place"),
    ("ancient_debris", "block.ancient_debris.place"),
    ("copper", "block.copper.place"),
    ("copper_bulb", "block.copper_bulb.place"),
    ("copper_grate", "block.copper_grate.place"),
    ("basalt", "block.basalt.place"),
    ("deepslate", "block.deepslate.place"),
    ("deepslate_bricks", "block.deepslate_bricks.place"),
    ("deepslate_tiles", "block.deepslate_tiles.place"),
    ("tuff", "block.tuff.place"),
    ("tuff_bricks", "block.tuff_bricks.place"),
    ("polished_tuff", "block.polished_tuff.place"),
    ("calcite", "block.calcite.place"),
    ("dripstone_block", "block.dripstone_block.place"),
    ("bone_block", "block.bone_block.place"),
    ("netherrack", "block.netherrack.place"),
    ("nether_bricks", "block.nether_bricks.place"),
    ("soul_sand", "block.soul_sand.place"),
    ("soul_soil", "block.soul_soil.place"),
    ("nylium", "block.nylium.place"),
    ("wart_block", "block.wart_block.place"),
    ("amethyst_block", "block.amethyst_block.place"),
    ("sculk", "block.sculk.place"),
    ("sculk_catalyst", "block.sculk_catalyst.place"),
    ("lodestone", "block.lodestone.place"),
    ("sponge", "block.sponge.place"),
    ("wet_sponge", "block.wet_sponge.place"),
    ("cherry_wood", "block.cherry_wood.place"),
    ("bamboo_wood", "block.bamboo_wood.place"),
    ("nether_wood", "block.nether_wood.place"),
    ("stem", "block.stem.place"),
    ("mud", "block.mud.place"),
    ("mud_bricks", "block.mud_bricks.place"),
    ("packed_mud", "block.packed_mud.place"),
    ("muddy_mangrove_roots", "block.muddy_mangrove_roots.place"),
    ("moss", "block.moss.place"),
    ("rooted_dirt", "block.rooted_dirt.place"),
    ("nether_ore", "block.nether_ore.place"),
    ("gilded_blackstone", "block.gilded_blackstone.place"),
    ("creaking_heart", "block.creaking_heart.place"),
    ("resin", "block.resin.place"),
    ("resin_bricks", "block.resin_bricks.place"),
    ("chiseled_bookshelf", "block.chiseled_bookshelf.place"),
    ("sulfur", "block.sulfur.place"),
    ("cinnabar", "block.cinnabar.place"),
    ("potent_sulfur", "block.potent_sulfur.place"),
    ("suspicious_gravel", "block.suspicious_gravel.place"),
    ("suspicious_sand", "block.suspicious_sand.place"),
    ("snow", "block.snow.place"),
    ("coral_block", "block.coral_block.place"),
    # 液体：原版是拿桶倒出来的声音
    ("fluid_water", "item.bucket.empty"),
    ("fluid_lava", "item.bucket.empty_lava"),
]

# --- 原版没有对应事件、要自己凑的组 ---------------------------------------
# (组名, 采样名列表, 音量, 播放音高)
# 玻璃：MC 的 block.glass.place 直接指向 dig/stone1..4，放玻璃听起来就是放石头，
# 只有 block.glass.break 是 glass1..3 这一组「叮」声。这里拿破坏音的采样当放置音，
# 音量压到 0.45、音高提到 1.18，听感是一下轻脆的落放声，不像砸碎。
SYNTH_GROUPS = [
    ("glass_place", ["random/glass1", "random/glass2", "random/glass3"], 0.45, 1.18),
]

# --- 方块名 -> 组 的规则（顺序敏感，先具体后通用） -----------------------
# 用 fullmatch 匹配，所以写 alternation 时要把全名写清楚；被规则顶掉的方块
# 会落到最后的兜底 stone，脚本会打印「哪些方块走了兜底」以便核对。
RULES = [
    # 玻璃：原版的放置音其实是石头声，这里换成破坏音的采样（见 SYNTH_GROUPS）
    (r".*glass.*", "glass_place"),
    # 26.2 的新材质族，先占位，别被下面的通用规则吃掉
    ("potent_sulfur", "potent_sulfur"),
    ("sulfur|sulfur_bricks|chiseled_sulfur|polished_sulfur", "sulfur"),
    ("cinnabar|cinnabar_bricks|chiseled_cinnabar|polished_cinnabar", "cinnabar"),
    ("resin_bricks|chiseled_resin_bricks", "resin_bricks"),
    ("resin_block", "resin"),
    ("creaking_heart", "creaking_heart"),

    # 木头：樱桃木、竹、下界木各有独立音色，原木/木板/菌柄分开
    ("cherry_log|cherry_wood|cherry_planks|stripped_cherry_log|stripped_cherry_wood", "cherry_wood"),
    ("bamboo_block|bamboo_planks|bamboo_mosaic|stripped_bamboo_block", "bamboo_wood"),
    ("crimson_stem|crimson_hyphae|warped_stem|warped_hyphae"
     "|stripped_crimson_stem|stripped_crimson_hyphae"
     "|stripped_warped_stem|stripped_warped_hyphae", "stem"),
    ("crimson_planks|warped_planks", "nether_wood"),
    ("chiseled_bookshelf", "chiseled_bookshelf"),
    ("barrel|bookshelf|crafting_table|cartography_table|fletching_table|smithing_table"
     "|loom|jukebox|note_block|crafter", "wood"),
    ("pumpkin|carved_pumpkin|jack_o_lantern|melon", "wood"),
    (r".*_log|.*_wood|.*_planks|.*_stem|.*_hyphae", "wood"),

    # 树叶：原版是 SoundType.GRASS（和草方块同一组），哪棵树上长的都一样。
    # 不写这条会落到兜底的 stone，踩到树叶会响成石头。
    (r".*_leaves", "grass"),

    # 羊毛 / 混凝土 / 陶瓦
    (r".*_wool", "wool"),
    (r".*_concrete_powder", "sand"),
    (r".*_concrete", "stone"),
    (r".*_terracotta", "stone"),

    (r"(dead_)?(brain|bubble|fire|horn|tube)_coral_block", "coral_block"),

    # 液体：原版放下水/岩浆响的是倒桶的声音
    ("water", "fluid_water"),
    ("lava", "fluid_lava"),

    # 金属与矿物
    ("ancient_debris", "ancient_debris"),
    ("netherite_block", "netherite_block"),
    ("iron_block", "iron"),
    ("gold_block|diamond_block|emerald_block", "metal"),
    ("lapis_block|coal_block|redstone_block|redstone_lamp", "stone"),
    ("nether_quartz_ore|nether_gold_ore", "nether_ore"),
    (r"deepslate_.*_ore", "deepslate"),
    (r".*_ore", "stone"),
    (r".*_copper_bulb|copper_bulb", "copper_bulb"),
    (r".*_copper_grate|copper_grate", "copper_grate"),
    (r".*_cut_copper|.*chiseled_copper|copper_block|cut_copper|exposed_copper"
     "|oxidized_copper|weathered_copper", "copper"),
    ("gilded_blackstone", "gilded_blackstone"),

    # 深板岩 / 凝灰岩 / 方解石 / 钟乳石
    ("deepslate_tiles|cracked_deepslate_tiles", "deepslate_tiles"),
    ("deepslate_bricks|cracked_deepslate_bricks|chiseled_deepslate", "deepslate_bricks"),
    ("polished_deepslate", "deepslate"),
    (r"deepslate|cobbled_deepslate|reinforced_deepslate", "deepslate"),
    ("tuff_bricks|chiseled_tuff_bricks", "tuff_bricks"),
    ("polished_tuff|chiseled_tuff", "polished_tuff"),
    ("tuff", "tuff"),
    ("calcite", "calcite"),
    ("dripstone_block", "dripstone_block"),
    ("basalt|smooth_basalt|polished_basalt", "basalt"),

    # 下界
    ("netherrack", "netherrack"),
    (r".*nether_bricks", "nether_bricks"),
    ("soul_sand", "soul_sand"),
    ("soul_soil", "soul_soil"),
    (r".*_nylium", "nylium"),
    (r".*wart_block", "wart_block"),
    ("bone_block", "bone_block"),

    # 泥土 / 沙子 / 砂砾 / 雪 / 草
    ("rooted_dirt", "rooted_dirt"),
    (r".*dirt|grass_block.*|podzol|mycelium", "grass"),
    ("hay_block|dried_kelp_block|tnt|target", "grass"),
    ("mud_bricks", "mud_bricks"),
    ("packed_mud", "packed_mud"),
    ("mud", "mud"),
    ("muddy_mangrove_roots", "muddy_mangrove_roots"),
    ("moss_block|pale_moss_block", "moss"),
    ("suspicious_gravel_0|suspicious_gravel", "suspicious_gravel"),
    ("suspicious_sand_0|suspicious_sand", "suspicious_sand"),
    ("gravel", "gravel"),
    ("red_sand|sand", "sand"),
    ("snow_block", "snow"),
    ("terracotta|clay", "stone"),

    # 其余按石质处理（石头、砖、砂岩、石英、海晶石、黑石、陶土块、机器方块…）
    ("sponge", "sponge"),
    ("wet_sponge", "wet_sponge"),
    ("lodestone", "lodestone"),
    ("amethyst_block|budding_amethyst", "amethyst_block"),
    ("sculk_catalyst", "sculk_catalyst"),
    ("sculk", "sculk"),
    (r".*", "stone"),
]


def load_index():
    """最新一份 assets 索引（和 extract_sound.py 同一套找法）。"""
    files = glob.glob(os.path.join(ASSETS, "indexes", "*.json"))
    files.sort(key=os.path.getmtime, reverse=True)
    for path in files:
        with open(path, encoding="utf-8") as f:
            objs = json.load(f).get("objects", {})
        if "minecraft/sounds.json" in objs:
            return path, objs
    raise SystemExit("找不到 assets 索引，先在启动器里下载一次完整资源")


def load_sounds_json(objs):
    digest = objs["minecraft/sounds.json"]["hash"]
    path = os.path.join(ASSETS, "objects", digest[:2], digest)
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def flat_name(sound):
    return sound.replace("/", "_")


def collect_clips(sounds):
    """组名 -> [(clip 名, volume)]，顺手算出要拷哪些文件。"""
    plan = []
    files = {}
    for group, event_name in GROUPS:
        event = sounds.get(event_name)
        if event is None:
            raise SystemExit("sounds.json 里没有 %s" % event_name)

        clips = []
        for entry in event["sounds"]:
            name = entry if isinstance(entry, str) else entry["name"]
            vol = 1.0 if isinstance(entry, str) else float(entry.get("volume", 1.0))
            clips.append((flat_name(name), vol))
            files[name] = True
        plan.append((group, clips))

    # 原版没有单独事件的组：采样名直接写死
    for group, names, vol, _pitch in SYNTH_GROUPS:
        clips = []
        for name in names:
            clips.append((flat_name(name), vol))
            files[name] = True
        plan.append((group, clips))
    return plan, sorted(files)


def classify(block_name):
    for pattern, group in RULES:
        if re.fullmatch(pattern, block_name):
            return group
    raise SystemExit("方块 %s 没匹配到任何规则" % block_name)


def main():
    dry = "--dry" in sys.argv

    index_path, objs = load_index()
    sounds = load_sounds_json(objs)
    plan, needed = collect_clips(sounds)

    with open(BLOCKS_JSON, encoding="utf-8") as f:
        block_names = [b["name"] for b in json.load(f)]

    # 分组 + 统计
    groups_of = []
    counts = {}
    fell_through = []
    for name in block_names:
        group = classify(name)
        groups_of.append(group)
        counts[group] = counts.get(group, 0) + 1

    stone_rule_hit = 0
    for name in block_names:
        for pattern, group in RULES:
            if re.fullmatch(pattern, name):
                if pattern == r".*":
                    fell_through.append(name)
                    stone_rule_hit += 1
                break

    print("索引: %s" % os.path.basename(index_path))
    print("方块 %d 个 -> %d 组" % (len(block_names), len(counts)))
    group_names = [g for g, _ in plan]
    for group in group_names:
        if group not in counts:
            print("  [空组] %-22s 0" % group)
    for group in sorted(counts, key=lambda g: -counts[g]):
        print("  %-22s %3d" % (group, counts[group]))
    print("走兜底 stone 规则的有 %d 个（应全是石质/砖类）：" % stone_rule_hit)
    print("  " + ", ".join(fell_through))

    if dry:
        print("\n--dry：没有写任何文件")
        return 0

    # 提取 ogg
    os.makedirs(SFX_DIR, exist_ok=True)
    copied = 0
    total = 0
    for sound in needed:
        entry = objs.get("minecraft/sounds/%s.ogg" % sound)
        if entry is None:
            raise SystemExit("索引里没有 minecraft/sounds/%s.ogg" % sound)
        src = os.path.join(ASSETS, "objects", entry["hash"][:2], entry["hash"])
        if not os.path.exists(src):
            raise SystemExit("objects 缺文件，需要重下一次资源: %s" % src)
        dst = os.path.join(SFX_DIR, flat_name(sound) + ".ogg")
        if not os.path.exists(dst) or os.path.getsize(dst) != os.path.getsize(src):
            shutil.copyfile(src, dst)
            copied += 1
        total += os.path.getsize(src)

    # 生成 C# 数据
    lines = []
    lines.append("// 由 tools/build_place_sfx.py 自动生成，不要手改。")
    lines.append("// 数据来源：%s 里的 minecraft/sounds.json（block.<组>.place 事件，"
                 "水和岩浆用 item.bucket.*）。" % os.path.basename(index_path))
    lines.append("// 玻璃没有现成事件，采样见脚本里的 SYNTH_GROUPS。")
    lines.append("namespace MinecraftTetris")
    lines.append("{")
    lines.append("    /// <summary>")
    lines.append("    /// 每个方块的「放置音效」分组，和 MC 原版的 SoundType 一一对应。")
    lines.append("    /// 一条数据是一组的候选 clip（MC 里同一事件有多个随机变体），")
    lines.append("    /// 播放时随机挑一条，和原版一样不会每次都是同一个声音。")
    lines.append("    /// </summary>")
    lines.append("    public static class BlockSfxData")
    lines.append("    {")
    lines.append("        public const int GroupCount = %d;" % len(plan))
    lines.append("")
    lines.append("        /// <summary>组名，下标即方块表里的组编号。</summary>")
    lines.append("        public static readonly string[] Groups =")
    lines.append("        {")
    for group in group_names:
        lines.append('            "%s",' % group)
    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>每组可用的 clip（Resources/Sfx/Place 下的文件名）。</summary>")
    lines.append("        public static readonly string[][] GroupClips =")
    lines.append("        {")
    for group, clips in plan:
        names = ", ".join('"%s"' % c for c, _ in clips)
        lines.append("            new[] { %s },   // %s" % (names, group))
    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>每组的基础音量（sounds.json 里个别条目自带 volume）。</summary>")
    lines.append("        public static readonly float[] GroupVolume =")
    lines.append("        {")
    for group, clips in plan:
        vols = sorted(set(v for _, v in clips))
        vol = vols[0] if len(vols) == 1 else 1.0
        lines.append("            %sf,   // %s" % (("%.2f" % vol).rstrip("0").rstrip(".") if vol != 1.0 else "1", group))
    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>每组的播放音高（1 = 原样）。只有自己凑的组会偏离 1。</summary>")
    lines.append("        public static readonly float[] GroupPitch =")
    lines.append("        {")
    pitch_of = {g: p for g, _names, _vol, p in SYNTH_GROUPS}
    for group, _clips in plan:
        pitch = pitch_of.get(group, 1.0)
        lines.append("            %sf,   // %s" % (("%.2f" % pitch).rstrip("0").rstrip(".") if pitch != 1.0 else "1", group))
    lines.append("        };")
    lines.append("")
    lines.append("        /// <summary>每个方块属于哪一组（下标和 BlockCatalogData.Names 对齐）。</summary>")
    lines.append("        public static readonly string[] BlockGroup =")
    lines.append("        {")
    for i, name in enumerate(block_names):
        lines.append('            "%s",   // %s' % (groups_of[i], name))
    lines.append("        };")
    lines.append("    }")
    lines.append("}")
    lines.append("")

    with open(CS_PATH, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))

    print("\nclip 文件 %d 个（新拷 %d 个），共 %.2f MB" % (len(needed), copied, total / 1048576.0))
    print("写出: %s" % CS_PATH)
    return 0


if __name__ == "__main__":
    sys.exit(main())
