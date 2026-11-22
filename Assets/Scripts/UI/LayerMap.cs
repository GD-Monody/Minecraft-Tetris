using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地右侧的「层俯视图」：一条滑条挑 y，把那一层的 x × z 平面摊平画出来。
    ///
    /// 只在深度两格以上时才建（见 UsefulFor）：两格深之后，后排的空洞从正面已经被
    /// 前排挡掉一部分了，层消「看着铺满了却没消」经常就差在那几格上，
    /// 而且**只有攒满一格的水才算填满**，半格水看着也是一片蓝，肉眼分不出来。
    /// 所以每格照实画：方块画它的顶面贴图、水按份数画高度、空格留暗格，
    /// 右边再标出每一排（每个 z）还差几格 —— 一眼就能定位差在哪。
    /// </summary>
    public class LayerMap : MonoBehaviour
    {
        public GameLoop Loop;
        public bool Hidden;

        /// <summary>
        /// 暂停 / 结算时锁交互：折叠键和层号滑条照样画着，但不响应点击和拖动。
        /// 之前暂停后还能点开面板、拖层号，底下的局面已经定了，动了也没意义。
        /// </summary>
        public bool InputLocked;

        /// <summary>
        /// 面板宽度上限（像素），0 = 不限。
        ///
        /// 桌面上面板是浮在 3D 画面上的，窗口一窄，按屏宽 34% 算出来的宽度就会伸进场地里，
        /// 把最右边那一列挡掉。Bootstrap 每帧把「场地右边缘到屏幕右边还剩多少」量出来填这儿，
        /// 面板按它收窄 —— 场地不用缩，面板自己让。
        /// </summary>
        public float MaxWidth;

        /// <summary>
        /// 面板底部不许越过这条线（像素 y），0 = 不限。
        ///
        /// 手机横屏时右下角是按键区，俯视图展开后如果一直往下长就会压在按键上 ——
        /// 之前就是这样，面板把「上翻 / 下翻」盖掉了一半。Bootstrap 每帧把操作层的
        /// 上沿填进来，面板按它收尾。
        /// </summary>
        public float BottomLimit;

        /// <summary>
        /// 手机上是否展开。默认收起：竖屏里场地只占中间一条，面板一展开就压掉小半个屏，
        /// 而它只是「查缺口」用的辅助面板，需要的时候点开看一眼就够。
        /// 收起状态下只留一个写着缺口数的小按钮。桌面不受这个影响，一直展开。
        /// </summary>
        public bool Open;

        /// <summary>
        /// 展开时被这块面板占掉的屏幕右边缘宽度（像素）。Bootstrap 按它把相机可视区
        /// 往左收一块 —— 手机上竖屏的场地本来就只占中间一条，面板一展开就会盖住
        /// 右半边，方块落点看不见。收完之后场地整体左移，面板落在空出来的右侧。
        /// </summary>
        public float ReservedWidth { get; private set; }

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
        int _fontTitle = -1;
        int _fontSmall = -1;
        static Texture2D _atlas;
        static bool _atlasTried;

        static bool Light { get { return UiSkin.Current == UiSkin.Theme.Light; } }

        // 空格子：暗色主题是一块深灰，亮色主题改成浅灰蓝，和半透明白面板不打架
        static Color SlotEmpty
        {
            get { return Light ? new Color(0.88f, 0.91f, 0.95f, 1f) : new Color(0.16f, 0.17f, 0.21f, 1f); }
        }
        static Color SlotLine
        {
            get { return Light ? new Color(0.70f, 0.76f, 0.84f, 0.90f) : new Color(0.32f, 0.35f, 0.42f, 0.90f); }
        }
        static readonly Color WaterFull = new Color(0.24f, 0.47f, 0.94f, 0.95f);
        static readonly Color WaterHalf = new Color(0.42f, 0.66f, 1.00f, 0.70f);
        static readonly Color LavaColor = new Color(0.95f, 0.45f, 0.12f, 0.95f);
        static readonly Color PieceMark = new Color(0.55f, 0.95f, 0.35f, 1f);
        static readonly Color OkGreen = new Color(0.52f, 0.78f, 0.30f, 1f);
        static readonly Color WarnYellow = new Color(0.94f, 0.74f, 0.32f, 1f);

        /// <summary>
        /// 两格深开始就值得看：后排这时候已经被前排挡掉一部分，
        /// 而每排还差几格、哪一格是半格水，从正面根本看不出来。
        /// </summary>
        public static bool UsefulFor(int sizeZ)
        {
            return sizeZ > 1;
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

            // 手机上默认收着：右边那条竖着的折叠键点开才画面板
            if (MobileMode.Enabled)
            {
                if (!DrawPill(grid))
                {
                    ReservedWidth = 0f;
                    return;
                }

                // 竖屏用竖版（右边那条窄带，高而窄），横屏仍然用横版 ——
                // 横屏可用空间是「宽而矮」（面板得停在右下角按键上方），十行格子挤在
                // 一百四十像素高里每格只有九像素，字全糊了。场地左移只在竖版时需要：
                // 横版那块面板浮在场地右上方的空档里，本来就不压场地。
                if (Screen.width > Screen.height)
                    Draw(grid);
                else
                    DrawVertical(grid);
                return;
            }

            Draw(grid);
        }

        /// <summary>
        /// 手机上的折叠开关：屏幕右边缘一条**竖着**的窄键。
        ///
        /// 横幅按钮贴在右上角会压住场地（竖屏里场地的右半边正好在那儿），
        /// 所以改成贴在屏幕最右边的一条竖键 —— 场地左右两边本来就留了空档，
        /// 竖着摆正好塞进那条空档里。键上竖排写着「俯视 / 还差几格」，展开后变「收起」。
        /// </summary>
        bool DrawPill(Grid grid)
        {
            Rect safe = Screen.safeArea;
            float safeRight = Mathf.Max(0f, Screen.width - (safe.x + safe.width));
            bool wide = Screen.width > Screen.height;

            float w, h;
            if (wide)
            {
                // 横屏维持原样：左上角那条横向按钮（横屏的版式一律不动）
                w = UiSkin.P(122f);
                h = UiSkin.P(34f);
            }
            else
            {
                // 竖屏：屏幕右边一条竖着的窄键
                w = Mathf.Clamp(Screen.width * 0.048f, UiSkin.P(16f), UiSkin.P(30f));
                h = Mathf.Clamp(Screen.height * 0.11f, UiSkin.P(40f), UiSkin.P(110f));
            }

            float x = wide
                ? safe.x + UiSkin.P(8f)
                : Screen.width - safeRight - w - UiSkin.P(4f);
            var r = new Rect(x, Hud.MobileBarHeight + UiSkin.P(4f), w, h);

            // 记下来：面板展开时要贴在它左边
            _tabRect = r;

            bool open = Open;
            Fill(r, Light
                ? (open ? new Color(0.80f, 0.90f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.92f))
                : (open ? new Color(0.14f, 0.22f, 0.30f, 0.94f) : new Color(0.10f, 0.13f, 0.18f, 0.90f)));
            DrawBorder(r, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, open ? 0.70f : 0.35f));

            // 按钮上只写「层次图」：原来那串「俯视 差40」太绕，也不好念。
            // 还差几格面板里本来就有（脚注），收起状态想看就点开。
            string text = open
                ? (wide ? "收起" : "收\n起")
                : (wide ? "层次图" : "层\n次\n图");
            GUI.Label(r, text, _pillStyle);

            // 按钮是透明的，只负责接点击，画好的那层不会被它盖掉
            // 暂停 / 结算时这颗键只画不响应
            if (!InputLocked && GUI.Button(r, GUIContent.none, GUIStyle.none))
            {
                Open = !open;
                return Open;
            }
            return open;
        }

        Rect _tabRect;

        /// <summary>
        /// 这个点（GUI 坐标，左上原点）算不算压在层次图这块界面上：折叠键 + 展开后的面板。
        ///
        /// 手机上 IMGUI 不消费 Input.touches，在面板上划手指相机照样跟着转，
        /// 所以相机那边按「起手位置」自己判一次（见 CameraRig.UiHit）。
        /// </summary>
        public bool HitUi(Vector2 gui)
        {
            if (Hidden || Loop == null || Loop.Grid == null || !UsefulFor(Loop.Grid.SizeZ))
                return false;

            if (_tabRect.width > 0f && _tabRect.Contains(gui))
                return true;

            if (Open && ReservedWidth > 0f)
            {
                float pad = UiSkin.P(4f);
                return new Rect(Screen.width - ReservedWidth - pad, 0f,
                    ReservedWidth + pad, Screen.height).Contains(gui);
            }
            return false;
        }

        /// <summary>
        /// 字号：手机上按屏高比例算。
        ///
        /// 早先这里用的是 UiSkin.Size(设计值)，而手机那套倍数按 dpi 折算会到 2 倍上下 ——
        /// 横屏（450 高）上「表头 + 滑条 + 脚注」三段固定高度加起来就要 250px，
        /// 占掉半个屏，格子再压也没救。改成按屏高比例之后，450 高的横屏和 900 高的竖屏
        /// 都能自动收放。
        /// </summary>
        void Fonts(out int title, out int small)
        {
            if (MobileMode.Enabled)
            {
                float h = Screen.height;
                if (Screen.width > Screen.height)
                {
                    // 横屏：原样，不动
                    title = Mathf.Max(10, Mathf.RoundToInt(h * 0.028f));
                    small = Mathf.Max(9, Mathf.RoundToInt(h * 0.023f));
                }
                else
                {
                    // 竖屏：竖版面板本来就窄，再收一档，原来那档字会把格子挤没
                    title = Mathf.Max(10, Mathf.RoundToInt(h * 0.021f));
                    small = Mathf.Max(9, Mathf.RoundToInt(h * 0.017f));
                }
            }
            else
            {
                title = UiSkin.Size(17);
                small = UiSkin.Size(14);
            }
        }

        void EnsureStyles()
        {
            int title, small;
            Fonts(out title, out small);
            if (_titleStyle != null && _fontTitle == title && _fontSmall == small
                && _styleTheme == (int)UiSkin.Current)
                return;

            _fontTitle = title;
            _fontSmall = small;
            _styleTheme = (int)UiSkin.Current;

            _titleStyle = Make(title, UiSkin.TextPrimary, TextAnchor.MiddleLeft);
            _smallStyle = Make(small, UiSkin.TextSecondary, TextAnchor.MiddleLeft);
            _rowFullStyle = Make(Mathf.Max(8, small - 1), OkGreen, TextAnchor.MiddleLeft);
            _rowMissStyle = Make(Mathf.Max(8, small - 1), WarnYellow, TextAnchor.MiddleLeft);
            _headFullStyle = Make(title, OkGreen, TextAnchor.MiddleRight);
            _headMissStyle = Make(title, WarnYellow, TextAnchor.MiddleRight);
            // 滑条上那行字的影子：样式只建一次，别每帧 new（IMGUI 每帧都走一遍这里）
            _sliderShadow = Make(title, Light ? new Color(0f, 0f, 0f, 0f) : new Color(0f, 0f, 0f, 0.85f), TextAnchor.MiddleLeft);
            _pillStyle = Make(Mathf.Max(10, Mathf.RoundToInt(small * 1.1f)),
                Light ? new Color(0.10f, 0.12f, 0.16f, 1f) : new Color(0.86f, 0.93f, 1f, 1f),
                TextAnchor.MiddleCenter);
            _pillStyle.wordWrap = false;
        }

        int _styleTheme = -1;

        static GUIStyle Make(int size, Color color, TextAnchor anchor)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = size;
            s.normal.textColor = color;
            s.alignment = anchor;
            s.wordWrap = false;
            return s;
        }

        /// <summary>
        /// 手机上的**竖版**俯视图。
        ///
        /// 行是 x（场地宽度方向）、列是 z（进深），右边的竖滑条挑层 —— 相当于把原来的
        /// 横版俯视图顺时针转 90°：竖屏里场地本身就是一根竖长条，面板跟着竖着摆，
        /// 横向只占三成屏宽，展开后把场地往左一挤就互不遮挡。
        /// </summary>
        void DrawVertical(Grid grid)
        {
            float sh = Screen.height;
            float sw = Screen.width;

            float pad = Mathf.Max(4f, sh * 0.012f);
            float headH = Mathf.Max(12f, sh * 0.028f);
            float footH = Mathf.Max(12f, sh * 0.026f);
            float gap = Mathf.Max(2f, sh * 0.006f);
            float sliderW = Mathf.Max(10f, sw * 0.026f);

            float topY = Hud.MobileBarHeight + UiSkin.P(4f);
            // 底边让开操作层（竖屏是底部通栏、横屏是右下角按键）
            float bottomY = BottomLimit > topY + sh * 0.18f ? BottomLimit - gap : sh - pad;

            // 宽度：三成屏宽封顶
            float availW = Mathf.Min(sw * 0.30f, UiSkin.P(190f));
            if (MaxWidth > 0f)
                availW = Mathf.Min(availW, MaxWidth);

            float availH = Mathf.Max(sh * 0.14f,
                bottomY - topY - pad * 2f - headH - footH - gap * 2f);

            // 格子：列 = z、行 = x，两个方向都要塞得下
            float cellByW = (availW - pad * 2f - gap - sliderW) / Mathf.Max(1, grid.SizeZ);
            float cellByH = availH / Mathf.Max(1, grid.SizeX);
            float cellMin = Mathf.Max(4f, sh * 0.006f);
            float cellMax = Mathf.Max(cellMin, sh * 0.026f);
            float cell = Mathf.Clamp(Mathf.Min(cellByW, cellByH), cellMin, cellMax);

            float mapW = cell * grid.SizeZ;
            float mapH = cell * grid.SizeX;
            float w = pad * 2f + mapW + gap + sliderW;
            float h = pad * 2f + headH + gap + mapH + gap + footH;

            // 贴在折叠键的左边（键一直占着最右边那一条）
            float x = Mathf.Max(UiSkin.P(4f), _tabRect.x - gap - w);
            var panel = new Rect(x, topY, w, h);
            Hud.DrawPanel(panel);

            // 场地按这个宽度往左让
            ReservedWidth = sw - panel.x;

            float cx = panel.x + pad;
            float cy = panel.y + pad;

            int missing = MissingInLayer(grid, _y);
            GUI.Label(new Rect(cx, cy, mapW + gap + sliderW, headH), "第 " + _y + " 层", _titleStyle);
            cy += headH + gap;

            // 行 = x（0 在最上），列 = z（0 在最左）—— 相当于把横版俯视图顺时针转 90°
            for (int xi = 0; xi < grid.SizeX; xi++)
            {
                float ry = cy + xi * cell;
                for (int z = 0; z < grid.SizeZ; z++)
                    DrawCell(new Rect(cx + z * cell, ry, cell, cell), grid, xi, _y, z);
            }

            DrawPieceMarkVertical(grid, cx, cy, cell);
            DrawYSliderV(new Rect(cx + mapW + gap, cy, sliderW, mapH), grid.SizeY);

            float fy = cy + mapH + gap;
            GUI.Label(new Rect(cx, fy, mapW + gap + sliderW, footH),
                missing == 0 ? "已满" : "还差 " + missing + " 格", _smallStyle);
        }

        /// <summary>竖版俯视图里标出当前方块占了哪几格（绿框）。</summary>
        void DrawPieceMarkVertical(Grid grid, float mapX, float mapY, float cell)
        {
            Piece p = Loop.Current;
            if (p == null)
                return;

            float t = Mathf.Max(1f, Mathf.Min(UiSkin.P(2f), cell * 0.18f));
            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector3Int c = p.WorldCell(i);
                if (c.y != _y)
                    continue;
                if (c.x < 0 || c.x >= grid.SizeX || c.z < 0 || c.z >= grid.SizeZ)
                    continue;

                var r = new Rect(mapX + c.z * cell, mapY + c.x * cell, cell, cell);
                GUI.color = PieceMark;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// 竖滑条：拖到最上面是最顶层、最下面是最底层（和「越往上越高」的直觉一致）。
        /// 层号写在面板表头上，滑条本身不写字 —— 一条十来像素宽，字放不下。
        /// </summary>
        void DrawYSliderV(Rect r, int sizeY)
        {
            Event e = Event.current;
            if (InputLocked)
                _drag = false;      // 暂停那一刻正在拖也断掉
            bool live = !InputLocked;
            float handle = Mathf.Clamp(r.height * 0.14f, 10f, 40f);
            float travel = Mathf.Max(1f, r.height - handle);
            float t = sizeY > 1 ? (float)_y / (sizeY - 1) : 1f;

            if (live && e != null && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                _drag = true;

            if (live && _drag && e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                // 屏幕 y 向下增大，层号向上增大，所以这里反过来算
                float nt = 1f - Mathf.Clamp01((e.mousePosition.y - r.y - handle * 0.5f) / travel);
                int ny = Mathf.RoundToInt(nt * (sizeY - 1));
                if (ny != _y)
                    _y = ny;
                e.Use();
            }

            if (live && e != null && e.type == EventType.MouseUp && _drag)
            {
                _drag = false;
                e.Use();
            }
            if (_drag && !Input.GetMouseButton(0))
                _drag = false;

            bool lit = e != null && (r.Contains(e.mousePosition) || _drag);
            Color border = lit
                ? (Light ? new Color(0.55f, 0.60f, 0.68f, 0.95f) : new Color(0.62f, 0.66f, 0.74f, 0.95f))
                : (Light ? new Color(0.78f, 0.82f, 0.88f, 0.95f) : new Color(0.35f, 0.37f, 0.44f, 0.95f));

            Fill(r, Light ? new Color(0.86f, 0.90f, 0.95f, 0.96f) : new Color(0.13f, 0.15f, 0.19f, 0.96f));
            // 已选到的高度从底部往上铺一段，看得出「现在在这一层」
            float fillH = (r.height - 2f) * t;
            Fill(new Rect(r.x + 1f, r.yMax - 1f - fillH, r.width - 2f, fillH),
                new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.16f));
            DrawBorder(r, border);

            float ky = r.y + travel * (1f - t);
            var knob = new Rect(r.x + 1f, ky, r.width - 2f, handle);
            Fill(knob, Light ? new Color(0.55f, 0.62f, 0.72f, 1f) : new Color(0.46f, 0.49f, 0.56f, 1f));
            Fill(new Rect(knob.x, knob.yMax - 1f, knob.width, 1f),
                Light ? new Color(0.40f, 0.46f, 0.55f, 1f) : new Color(0.26f, 0.28f, 0.33f, 1f));
        }

        void Draw(Grid grid)
        {
            bool compact = MobileMode.Enabled;
            bool wide = Screen.width > Screen.height;

            // ── 一、外框各段的尺寸 ──────────────────────────────────────────
            float pad, headH, sliderH, footH, gapA, gapB;
            if (compact)
            {
                // 手机上全部按屏高比例算（理由见 Fonts 的注释）
                float sh = Screen.height;
                pad = Mathf.Max(4f, sh * 0.014f);
                headH = Mathf.Max(14f, sh * 0.040f);
                sliderH = Mathf.Max(16f, sh * 0.046f);
                footH = Mathf.Max(12f, sh * 0.034f);
                gapA = Mathf.Max(2f, sh * 0.007f);
                gapB = Mathf.Max(2f, sh * 0.008f);
            }
            else
            {
                pad = UiSkin.P(12f);
                headH = UiSkin.P(30f);
                sliderH = UiSkin.P(32f);
                footH = 0f;         // 桌面按内容另算，见下面
                gapA = UiSkin.P(6f);
                gapB = UiSkin.P(8f);
            }

            // 手机上不画每排的「差 N」那列：窄屏里那一列光放字就要三十多像素，
            // 挤到面板只剩几像素一格，字还显示不全。这一列让给格子，缺口总数脚注里还有。
            float labelW = compact ? 0f : UiSkin.P(48f);

            // ── 二、可用宽度 / 高度 ────────────────────────────────────────
            float availW = Mathf.Min(Screen.width * 0.34f, UiSkin.P(compact ? 300f : 360f));
            if (MaxWidth > 0f)
                availW = Mathf.Min(availW, MaxWidth);

            float topY = compact
                ? Hud.MobileBarHeight + UiSkin.P(6f) + (wide ? 0f : UiSkin.P(34f) + UiSkin.P(6f))
                : UiSkin.P(14f);

            float cellMin = compact
                ? Mathf.Max(4f, Screen.height * 0.008f)
                : UiSkin.P(12f);

            // 桌面：脚注高度按「能不能并排写下两段」决定，跟以前一样
            if (!compact)
            {
                float mapWEstimate = (availW - pad * 2f - labelW);
                footH = UiSkin.P(mapWEstimate < UiSkin.P(210f) ? 26f : 44f);
            }

            float fixedH = pad * 2f + headH + gapA + sliderH + gapB + gapB + footH;

            // 高度预算：底部不许越过操作层（BottomLimit），否则会压在按键上
            float bottom = BottomLimit > 0f ? BottomLimit - gapB : Screen.height - pad;
            float maxPanelH = compact
                ? Mathf.Max(fixedH + grid.SizeZ * cellMin, bottom - topY)
                : Screen.height;

            // ── 三、格子：宽、高各自算 ────────────────────────────────────
            // 横屏是「宽而矮」，横竖两个方向都必须能塞下才放得进去，
            // 所以格子的宽高分开算 —— 允许画成扁的，总比面板长出一截压住按键好。
            float cellW = (availW - pad * 2f - labelW) / Mathf.Max(1, grid.SizeX);
            float cellH = (maxPanelH - fixedH) / Mathf.Max(1, grid.SizeZ);

            cellW = Mathf.Clamp(cellW, cellMin, UiSkin.P(compact ? 18f : 26f));
            if (compact)
            {
                // 高度富余时也不要把格子拉成细长竖条（竖屏第一版就是这样，一列列看得别扭）：
                // 最多到格子宽的 1.35 倍，剩下的高度宁可空着
                cellH = Mathf.Clamp(cellH, cellMin, Mathf.Max(cellMin, cellW * 1.35f));
            }
            else
            {
                cellH = cellW;      // 桌面保持正方格
            }

            float mapW = cellW * grid.SizeX + labelW;
            float mapH = cellH * grid.SizeZ;

            float w = mapW + pad * 2f;
            float h = fixedH + mapH;

            bool tight = compact || mapW < UiSkin.P(210f);

            Rect safe = Screen.safeArea;
            float safeRight = Mathf.Max(0f, Screen.width - (safe.x + safe.width));
            var panel = new Rect(Screen.width - safeRight - w - UiSkin.P(compact ? 8f : 14f),
                topY, w, h);
            Hud.DrawPanel(panel);

            float x = panel.x + pad;
            float y = panel.y + pad;

            int missing = MissingInLayer(grid, _y);
            if (tight)
            {
                // 窄屏放不下「俯视图 y = 12」加「还差 40」两段，缺口数字挪到底下那行；
                // 表头也压成「y=0 层」—— 一百多像素宽写六个汉字会被切掉半个字（真机截图
                // 里就是「俯视图 y=(」）。「俯视」两个字外面那颗按钮上已经有了。
                GUI.Label(new Rect(x, y, mapW, headH), "y=" + _y + " 层", _titleStyle);
            }
            else
            {
                GUI.Label(new Rect(x, y, mapW - UiSkin.P(96f), headH), "层次图  y = " + _y, _titleStyle);
                GUI.Label(new Rect(x + mapW - UiSkin.P(96f), y, UiSkin.P(96f), headH),
                    missing == 0 ? "已满" : "还差 " + missing,
                    missing == 0 ? _headFullStyle : _headMissStyle);
            }

            y += headH + gapA;
            DrawYSlider(new Rect(x, y, mapW, sliderH), grid.SizeY);

            y += sliderH + gapB;
            float mapX = x;
            float mapY = y;

            // z 越大越远离镜头，所以后排画在上面 —— 和「从上方俯视」的直觉一致：
            // 画面下边是靠自己这一侧（z = 0），上边是场地深处。
            for (int zi = 0; zi < grid.SizeZ; zi++)
            {
                int z = grid.SizeZ - 1 - zi;
                float ry = mapY + zi * cellH;

                for (int xi = 0; xi < grid.SizeX; xi++)
                    DrawCell(new Rect(mapX + xi * cellW, ry, cellW, cellH), grid, xi, _y, z);

                int rowMiss = MissingInRow(grid, _y, z);
                if (labelW > 0f)
                    GUI.Label(new Rect(mapX + grid.SizeX * cellW + UiSkin.P(5f), ry,
                            labelW - UiSkin.P(5f), cellH),
                        rowMiss == 0 ? "满" : "差" + rowMiss,
                        rowMiss == 0 ? _rowFullStyle : _rowMissStyle);
            }

            DrawPieceMark(grid, mapX, mapY, cellW, cellH);

            float fy = mapY + mapH + gapB;
            if (tight)
            {
                GUI.Label(new Rect(x, fy, mapW, footH),
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

            // 装饰线只在桌面画：手机上面板本来就小，这条线正好压在脚注「还差 N 格」上
            if (!compact)
                Hud.DrawAccentLine(panel);

            // 横版面板浮在场地右上方，不压场地，不用给相机让位
            ReservedWidth = 0f;
        }

        /// <summary>滑条：和设置页那条一样的手感，拖到哪儿算哪儿，数值写在滑条上。</summary>
        void DrawYSlider(Rect r, int sizeY)
        {
            Event e = Event.current;
            if (InputLocked)
                _drag = false;      // 暂停那一刻正在拖也断掉
            bool live = !InputLocked;
            float handle = Mathf.Min(UiSkin.P(12f), r.height);
            float travel = Mathf.Max(1f, r.width - handle);
            float t = sizeY > 1 ? (float)_y / (sizeY - 1) : 0f;

            if (live && e != null && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                _drag = true;

            if (live && _drag && e != null && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
            {
                float nt = Mathf.Clamp01((e.mousePosition.x - r.x - handle * 0.5f) / travel);
                int ny = Mathf.RoundToInt(nt * (sizeY - 1));
                if (ny != _y)
                    _y = ny;
                e.Use();
            }

            if (live && e != null && e.type == EventType.MouseUp && _drag)
            {
                _drag = false;
                e.Use();
            }
            // 鼠标在窗口外松开时收不到 MouseUp，这里兜一下，不然滑条会一直粘着光标
            if (_drag && !Input.GetMouseButton(0))
                _drag = false;

            bool lit = e != null && (r.Contains(e.mousePosition) || _drag);
            Color border = lit
                ? (Light ? new Color(0.55f, 0.60f, 0.68f, 0.95f) : new Color(0.62f, 0.66f, 0.74f, 0.95f))
                : (Light ? new Color(0.78f, 0.82f, 0.88f, 0.95f) : new Color(0.35f, 0.37f, 0.44f, 0.95f));

            Fill(r, Light ? new Color(0.86f, 0.90f, 0.95f, 0.96f) : new Color(0.13f, 0.15f, 0.19f, 0.96f));
            Fill(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * t, r.height - 2f),
                new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.16f));
            DrawBorder(r, border);

            var knob = new Rect(r.x + travel * t, r.y + 1f, handle, r.height - 2f);
            // 圆头用中灰：白字要从它上面压过去，浅灰上白字会糊成一片
            Fill(knob, Light ? new Color(0.55f, 0.62f, 0.72f, 1f) : new Color(0.46f, 0.49f, 0.56f, 1f));
            Fill(new Rect(knob.xMax - 1f, knob.y, 1f, knob.height), Light ? new Color(0.40f, 0.46f, 0.55f, 1f) : new Color(0.26f, 0.28f, 0.33f, 1f));

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
        void DrawPieceMark(Grid grid, float mapX, float mapY, float cellW, float cellH)
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
                var r = new Rect(mapX + c.x * cellW, mapY + zi * cellH, cellW, cellH);
                float t = Mathf.Min(UiSkin.P(2f), Mathf.Min(cellW, cellH) * 0.25f);
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
