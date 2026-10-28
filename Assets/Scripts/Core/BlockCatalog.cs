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
