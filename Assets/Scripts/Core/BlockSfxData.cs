// Substrata 的放置音效。
//
// 只有一组：「嗒」。
//
// 这是试了三种映射之后的结论（按材质分组 → 按放置顺序走音阶 → 按落点高度）：
// 全都"吵"。根因不在音色，在于放置每秒响 1~2 次，任何有音高的东西连放起来
// 都会盖住背景音乐（BGM 是钢琴氛围曲，主要能量在 200~500Hz，正好和放置音撞）。
//
// 现在它降级成节奏点：2~6kHz、60ms、音量 0.42 —— 只负责"我又放下一块"这个信息，
// 音乐性交给消行时的钢琴琶音（SfxPlayer.PlayClear）。

namespace MinecraftTetris
{
    public static class BlockSfxData
    {
        public const int GroupCount = 1;

        /// <summary>组名。SfxPlayer 按这个字符串查索引。</summary>
        public static readonly string[] Groups =
        {
            "tap",
        };

        /// <summary>每组可用的 clip（Resources/Sfx/Place 下的文件名，不带扩展名）。</summary>
        public static readonly string[][] GroupClips =
        {
            new[] { "tap1", "tap2", "tap3", "tap4" },
        };

        /// <summary>音量压到 0.42：它是节奏点，不是主角。</summary>
        public static readonly float[] GroupVolume =
        {
            0.42f,
        };
    }
}
