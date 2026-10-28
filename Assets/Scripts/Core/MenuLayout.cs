using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 主菜单那摞按钮的排布计算。纯函数，不碰 GUI，方便自检直接断言。
    ///
    /// 为什么要单独抽出来：按钮放大以后就装不下了。「放大一倍」是个目标值，
    /// 真正的尺寸必须由屏幕高度反推——1440×900 里标题 + 副标题 + 底部署名
    /// 已经吃掉一大半，6 个 110px 高的按钮根本塞不进去。
    /// 所以这里算的是「在 [bandTop, bandBottom] 这条带子里，最多能放大到几倍」，
    /// 拿目标倍数和它取小，再整体垂直居中。
    /// </summary>
    public struct MenuLayout
    {
        /// <summary>实际采用的放大倍数（可能小于目标值）。</summary>
        public float scale;
        public float width;
        public float height;
        public float gap;
        /// <summary>第一个按钮的左上角 Y。</summary>
        public float startY;
        /// <summary>整摞按钮占的总高度。</summary>
        public float blockHeight;

        public float BottomY { get { return startY + blockHeight; } }

        /// <param name="count">按钮个数（少于 1 视为 1）。</param>
        /// <param name="baseWidth">单个按钮在 scale=1 时的宽度（像素，已含 TextScale）。</param>
        /// <param name="baseHeight">scale=1 时的高度。</param>
        /// <param name="baseGap">scale=1 时的按钮间距。</param>
        /// <param name="bandTop">可用区域上沿（副标题下方）。</param>
        /// <param name="bandBottom">可用区域下沿（底部署名上方）。</param>
        /// <param name="maxWidth">按钮最大宽度，一般是屏宽的 86% 左右。</param>
        /// <param name="targetScale">希望的放大倍数，装不下时自动降。</param>
        public static MenuLayout Compute(int count, float baseWidth, float baseHeight, float baseGap,
            float bandTop, float bandBottom, float maxWidth, float targetScale)
        {
            count = Mathf.Max(1, count);

            // scale=1 时整摞按钮要占多高
            float unit = count * baseHeight + (count - 1) * baseGap;
            float availH = Mathf.Max(1f, bandBottom - bandTop);

            float byHeight = availH / Mathf.Max(1f, unit);
            float byWidth = maxWidth / Mathf.Max(1f, baseWidth);

            var layout = new MenuLayout();
            layout.scale = Mathf.Max(0.5f, Mathf.Min(targetScale, Mathf.Min(byHeight, byWidth)));

            layout.width = baseWidth * layout.scale;
            layout.height = baseHeight * layout.scale;
            layout.gap = baseGap * layout.scale;
            layout.blockHeight = count * layout.height + (count - 1) * layout.gap;

            // 垂直居中；万一还是超出（scale 已经压到 0.5 下限），贴住上沿，别把标题盖掉
            float centered = bandTop + (availH - layout.blockHeight) * 0.5f;
            layout.startY = Mathf.Max(bandTop, centered);

            return layout;
        }
    }
}
