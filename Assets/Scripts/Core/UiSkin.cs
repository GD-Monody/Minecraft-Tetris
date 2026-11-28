using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 全局 UI 皮肤：一套字体 + 一个整体放大系数。
    ///
    /// 为什么要统一走这里：界面是 IMGUI 手写 Rect，字号和 Rect 是两套独立的数字。
    /// 只调字号 = 字变大了框没变大，文字立刻被裁掉或盖到别的控件上（这坑踩过两次）。
    /// 所以布局里的每个像素值都过一遍 P()，字号过一遍 Size()，
    /// 想整体放大 / 缩小界面，只改 TextScale 一个数字就行，面板和文字等比长大。
    /// </summary>
    public static class UiSkin
    {
        /// <summary>
        /// 桌面版的放大倍数（窗口基准尺寸下）。1 = 原始尺寸（15~40px 那套）。
        /// </summary>
        public const float DesktopScale = 1.25f;

        /// <summary>
        /// 界面按多大窗口调出来的。窗口比它小就等比缩界面，比它大就维持 DesktopScale。
        ///
        /// 为什么要缩：面板尺寸是绝对像素（分数面板宽 246×1.25 ≈ 307），窗口从 1600 缩到 870
        /// 时它一点没变，占屏比却从 19% 涨到 35% —— 左右两块一夹，场地只剩中间一条，
        /// 看上去就是「UI 把屏幕糊满了」。按窗口比例缩放之后，占屏比和在大窗口里一致。
        /// </summary>
        const float DesignW = 1600f;
        const float DesignH = 900f;

        /// <summary>
        /// 缩到多小为止。第一版放到 0.58，870 宽的窗口中文字只有 10px，看着太吃力；
        /// 现在压到 0.82 —— 再窄的窗口靠相机把场地让进中间那条，不再靠缩字解决问题。
        /// </summary>
        const float MinWindowFactor = 0.82f;

        /// <summary>
        /// 主菜单按钮相对当前尺寸的目标放大倍数（2 = 放大一倍）。
        ///
        /// 注意这是「目标值」不是「一定值」：屏幕不够高时会按可用空间自动缩一档，
        /// 见 MenuLayout.Compute。1440×900 的窗口里 6 个按钮只能到 1.3 倍左右，
        /// 1080p 全屏、5 个按钮时才能真吃到 2 倍——不然按钮会越过底部署名跑到屏幕外。
        /// </summary>
        public const float MenuScale = 2f;

        /// <summary>字体资源在 Resources 下的路径（不含扩展名）。</summary>
        // 思源黑体（Noto Sans SC Medium，SIL OFL 1.1，可商用）。
        // 原来是华文楷体：楷体是商用需授权的字体，去 IP 版必须换掉。
        // 选 Medium 而不是 Regular：界面大量小字号，Regular 在深色底上发虚。
        public const string FontPath = "Fonts/SubstrataSans";

        static float _scale = -1f;
        static int _w;
        static int _h;

        /// <summary>
        /// 字号 / 间距的整体放大倍数。
        ///
        /// 手机上按屏幕密度算（见 ComputeMobileScale），因为同一个 19px 的字在 96dpi 的显示器上
        /// 约 4 毫米高，在 400dpi 的手机上只有 1 毫米 —— 直接照搬会小到看不清。
        /// 桌面上按窗口尺寸算（见 ComputeDesktopScale），窗口拉小的时候面板跟着缩，
        /// 不然绝对像素的面板会相对越来越大，把场地整个盖住。
        ///
        /// 注意这是**缓存**值：屏幕尺寸变了才重算。布局代码一帧要问它几十次，
        /// 每次去读 Screen.dpi 是白烧。
        /// </summary>
        public static float TextScale
        {
            get
            {
                if (_scale > 0f && Screen.width == _w && Screen.height == _h)
                    return _scale;
                _w = Screen.width;
                _h = Screen.height;
                _scale = MobileMode.Enabled ? ComputeMobileScale() : ComputeDesktopScale(_w, _h);
                return _scale;
            }
        }

        /// <summary>
        /// 桌面版的倍数：窗口相对 1600×900 缩了多少，界面就跟着缩多少。
        ///
        /// 只缩不放 —— 大窗口上维持原来的 1.25，老玩家看到的界面一点不变；
        /// 小窗口才折减。宽高各算一次取小的那个，窗口拉成细长条时也不会有一边被撑爆。
        /// </summary>
        static float ComputeDesktopScale(float w, float h)
        {
            float factor = Mathf.Min(w / DesignW, h / DesignH);
            return DesktopScale * Mathf.Clamp(factor, MinWindowFactor, 1f);
        }

        /// <summary>
        /// 手机上的倍数：以 dp（= dpi/160 个像素）为单位。
        ///
        /// dpi/160 正好是「1 个设计像素 ≈ 1 个设备无关像素」，按它放大，按钮的物理大小
        /// 在所有手机上基本一致，也够到 48dp 的触摸下限。
        /// 上限卡在 2.4：再大，主菜单那摞按钮会把屏幕撑满，场地一点都看不见。
        /// 取不到 dpi 的浏览器按 320 兜底（约 2 倍）。
        /// </summary>
        static float ComputeMobileScale()
        {
            float dpi = Screen.dpi;
            if (dpi < 1f)
                dpi = 320f;
            return Mathf.Clamp(dpi / 160f, 1.15f, 2.4f);
        }

        static Font _font;
        static bool _tried;

        public static Font Font
        {
            get
            {
                if (!_tried)
                {
                    _tried = true;
                    _font = Load();
                }
                return _font;
            }
        }

        static Font Load()
        {
            Font f = Resources.Load<Font>(FontPath);
            if (f != null)
                return f;

            // 资源没导进来（比如有人删了 ttf）时退回系统字体，至少不是一屏方框
            Debug.LogWarning("[MinecraftTetris] 没找到 Resources/" + FontPath + "，退回系统字体");
            return Font.CreateDynamicFontFromOSFont(
                new[] { "Noto Sans SC", "Microsoft YaHei", "SimHei", "Arial" }, 40);
        }

        // ── 配色 ────────────────────────────────────────────────────────────

        /// <summary>
        /// 界面配色。暗色是原来那套；亮色给手机在户外/强光下用 ——
        /// 黑底在小屏上又闷又费眼，而且手机屏幕反光时几乎看不见东西。
        /// </summary>
        public enum Theme { Dark = 0, Light = 1 }

        // 键名带 v2：旧存档里存的是浅色（那时浅色是默认），
        // 不换键的话老玩家进来仍然是浅色，跟新的深色标题页接不上。
        const string ThemeKey = "ui.theme.v2";
        static int _themeCache = -1;

        /// <summary>当前主题。存在 PlayerPrefs 里，默认亮色。</summary>
        /// <summary>
        /// 调试用：临时覆盖主题，不写 PlayerPrefs（命令行 -light / -dark）。
        /// 手动改注册表不行 —— Unity 2020+ 把键名存成 "key_h&lt;hash&gt;"，外部写不进去。
        /// </summary>
        public static void DebugForceTheme(Theme t)
        {
            _themeCache = (int)t;
        }

        public static Theme Current
        {
            get
            {
                if (_themeCache < 0)
                    // 默认深色：标题页固定深底（背景方块要压在深色上才看得见），
            // 设置页跟着深色才是一套。浅色仍可在设置里切，给户外强光用。
            _themeCache = Mathf.Clamp(PlayerPrefs.GetInt(ThemeKey, (int)Theme.Dark), 0, 1);
                return (Theme)_themeCache;
            }
            set
            {
                _themeCache = (int)value;
                PlayerPrefs.SetInt(ThemeKey, _themeCache);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// 调试用：只改这一次运行的主题，不写 PlayerPrefs。
        /// 截图核对亮/暗配色时用（-theme light / -theme dark），不会把玩家自己的选择覆盖掉。
        /// </summary>
        public static void ForceForSession(Theme t)
        {
            _themeCache = (int)t;
        }

        /// <summary>切主题后通知各界面重建缓存的样式（字体颜色的副本在 GUIStyle 里）。</summary>
        public static event System.Action Changed;
        public static void NotifyChanged()
        {
            if (Changed != null)
                Changed();
        }

        /// <summary>现在是亮色主题吗（教程等外部模块也要按它挑颜色）。</summary>
        public static bool Light { get { return Current == Theme.Light; } }

        /// <summary>3D 场景的背景色。整屏的底色就是它。</summary>
        public static Color SceneBg
        {
            get { return Light ? new Color(0.87f, 0.91f, 0.96f) : new Color(0.08f, 0.09f, 0.12f); }
        }

        /// <summary>面板底：亮色下是半透明白，能透出一点场景。</summary>
        public static Color PanelFill
        {
            get { return Light ? new Color(1f, 1f, 1f, 0.86f) : new Color(0.13f, 0.15f, 0.19f, 0.96f); }
        }

        /// <summary>更实一点的面板底（大页面、弹层用，不希望透出背景）。</summary>
        public static Color PanelSolid
        {
            get { return Light ? new Color(0.98f, 0.99f, 1f, 0.97f) : new Color(0.10f, 0.12f, 0.16f, 0.97f); }
        }

        /// <summary>面板描边。</summary>
        public static Color PanelBorder
        {
            get { return Light ? new Color(0.10f, 0.47f, 0.85f, 0.35f) : new Color(0.42f, 0.86f, 1f, 0.30f); }
        }

        public static Color TextPrimary
        {
            get { return Light ? new Color(0.11f, 0.13f, 0.17f) : Color.white; }
        }

        public static Color TextSecondary
        {
            get { return Light ? new Color(0.34f, 0.38f, 0.45f) : new Color(0.72f, 0.76f, 0.82f); }
        }

        public static Color TextDim
        {
            get { return Light ? new Color(0.50f, 0.55f, 0.62f) : new Color(0.55f, 0.58f, 0.64f); }
        }

        /// <summary>强调色（分隔线、选中态、焦点框）。</summary>
        public static Color Accent
        {
            get { return Light ? new Color(0.09f, 0.45f, 0.83f) : new Color(0.42f, 0.86f, 1f); }
        }

        public static Color AccentSoft
        {
            get { return Light ? new Color(0.09f, 0.45f, 0.83f, 0.45f) : new Color(0.35f, 0.72f, 1f, 0.45f); }
        }

        /// <summary>顶部/底部那两条通栏的底色。</summary>
        public static Color StripFill
        {
            get { return Light ? new Color(1f, 1f, 1f, 0.94f) : new Color(0.05f, 0.06f, 0.08f, 0.97f); }
        }

        /// <summary>按钮/按键的底色。</summary>
        public static Color ButtonFill
        {
            get { return Light ? new Color(1f, 1f, 1f, 0.80f) : new Color(0.16f, 0.19f, 0.24f, 0.90f); }
        }

        /// <summary>暂停 / 结算时压在操作区上的遮罩：暗色主题压黑，亮色主题压白。</summary>
        public static Color FreezeOverlay
        {
            get { return Light ? new Color(0.87f, 0.91f, 0.96f, 0.60f) : new Color(0f, 0f, 0f, 0.55f); }
        }

        /// <summary>设计像素 → 实际像素。布局尺寸都用它包一层。</summary>
        public static float P(float design)
        {
            return design * TextScale;
        }

        /// <summary>设计字号 → 实际字号。</summary>
        public static int Size(int design)
        {
            return Mathf.RoundToInt(design * TextScale);
        }

        /// <summary>
        /// 给定一页的设计宽高和当前可用空间，算出这一页最多能放大到几倍。
        /// 目标倍数装不下就按最紧的那一维折减，折到 minScale 为止。
        ///
        /// 纯函数、不碰 GUI，自检可以直接拿它断言「各页在任何窗口尺寸下都装得进屏幕」。
        /// 设置 / 规则那几页和暂停面板共用这一套，免得各自写一遍 clamp 又各漏一边。
        /// </summary>
        public static float FitScale(float designW, float designH, float target,
            float availW, float availH, float minScale)
        {
            float byW = availW / Mathf.Max(1f, designW * TextScale);
            float byH = availH / Mathf.Max(1f, designH * TextScale);
            return Mathf.Clamp(Mathf.Min(target, byW, byH), minScale, target);
        }

        /// <summary>在 OnGUI 开头调一次，让整块 IMGUI 皮肤都用这套字体。</summary>
        public static void Apply()
        {
            Font f = Font;
            if (f != null && GUI.skin.font != f)
                GUI.skin.font = f;
        }

        /// <summary>
        /// 这一趟 OnGUI 值不值得干活。
        ///
        /// IMGUI 不是「一帧一次」：Layout 和 Repaint 各一趟，鼠标每动一下还会再来一趟
        /// MouseMove。拖窗口边框时鼠标一直动，OnGUI 一帧能被叫十几次，里面的活也跟着
        /// 做十几遍 —— 那是缩放卡顿的一大半来源。
        ///
        /// 界面画的都是绝对 Rect，鼠标动过之后画出来的东西一模一样（按钮悬停也是 Repaint
        /// 时照鼠标位置现算的），所以 MouseMove 这一趟直接跳过。
        ///
        /// Layout 那一趟不能跳：控件的 id 是在 Layout 阶段从头分配的，跳掉之后 id 会一路
        /// 漂移，按钮按下时记的 id 和抬起时对不上，点击就失灵了。
        ///
        /// 另外：拖窗口的冻结期（ResizeGuard.Frozen）整段都不画 —— 那几帧画面正在变形，
        /// 画出来的界面下一帧就作废，纯属白烧 CPU；松手后自动恢复。跳过的是「整段」而不是
        /// 单独跳某一趟，所以不会出现 id 漂移：恢复后的第一帧 Layout 会从头分配。
        /// </summary>
        public static bool WorthDrawing()
        {
            Event e = Event.current;
            if (e == null)
                return true;

            EventType t = e.type;
            return t != EventType.MouseMove && t != EventType.Ignore;
        }
    }
}
