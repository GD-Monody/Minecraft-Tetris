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
        /// <summary>字号 / 间距的整体放大倍数。1 = 原始尺寸（15~40px 那套）。</summary>
        public const float TextScale = 1.25f;

        /// <summary>
        /// 主菜单按钮相对当前尺寸的目标放大倍数（2 = 放大一倍）。
        ///
        /// 注意这是「目标值」不是「一定值」：屏幕不够高时会按可用空间自动缩一档，
        /// 见 MenuLayout.Compute。1440×900 的窗口里 6 个按钮只能到 1.3 倍左右，
        /// 1080p 全屏、5 个按钮时才能真吃到 2 倍——不然按钮会越过底部署名跑到屏幕外。
        /// </summary>
        public const float MenuScale = 2f;

        /// <summary>字体资源在 Resources 下的路径（不含扩展名）。</summary>
        public const string FontPath = "Fonts/STKaiTi";

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
                new[] { "STKaiti", "华文楷体", "KaiTi", "楷体", "Microsoft YaHei", "SimHei", "Arial" }, 40);
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
            if (ResizeGuard.Frozen)
                return false;

            Event e = Event.current;
            if (e == null)
                return true;

            EventType t = e.type;
            return t != EventType.MouseMove && t != EventType.Ignore;
        }
    }
}
