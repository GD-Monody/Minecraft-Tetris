// 由 tools/build_substrata_atlas.py 自动生成，不要手改。
namespace MinecraftTetris
{
    public static class BlockCatalogData
    {
        public const int AtlasColumns = 18;
        public const int AtlasRows = 10;
        public const int TilePadding = 1;
        public const int TilePixels = 16;
        public const int CellPixels = 18;

        public static readonly string[] Names =
        {
            "stone",
            "stone_smooth",
            "cobble",
            "slate",
            "marble",
            "granite",
            "brick",
            "tile",
            "soil",
            "grass",
            "clay",
            "mud",
            "gravel",
            "ash",
            "sand",
            "dust",
            "salt",
            "snow",
            "water",
            "lava",
            "acid",
            "fuel",
            "glow",
            "paper_kraft",
            "cardboard",
            "corrugated",
            "plank",
            "log",
            "leaf",
            "hazard",
            "explosive",
            "fuse",
            "lamp",
            "foil",
            "paper_roll",
            "kraft_tape",
            "wax_paper",
            "glass",
            "frosted",
            "stained_red",
            "stained_blue",
            "crystal",
            "bedrock",
            "obsidian",
            "ice",
            "structure_block",
            "dye_red",
            "dye_orange",
            "dye_yellow",
            "dye_green",
            "dye_teal",
            "dye_blue",
            "dye_purple",
            "dye_pink",
            "dye_brown",
            "dye_gray",
            "dye_black",
            "dye_white",
        };

        public static readonly string[] NamesZh =
        {
            "石",
            "磨光石",
            "圆石",
            "板岩",
            "大理石",
            "花岗岩",
            "砖",
            "瓷砖",
            "土",
            "草",
            "黏土",
            "泥",
            "砾石",
            "灰",
            "沙",
            "粉尘",
            "盐",
            "雪",
            "水",
            "岩浆",
            "酸",
            "燃料",
            "辉光液",
            "纸张",
            "板纸",
            "瓦楞纸",
            "木板",
            "原木",
            "叶",
            "警示板",
            "炸药",
            "引线",
            "灯",
            "铝箔",
            "纸卷",
            "胶带",
            "蜡纸",
            "玻璃",
            "磨砂玻璃",
            "红彩玻",
            "蓝彩玻",
            "水晶",
            "基岩",
            "黑曜石",
            "冰",
            "结构方块",
            "红纸",
            "橙纸",
            "黄纸",
            "绿纸",
            "青纸",
            "蓝纸",
            "紫纸",
            "粉纸",
            "棕纸",
            "灰纸",
            "黑纸",
            "白纸",
        };

        public static readonly string[] Tags =
        {
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "gravity",
            "gravity",
            "gravity",
            "",
            "liquid",
            "liquid,melt:slate",
            "liquid",
            "flammable,liquid",
            "glow",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "",
            "explosive",
            "fuse",
            "glow",
            "",
            "flammable",
            "",
            "",
            "",
            "",
            "",
            "",
            "glow",
            "",
            "",
            "",
            "",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
            "flammable",
        };

        public static readonly string[] SfxGroups =
        {
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "gravel",
            "grass",
            "gravel",
            "gravel",
            "gravel",
            "gravel",
            "sand",
            "sand",
            "snow",
            "snow",
            "glass",
            "stone",
            "glass",
            "wood",
            "glass",
            "wood",
            "wood",
            "wood",
            "wood",
            "wood",
            "grass",
            "stone",
            "grass",
            "wood",
            "glass",
            "metal",
            "wood",
            "wood",
            "wood",
            "glass",
            "glass",
            "glass",
            "glass",
            "crystal",
            "stone",
            "stone",
            "glass",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
            "stone",
        };

        public static readonly int[] FaceTiles =
        {
            0, 2, 1, 1, 1, 1,   // stone
            3, 5, 4, 4, 4, 4,   // stone_smooth
            6, 8, 7, 7, 7, 7,   // cobble
            9, 11, 10, 10, 10, 10,   // slate
            12, 14, 13, 13, 13, 13,   // marble
            15, 17, 16, 16, 16, 16,   // granite
            18, 20, 19, 19, 19, 19,   // brick
            21, 23, 22, 22, 22, 22,   // tile
            24, 26, 25, 25, 25, 25,   // soil
            27, 29, 28, 28, 28, 28,   // grass
            30, 32, 31, 31, 31, 31,   // clay
            33, 35, 34, 34, 34, 34,   // mud
            36, 38, 37, 37, 37, 37,   // gravel
            39, 41, 40, 40, 40, 40,   // ash
            42, 44, 43, 43, 43, 43,   // sand
            45, 47, 46, 46, 46, 46,   // dust
            48, 50, 49, 49, 49, 49,   // salt
            51, 53, 52, 52, 52, 52,   // snow
            54, 56, 55, 55, 55, 55,   // water
            57, 59, 58, 58, 58, 58,   // lava
            60, 62, 61, 61, 61, 61,   // acid
            63, 65, 64, 64, 64, 64,   // fuel
            66, 68, 67, 67, 67, 67,   // glow
            69, 71, 70, 70, 70, 70,   // paper_kraft
            72, 74, 73, 73, 73, 73,   // cardboard
            75, 77, 76, 76, 76, 76,   // corrugated
            78, 80, 79, 79, 79, 79,   // plank
            81, 83, 82, 82, 82, 82,   // log
            84, 86, 85, 85, 85, 85,   // leaf
            87, 89, 88, 88, 88, 88,   // hazard
            90, 92, 91, 91, 91, 91,   // explosive
            93, 95, 94, 94, 94, 94,   // fuse
            96, 98, 97, 97, 97, 97,   // lamp
            99, 101, 100, 100, 100, 100,   // foil
            102, 104, 103, 103, 103, 103,   // paper_roll
            105, 107, 106, 106, 106, 106,   // kraft_tape
            108, 110, 109, 109, 109, 109,   // wax_paper
            111, 113, 112, 112, 112, 112,   // glass
            114, 116, 115, 115, 115, 115,   // frosted
            117, 119, 118, 118, 118, 118,   // stained_red
            120, 122, 121, 121, 121, 121,   // stained_blue
            123, 125, 124, 124, 124, 124,   // crystal
            126, 128, 127, 127, 127, 127,   // bedrock
            129, 131, 130, 130, 130, 130,   // obsidian
            132, 134, 133, 133, 133, 133,   // ice
            135, 137, 136, 136, 136, 136,   // structure_block
            138, 140, 139, 139, 139, 139,   // dye_red
            141, 143, 142, 142, 142, 142,   // dye_orange
            144, 146, 145, 145, 145, 145,   // dye_yellow
            147, 149, 148, 148, 148, 148,   // dye_green
            150, 152, 151, 151, 151, 151,   // dye_teal
            153, 155, 154, 154, 154, 154,   // dye_blue
            156, 158, 157, 157, 157, 157,   // dye_purple
            159, 161, 160, 160, 160, 160,   // dye_pink
            162, 164, 163, 163, 163, 163,   // dye_brown
            165, 167, 166, 166, 166, 166,   // dye_gray
            168, 170, 169, 169, 169, 169,   // dye_black
            171, 173, 172, 172, 172, 172,   // dye_white
        };

        public static readonly byte[] TransparentFlags =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };

        public static readonly byte[] CutoutFlags =
        {
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };
    }
}
