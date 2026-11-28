// Substrata 的放置音效分组。
//
// 和旧版（MC 的 61 组 SoundType）无关了：现在只有 9 种材质，
// 每个方块属于哪一组由 BlockCatalogData.SfxGroups 给出（图集生成器一并导出），
// 这里只负责「组名 → clip 文件名」。
//
// clip 由 tools/build_sfx.py 程序合成，每组 4 个变体（音高/噪声种子不同），
// 播放时随机挑一个，避免每次都是同一声。

namespace MinecraftTetris
{
    public static class BlockSfxData
    {
        public const int GroupCount = 11;

        /// <summary>组名。SfxPlayer 按这个字符串查索引。</summary>
        public static readonly string[] Groups =
        {
            "stone",
            "wood",
            "gravel",
            "grass",
            "sand",
            "snow",
            "glass",
            "metal",
            "crystal",
            "water",
            "lava",
        };

        /// <summary>每组可用的 clip（Resources/Sfx/Place 下的文件名，不带扩展名）。</summary>
        public static readonly string[][] GroupClips =
        {
            new[] { "stone1", "stone2", "stone3", "stone4" },
            new[] { "wood1", "wood2", "wood3", "wood4" },
            new[] { "gravel1", "gravel2", "gravel3", "gravel4" },
            new[] { "grass1", "grass2", "grass3", "grass4" },
            new[] { "sand1", "sand2", "sand3", "sand4" },
            new[] { "snow1", "snow2", "snow3", "snow4" },
            new[] { "glass1", "glass2", "glass3", "glass4" },
            new[] { "metal1", "metal2", "metal3", "metal4" },
            new[] { "crystal1", "crystal2", "crystal3", "crystal4" },
            new[] { "water1", "water2", "water3", "water4" },
            new[] { "lava1", "lava2", "lava3", "lava4" },
        };

        /// <summary>
        /// 每组的基础音量。合成时按峰值归一化，但人对不同频段的响度感受不一样：
        /// 高频（玻璃、草）听着更响，低频（金属、砂砾）听着更闷，这里拉平一下。
        /// </summary>
        public static readonly float[] GroupVolume =
        {
            1.00f,   // stone
            1.05f,   // wood
            1.00f,   // gravel
            0.85f,   // grass
            0.95f,   // sand
            0.90f,   // snow
            0.80f,   // glass
            1.00f,   // metal
            0.85f,   // crystal
            0.95f,   // water
            1.00f,   // lava
        };
    }
}
