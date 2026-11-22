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
            MoveL, MoveR,        // 左右平移
            MoveAway, MoveNear,  // 上下游：往远处 / 往近处
            Soft, Hard,
        }

        /// <summary>键位区槽位数（3x3，初级只用前四个）。</summary>
        const int GridSlots = 9;

        /// <summary>再加上「移动：十字键」那四颗（9 上 / 10 下 / 11 左 / 12 右）。</summary>
        const int Slots = 13;

        const int DirUp = 9, DirDown = 10, DirLeft = 11, DirRight = 12;

        /// <summary>软降按住后的连发间隔（秒）。松手就停。</summary>
        const float SoftRepeat = 0.055f;

        /// <summary>平移键按住后的连发间隔。比软降慢一点，不然一碰就滑出去好几格。</summary>
        const float MoveRepeat = 0.10f;

        /// <summary>平移键按下到第一次连发的等待（秒）。</summary>
        const float MoveFirstDelay = 0.22f;

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
        /// <summary>右边网格算出来的圆钮直径，左边的按键按它对齐大小。</summary>
        float _btnDia = 40f;
        Rect _pad;
        Rect _pauseRect;
        float _stripH;

        int _padFinger = -1;
        Vector2 _padLast;
        Vector2 _mousePrev;
        float _accX, _accY;

        GUIStyle _labelStyle;
        GUIStyle _pauseStyle;
        int _styleSize = -1;
        int _lastW, _lastH;
        int _lastDepth = -1;
        int _lastLayout = -1;
        int _lastMove = -1;
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
                || KeyLayout != _lastLayout || GameSettings.MoveStyle != _lastMove)
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
            float now = Time.unscaledTime;
            for (int i = 0; i < Slots; i++)
            {
                if (!_enabled[i] || _finger[i] < 0)
                    continue;

                bool softKey = _act[i] == Act.Soft;
                if (!softKey && !IsRepeatAct(_act[i]))
                    continue;

                while (now >= _next[i])
                {
                    if (softKey)
                    {
                        _out.SoftDrop = true;
                        _next[i] += SoftRepeat;
                    }
                    else
                    {
                        Fire(i);
                        _next[i] += MoveRepeat;
                    }
                }
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

            BuildDpadSlots();

            _rows = 2;
            _cols = 2;

            if (KeyLayout == GameSettings.KeysBasic)
            {
                // 初级就是「按左就往左转、按右就往右转」，固定面内旋转。
                // 之前做过「每按一次轮换一根轴」，结果第二下开始就转到别的轴上去了，
                // 连转两下等于没转 —— 用户直接当成 bug 报了，撤掉。
                _act[0] = Act.InL;
                _act[1] = Act.InR;
                _act[2] = Act.Soft;
                _act[3] = Act.Hard;
                for (int i = 0; i < 4; i++)
                    _enabled[i] = true;
                return;
            }

            // 高级：按行优先摆进 3x3，正中（下标 4）空着。
            // 十字那四个位置（1/2/3/8）**功能仍是旋转**，只是图标画成箭头
            // （用户明确要求：图标换箭头，但别改动作）。
            _rows = 3;
            _cols = 3;
            _act[0] = Act.YawL;       // 1 俯视左（←）
            _act[1] = Act.PitchUp;    // 2 上翻（↑）
            _act[2] = Act.YawR;       // 3 俯视右（→）
            _act[3] = Act.InL;        // 4 面内左
            _act[4] = Act.None;       // 5（空）
            _act[5] = Act.InR;        // 6 面内右
            _act[6] = Act.Soft;       // 7 软降
            _act[7] = Act.PitchDown;  // 8 下翻（↓）
            _act[8] = Act.Hard;       // 9 硬降

            for (int i = 0; i < GridSlots; i++)
            {
                if (_act[i] == Act.None)
                    continue;
                // z = 1 时俯视和前后翻没有意义，画成灰的、不接输入
                _enabled[i] = IsDepthAct(_act[i]) ? CanDepth : true;
            }
        }

        /// <summary>「移动：十字键」时补上那四颗方向键（滑动模式下不出现）。</summary>
        void BuildDpadSlots()
        {
            bool on = GameSettings.MoveStyle == GameSettings.MoveDpad;
            _act[DirUp] = Act.MoveAway;
            _act[DirDown] = Act.MoveNear;
            _act[DirLeft] = Act.MoveL;
            _act[DirRight] = Act.MoveR;

            _enabled[DirUp] = on && CanDepth;   // 深度 1 没有前后可言
            _enabled[DirDown] = on && CanDepth;
            _enabled[DirLeft] = on;
            _enabled[DirRight] = on;
        }

        /// <summary>没有深度（z = 1）就做不了的动作：绕竖直轴转、前后翻、上下游。</summary>
        static bool IsDepthAct(Act a)
        {
            return a == Act.YawL || a == Act.YawR || a == Act.PitchUp || a == Act.PitchDown
                   || a == Act.MoveAway || a == Act.MoveNear;
        }

        /// <summary>按住会连发的动作：平移和软降。</summary>
        static bool IsRepeatAct(Act a)
        {
            return a == Act.MoveL || a == Act.MoveR || a == Act.MoveAway || a == Act.MoveNear;
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
            _lastMove = GameSettings.MoveStyle;

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

                // 右边按键区：贴着右下角
                float btnW = (basic ? 0.20f : 0.26f) * w - pad;
                float btnLeft = w - safeRight - pad - btnW;
                float btnTop = topY + availH * (basic ? 0.52f : 0.40f);
                float btnH = Mathf.Max(1f, h - safeBottom - pad - btnTop);

                // 先排右边的网格 —— 它会算出「标准圆钮直径」，左边按这个尺寸对齐
                LayoutGrid(new Rect(btnLeft, btnTop, btnW, btnH), pad * 0.5f);

                // 左边：拖动区 / 十字键区。宽度按十字的实际占位倒推，
                // 免得圆钮放大之后挤出这块区域
                float d = _btnDia;
                float need = d * 2.95f;                       // 十字的外接边长（两臂 + 钮）
                float padW = Mathf.Clamp(Mathf.Max(w * 0.24f, need), 1f, w * 0.38f) - pad;
                float padH = Mathf.Clamp(Mathf.Max(availH * 0.58f, need), 1f, availH);
                _pad = new Rect(safeLeft + pad, topY + availH - padH, padW, padH);

                LayoutDpad();
                ContentBottom = btnTop;
                return;
            }

            // 竖屏：底部一条通栏，左边拖动、右边按钮。
            // 九键要摆 3 行，18% 的通栏只够每个键 33 像素，符号糊成一团；
            // 加高到 26%（相机取景按 ContentBottom 让位，不会压住方块）
            _stripH = Mathf.Clamp(h * (basic ? StripRatio : 0.26f), 1f, h * 0.5f);
            float top = h - _stripH;

            // 竖屏拖动区别占太宽：初级 0.40 时右边只剩 58%，四个键被压得比拖动框还小；
            // 高级 0.30 时十字键被挤到 30 像素，基本点不中。两边各让一步。
            float vPadW = (w - safeLeft - safeRight) * (basic ? 0.34f : 0.36f);
            _pad = new Rect(safeLeft + pad, top + pad,
                vPadW - pad * 1.5f, _stripH - pad * 2f - safeBottom);

            // 注意别和上面横屏分支的 btnLeft / btnW 重名，C# 会报 CS0136
            float keysLeft = _pad.xMax + pad;
            float keysW = w - safeRight - pad - keysLeft;

            // 四键 2x2、九键 3x3 都铺在通栏里，通栏高度已经按键位数调过
            LayoutGrid(new Rect(keysLeft, _pad.y, keysW, _pad.height), pad * 0.8f);
            LayoutDpad();
            ContentBottom = top;
        }

        /// <summary>把在用的槽位铺成 rows x cols 的网格。</summary>
        void LayoutGrid(Rect block, float gap)
        {
            float bw = (block.width - gap * (_cols - 1)) / _cols;
            float bh = (block.height - gap * (_rows - 1)) / _rows;
            // 圆钮直径（DrawSlot 里是 min(cell) * 0.94）：左侧按它对齐
            _btnDia = Mathf.Min(bw, bh) * 0.94f;

            for (int i = 0; i < GridSlots; i++)
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

        /// <summary>十字移动键摆在拖动区正中：四个圆钮，正中留空。</summary>
        void LayoutDpad()
        {
            if (GameSettings.MoveStyle != GameSettings.MoveDpad)
                return;

            bool wide = Screen.width > Screen.height;
            float m = Mathf.Min(_pad.width, _pad.height);

            // 横屏：和右边按键一样大（用户要求两边齐）；竖屏沿用原来那档，
            // 竖屏拖动区窄，直接用标准直径会顶出去。
            float side = wide ? _btnDia : m * 0.46f;
            float arm = wide ? side * 0.95f : m * 0.34f;

            if (wide && !CanDepth)
            {
                // 只有左右两颗（z = 1）：摆到左下角、并排，不再吊在左侧中间
                float gap = side * 0.30f;
                float y = _pad.yMax - side;
                _slot[DirLeft] = new Rect(_pad.x, y, side, side);
                _slot[DirRight] = new Rect(_pad.x + side + gap, y, side, side);
                return;
            }

            float cx = _pad.x + _pad.width * 0.5f;
            float cy = _pad.y + _pad.height * 0.5f;
            _slot[DirUp] = new Rect(cx - side * 0.5f, cy - arm - side * 0.5f, side, side);
            _slot[DirDown] = new Rect(cx - side * 0.5f, cy + arm - side * 0.5f, side, side);
            _slot[DirLeft] = new Rect(cx - arm - side * 0.5f, cy - side * 0.5f, side, side);
            _slot[DirRight] = new Rect(cx + arm - side * 0.5f, cy - side * 0.5f, side, side);
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
                _next[i] = Time.unscaledTime
                    + (IsRepeatAct(_act[i]) ? MoveFirstDelay : SoftRepeat);
                Fire(i);
                return;
            }

            // 选了「移动：十字键」就只认按钮，拖动不再移动方块（两套方式是分开的）
            if (GameSettings.MoveStyle == GameSettings.MoveSwipe && _pad.Contains(p))
            {
                _padFinger = finger;
                _padLast = p;
                _accX = 0f;
                _accY = 0f;
            }
        }

        void Drag(int finger, Vector2 p)
        {
            if (finger != _padFinger || GameSettings.MoveStyle != GameSettings.MoveSwipe)
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
                case Act.MoveL: _out.Left = true; break;
                case Act.MoveR: _out.Right = true; break;
                // 屏幕上「上」是把方块往远处推：InputState 里那是 Down（z 增大）
                case Act.MoveAway: _out.Down = true; break;
                case Act.MoveNear: _out.Up = true; break;
                case Act.Soft: _out.SoftDrop = true; break;
                case Act.Hard: _out.HardDrop = true; break;
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

            // 暂停时整条操作栏都不画：按钮收起来之后，那条底比背景亮一档，
            // 会在屏幕下方留下一块很显眼的矩形（竖屏截图里就是一条白白的大条）。
            // 只跳过绘制 —— 布局、相机让出的那一条都不动，否则取消暂停时场地会跳一下。
            if (!Paused)
            {
                Rect strip = new Rect(0f, Screen.height - _stripH, Screen.width, _stripH);
                Fill(strip, UiSkin.StripFill);
                Fill(new Rect(0f, strip.y, Screen.width, 1f), UiSkin.AccentSoft);

                DrawPad();

                for (int i = 0; i < Slots; i++)
                {
                    if (_enabled[i])
                        DrawSlot(i);
                }

                // 结算界面按键照样画着，只是压暗
                if (Locked)
                    Fill(strip, UiSkin.FreezeOverlay);
            }

            DrawPause();
        }

        void DrawPad()
        {
            // 十字键模式下这块只是「摆方向的空地」，画个框反而像一块没用的面板，
            // 直接不画；滑动模式才需要它提示「这儿能划」。
            if (GameSettings.MoveStyle == GameSettings.MoveDpad)
                return;

            bool active = _padFinger >= 0;
            Fill(_pad, Light
                ? (active ? new Color(0.74f, 0.86f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.45f))
                : (active ? new Color(0.12f, 0.17f, 0.24f, 0.55f) : new Color(0.09f, 0.11f, 0.15f, 0.5f)));
            Border(_pad, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, active ? 0.65f : 0.22f));

        }

        /// <summary>
        /// 圆形按键：一圈淡描边 + 实心圆底 + 中间的符号。
        /// 原来画的是方框，四角硬邦邦地压在通栏里，和整体风格不搭。
        /// </summary>
        void DrawSlot(int i)
        {
            bool down = _finger[i] >= 0;
            bool hot = _act[i] == Act.Hard;

            Rect cell = _slot[i];
            float d = Mathf.Min(cell.width, cell.height) * 0.94f;
            var box = new Rect(cell.center.x - d * 0.5f, cell.center.y - d * 0.5f, d, d);

            Color fill = down
                ? (Light ? new Color(0.72f, 0.85f, 1f, 0.98f) : new Color(0.22f, 0.50f, 0.70f, 0.95f))
                : (Light ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.12f, 0.15f, 0.20f, 0.92f));

            // 硬降是最高频的一下，给个暖色，一眼能找到
            if (hot)
            {
                fill = down
                    ? new Color(0.88f, 0.50f, 0.22f, 0.98f)
                    : (Light ? new Color(1f, 0.93f, 0.87f, 0.98f) : new Color(0.30f, 0.18f, 0.10f, 0.95f));
            }

            Color rim = hot
                ? new Color(1f, 0.60f, 0.30f, down ? 0.95f : 0.55f)
                : new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, down ? 0.95f : 0.34f);

            float rimW = Mathf.Max(1.6f, d * 0.040f);
            DrawDisc(box, rim);
            DrawDisc(new Rect(box.x + rimW, box.y + rimW, d - rimW * 2f, d - rimW * 2f), fill);

            Color ink = Light
                ? new Color(0.14f, 0.18f, 0.24f, down ? 0.95f : 0.82f)
                : new Color(0.88f, 0.94f, 1f, down ? 0.95f : 0.84f);
            DrawSymbol(box, _act[i], ink);
        }

        /// <summary>画一个圆（用带抗锯齿的圆盘贴图拉伸）。</summary>
        static void DrawDisc(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, DiscTexture);
            GUI.color = old;
        }

        // ── 符号 ──────────────────────────────────────────────────────────

        /// <summary>按键上一律画符号，不写文字。</summary>
        void DrawSymbol(Rect r, Act a, Color c)
        {
            switch (a)
            {
                // 平移用「带杆的箭头」（←→↑↓），和降落那些实心三角分得开
                case Act.MoveL:
                    DrawLineArrow(r, 180f, c);
                    break;
                case Act.MoveR:
                    DrawLineArrow(r, 0f, c);
                    break;
                case Act.MoveAway:
                    DrawLineArrow(r, -90f, c);
                    break;
                case Act.MoveNear:
                    DrawLineArrow(r, 90f, c);
                    break;

                case Act.Soft:
                    // 空心三角：硬降是实心双箭、平移是实心单箭，
                    // 软降再来一个实心单箭就分不清了（实测两个 ▼ 挨着摆没法认）
                    DrawHollow(r, c);
                    break;

                case Act.Hard:
                    DrawDown(r, 2, c);
                    break;

                // 面内的圈画成圆的
                case Act.InL:
                    DrawArc(r, false, 1f, 1f, c);
                    break;
                case Act.InR:
                    // 开口朝上看着别扭，整块逆时针转 90 度
                    DrawArc(r, true, 1f, 1f, c, -90f);
                    break;

                // 俯视 / 前后翻：图标按用户要求换成箭头，动作仍是绕轴旋转
                case Act.YawL:
                    DrawLineArrow(r, 180f, c);
                    break;
                case Act.YawR:
                    DrawLineArrow(r, 0f, c);
                    break;
                case Act.PitchUp:
                    DrawLineArrow(r, -90f, c);
                    break;
                case Act.PitchDown:
                    DrawLineArrow(r, 90f, c);
                    break;
            }
        }

        /// <summary>带杆的箭头（←→↑↓），0 度朝右。整块绕中心旋转到指定角度。</summary>
        void DrawLineArrow(Rect r, float deg, Color c)
        {
            float side = Mathf.Min(r.width, r.height) * 0.62f;
            var box = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);

            Matrix4x4 old = GUI.matrix;
            // 注意 GUI 坐标 y 朝下：正角度是顺时针，所以「朝上」得用 -90
            GUIUtility.RotateAroundPivot(deg, box.center);

            Color oc = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(box, LineArrowTexture);
            GUI.color = oc;
            GUI.matrix = old;
        }

        /// <summary>空心的向下三角（软降）。</summary>
        void DrawHollow(Rect r, Color c)
        {
            float side = Mathf.Min(r.width, r.height) * 0.50f;
            var box = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);

            Matrix4x4 old = GUI.matrix;
            GUIUtility.RotateAroundPivot(180f, box.center);

            Color oc = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(box, HollowTriangle);
            GUI.color = oc;
            GUI.matrix = old;
        }

        /// <summary>单个三角箭头：dir 0 上 / 1 下 / 2 左 / 3 右。</summary>
        void DrawArrow(Rect r, int dir, Color c)
        {
            float side = Mathf.Min(r.width, r.height) * 0.50f;
            var box = new Rect(r.center.x - side * 0.5f, r.center.y - side * 0.5f, side, side);

            Matrix4x4 old = GUI.matrix;
            // GUI 坐标 y 朝下，正角度是顺时针：左边那颗要指左，得逆时针转 90
            float deg = dir == 0 ? 0f : dir == 1 ? 180f : dir == 2 ? -90f : 90f;
            GUIUtility.RotateAroundPivot(deg, box.center);

            Color oc = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(box, ArrowTexture);
            GUI.color = oc;
            GUI.matrix = old;
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
        void DrawArc(Rect r, bool cw, float aspectX, float aspectY, Color c, float rotDeg = 0f)
        {
            float side = Mathf.Min(r.width, r.height) * 0.54f;
            var box = new Rect(r.center.x - side * aspectX * 0.5f,
                r.center.y - side * aspectY * 0.5f,
                side * aspectX, side * aspectY);

            Color oc = GUI.color;
            GUI.color = c;

            if (Mathf.Abs(rotDeg) > 0.01f)
            {
                // 绕按钮中心整体转一下：符号的开口朝向按视觉再调（用户指定）
                Matrix4x4 old = GUI.matrix;
                GUIUtility.RotateAroundPivot(rotDeg, r.center);
                GUI.DrawTexture(box, cw ? ArcCw : ArcCcw);
                GUI.matrix = old;
            }
            else
            {
                GUI.DrawTexture(box, cw ? ArcCw : ArcCcw);
            }
            GUI.color = oc;
        }

        // ── 程序生成的贴图 ────────────────────────────────────────────────
        //
        // 一律 64×64、每像素 3×3 超采样。原来用 32×32 逐像素硬判决，放大之后
        // 圆弧全是台阶，用户直接说「太丑」—— 抗锯齿是必须的。

        const int TexSize = 64;
        const int TesSs = 3;

        static Texture2D NewTex()
        {
            return new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        /// <summary>把「某点是否在形状里」的判定按 3×3 超采样铺成一张贴图。</summary>
        delegate bool ShapeTest(float x, float y);

        static Texture2D BuildShape(ShapeTest test)
        {
            Texture2D tex = NewTex();
            var px = new Color32[TexSize * TexSize];
            float step = 1f / TesSs;
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < TesSs; sy++)
                    {
                        for (int sx = 0; sx < TesSs; sx++)
                        {
                            if (test(x + (sx + 0.5f) * step, y + (sy + 0.5f) * step))
                                a += 1f / (TesSs * TesSs);
                        }
                    }
                    px[y * TexSize + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static Texture2D _discTex;

        /// <summary>实心圆，用来画圆形按键。</summary>
        static Texture2D DiscTexture
        {
            get
            {
                if (_discTex != null)
                    return _discTex;

                float c = (TexSize - 1) * 0.5f;
                float rMax = c - 0.6f;
                _discTex = BuildShape(delegate (float x, float y)
                {
                    float dx = x - c, dy = y - c;
                    return dx * dx + dy * dy <= rMax * rMax;
                });
                return _discTex;
            }
        }

        static Texture2D _arrowTex;

        /// <summary>朝上的实心三角。转 180 度就是向下的降落箭头。</summary>
        static Texture2D ArrowTexture
        {
            get
            {
                if (_arrowTex != null)
                    return _arrowTex;

                _arrowTex = BuildShape(delegate (float x, float y)
                {
                    // 注意 SetPixels 的 y = 0 是贴图**底部**，所以 v 越大越靠上
                    float u = x / TexSize - 0.5f;
                    float v = y / TexSize;
                    const float baseV = 0.12f, tipV = 0.90f;
                    if (v < baseV || v > tipV)
                        return false;
                    float t = (v - baseV) / (tipV - baseV);
                    return Mathf.Abs(u) <= 0.42f * (1f - t);
                });
                return _arrowTex;
            }
        }

        static Texture2D _lineArrowTex;

        /// <summary>带杆的箭头，朝右：一根横杆 + 末端三角头。</summary>
        static Texture2D LineArrowTexture
        {
            get
            {
                if (_lineArrowTex != null)
                    return _lineArrowTex;

                float cy = (TexSize - 1) * 0.5f;
                const float shaftFrom = 11f;
                const float shaftTo = 41f;
                const float tipX = 55f;
                const float halfShaft = 3.0f;
                const float headHalf = 10.0f;

                _lineArrowTex = BuildShape(delegate (float x, float y)
                {
                    if (x >= shaftFrom && x <= shaftTo && Mathf.Abs(y - cy) <= halfShaft)
                        return true;
                    return PointInTri(x, y,
                        tipX, cy,
                        shaftTo - 1f, cy - headHalf,
                        shaftTo - 1f, cy + headHalf);
                });
                return _lineArrowTex;
            }
        }

        static Texture2D _hollowTex;

        /// <summary>空心的向上三角：外三角减去缩小的内三角，剩下一条边。</summary>
        static Texture2D HollowTriangle
        {
            get
            {
                if (_hollowTex != null)
                    return _hollowTex;

                _hollowTex = BuildShape(delegate (float x, float y)
                {
                    return InTriUp(x, y, 1f) && !InTriUp(x, y, 0.66f);
                });
                return _hollowTex;
            }
        }

        /// <summary>朝上三角的判定；shrink 越小，三角越往里缩（用来挖空心）。</summary>
        static bool InTriUp(float x, float y, float shrink)
        {
            float u = (x / TexSize - 0.5f) / shrink;
            float v = (y / TexSize - 0.5f) / shrink + 0.5f;
            const float baseV = 0.12f, tipV = 0.90f;
            if (v < baseV || v > tipV)
                return false;
            float t = (v - baseV) / (tipV - baseV);
            return Mathf.Abs(u) <= 0.42f * (1f - t);
        }

        static Texture2D _arcCw, _arcCcw;

        static Texture2D ArcCw { get { return _arcCw != null ? _arcCw : (_arcCw = BuildArc(true)); } }
        static Texture2D ArcCcw { get { return _arcCcw != null ? _arcCcw : (_arcCcw = BuildArc(false)); } }

        /// <summary>圆弧 + 末端三角箭头，表示「转」。顺时针 / 逆时针各一张。</summary>
        static Texture2D BuildArc(bool cw)
        {
            float cx = (TexSize - 1) * 0.5f;
            float cy = (TexSize - 1) * 0.5f;
            float radius = 21.0f;
            float half = 3.9f;      // 弧线粗细的一半
            float from = 58f;       // 起点角度
            float sweep = 246f;     // 扫过的角度，剩下留给箭头

            // 箭头落在弧的终点，沿切线方向外指
            float endDeg = cw ? from - sweep : from + sweep;
            float er = endDeg * Mathf.Deg2Rad;
            float ex = cx + Mathf.Cos(er) * radius;
            float ey = cy + Mathf.Sin(er) * radius;
            float tDeg = cw ? endDeg - 90f : endDeg + 90f;
            float tr = tDeg * Mathf.Deg2Rad;
            float tx = Mathf.Cos(tr), ty = Mathf.Sin(tr);
            float nx = -ty, ny = tx;
            float tipX = ex + tx * 13.5f, tipY = ey + ty * 13.5f;
            float b1x = ex - tx * 4.5f + nx * 9.6f, b1y = ey - ty * 4.5f + ny * 9.6f;
            float b2x = ex - tx * 4.5f - nx * 9.6f, b2y = ey - ty * 4.5f - ny * 9.6f;

            return BuildShape(delegate (float x, float y)
            {
                float dx = x - cx, dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (Mathf.Abs(dist - radius) <= half)
                {
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float t = Mathf.Repeat(cw ? from - ang : ang - from, 360f);
                    if (t <= sweep)
                        return true;
                }
                return PointInTri(x, y, tipX, tipY, b1x, b1y, b2x, b2y);
            });
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
