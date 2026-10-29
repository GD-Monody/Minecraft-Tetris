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

        /// <summary>在 OnGUI 开头调一次，让整块 IMGUI 皮肤都用这套字体。</summary>
        public static void Apply()
        {
            Font f = Font;
            if (f != null && GUI.skin.font != f)
                GUI.skin.font = f;
        }
    }
}
