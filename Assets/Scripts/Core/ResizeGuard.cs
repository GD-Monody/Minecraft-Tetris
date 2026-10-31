using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 窗口缩放检测：拖窗口那段时间里把画面整个「冻住」。
    ///
    /// 为什么要冻：拖动边框时 Windows 连着发 WM_SIZE，Unity 每收到一次都要重建后台缓冲
    /// （当前画质档还带 2x MSAA，重建比单纯换个尺寸更贵）；同时鼠标一直在动，IMGUI 又会
    /// 为每个 MouseMove 事件把 OnGUI 整段再跑一遍；场上的方块、粒子、光尘也照样一帧帧
    /// 算动画。这些全挤在拖动的那几秒里，主线程被占满，Windows 的拖动消息排不上队，
    /// 窗口就跟在鼠标后面一跳一跳 —— 看起来就是「卡死」。
    ///
    /// 冻住的做法（松手后自动还原，不留痕迹）：
    /// - Time.timeScale = 0：所有动画停摆。deltaTime 归零，方块不再追位、粒子不再飞、
    ///   光尘不再飘，各系统 Update 里的活自然降到最低，不用挨个去加判断。
    /// - targetFrameRate 压到 8（要顺带关掉 vsync 才生效）：一秒只渲染 8 次，
    ///   后台缓冲的重建次数同比例降下来，剩下的时间留给 Windows 处理拖动。
    /// - IMGUI 那一大块由 UiSkin.WorthDrawing 挡掉，冻结期干脆不画。
    /// </summary>
    public static class ResizeGuard
    {
        /// <summary>尺寸停止变化后还要维持多久（秒）。WM_SIZE 是断续来的，太短会来回切档。</summary>
        const float Hold = 0.35f;

        /// <summary>冻结期的帧率上限。够低才省得出时间，再低就只是白等。</summary>
        const int FrozenFps = 8;

        static int _w;
        static int _h;
        static float _hold;
        static bool _frozen;
        static bool _justFroze;
        static int _vsync;
        static int _fps;
        static float _timeScale;

        /// <summary>当前是否处在「刚缩放过 / 还在缩放」的冻结期。</summary>
        public static bool Frozen { get { return _frozen; } }

        /// <summary>
        /// 这一帧刚刚进入冻结。各系统可以借这一帧做一次收尾（比如把还在半路的方块
        /// 一次性按到位），之后就完全不动了。
        /// </summary>
        public static bool JustFroze { get { return _justFroze; } }

        /// <summary>每帧调一次，放在所有系统之前（Bootstrap.Update 开头）。</summary>
        public static void Tick()
        {
            _justFroze = false;

            if (_frozen)
            {
                // 用 unscaled：冻结期 deltaTime 恒为 0，用它倒计时永远退不出去
                _hold -= Time.unscaledDeltaTime;
                if (_hold <= 0f)
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
            if (!_frozen)
            {
                Throttle();
                _justFroze = true;
            }
        }

        static void Throttle()
        {
            _vsync = QualitySettings.vSyncCount;
            _fps = Application.targetFrameRate;
            _timeScale = Time.timeScale;
            _frozen = true;

            // Application.targetFrameRate 只在关掉垂直同步时才生效，所以两个一起改。
            // 改的是 present 参数，不会重建后台缓冲 —— 那才是缩放时最贵的一步。
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FrozenFps;

            // 冻结动画：deltaTime 归零，所有靠 Update 推进的东西自动停下。
            Time.timeScale = 0f;

            Debug.Log("[resize] 冻住画面 " + Screen.width + "x" + Screen.height
                + "（松手 " + Hold + " 秒后恢复，期间只渲染 " + FrozenFps + " 帧/秒）");
        }

        static void Restore()
        {
            if (!_frozen)
                return;
            _frozen = false;
            QualitySettings.vSyncCount = _vsync;
            Application.targetFrameRate = _fps;
            Time.timeScale = _timeScale;

            Debug.Log("[resize] 恢复 " + Screen.width + "x" + Screen.height
                + "（timeScale=" + _timeScale.ToString("0.##") + "，帧率 " + (_fps <= 0 ? "默认" : _fps.ToString()) + "）");
        }
    }
}
