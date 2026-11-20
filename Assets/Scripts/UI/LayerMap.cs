using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地右侧的「层俯视图」：一条滑条挑 y，把那一层的 x × z 平面摊平画出来。
    ///
    /// 只在深度 z > 2 时才建（见 UsefulFor）：三格以上之后，中间列和后排的空洞
    /// 从正面是被挡住的，层消「看着铺满了却没消」基本都差在这些看不见的格子上，
    /// 而且**只有攒满一格的水才算填满**，半格水看着也是一片蓝，肉眼分不出来。
    /// 所以每格照实画：方块画它的顶面贴图、水按份数画高度、空格留暗格，
    /// 右边再标出每一排（每个 z）还差几格 —— 一眼就能定位差在哪。
    /// </summary>
    public class LayerMap : MonoBehaviour
    {
        public GameLoop Loop;
        public bool Hidden;

        /// <summary>
        /// 面板宽度上限（像素），0 = 不限。
        ///
        /// 桌面上面板是浮在 3D 画面上的，窗口一窄，按屏宽 34% 算出来的宽度就会伸进场地里，
        /// 把最右边那一列挡掉。Bootstrap 每帧把「场地右边缘到屏幕右边还剩多少」量出来填这儿，
        /// 面板按它收窄 —— 场地不用缩，面板自己让。
        /// </summary>
        public float MaxWidth;

        /// <summary>
        /// 手机上是否展开。默认收起：竖屏里场地只占中间一条，面板一展开就压掉小半个屏，
        /// 而它只是「查缺口」用的辅助面板，需要的时候点开看一眼就够。
        /// 收起状态下只留右上角一个写着缺口数的小按钮。桌面不受这个影响，一直展开。
        /// </summary>
        public bool Open;

        /// <summary>
        /// 当前看着第几层（0 = 最底层）。**只由玩家调**：滑条拖到哪儿就是哪儿，
        /// 方块往下掉不带动它 —— 早先会自动跟着当前方块跳层，结果玩家刚翻到某一层
        /// 看缺口，画面自己就跳走了，反而更难找。
        /// </summary>
        public int LayerY
        {
            get { return _y; }
            set { _y = value; }
        }

        int _y;
        bool _drag;

        GUIStyle _titleStyle;
        GUIStyle _smallStyle;
        GUIStyle _rowFullStyle;
        GUIStyle _rowMissStyle;
        GUIStyle _headFullStyle;
        GUIStyle _headMissStyle;
        GUIStyle _sliderShadow;
        GUIStyle _pillStyle;
        static Texture2D _atlas;
        static bool _atlasTried;

        static readonly Color SlotEmpty = new Color(0.16f, 0.17f, 0.21f, 1f);
        static readonly Color SlotLine = new Color(0.32f, 0.35f, 0.42f, 0.90f);
        static readonly Color WaterFull = new Color(0.24f, 0.47f, 0.94f, 0.95f);
        static readonly Color WaterHalf = new Color(0.42f, 0.66f, 1.00f, 0.70f);
        static readonly Color LavaColor = new Color(0.95f, 0.45f, 0.12f, 0.95f);
        static readonly Color PieceMark = new Color(0.55f, 0.95f, 0.35f, 1f);
        static readonly Color OkGreen = new Color(0.52f, 0.78f, 0.30f, 1f);
        static readonly Color WarnYellow = new Color(0.94f, 0.74f, 0.32f, 1f);

        /// <summary>
        /// 两格深从正面一眼能看完（后排就是紧贴着的一层），三格开始中间列才真的看不见，
        /// 所以门槛定在「大于 2」，和界面上写的一致。
        /// </summary>
        public static bool UsefulFor(int sizeZ)
        {
            return sizeZ > 2;
        }

        /// <summary>这一层还差几格才算满（水攒满一格也算）。自检也用它。</summary>
        public static int MissingInLayer(Grid g, int y)
        {
            if (g == null)
                return 0;
            int miss = 0;
            for (int x = 0; x < g.SizeX; x++)
                for (int z = 0; z < g.SizeZ; z++)
                    if (!g.IsFilledPublic(x, y, z))
                        miss++;
            return miss;
        }

        /// <summary>某一层里这一排（固定 z）还差几格。行消模式下看这个更有用。</summary>
        public static int MissingInRow(Grid g, int y, int z)
        {
            if (g == null)
                return 0;
            int miss = 0;
            for (int x = 0; x < g.SizeX; x++)
                if (!g.IsFilledPublic(x, y, z))
                    miss++;
            return miss;
        }

        public static Texture2D Atlas
        {
            get
            {
                if (!_atlasTried)
                {
                    _atlasTried = true;
                    _atlas = Resources.Load<Texture2D>("atlas");
                }
                return _atlas;
            }
        }

        void OnGUI()
        {
            Grid grid = Loop != null ? Loop.Grid : null;
            if (Hidden || grid == null || !UsefulFor(grid.SizeZ))
                return;

            // 这一块是全场最费的一块 IMGUI：每格两趟贴图 + 四趟边框，还要扫好几遍格子
            // 数缺口。鼠标动一下就被重画一遍，拖窗口时一帧十几遍，挡掉白跑的那些。
            if (!UiSkin.WorthDrawing())
                return;

            UiSkin.Apply();
            EnsureStyles();

            // 只做范围收拢：改场地尺寸（16 → 更高）时别让层号跑到界外，
            // 剩下的全交给玩家拖滑条。
            _y = Mathf.Clamp(_y, 0, grid.SizeY - 1);

            // 手机上默认收着：那个小按钮点开才画面板
            if (MobileMode.Enabled && !DrawPill(grid))
                return;

            Draw(grid);
        }

        /// <summary>
        /// 手机上的折叠开关。收起时它就是右上角一个小按钮，上面直接写着这一层还差几格；
        /// 点一下展开，再点一下收起。
        /// </summary>
        bool DrawPill(Grid grid)
        {
            float w = UiSkin.P(122f);
            float h = UiSkin.P(34f);
            var r = new Rect(Screen.width - w - UiSkin.P(14f),
                Hud.MobileBarHeight + UiSkin.P(6f), w, h);

            bool open = Open;
            Fill(r, open ? new Color(0.14f, 0.22f, 0.30f, 0.94f) : new Color(0.10f, 0.13f, 0.18f, 0.90f));
            DrawBorder(r, new Color(0.42f, 0.86f, 1f, open ? 0.70f : 0.35f));

            int missing = MissingInLayer(grid, _y);
            GUI.Label(r, open ? "收起俯视图" : (missing == 0 ? "俯视 已满" : "俯视 差" + missing),
                _pillStyle);

            // 按钮是透明的，只负责接点击，画好的那层不会被它盖掉
            if (GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                Open = !open;
                return Open;
            }
            return open;
        }

        void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = Make(17, new Color(0.88f, 0.90f, 0.94f, 1f), TextAnchor.MiddleLeft);
            _smallStyle = Make(14, new Color(0.66f, 0.70f, 0.76f, 1f), TextAnchor.MiddleLeft);
            _rowFullStyle = Make(13, OkGreen, TextAnchor.MiddleLeft);
            _rowMissStyle = Make(13, WarnYellow, TextAnchor.MiddleLeft);
            _headFullStyle = Make(17, OkGreen, TextAnchor.MiddleRight);
            _headMissStyle = Make(17, WarnYellow, TextAnchor.MiddleRight);
            // 滑条上那行字的影子：样式只建一次，别每帧 new（IMGUI 每帧都走一遍这里）
            _sliderShadow = Make(17, new Color(0f, 0f, 0f, 0.85f), TextAnchor.MiddleLeft);
            _pillStyle = Make(16, new Color(0.86f, 0.93f, 1f, 1f), TextAnchor.MiddleCenter);
        }

        static GUIStyle Make(int size, Color color, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = UiSkin.Size(size);
            s.normal.textColor = color;
            s.alignment = anchor;
            s.wordWrap = false;
            return s;
        }

        void Draw(Grid grid)
        {
            // TODO(竖屏): 手机上面板还是压在场地右上角，方块刚出生时会挡住右半边。
            // 思路是缩窄面板（去掉每排「差 N」那列、按屏宽比例反推格宽）塞进右边那条黑边，
            // 但改完还没跑过自检和构建，先回退到已验证的版本。
            bool compact = MobileMode.Enabled;

            float pad = UiSkin.P(12f);
            // 手机上不画每排的「差 N」那列：42dp 的竖屏里那一列光放字就要 30 多像素，
            // 挤到面板只剩 8px 一格，字还显示不全。这一列让给格子，缺口总数脚注里还有。
            float labelW = compact ? 0f : UiSkin.P(48f);
            float avail = Mathf.Min(Screen.width * (compact ? 0.40f : 0.34f), UiSkin.P(360f));
            // 场地右边只剩这么宽了，按它收（MaxWidth 是整块面板的宽度，含内边距）
            if (MaxWidth > 0f)
                avail = Mathf.Min(avail, MaxWidth);
            // 窄屏上单元格会被最小宽度顶住，整块面板反而比 avail 宽 —— 手机上下限放低一点
            float cell = Mathf.Clamp((avail - pad * 2f - labelW) / grid.SizeX,
                UiSkin.P(compact ? 7f : 12f), UiSkin.P(26f));

            float mapW = cell * grid.SizeX + labelW;
            float mapH = cell * grid.SizeZ;
            float headH = UiSkin.P(30f);
            float sliderH = UiSkin.P(32f);
            // 挤到放不下「俯视图 y=0」+「还差 40」两段时，走和手机一样的单行版式，
            // 不然两段字会叠在一起
            bool tight = compact || mapW < UiSkin.P(210f);
            float footH = UiSkin.P(tight ? 26f : 44f);
            float w = mapW + pad * 2f;
            float h = pad * 2f + headH + UiSkin.P(6f) + sliderH + UiSkin.P(8f) + mapH
                      + UiSkin.P(6f) + footH;

            // 手机上顶部有一条分数横条，俯视图得让开，不然两块面板会叠在一起；
            // 展开时还要再让开那个折叠按钮
            float topY = compact
                ? Hud.MobileBarHeight + UiSkin.P(6f) + UiSkin.P(34f) + UiSkin.P(6f)
                : UiSkin.P(14f);
            var panel = new Rect(Screen.width - w - UiSkin.P(14f), topY, w, h);
            Hud.DrawPanel(panel);

            float x = panel.x + pad;
            float y = panel.y + pad;

            int missing = MissingInLayer(grid, _y);
            if (tight)
            {
                // 窄屏放不下「俯视图 y = 12」加「还差 40」两段，缺口数字挪到底下那行
                GUI.Label(new Rect(x, y, mapW, headH), "俯视图 y=" + _y, _titleStyle);
            }
            else
            {
                GUI.Label(new Rect(x, y, mapW - UiSkin.P(96f), headH), "俯视图  y = " + _y, _titleStyle);
                GUI.Label(new Rect(x + mapW - UiSkin.P(96f), y, UiSkin.P(96f), headH),
                    missing == 0 ? "已满" : "还差 " + missing,
                    missing == 0 ? _headFullStyle : _headMissStyle);
            }

            y += headH + UiSkin.P(6f);
            DrawYSlider(new Rect(x, y, mapW, sliderH), grid.SizeY);

            y += sliderH + UiSkin.P(8f);
            float mapX = x;
            float mapY = y;

            // z 越大越远离镜头，所以后排画在上面 —— 和「从上方俯视」的直觉一致：
            // 画面下边是靠自己这一侧（z = 0），上边是场地深处。
            for (int zi = 0; zi < grid.SizeZ; zi++)
            {
                int z = grid.SizeZ - 1 - zi;
                float ry = mapY + zi * cell;

                for (int xi = 0; xi < grid.SizeX; xi++)
                    DrawCell(new Rect(mapX + xi * cell, ry, cell, cell), grid, xi, _y, z);

                int rowMiss = MissingInRow(grid, _y, z);
                if (labelW > 0f)
                    GUI.Label(new Rect(mapX + grid.SizeX * cell + UiSkin.P(5f), ry,
                            labelW - UiSkin.P(5f), cell),
                        rowMiss == 0 ? "满" : "差" + rowMiss,
                        rowMiss == 0 ? _rowFullStyle : _rowMissStyle);
            }

            DrawPieceMark(grid, mapX, mapY, cell);

            float fy = mapY + mapH + UiSkin.P(6f);
            if (tight)
            {
                GUI.Label(new Rect(x, fy, mapW, UiSkin.P(22f)),
                    missing == 0 ? "本层已满" : "还差 " + missing + " 格", _smallStyle);
            }
            else
            {
                bool layerMode = GameSettings.ElimMode == GameSettings.ElimLayer;
                GUI.Label(new Rect(x, fy, mapW, UiSkin.P(20f)),
                    layerMode ? "层消：整层填满才消" : "行消：每一排单独消", _smallStyle);
                if (HasPartialWater(grid, _y))
                    GUI.Label(new Rect(x, fy + UiSkin.P(20f), mapW, UiSkin.P(20f)),
                        "有半格水，不算填满", _smallStyle);
                else
                {
                    // 不再自动跟着方块跳层之后，方块掉到别的层就看不见了，
                    // 这里指一下它在第几层 —— 想看就自己拖过去，别替玩家做决定。
                    int py = PieceMinY();
                    if (py >= 0 && py != _y)
                        GUI.Label(new Rect(x, fy + UiSkin.P(20f), mapW, UiSkin.P(20f)),
                            "当前方块在 y = " + py, _smallStyle);
                }
            }

            Hud.DrawAccentLine(panel);
        }

        /// <summary>滑条：和设置页那条一样的手感，拖到哪儿算哪儿，数值写在滑条上。</summary>
        void DrawYSlider(Rect r, int sizeY)
        {
            Event e = Event.current;
            float handle = UiSkin.P(12f);
            float travel = Mathf.Max(1f, r.width - handle);
            float t = sizeY > 1 ? (float)_y / (sizeY - 1) : 0f;

            if (e != null && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                _drag = true;

            if (_drag && e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                float nt = Mathf.Clamp01((e.mousePosition.x - r.x - handle * 0.5f) / travel);
                int ny = Mathf.RoundToInt(nt * (sizeY - 1));
                if (ny != _y)
                    _y = ny;
                e.Use();
            }

            if (e != null && e.type == EventType.MouseUp && _drag)
            {
                _drag = false;
                e.Use();
            }
            // 鼠标在窗口外松开时收不到 MouseUp，这里兜一下，不然滑条会一直粘着光标
            if (_drag && !Input.GetMouseButton(0))
                _drag = false;

            bool lit = e != null && (r.Contains(e.mousePosition) || _drag);
            Color border = lit
                ? new Color(0.62f, 0.66f, 0.74f, 0.95f)
                : new Color(0.35f, 0.37f, 0.44f, 0.95f);

            Fill(r, new Color(0.13f, 0.15f, 0.19f, 0.96f));
            Fill(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * t, r.height - 2f),
                new Color(0.42f, 0.86f, 1f, 0.16f));
            DrawBorder(r, border);

            var knob = new Rect(r.x + travel * t, r.y + 1f, handle, r.height - 2f);
            // 圆头用中灰：白字要从它上面压过去，浅灰上白字会糊成一片
            Fill(knob, new Color(0.46f, 0.49f, 0.56f, 1f));
            Fill(new Rect(knob.xMax - 1f, knob.y, 1f, knob.height), new Color(0.26f, 0.28f, 0.33f, 1f));

            // 字压在滑条上：先描一层深色影子再写白字，不然压在圆头上就看不见了
            GUI.Label(new Rect(r.x + UiSkin.P(8f) + 1f, r.y + 2f, r.width - UiSkin.P(16f), r.height),
                "第 " + _y + " 层", _sliderShadow);
            GUI.Label(new Rect(r.x + UiSkin.P(8f), r.y + 1f, r.width - UiSkin.P(16f), r.height),
                "第 " + _y + " 层", _titleStyle);
        }

        void DrawCell(Rect r, Grid grid, int x, int y, int z)
        {
            int id = grid.Get(x, y, z);
            Texture2D tex = Atlas;

            if (id > 0 && tex != null)
            {
                GUI.DrawTextureWithTexCoords(r, tex, TileUV(BlockCatalog.Of(id).Up), true);
            }
            else
            {
                GUI.color = SlotEmpty;
                GUI.DrawTexture(r, Texture2D.whiteTexture);

                // 水（岩浆）不写进网格，只能靠 Fluids 查。半格水按份数画高度：
                // 一眼能看出「这一格还没攒满」，也就知道它为什么不参与消除。
                int amount = Fluids.AmountAt(x, y, z);
                if (amount > 0)
                {
                    bool lava = Fluids.IdAt(x, y, z) == Fluids.LavaId && Fluids.LavaId > 0;
                    GUI.color = lava ? LavaColor : (amount >= Fluids.MaxAmount ? WaterFull : WaterHalf);
                    float hh = r.height * (amount / (float)Fluids.MaxAmount);
                    GUI.DrawTexture(new Rect(r.x, r.yMax - hh, r.width, hh), Texture2D.whiteTexture);
                }
                GUI.color = Color.white;
            }

            DrawBorder(r, SlotLine);
        }

        /// <summary>当前方块在这一层占了哪几格，画个绿框 —— 3D 场地里对位全靠它。</summary>
        void DrawPieceMark(Grid grid, float mapX, float mapY, float cell)
        {
            Piece p = Loop.Current;
            if (p == null)
                return;

            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector3Int c = p.WorldCell(i);
                if (c.y != _y)
                    continue;
                if (c.x < 0 || c.x >= grid.SizeX || c.z < 0 || c.z >= grid.SizeZ)
                    continue;

                int zi = grid.SizeZ - 1 - c.z;
                var r = new Rect(mapX + c.x * cell, mapY + zi * cell, cell, cell);
                float t = UiSkin.P(2f);
                GUI.color = PieceMark;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        /// <summary>当前方块所在的最低那一层；没有方块就给 -1。</summary>
        int PieceMinY()
        {
            Piece p = Loop != null ? Loop.Current : null;
            if (p == null)
                return -1;

            int minY = int.MaxValue;
            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector3Int c = p.WorldCell(i);
                if (c.y < minY)
                    minY = c.y;
            }
            return minY == int.MaxValue ? -1 : minY;
        }

        static bool HasPartialWater(Grid grid, int y)
        {
            for (int x = 0; x < grid.SizeX; x++)
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    if (grid.Get(x, y, z) != 0)
                        continue;
                    int a = Fluids.AmountAt(x, y, z);
                    if (a > 0 && a < Fluids.MaxAmount)
                        return true;
                }
            return false;
        }

        /// <summary>
        /// 图集格子外面有 1 像素 padding（复制边缘像素），UV 只取里面那 16×16，
        /// 和 BlockMesh.Quad 的算法保持一致。GUI 的 UV 原点在左下，和贴图一样。
        /// </summary>
        static Rect TileUV(Tile tile)
        {
            int cell = BlockCatalog.CellPixels;
            int pad = BlockCatalog.TilePadding;
            float w = (float)BlockCatalog.AtlasColumns * cell;
            float h = (float)BlockCatalog.AtlasRows * cell;

            float x0 = tile.Col * cell + pad;
            float yTop = tile.Row * cell + pad;
            float u0 = x0 / w;
            float u1 = (x0 + BlockCatalog.TilePixels) / w;
            float v1 = 1f - yTop / h;
            float v0 = 1f - (yTop + BlockCatalog.TilePixels) / h;
            return Rect.MinMaxRect(u0, v0, u1, v1);
        }

        static void Fill(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        static void DrawBorder(Rect r, Color c)
        {
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
