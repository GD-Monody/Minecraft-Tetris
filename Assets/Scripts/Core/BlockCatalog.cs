using System.Collections.Generic;
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
        public readonly Tile Top;
        public readonly Tile Bottom;
        public readonly Tile Side;
        public readonly string PlaceSfx;
        public readonly bool Transparent;

        public BlockDef(string name, Tile top, Tile bottom, Tile side, string placeSfx, bool transparent = false)
        {
            Name = name;
            Top = top;
            Bottom = bottom;
            Side = side;
            PlaceSfx = placeSfx;
            Transparent = transparent;
        }
    }

    /// <summary>
    /// 7 种方块对应哪些 MC 方块。图集格子与 tools/build_atlas.py 输出的顺序一致。
    /// </summary>
    public static class BlockCatalog
    {
        public const int AtlasColumns = 8;
        public const int AtlasRows = 7;
        public const int TilePixels = 16;
        public const int TilePadding = 1;
        public const int CellPixels = TilePixels + TilePadding * 2;

        static readonly Dictionary<string, Tile> Tiles = new Dictionary<string, Tile>
        {
            { "dirt", new Tile(6, 1) },
            { "grass_block_top", new Tile(2, 2) },
            { "grass_block_side", new Tile(0, 2) },
            { "stone", new Tile(1, 5) },
            { "cobblestone", new Tile(0, 1) },
            { "sand", new Tile(2, 4) },
            { "gravel", new Tile(3, 2) },
            { "oak_planks", new Tile(5, 3) },
            { "oak_log", new Tile(3, 3) },
            { "oak_log_top", new Tile(4, 3) },
            { "glass", new Tile(7, 1) },
        };

        public static Tile TileOf(string name)
        {
            return Tiles[name];
        }

        /// <summary>顺序必须和 PieceType 一致。</summary>
        public static readonly BlockDef[] Pieces =
        {
            new BlockDef("oak_log", TileOf("oak_log_top"), TileOf("oak_log_top"), TileOf("oak_log"), "place_wood"),
            new BlockDef("grass_block", TileOf("grass_block_top"), TileOf("dirt"), TileOf("grass_block_side"), "place_stone"),
            new BlockDef("stone", TileOf("stone"), TileOf("stone"), TileOf("stone"), "place_stone"),
            new BlockDef("sand", TileOf("sand"), TileOf("sand"), TileOf("sand"), "place_stone"),
            new BlockDef("gravel", TileOf("gravel"), TileOf("gravel"), TileOf("gravel"), "place_stone"),
            new BlockDef("oak_planks", TileOf("oak_planks"), TileOf("oak_planks"), TileOf("oak_planks"), "place_wood"),
            new BlockDef("glass", TileOf("glass"), TileOf("glass"), TileOf("glass"), "place_glass", true),
        };

        public static BlockDef For(PieceType type)
        {
            return Pieces[(int)type];
        }
    }
}
