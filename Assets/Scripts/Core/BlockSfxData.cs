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
        public const int GroupCount = 7;

        /// <summary>组名。SfxPlayer 按这个字符串查索引。</summary>
        public static readonly string[] Groups =
        {
            "do", "re", "mi", "fa", "so", "la", "xi",
        };

        /// <summary>每组可用的 clip（Resources/Sfx/Place 下的文件名，不带扩展名）。</summary>
        public static readonly string[][] GroupClips =
        {
            new[] { "do1", "do2", "do3", "do4" },
            new[] { "re1", "re2", "re3", "re4" },
            new[] { "mi1", "mi2", "mi3", "mi4" },
            new[] { "fa1", "fa2", "fa3", "fa4" },
            new[] { "so1", "so2", "so3", "so4" },
            new[] { "la1", "la2", "la3", "la4" },
            new[] { "xi1", "xi2", "xi3", "xi4" },
        };

        /// <summary>
        /// 每组的基础音量。合成时按峰值归一化，但人对不同频段的响度感受不一样：
        /// 高频（玻璃、草）听着更响，低频（金属、砂砾）听着更闷，这里拉平一下。
        /// </summary>
        public static readonly float[] GroupVolume =
        {
            1f, 1f, 1f, 1f, 1f, 1f, 1f,
        };
    }
}
