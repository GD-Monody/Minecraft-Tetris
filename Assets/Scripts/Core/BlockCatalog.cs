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

        public BlockDef(string name, string nameZh,
                        Tile up, Tile down, Tile north, Tile south, Tile east, Tile west,
                        string placeSfxGroup, bool transparent)
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

        /// <summary>这一格是不是「消除时不下落」的锚点方块（目前只有基岩）。</summary>
        public static bool IsAnchor(int blockId)
        {
            return blockId > 0 && blockId == BedrockId;
        }

        /// <summary>
        /// TNT 炸不掉的方块。原版里这几个的爆炸抗性极高（TNT 的爆炸威力只有 4）：
        /// 基岩 3600000、黑曜石和哭泣的黑曜石 1200，所以原版 TNT 也炸不动它们。
        /// </summary>
        static readonly string[] BlastProofNames =
        {
            "bedrock", "obsidian", "crying_obsidian",
        };

        static bool[] _blastProof;

        /// <summary>这一格是不是炸不掉的（在爆炸范围内也会保留）。</summary>
        public static bool IsBlastProof(int blockId)
        {
            if (blockId <= 0)
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

        static int FindId(string name)
        {
            for (int i = 0; i < Blocks.Length; i++)
            {
                if (Blocks[i].Name == name)
                    return i + 1;
            }
            return 0;
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
                var tiles = new Tile[6];
                for (int f = 0; f < 6; f++)
                {
                    int tile = BlockCatalogData.FaceTiles[i * 6 + f];
                    tiles[f] = new Tile(tile % AtlasColumns, tile / AtlasColumns);
                }

                string name = BlockCatalogData.Names[i];
                defs[i] = new BlockDef(
                    name,
                    BlockCatalogData.NamesZh[i],
                    tiles[0], tiles[1], tiles[2], tiles[3], tiles[4], tiles[5],
                    PlaceSfxGroupOf(i),
                    IsTransparent(name));
            }
            return defs;
        }

        /// <summary>放置音效分组，数据在 BlockSfxData（由 tools/build_place_sfx.py 生成）。</summary>
        static string PlaceSfxGroupOf(int index)
        {
            if (index < 0 || index >= BlockSfxData.BlockGroup.Length)
                return "stone";
            return BlockSfxData.BlockGroup[index];
        }

        static bool IsTransparent(string name)
        {
            return name.Contains("glass") || name.Contains("leaves");
        }
    }
}
