using System.Diagnostics;

namespace MinecraftTetris
{
    /// <summary>
    /// 每帧各段耗时的累加器（调试用，配合 PerfOverlay 显示）。
    ///
    /// 手机上掉帧只能靠猜的教训：menu 60fps、进游戏 40fps，光看数字分不出是
    /// 逻辑 / 视图 / IMGUI 界面 / 渲染哪一块。这里把四段各自掐表，剩下的
    /// （帧时间减掉这四段）就是渲染与引擎本身的开销。
    ///
    /// 用 Stopwatch 的计数而不是 Time.realtimeSinceStartup：后者精度只有毫秒级，
    /// 单帧几百微秒的量测不出来。
    /// </summary>
    public static class PerfCounters
    {
        /// <summary>玩法逻辑（GameLoop.Tick）。</summary>
        public static double LoopMs;
        /// <summary>视图层（RefreshPiece / Sync*，含 GridView.Update 的插值）。</summary>
        public static double ViewMs;
        /// <summary>游戏内 HUD 的 IMGUI。</summary>
        public static double HudMs;
        /// <summary>手机按键层的 IMGUI。</summary>
        public static double CtlMs;
        /// <summary>俯视图面板的 IMGUI。</summary>
        public static double MapMs;

        public static long Now
        {
            get { return Stopwatch.GetTimestamp(); }
        }

        public static double Ms(long from)
        {
            return (Now - from) * 1000.0 / Stopwatch.Frequency;
        }

        public static void Reset()
        {
            LoopMs = 0.0;
            ViewMs = 0.0;
            HudMs = 0.0;
            CtlMs = 0.0;
            MapMs = 0.0;
        }
    }
}
