using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>图集里的一个格子。</summary>
    public struct Tile
    {
        public readonly int Col;
        public readonly int Row;

        public Tile(int col, int row)
        {
            Col = col;
            Row = row;
        }
    }

    /// <summary>一种方块的外观和声音。</summary>
    public class BlockDef
    {
        public readonly string Name;
        public readonly string NameZh;
        public readonly Tile Up;
        public readonly Tile Down;
        public readonly Tile North;
        public readonly Tile South;
        public readonly Tile East;
        public readonly Tile West;

        /// <summary>
        /// 放置音效分组名，对应 BlockSfxData.Groups（也就是 MC 原版的 SoundType）。
        /// 同一种材质的所有方块共用一个组，比如所有木板都是 "wood"。
        /// </summary>
        public readonly string PlaceSfxGroup;

        public readonly bool Transparent;

        /// <summary>
        /// 贴图里有没有整块透明的像素（铜格栅的网格洞）。有的话要用 alpha test 的材质画，
        /// 不然那些洞会被画成实心，格栅看着就是一块铜砖。
        /// </summary>
        public readonly bool Cutout;

        public BlockDef(string name, string nameZh,
                        Tile up, Tile down, Tile north, Tile south, Tile east, Tile west,
                        string placeSfxGroup, bool transparent, bool cutout)
        {
            Name = name;
            NameZh = nameZh;
            Up = up;
            Down = down;
            North = north;
            South = south;
            East = east;
            West = west;
            PlaceSfxGroup = placeSfxGroup;
            Transparent = transparent;
            Cutout = cutout;
        }
    }

    /// <summary>
    /// 方块材质表。数据在 BlockCatalogData.cs（由 tools/build_full_atlas.py 生成），
    /// 这里把它变成运行时用的 BlockDef 数组。方块编号是 1 起（网格里 0 表示空）。
    /// </summary>
    public static class BlockCatalog
    {
        public const int AtlasColumns = BlockCatalogData.AtlasColumns;
        public const int AtlasRows = BlockCatalogData.AtlasRows;
        public const int TilePixels = BlockCatalogData.TilePixels;
        public const int TilePadding = BlockCatalogData.TilePadding;
        public const int CellPixels = BlockCatalogData.CellPixels;

        public static readonly BlockDef[] Blocks = BuildBlocks();

        /// <summary>
        /// 基岩的编号（0 = 表里没这个方块）。消除时它钉在原地不跟着下落，
        /// 但自己所在的那一行被消掉时照样消失。
        /// 声明在 Blocks 之后：静态字段按文本顺序初始化，放前面会读到还没建好的表。
        /// </summary>
        public static readonly int BedrockId = FindId("bedrock");

        /// <summary>结构方块的编号（0 = 表里没这个方块）。落定时记住构造，被消时还原。</summary>
        public static readonly int StructureId = FindId("structure_block");

        /// <summary>
        /// 侦测器的编号。Substrata 没有这个方块（MC 专属机制），常量保留让代码能编译，
        /// 值为 0 时 Observer.cs 的判定全部落空，整个侦测器玩法自动休眠。
        /// </summary>
        public static readonly int ObserverId = FindId("observer");

        /// <summary>玻璃的编号（0 = 表里没这个方块）。上面压着方块就会碎，见 Glass.cs。</summary>
        public static readonly int GlassId = FindId("glass");

        /// <summary>雪块的编号。挨着岩浆会化成水，见 Melt.cs。</summary>
        public static readonly int SnowId = FindId("snow");

        /// <summary>冰的编号（0 = 表里没这个方块）。挨着岩浆会化成水，见 Melt.cs。</summary>
        public static readonly int IceId = FindId("ice");

        // —— 以下都是 MC 专属方块（傀儡系统），Substrata 表里没有，值恒为 0 ——
        // 常量保留是为了引用它们的代码能编译；IsHead / 身体判定全部落空，傀儡玩法休眠。
        public static readonly int CarvedPumpkinId = FindId("carved_pumpkin");
        public static readonly int PumpkinId = FindId("pumpkin");
        public static readonly int IronBlockId = FindId("iron_block");
        public static readonly int CopperBlockId = FindId("copper_block");
        public static readonly int OakLogId = FindId("oak_log");
        public static readonly int ExposedCopperId = FindId("exposed_copper");
        public static readonly int WeatheredCopperId = FindId("weathered_copper");
        public static readonly int OxidizedCopperId = FindId("oxidized_copper");

        /// <summary>水的编号。Fluids.cs 的流动与「岩浆遇水凝固」都靠它。</summary>
        public static readonly int WaterId = FindId("water");
        public static readonly int LavaId = FindId("lava");
        public static readonly int AcidId = FindId("acid");
        public static readonly int FuelId = FindId("fuel");
        public static readonly int SandId = FindId("sand");
        public static readonly int DustId = FindId("dust");
        public static readonly int GravelId = FindId("gravel");
        public static readonly int SaltId = FindId("salt");

        /// <summary>机制标签查询：tags 由 blocks.json 一并生成（gravity/flammable/liquid/…）。</summary>
        public static bool HasTag(int blockId, string tag)
        {
            int index = blockId - 1;
            if (index < 0 || index >= Blocks.Length)
                return false;
            string tags = BlockCatalogData.Tags[index];
            if (string.IsNullOrEmpty(tags))
                return false;
            // 精确匹配逗号分段，避免 "sand" 撞上 "quicksand" 这类前缀
            foreach (var part in tags.Split(','))
            {
                if (part.Trim() == tag)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 这一格是不是「消除时不下落」的锚点方块（目前只有基岩）。
        /// 设置里关掉「基岩钉住不动」之后，基岩和普通方块一样跟着走。
        /// </summary>
        public static bool IsAnchor(int blockId)
        {
            return blockId > 0 && blockId == BedrockId && GameSettings.BedrockAnchors;
        }

        /// <summary>
        /// TNT 炸不掉的方块。原版里这些的爆炸抗性高到 TNT 威力（4）打不动：
        /// 基岩和三种命令方块是 3600000，黑曜石、哭泣的黑曜石、远古残骸、下界合金块、
        /// 强化深板岩是 1200。
        ///
        /// 没把结构方块列进来：它的抗性在原版也是 3600000，但在这里它是要被炸掉才有戏的
        /// 游戏道具（TNT 是它的两个触发点之一），炸不动就废了一半。
        /// </summary>
        static readonly string[] BlastProofNames =
        {
            "bedrock", "obsidian",
        };

        static bool[] _blastProof;

        /// <summary>这一格是不是炸不掉的（在爆炸范围内也会保留）。设置里关掉「防爆方块」之后一律炸得掉。</summary>
        public static bool IsBlastProof(int blockId)
        {
            if (blockId <= 0 || !GameSettings.BlastProofOn)
                return false;

            if (_blastProof == null)
            {
                _blastProof = new bool[Blocks.Length + 1];
                for (int i = 0; i < Blocks.Length; i++)
                {
                    for (int k = 0; k < BlastProofNames.Length; k++)
                    {
                        if (Blocks[i].Name == BlastProofNames[k])
                        {
                            _blastProof[i + 1] = true;
                            break;
                        }
                    }
                }
            }

            return blockId <= _blastProof.Length && _blastProof[blockId];
        }

        /// <summary>按英文名查编号。公开给图鉴/工坊这类「按名字找方块」的调用方。</summary>
        public static int FindId(string name)
        {
            for (int i = 0; i < Blocks.Length; i++)
            {
                if (Blocks[i].Name == name)
                    return i + 1;
            }
            return 0;
        }

        /// <summary>按英文名查编号（0 = 表里没这个方块）。创意工坊的建造台要用它挑编辑器里的外观。</summary>
        public static int IdOf(string name)
        {
            return FindId(name);
        }

        public static BlockDef Of(int blockId)
        {
            int index = blockId - 1;
            if (index < 0 || index >= Blocks.Length)
                index = 0;
            return Blocks[index];
        }

        static BlockDef[] BuildBlocks()
        {
            int count = BlockCatalogData.Names.Length;
            var defs = new BlockDef[count];
            for (int i = 0; i < count; i++)
            {
                int index = i;
                var tiles = new Tile[6];
                for (int f = 0; f < 6; f++)
                {
                    int tile = BlockCatalogData.FaceTiles[i * 6 + f];
                    tiles[f] = new Tile(tile % AtlasColumns, tile / AtlasColumns);
                }

                string name = BlockCatalogData.Names[i];
                bool transparent = BlockCatalogData.TransparentFlags != null
                    && index < BlockCatalogData.TransparentFlags.Length
                    && BlockCatalogData.TransparentFlags[index] != 0;
                defs[i] = new BlockDef(
                    name,
                    BlockCatalogData.NamesZh[i],
                    tiles[0], tiles[1], tiles[2], tiles[3], tiles[4], tiles[5],
                    PlaceSfxGroupOf(i),
                    transparent,
                    IsCutout(index));
            }
            return defs;
        }

        /// <summary>放置音效分组，数据在 BlockSfxData（由 tools/build_place_sfx.py 生成）。</summary>
        static string PlaceSfxGroupOf(int index)
        {
            // 音效组由生成器按材质写进数据（stone/wood/sand/glass/...），
            // 实际的音频文件还是 MC 的，第二步换素材时只要替换 clip，组名不动。
            if (BlockCatalogData.SfxGroups == null || index >= BlockCatalogData.SfxGroups.Length)
                return "stone";
            return BlockCatalogData.SfxGroups[index];
        }

        /// <summary>
        /// 半透明材质（水/玻璃/冰/燃料这些贴图自带 alpha 的）走 BlockTransparentMat，
        /// 不然会被画成不透明的色块。标记由生成器扫图集 alpha 算出来。
        /// </summary>
        static bool IsTransparent(int index)
        {
            return BlockCatalogData.TransparentFlags != null
                && index < BlockCatalogData.TransparentFlags.Length
                && BlockCatalogData.TransparentFlags[index] != 0;
        }

        /// <summary>
        /// 贴图带镂空的方块要按 alpha test 画。标记由生成器扫图集 alpha 算出来
        /// （面里既有全透明像素又有不透明像素才算，纯半透明走 transparent）。
        /// </summary>
        static bool IsCutout(int index)
        {
            if (BlockCatalogData.CutoutFlags == null || index >= BlockCatalogData.CutoutFlags.Length)
                return false;
            return BlockCatalogData.CutoutFlags[index] != 0;
        }

        /// <summary>这一格要不要按半透明渲染（贴图自带 alpha 的材质）。</summary>
        public static bool IsTransparentBlock(int blockId)
        {
            return blockId > 0 && blockId <= Blocks.Length && Blocks[blockId - 1].Transparent;
        }

        /// <summary>这一格要不要按镂空渲染（贴图有透明的洞）。</summary>
        public static bool IsCutoutBlock(int blockId)
        {
            return blockId > 0 && blockId <= Blocks.Length && Blocks[blockId - 1].Cutout;
        }
    }
}
