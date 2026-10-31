using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 窗口缩放检测：告诉各系统「现在正在拖窗口」，好让它们临时降一档开销。
    ///
    /// 为什么需要它：拖动窗口边框时，Windows 连着发 WM_SIZE，Unity 每收到一次都要
    /// 重建后台缓冲（当前画质档还带 2x MSAA，重建比单纯的换尺寸更贵）；同时鼠标一直在动，
    /// IMGUI 又会为每个 MouseMove 事件把 OnGUI 整段跑一遍。两件事挤在同一段时间里，
    /// 每一帧都被撑得很长，拖起来就像卡住了。
    ///
    /// 这里只做两件事：
    /// - 记下「尺寸刚变过」，对外给一个 Busy；
    /// - Busy 期间把帧率压到 FramerateCap，给 Windows 的消息泵腾出时间处理拖动，
    ///   松手后（Hold 秒没再变）自动还原，不留痕迹。
    /// </summary>
    public static class ResizeGuard
    {
        /// <summary>尺寸停止变化后还要维持多久（秒）。WM_SIZE 是断续来的，太短会来回切档。</summary>
        const float Hold = 0.4f;

        /// <summary>缩放期间的帧率上限。再低画面就明显一跳一跳，再高省不出时间。</summary>
        const int FramerateCap = 30;

        static int _w;
        static int _h;
        static float _hold;
        static bool _throttled;
        static int _vsync;
        static int _fps;

        /// <summary>当前是否处在「刚缩放过 / 还在缩放」的窗口期。</summary>
        public static bool Busy { get { return _hold > 0f; } }

        /// <summary>每帧调一次，放在所有系统之前（Bootstrap.Update 开头）。</summary>
        public static void Tick()
        {
            if (_hold > 0f)
            {
                // 用 unscaled：暂停时 timeScale = 0，用 deltaTime 会永远退不出降载状态
                _hold -= Time.unscaledDeltaTime;
                if (_hold <= 0f && _throttled)
                    Restore();
            }

            if (Screen.width == _w && Screen.height == _h)
                return;

            bool first = _w == 0 && _h == 0;
            _w = Screen.width;
            _h = Screen.height;
            if (first)
                return;      // 第一帧只是记下初始尺寸，不算缩放

            _hold = Hold;
            if (!_throttled)
                Throttle();
        }

        static void Throttle()
        {
            _vsync = QualitySettings.vSyncCount;
            _fps = Application.targetFrameRate;
            _throttled = true;

            // Application.targetFrameRate 只在关掉垂直同步时才生效，所以两个一起改。
            // 改的是 present 参数，不会重建后台缓冲 —— 那才是缩放时最贵的一步。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FramerateCap;
        }

        static void Restore()
        {
            if (!_throttled)
                return;
            _throttled = false;
            QualitySettings.vSyncCount = _vsync;
            Application.targetFrameRate = _fps;
        }
    }
}
