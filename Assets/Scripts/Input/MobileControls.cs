using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 手机上的操作层：左边一块拖动区移动方块，右边一组虚拟键做旋转和降落。
    ///
    /// 为什么不直接摆 IMGUI 按钮：Unity 只把**第一根手指**当鼠标喂给 IMGUI，第二根手指
    /// 它根本收不到。而俄罗斯方块最常用的操作恰恰是「按住方向的同时点一下旋转」——
    /// 用 GUI.Button 做，这个组合是一按就哑。所以这里自己读 Input.touches：
    ///
    /// - 每个手指进来先认领一个角色（某个按键 / 拖动区），之后一直归它，直到抬起；
    /// - 按键判定和绘制都用同一份 Rect，画在哪儿就只能点哪儿；
    /// - 按键可以有多个同时按着，互不干扰（这是 IMGUI 做不到的那部分）。
    ///
    /// 键位分两套（设置里选，默认初级）：
    /// - 初级：四键。左上/右上两个旋转键**循环**切换轴（面内 → 俯视 → 前后翻），
    ///   左下软降、右下硬降。
    /// - 高级：3x3 九宫格，正中留空，八个键各管一件事。
    /// 按键上一律画符号、不写文字；符号全是程序生成的贴图 —— 字体里没有这些形状，
    /// WebGL 又没有字体回退，缺字会直接画成空白。
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        /// <summary>一个键能做的事。</summary>
        enum Act
        {
            None,
            InL, InR,            // 面内旋转（绕屏幕法线）
            YawL, YawR,          // 俯视旋转（绕竖直轴）
            PitchUp, PitchDown,  // 往背面 / 往正面翻（绕水平轴）
            Soft, Hard,
        }

        /// <summary>槽位数按高级键位算（3x3），初级只用前四个。下标即“行优先”的位置。</summary>
        const int Slots = 9;

        /// <summary>初级那两个旋转键各自停在哪一档：0 面内 / 1 俯视 / 2 前后翻。</summary>
        int _cycleL, _cycleR;

        /// <summary>软降按住后的连发间隔（秒）。松手就停。</summary>
        const float SoftRepeat = 0.055f;

        /// <summary>竖屏底部操作条占屏高的比例。</summary>
        public const float StripRatio = 0.18f;

        /// <summary>横屏时操作条占屏高的比例。横屏高度本来就少，再按 0.34 切会把场地压扁。</summary>
        public const float WideStripRatio = 0.23f;

        /// <summary>
        /// 拖动区攒够多远走一格（**拖动区自身**宽高的倍数，不是屏宽的）。
        /// 原来按屏宽算，横屏拖动区窄、同样距离换来的格数少，手感发涩。
        /// </summary>
        const float StepX = 0.18f;
        const float StepZ = 0.22f;

        public bool Visible = true;

        /// <summary>亮色主题。配色分支都走它，省得每处都写全名。</summary>
        static bool Light { get { return UiSkin.Current == UiSkin.Theme.Light; } }

        /// <summary>当前是不是暂停着。只影响那颗键上的字（暂停 / 继续）。</summary>
        public bool Paused;

        /// <summary>
        /// 这一局已经结束。按键照旧画出来（画面不重排），但一个都不接，
        /// 连右上角那颗也不响应 —— 结束面板上本来就有「再来一局」。
        /// </summary>
        public bool Over;

        /// <summary>暂停或结束：按键画着但不接输入。</summary>
        bool Locked { get { return Paused || Over; } }

        /// <summary>
        /// 场上实际深度（Bootstrap 每帧给）。0 = 还不知道，退回读设置值。
        /// 不能直接用 GameSettings.SizeZ：命令行 -size 会把运行时场地改掉，而设置值不变，
        /// 结果 z = 1 的局里还挂着俯视/前后翻那四个按不动的键。
        /// </summary>
        public int Depth;

        /// <summary>点了右上角的暂停键。</summary>
        public System.Action OnPause;

        readonly Rect[] _slot = new Rect[Slots];
        readonly Act[] _act = new Act[Slots];
        readonly int[] _finger = new int[Slots];
        readonly float[] _next = new float[Slots];
        readonly bool[] _enabled = new bool[Slots];

        int _rows = 2, _cols = 2;
        Rect _pad;
        Rect _pauseRect;
        float _stripH;

        int _padFinger = -1;
        Vector2 _padLast;
        Vector2 _mousePrev;
        float _accX, _accY;

        GUIStyle _labelStyle;
        GUIStyle _pauseStyle;
        GUIStyle _padStyle;
        int _styleSize = -1;
        int _lastW, _lastH;
        int _lastDepth = -1;
        int _lastLayout = -1;
        int _styleTheme = -1;
        bool _styleWide;

        InputState _out;

        /// <summary>控制条的高度（像素）。相机用它算可视区。</summary>
        public float StripHeight { get { return _stripH; } }

        /// <summary>
        /// 控制区的最上沿（像素 y）。上方的面板（俯视图）按它收尾，别压在按键上。
        /// 竖屏是底部通栏的顶边，横屏是右下角那堆按键的顶边。
        /// </summary>
        public float ContentBottom { get; private set; }

        void Awake()
        {
            for (int i = 0; i < Slots; i++)
                _finger[i] = -1;
            Layout();
        }

        void Update()
        {
            if (Screen.width != _lastW || Screen.height != _lastH || CurrentDepth != _lastDepth
                || KeyLayout != _lastLayout)
                Layout();

            _out = default(InputState);

            if (!Visible)
            {
                ReleaseAll();
                return;
            }

            TickTouches();

            if (Locked)
                return;

            // 软降按住连发：按下那一帧已经发过一次，之后靠这里续
            int soft = SlotOf(Act.Soft);
            if (soft < 0 || _finger[soft] < 0)
                return;

            float now = Time.unscaledTime;
            while (now >= _next[soft])
            {
                _out.SoftDrop = true;
                _next[soft] += SoftRepeat;
            }
        }

        // ── 键位表 ────────────────────────────────────────────────────────

        /// <summary>0 = 初级（四键），1 = 高级（九键，正中留空）。</summary>
        static int KeyLayout { get { return GameSettings.KeyLayout; } }

        /// <summary>这一局画不画俯视 / 前后翻（z = 1 时转不过去，按了也是白按）。</summary>
        bool CanDepth { get { return CurrentDepth > 1; } }

        void BuildSlots()
        {
            for (int i = 0; i < Slots; i++)
            {
                _act[i] = Act.None;
                _enabled[i] = false;
            }

            _rows = 2;
            _cols = 2;

            if (KeyLayout == GameSettings.KeysBasic)
            {
                // 深度 1 时没有「俯视 / 前后翻」可言，循环锁在面内那一档
                int cl = CanDepth ? _cycleL : 0;
                int cr = CanDepth ? _cycleR : 0;

                _act[0] = cl == 0 ? Act.InL : (cl == 1 ? Act.YawL : Act.PitchUp);
                _act[1] = cr == 0 ? Act.InR : (cr == 1 ? Act.YawR : Act.PitchDown);
                _act[2] = Act.Soft;
                _act[3] = Act.Hard;
                for (int i = 0; i < 4; i++)
                    _enabled[i] = true;
                return;
            }

            // 高级：按行优先摆进 3x3，正中（下标 4）空着
            _rows = 3;
            _cols = 3;
            _act[0] = Act.YawL;       // 1
            _act[1] = Act.PitchUp;    // 2
            _act[2] = Act.YawR;       // 3
            _act[3] = Act.InL;        // 4
            _act[4] = Act.None;       // 0（空）
            _act[5] = Act.InR;        // 5
            _act[6] = Act.Soft;       // 6
            _act[7] = Act.PitchDown;  // 7
            _act[8] = Act.Hard;       // 8

            for (int i = 0; i < Slots; i++)
            {
                if (_act[i] == Act.None)
                    continue;
                // z = 1 时俯视和前后翻没有意义，画成灰的、不接输入
                _enabled[i] = IsDepthAct(_act[i]) ? CanDepth : true;
            }
        }

        static bool IsDepthAct(Act a)
        {
            return a == Act.YawL || a == Act.YawR || a == Act.PitchUp || a == Act.PitchDown;
        }

        /// <summary>某个动作当前占的槽位，没有就 -1。</summary>
        int SlotOf(Act a)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (_act[i] == a && _enabled[i])
                    return i;
            }
            return -1;
        }

        /// <summary>场地深度：优先用 Bootstrap 传来的真实值，拿不到再看设置。</summary>
        int CurrentDepth
        {
            get { return Depth > 0 ? Depth : GameSettings.SizeZ; }
        }

        // ── 布局 ──────────────────────────────────────────────────────────

        void Layout()
        {
            _lastW = Screen.width;
            _lastH = Screen.height;
            _lastDepth = CurrentDepth;
            _lastLayout = KeyLayout;

            BuildSlots();

            float w = Screen.width;
            float h = Screen.height;
            bool wide = w > h;
            bool basic = KeyLayout == GameSettings.KeysBasic;

            // 安全区：刘海、挖孔、手势条都在这儿让开。竖屏只看上下，横屏再看左右。
            Rect safe = Screen.safeArea;
            float safeBottom = Mathf.Max(0f, safe.y);
            float safeLeft = Mathf.Max(0f, safe.x);
            float safeRight = Mathf.Max(0f, w - (safe.x + safe.width));

            float pad = Mathf.Max(6f, w * 0.016f);

            // 暂停键贴右上角（Hud.MobilePauseSize 是唯一的尺寸来源）
            float ps = Hud.MobilePauseSize;
            float m = UiSkin.P(wide ? 6f : 8f);
            float topInset = Screen.height - (safe.y + safe.height);
            _pauseRect = new Rect(w - safeRight - m - ps, topInset + m, ps, ps);

            if (wide)
            {
                // 横屏不要底部通栏：横屏高度本来就紧张，一条通栏吃掉四分之一，
                // 场地被压成一条缝。改成左右分边 —— 左边拖动，右边按键。
                _stripH = 0f;

                float topY = Mathf.Max(topInset + ps + m, Hud.MobileBarHeight);
                float availH = Mathf.Max(1f, h - topY - safeBottom - pad);

                float padW = w * (basic ? 0.34f : 0.30f) - pad;
                float padH = availH * (basic ? 0.62f : 0.72f);
                _pad = new Rect(safeLeft + pad, topY + availH - padH, padW, padH);

                // 右边按键区：九键要比四键占得更宽、更高
                float btnW = (basic ? 0.26f : 0.36f) * w - pad;
                float btnLeft = w - safeRight - pad - btnW;
                float btnTop = topY + availH * (basic ? 0.46f : 0.26f);
                float btnH = Mathf.Max(1f, h - safeBottom - pad - btnTop);

                LayoutGrid(new Rect(btnLeft, btnTop, btnW, btnH), pad * 0.5f);
                ContentBottom = btnTop;
                return;
            }

            // 竖屏：底部一条通栏，左边拖动、右边按钮。
            // 九键要摆 3 行，18% 的通栏只够每个键 33 像素，符号糊成一团；
            // 加高到 26%（相机取景按 ContentBottom 让位，不会压住方块）
            _stripH = Mathf.Clamp(h * (basic ? StripRatio : 0.26f), 1f, h * 0.5f);
            float top = h - _stripH;

            float vPadW = (w - safeLeft - safeRight) * (basic ? 0.40f : 0.30f);
            _pad = new Rect(safeLeft + pad, top + pad,
                vPadW - pad * 1.5f, _stripH - pad * 2f - safeBottom);

            // 注意别和上面横屏分支的 btnLeft / btnW 重名，C# 会报 CS0136
            float keysLeft = _pad.xMax + pad;
            float keysW = w - safeRight - pad - keysLeft;

            // 四键 2x2、九键 3x3 都铺在通栏里，通栏高度已经按键位数调过
            LayoutGrid(new Rect(keysLeft, _pad.y, keysW, _pad.height), pad * 0.8f);
            ContentBottom = top;
        }

        /// <summary>把在用的槽位铺成 rows x cols 的网格。</summary>
        void LayoutGrid(Rect block, float gap)
        {
            float bw = (block.width - gap * (_cols - 1)) / _cols;
            float bh = (block.height - gap * (_rows - 1)) / _rows;

            for (int i = 0; i < Slots; i++)
            {
                if (!_enabled[i])
                {
                    _finger[i] = -1;
                    continue;
                }
                int row = i / _cols;
                int col = i % _cols;
                _slot[i] = new Rect(block.x + col * (bw + gap), block.y + row * (bh + gap), bw, bh);
            }
        }

        // ── 触摸 ──────────────────────────────────────────────────────────

        void TickTouches()
        {
            if (MobileMode.Forced && Input.touchCount == 0)
            {
                TickMouseAsFinger();
                return;
            }

            int n = Input.touchCount;
            for (int i = 0; i < n; i++)
            {
                Touch t = Input.GetTouch(i);
                Vector2 p = ToGui(t.position);

                if (t.phase == TouchPhase.Began)
                    Begin(t.fingerId, p);
                else if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                    Drag(t.fingerId, p);
                else if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                    End(t.fingerId);
            }
        }

        /// <summary>
        /// 电脑上用 -mobile 跑的时候把鼠标当一根手指：按下 = Began，拖动 = Moved，松开 = Ended。
        /// 真机上这一段不参与（touchCount 不为 0，或者根本不是 Forced）。
        /// </summary>
        void TickMouseAsFinger()
        {
            const int MouseFinger = 999;
            Vector2 p = ToGui(Input.mousePosition);

            if (Input.GetMouseButtonDown(0))
            {
                _mousePrev = p;
                Begin(MouseFinger, p);
            }
            else if (Input.GetMouseButton(0))
            {
                if (_mousePrev != p)
                {
                    Drag(MouseFinger, p);
                    _mousePrev = p;
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                End(MouseFinger);
            }
        }

        void Begin(int finger, Vector2 p)
        {
            if (_pauseRect.Contains(p))
            {
                // 暂停键按下即响应：等抬手指再响应的话，手指划出去就点不动了
                if (!Over && OnPause != null)
                    OnPause();
                return;
            }

            if (Locked)
                return;   // 暂停 / 已结束：按键照旧画着（画面不重排），但不接输入

            for (int i = 0; i < Slots; i++)
            {
                if (!_enabled[i] || _finger[i] >= 0 || !_slot[i].Contains(p))
                    continue;

                _finger[i] = finger;
                _next[i] = Time.unscaledTime + SoftRepeat;
                Fire(i);
                return;
            }

            if (_pad.Contains(p))
            {
                _padFinger = finger;
                _padLast = p;
                _accX = 0f;
                _accY = 0f;
            }
        }

        void Drag(int finger, Vector2 p)
        {
            if (finger != _padFinger)
                return;

            float dx = p.x - _padLast.x;
            float dy = p.y - _padLast.y;   // GUI 坐标：往下是正，往上划 dy 为负
            _padLast = p;
            _accX += dx;
            // 取反之后「往上划 = 正数」。往上划是把方块往远处推：相机在 z 负半轴，
            // 远离镜头是 z 变大，对应的是 Down 那条指令（Up / Down 是按 z 减 / z 增定义的）。
            _accY -= dy;

            float stepX = Mathf.Max(8f, _pad.width * StepX);
            float stepZ = Mathf.Max(8f, _pad.height * StepZ);

            while (_accX >= stepX)
            {
                _out.Right = true;
                _accX -= stepX;
            }
            while (_accX <= -stepX)
            {
                _out.Left = true;
                _accX += stepX;
            }
            while (_accY >= stepZ)
            {
                _out.Down = true;          // 上划 → 远离镜头（z 变大）
                _accY -= stepZ;
            }
            while (_accY <= -stepZ)
            {
                _out.Up = true;            // 下划 → 靠近镜头（z 变小）
                _accY += stepZ;
            }
        }

        void End(int finger)
        {
            if (finger == _padFinger)
            {
                _padFinger = -1;
                _accX = 0f;
                _accY = 0f;
            }
            for (int i = 0; i < Slots; i++)
            {
                if (_finger[i] == finger)
                    _finger[i] = -1;
            }
        }

        void ReleaseAll()
        {
            _padFinger = -1;
            for (int i = 0; i < Slots; i++)
                _finger[i] = -1;
        }

        /// <summary>
        /// 按下某个槽位：发指令。初级那两个旋转键按完还要轮换一档
        /// （左：面内左 → 俯视左 → 上翻；右：面内右 → 俯视右 → 下翻）。
        /// </summary>
        void Fire(int slot)
        {
            switch (_act[slot])
            {
                case Act.InL: _out.RotateCCW = true; break;
                case Act.InR: _out.RotateCW = true; break;
                case Act.YawL: _out.RotateYawCCW = true; break;
                case Act.YawR: _out.RotateYawCW = true; break;
                case Act.PitchUp: _out.RotatePitchCW = true; break;
                case Act.PitchDown: _out.RotatePitchCCW = true; break;
                case Act.Soft: _out.SoftDrop = true; break;
                case Act.Hard: _out.HardDrop = true; break;
            }

            if (KeyLayout != GameSettings.KeysBasic || !CanDepth)
                return;

            if (slot == 0)
            {
                _cycleL = (_cycleL + 1) % 3;
                BuildSlots();      // 换档之后按钮上的符号要跟着变
            }
            else if (slot == 1)
            {
                _cycleR = (_cycleR + 1) % 3;
                BuildSlots();
            }
        }

        /// <summary>把这一帧的触屏操作交给玩法层。和键盘的输入按位或，两边同时生效。</summary>
        public InputState Read()
        {
            return _out;
        }

        /// <summary>这个手指是不是正按在控制区里（相机手势据此避让，免得转视角时把方块也拖走）。</summary>
        public bool Claims(int finger)
        {
            if (finger == _padFinger)
                return true;
            for (int i = 0; i < Slots; i++)
            {
                if (_finger[i] == finger)
                    return true;
            }
            return false;
        }

        static Vector2 ToGui(Vector2 screen)
        {
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        // ── 绘制 ──────────────────────────────────────────────────────────

        void OnGUI()
        {
            if (!Visible || !UiSkin.WorthDrawing())
                return;

            UiSkin.Apply();
            EnsureStyles();

            Rect strip = new Rect(0f, Screen.height - _stripH, Screen.width, _stripH);
            Fill(strip, UiSkin.StripFill);
            Fill(new Rect(0f, strip.y, Screen.width, 1f), UiSkin.AccentSoft);

            DrawPad();

            for (int i = 0; i < Slots; i++)
            {
                if (_enabled[i])
                    DrawSlot(i);
            }

            if (Locked)
                Fill(strip, UiSkin.FreezeOverlay);

            DrawPause();
        }

        void DrawPad()
        {
            bool active = _padFinger >= 0;
            Fill(_pad, Light
                ? (active ? new Color(0.74f, 0.86f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.45f))
                : (active ? new Color(0.12f, 0.17f, 0.24f, 0.55f) : new Color(0.09f, 0.11f, 0.15f, 0.5f)));
            Border(_pad, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, active ? 0.65f : 0.22f));

            // 中间一个「拖」：这块是移动区，按键只管旋转和降落
            GUI.Label(_pad, "拖", _padStyle);
        }

        void DrawSlot(int i)
        {
            bool down = _finger[i] >= 0;
            bool hot = _act[i] == Act.Hard;

            Color fill = down
                ? (Light ? new Color(0.70f, 0.84f, 1f, 0.95f) : new Color(0.20f, 0.48f, 0.68f, 0.92f))
                : (Light ? new Color(1f, 1f, 1f, 0.86f) : new Color(0.11f, 0.13f, 0.18f, 0.88f));

            // 硬降是最高频的一下，给个暖色，一眼能找到
            if (hot)
                fill = down ? new Color(0.85f, 0.45f, 0.18f, 0.95f)
                    : (Light ? new Color(1f, 0.92f, 0.86f, 0.95f) : new Color(0.28f, 0.16f, 0.09f, 0.9f));

            Color border = hot
                ? new Color(1f, 0.62f, 0.32f, down ? 0.95f : 0.5f)
                : new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, down ? 0.9f : 0.28f);

            Fill(_slot[i], fill);
            Border(_slot[i], border);

            Color ink = Light
                ? new Color(0.12f, 0.16f, 0.22f, down ? 0.95f : 0.8f)
                : new Color(0.86f, 0.93f, 1f, down ? 0.95f : 0.8f);
            DrawSymbol(_slot[i], _act[i], ink);
        }

        // ── 符号 ──────────────────────────────────────────────────────────

        /// <summary>按键上一律画符号，不写文字。</summary>
        void DrawSymbol(Rect r, Act a, Color c)
        {
            switch (a)
            {
                case Act.Soft:
                    DrawDown(r, 1, c);
                    break;

                case Act.Hard:
                    DrawDown(r, 2, c);
                    break;

                // 面内的圈画成圆的
                case Act.InL:
                    DrawArc(r, false, 1f, 1f, c);
                    break;
                case Act.InR:
                    DrawArc(r, true, 1f, 1f, c);
                    break;

                // 俯视是绕竖直轴，画成扁椭圆（像从上面看下去的圈）
                case Act.YawL:
                    DrawArc(r, false, 1.2f, 0.6f, c);
                    break;
                case Act.YawR:
                    DrawArc(r, true, 1.2f, 0.6f, c);
                    break;

                // 前后翻是绕水平轴，画成竖椭圆
                case Act.PitchUp:
                    DrawArc(r, true, 0.6f, 1.2f, c);
                    break;
                case Act.PitchDown:
                    DrawArc(r, false, 0.6f, 1.2f, c);
                    break;
            }
        }

        /// <summary>向下的箭头：n = 1 单个（软降），n = 2 两个叠着（硬降）。</summary>
        void DrawDown(Rect r, int n, Color c)
        {
            float side = Mathf.Min(r.width, r.height) * (n == 1 ? 0.44f : 0.30f);
            float gap = n == 1 ? 0f : side * 0.35f;
            float totalH = side * n + gap * (n - 1);

            Color oc = GUI.color;
            GUI.color = c;
            for (int i = 0; i < n; i++)
            {
                float y = r.center.y - totalH * 0.5f + i * (side + gap);
                var box = new Rect(r.center.x - side * 0.5f, y, side, side);
                Matrix4x4 old = GUI.matrix;
                GUIUtility.RotateAroundPivot(180f, box.center);
                GUI.DrawTexture(box, ArrowTexture);
                GUI.matrix = old;
            }
            GUI.color = oc;
        }

        /// <summary>圆弧 + 箭头，表示「转」。aspect 把圆压成椭圆，用来区分转哪根轴。</summary>
        void DrawArc(Rect r, bool cw, float aspectX, float aspectY, Color c)
        {
            float side = Mathf.Min(r.width, r.height) * 0.54f;
            var box = new Rect(r.center.x - side * aspectX * 0.5f,
                r.center.y - side * aspectY * 0.5f,
                side * aspectX, side * aspectY);

            Color oc = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(box, cw ? ArcCw : ArcCcw);
            GUI.color = oc;
        }

        // ── 程序生成的贴图 ────────────────────────────────────────────────

        static Texture2D _arrowTex;

        /// <summary>朝上的实心三角（白 + 透明）。转 180 度就是向下。</summary>
        static Texture2D ArrowTexture
        {
            get
            {
                if (_arrowTex != null)
                    return _arrowTex;

                const int S = 32;
                var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
                tex.filterMode = FilterMode.Bilinear;
                var px = new Color32[S * S];
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        // SetPixels 的 y = 0 是贴图**底部**，所以顶点落在 v = 1（踩过一次）
                        float u = (x + 0.5f) / S - 0.5f;
                        float v = (y + 0.5f) / S;
                        float halfW = 0.46f * (1f - v);
                        float d = Mathf.Abs(u) - halfW;
                        float a = Mathf.Clamp01(0.5f - d * S * 0.5f);
                        px[y * S + x] = new Color(1f, 1f, 1f, a);
                    }
                }
                tex.SetPixels32(px);
                tex.Apply();
                _arrowTex = tex;
                return tex;
            }
        }

        static Texture2D _arcCw, _arcCcw;

        static Texture2D ArcCw { get { return _arcCw != null ? _arcCw : (_arcCw = BuildArc(true)); } }
        static Texture2D ArcCcw { get { return _arcCcw != null ? _arcCcw : (_arcCcw = BuildArc(false)); } }

        /// <summary>
        /// 一段圆弧 + 末端三角箭头，表示旋转；顺时针 / 逆时针各生成一张。
        /// 逐像素算出来的 —— 字体里没有这种符号，也没有现成的矢量绘制可用。
        /// </summary>
        static Texture2D BuildArc(bool cw)
        {
            const int S = 32;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[S * S];

            float cx = (S - 1) * 0.5f;
            float cy = (S - 1) * 0.5f;
            float radius = 10.5f;
            float half = 2.0f;     // 弧线粗细的一半
            float from = 55f;      // 起点角度（度）
            float sweep = 250f;    // 扫过的角度，剩下 110 度留给箭头

            // 箭头落在弧的终点上，沿切线方向外指
            float endDeg = cw ? from - sweep : from + sweep;
            float er = endDeg * Mathf.Deg2Rad;
            float ex = cx + Mathf.Cos(er) * radius;
            float ey = cy + Mathf.Sin(er) * radius;
            float tDeg = cw ? endDeg - 90f : endDeg + 90f;
            float tr = tDeg * Mathf.Deg2Rad;
            float tx = Mathf.Cos(tr), ty = Mathf.Sin(tr);
            float nx = -ty, ny = tx;
            float tipX = ex + tx * 7.4f, tipY = ey + ty * 7.4f;
            float b1x = ex - tx * 2.8f + nx * 5.4f, b1y = ey - ty * 2.8f + ny * 5.4f;
            float b2x = ex - tx * 2.8f - nx * 5.4f, b2y = ey - ty * 2.8f - ny * 5.4f;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float t = Mathf.Repeat(cw ? from - ang : ang - from, 360f);

                    bool onArc = Mathf.Abs(d - radius) <= half && t <= sweep;
                    bool inHead = PointInTri(x, y, tipX, tipY, b1x, b1y, b2x, b2y);

                    if (onArc || inHead)
                        px[y * S + x] = new Color(1f, 1f, 1f, 1f);
                }
            }

            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static bool PointInTri(float px, float py,
            float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool pos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(neg && pos);
        }

        // ── 暂停键与样式 ──────────────────────────────────────────────────

        void DrawPause()
        {
            Fill(_pauseRect, Paused
                ? (Light ? new Color(0.72f, 0.86f, 1f, 0.95f) : new Color(0.16f, 0.26f, 0.34f, 0.92f))
                : (Light ? new Color(1f, 1f, 1f, 0.88f) : new Color(0.11f, 0.13f, 0.18f, 0.8f)));
            Border(_pauseRect, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, Paused ? 0.7f : 0.3f));
            // 暂停中这颗键就是「继续」——字不换的话，玩家会以为它点不动
            GUI.Label(_pauseRect, Paused ? "继续" : "暂停", _pauseStyle);
        }

        void EnsureStyles()
        {
            bool wide = Screen.width > Screen.height;
            int size = Mathf.RoundToInt(Mathf.Clamp(_pad.height * 0.11f, 13f, 30f));
            if (_labelStyle != null && size == _styleSize && _styleTheme == (int)UiSkin.Current
                && _styleWide == wide)
                return;

            _styleSize = size;
            _styleTheme = (int)UiSkin.Current;
            _styleWide = wide;

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.font = UiSkin.Font;
            _labelStyle.fontSize = size;
            _labelStyle.alignment = TextAnchor.MiddleCenter;

            _padStyle = new GUIStyle(_labelStyle);
            _padStyle.normal.textColor = Light
                ? new Color(0.30f, 0.40f, 0.52f, 0.85f)
                : new Color(0.55f, 0.72f, 0.86f, 0.75f);

            // 暂停键上的「暂停 / 继续」单独一档：方键，字太大就折成两行
            _pauseStyle = new GUIStyle(_labelStyle);
            _pauseStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(Hud.MobilePauseSize * 0.32f));
            _pauseStyle.normal.textColor = Light
                ? new Color(0.10f, 0.12f, 0.16f, 0.95f)
                : new Color(0.88f, 0.94f, 1f, 0.95f);
        }

        static void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Border(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 1f), c);
            Fill(new Rect(r.x, r.yMax - 1f, r.width, 1f), c);
            Fill(new Rect(r.x, r.y, 1f, r.height), c);
            Fill(new Rect(r.xMax - 1f, r.y, 1f, r.height), c);
        }
    }
}
