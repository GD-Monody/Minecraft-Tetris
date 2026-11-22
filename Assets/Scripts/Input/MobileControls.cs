using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 手机上的操作层：左半屏拖动移动方块，右侧一组虚拟键做三轴旋转和降落。
    ///
    /// 为什么不直接摆 IMGUI 按钮：Unity 只把**第一根手指**当鼠标喂给 IMGUI，第二根手指
    /// 它根本收不到。而俄罗斯方块最常用的操作恰恰是「按住方向的同时点一下旋转」——
    /// 用 GUI.Button 做，这个组合是一按就哑。所以这里自己读 Input.touches：
    ///
    /// - 每个手指进来先认领一个角色（某个按键 / 拖动区），之后一直归它，直到抬起；
    /// - 按键判定和绘制都用同一份 Rect，画在哪儿就只能点哪儿；
    /// - 按键可以有多个同时按着，互不干扰（这是 IMGUI 做不到的那部分）。
    ///
    /// 拖动区用「位移攒够一格就走一步」而不是「按一次走一步」：手指划过去的距离
    /// 就是方块走的格数，划得越远走得越多，比连点跟手。步长取屏宽的 7%（约一格的
    /// 屏幕宽度），竖着略宽一点 —— 前后方向的透视压缩更大，太灵敏会一直误判。
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        // 按键顺序就是画出来的顺序：上面三行是旋转，最后一行是软降 / 硬降。
        // 左右按「左在左、右在右」摆：两组旋转都是左边逆、右边顺。
        // （第一版是「右 / 左」，按的人第一眼会愣一下，拇指也总先摸到左边那个。）
        const int RotL = 0;       // 面内逆时针
        const int RotR = 1;       // 面内顺时针
        const int YawL = 2;       // 俯视逆时针
        const int YawR = 3;       // 俯视顺时针
        const int PitchUp = 4;    // 往背面翻
        const int PitchDown = 5;  // 往正面翻
        const int SoftDropBtn = 6;
        const int HardDropBtn = 7;

        // 十字方向键：上 = 往远处推（z 变大），下 = 往近处拉。和拖动那条路一致。
        // 它们不参与右下角那套两列网格，位置由 LayoutDpad 单独算。
        const int DirUp = 8;
        const int DirDown = 9;
        const int DirLeft = 10;
        const int DirRight = 11;
        const int DirFirst = 8;
        const int BtnCount = 12;

        static readonly string[] Labels =
        {
            "面内 左", "面内 右",
            "俯视 左", "俯视 右",
            "上翻", "下翻",
            "软降", "硬降",
            "上", "下", "左", "右",
        };

        /// <summary>方向键按住后的连发：第一下立即生效，之后按这个间隔续。</summary>
        const float DirFirstDelay = 0.24f;
        const float DirRepeat = 0.10f;

        /// <summary>控制条占屏高的比例。相机取景按这个让出下面一块，见 Bootstrap.SetCameraRect。</summary>
        /// <summary>
        /// 竖屏底部操作条占屏高的比例。
        /// 0.34 → 0.18：原来四个按键铺满底部三分之一屏，手机上显得又大又笨，
        /// 压到一半之后场地多出不少高度，拇指也够得着（按钮仍然在屏幕最下沿）。
        /// </summary>
        public const float StripRatio = 0.18f;

        /// <summary>横屏时操作条占屏高的比例。横屏高度本来就少，再按 0.34 切会把场地压扁。</summary>
        public const float WideStripRatio = 0.23f;

        /// <summary>
        /// 拖动区攒够多远走一格（**拖动区自身**宽高的倍数，不是屏宽的）。
        /// 原来是屏宽的 7% / 8.5%，横屏拖动区窄、同样距离换来的格数就少，手感发涩。
        /// </summary>
        const float StepX = 0.18f;
        const float StepZ = 0.22f;

        /// <summary>软降按住后的连发间隔（秒）。松手就停。</summary>
        const float SoftRepeat = 0.055f;

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

        readonly Rect[] _btn = new Rect[BtnCount];
        /// <summary>十字键的四个矩形：0 上 / 1 下 / 2 左 / 3 右。</summary>
        readonly Rect[] _dirRect = new Rect[4];
        /// <summary>这一局画不画上下两个方向键（z = 1 时只有左右有意义）。</summary>
        bool _dirUpDown;
        readonly int[] _btnFinger = new int[BtnCount];
        readonly float[] _btnNext = new float[BtnCount];

        /// <summary>
        /// 这一局实际画出来的按键（索引指向上面那几个常量）。
        /// 场地只有一层（z = 1）时俯视旋转和前后翻都没有意义 —— 那两个方向转不过去，
        /// 按了也是白按，干脆只留面内左右加软降硬降四个，按钮还能做大一点。
        /// </summary>
        readonly int[] _vis = new int[BtnCount];
        int _visCount;
        int _lastSizeZ = -1;
        bool _lastDpad;
        Rect _pad;
        Rect _pauseRect;
        float _stripH;

        int _padFinger = -1;
        Vector2 _padLast;
        Vector2 _mousePrev;
        float _accX, _accY;

        GUIStyle _labelStyle;
        GUIStyle _dirStyle;
        int _styleSize = -1;
        int _lastW, _lastH;

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
            for (int i = 0; i < BtnCount; i++)
                _btnFinger[i] = -1;
            Layout();
        }

        void Update()
        {
            if (Screen.width != _lastW || Screen.height != _lastH || CurrentDepth != _lastSizeZ
                || DpadMode != _lastDpad)
                Layout();

            _out = default(InputState);

            if (!Visible)
            {
                ReleaseAll();
                return;
            }

            TickTouches();

            // 软降按住连发：按键本身只在按下那一帧发一次，之后靠这里续。
            // 暂停 / 已结束时按键照旧画着，但不续发。
            if (Locked)
                return;

            for (int i = 0; i < BtnCount; i++)
            {
                if (_btnFinger[i] < 0)
                    continue;
                float now = Time.unscaledTime;
                while (now >= _btnNext[i])
                {
                    if (i == SoftDropBtn)
                    {
                        _out.SoftDrop = true;
                        _btnNext[i] += SoftRepeat;
                    }
                    else if (i >= DirFirst)
                    {
                        // 按住方向键一直走：Tetris 里按住方向是常规操作，
                        // 只发一次的话一格一格点太累
                        Fire(i);
                        _btnNext[i] += DirRepeat;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // ── 布局 ──────────────────────────────────────────────────────────

        void Layout()
        {
            _lastW = Screen.width;
            _lastH = Screen.height;

            float w = Screen.width;
            float h = Screen.height;
            bool wide = w > h;

            // 安全区：刘海、挖孔、手势条都在这儿让开。竖屏只看上下，横屏再看左右。
            Rect safe = Screen.safeArea;
            float safeBottom = Mathf.Max(0f, safe.y);
            float safeLeft = Mathf.Max(0f, safe.x);
            float safeRight = Mathf.Max(0f, w - (safe.x + safe.width));

            float pad = Mathf.Max(6f, w * 0.016f);

            _lastDpad = DpadMode;
            PickVisibleButtons(CurrentDepth);

            // 暂停键贴右上角，和顶部横条同高同尺寸（Hud.MobilePauseSize 是唯一的尺寸来源，
            // 两边各算一套的话窗口一变就错位）
            float ps = Hud.MobilePauseSize;
            float m = UiSkin.P(wide ? 6f : 8f);
            float topInset = Screen.height - (safe.y + safe.height);
            _pauseRect = new Rect(w - safeRight - m - ps, topInset + m, ps, ps);

            if (wide)
            {
                // 横屏不要底部那条通栏了：横屏高度本来就紧张，一条通栏吃掉四分之一，
                // 场地被压成一条缝。改成左右分边 —— 左边滑动手势，右边旋转 + 降落。
                // 场地只有 10 格宽、按高度取景，两侧天然有空档，控件正好放进空档里。
                _stripH = 0f;

                float topY = Mathf.Max(topInset + ps + m, Hud.MobileBarHeight);
                float availH = Mathf.Max(1f, h - topY - safeBottom - pad);

                // 左：拖动区，占 28% 宽、可用高度的一半，贴在左下角。
                // 铺满整条左侧太占地方（画面里一大块空白），实际拇指够得到的也就下半截。
                float padW = w * 0.28f - pad;
                float padH = availH * 0.5f;
                _pad = new Rect(safeLeft + pad, topY + availH - padH, padW, padH);
                LayoutDpad();

                // 右：按键只占右下角 —— 铺满整条右边太大了（实测占了近三分之一屏），
                // 而且把右上角腾出来留给俯视图按钮和面板。
                float btnW = w * 0.24f - pad;
                float btnLeft = w - safeRight - pad - btnW;
                float btnTop = topY + availH * 0.40f;
                float btnAvailH = availH - availH * 0.40f;
                float gap = pad * 0.5f;
                const int cols = 2;
                int rows = Mathf.Max(1, (_visCount + cols - 1) / cols);
                float bw = (btnW - gap * (cols - 1)) / cols;
                float bh = (btnAvailH - gap * (rows - 1)) / rows;

                for (int k = 0; k < _visCount; k++)
                {
                    int row = k / cols;
                    int col = k % cols;
                    _btn[_vis[k]] = new Rect(btnLeft + col * (bw + gap), btnTop + row * (bh + gap), bw, bh);
                }
                ContentBottom = btnTop;
                return;
            }

            // 竖屏：底部一条通栏，左边拖动、右边按钮两列
            _stripH = Mathf.Clamp(h * StripRatio, 1f, h * 0.5f);
            float top = h - _stripH;

            float vPadW = (w - safeLeft - safeRight) * 0.42f;
            _pad = new Rect(safeLeft + pad, top + pad,
                vPadW - pad * 1.5f, _stripH - pad * 2f - safeBottom);
            LayoutDpad();

            float vBtnLeft = _pad.xMax + pad;
            float vBtnRight = w - safeRight - pad;
            float vBtnW = vBtnRight - vBtnLeft;

            float vGap = pad * 0.8f;
            float vBw = (vBtnW - vGap) * 0.5f;

            int vRows = Mathf.Max(1, (_visCount + 1) / 2);
            float vBh = (_pad.height - vGap * (vRows - 1)) / vRows;

            for (int k = 0; k < _visCount; k++)
            {
                int row = k / 2;
                int col = k % 2;
                _btn[_vis[k]] = new Rect(vBtnLeft + col * (vBw + vGap),
                    _pad.y + row * (vBh + vGap), vBw, vBh);
            }
            ContentBottom = top;
        }

        /// <summary>
        /// 十字键摆在拖动区正中：四根箭头贴着中心往外伸，四个角留空 ——
        /// 角上还能继续按住拖动，两种操作方式并存，不用二选一。
        /// </summary>
        void LayoutDpad()
        {
            float cx = _pad.x + _pad.width * 0.5f;
            float cy = _pad.y + _pad.height * 0.5f;
            // 十字模式下拖动区整块都给箭头，臂长/宽度都放大一档
            float arm = Mathf.Min(_pad.width, _pad.height) * (DpadMode ? 0.47f : 0.42f);
            float t = arm * (DpadMode ? 0.70f : 0.62f);   // 箭头宽度
            float len = Mathf.Max(8f, arm - t * 0.5f);

            _dirRect[0] = new Rect(cx - t * 0.5f, cy - arm, t, len);
            _dirRect[1] = new Rect(cx - t * 0.5f, cy + t * 0.5f, t, len);
            _dirRect[2] = new Rect(cx - arm, cy - t * 0.5f, len, t);
            _dirRect[3] = new Rect(cx + t * 0.5f, cy - t * 0.5f, len, t);
        }

        /// <summary>这一局用的是不是十字键（另一套是滑动）。两套二选一，不混用。</summary>
        static bool DpadMode
        {
            get { return GameSettings.MoveStyle == GameSettings.MoveDpad; }
        }

        /// <summary>场地深度：优先用 Bootstrap 传来的真实值，拿不到再看设置。</summary>
        int CurrentDepth
        {
            get { return Depth > 0 ? Depth : GameSettings.SizeZ; }
        }

        /// <summary>
        /// 按场地深度挑这一局要显示的按键。
        /// z = 1 时只有面内旋转有意义（俯视旋转绕竖直轴、前后翻绕水平轴，没有深度就转不过去）。
        /// </summary>
        void PickVisibleButtons(int sizeZ)
        {
            _lastSizeZ = sizeZ;
            _visCount = 0;
            _vis[_visCount++] = RotL;
            _vis[_visCount++] = RotR;
            if (sizeZ > 1)
            {
                _vis[_visCount++] = YawL;
                _vis[_visCount++] = YawR;
                // 「下翻」在左、「上翻」在右 —— 横竖屏统一成这个顺序，
                // 免得转个屏两个键就换了位置、手指按错。
                // 只是摆放顺序，两个键各自的动作没变（下标对应各自的输入位）。
                _vis[_visCount++] = PitchDown;
                _vis[_visCount++] = PitchUp;
            }
            _vis[_visCount++] = SoftDropBtn;
            _vis[_visCount++] = HardDropBtn;

            // 十字键：z = 1 时前后没有意义（转了也转不过去），只留左右
            _dirUpDown = sizeZ > 1;

            // 藏起来的按键要松开，不然它记着上一次的手指，结算时还在发指令
            for (int i = 0; i < BtnCount; i++)
            {
                if (!IsVisible(i))
                    _btnFinger[i] = -1;
            }
        }

        bool IsVisible(int i)
        {
            // 方向键不在 _vis 里（它们不走右下角那套网格），单独判。
            // 滑动模式下整组都不出现 —— 两套是二选一。
            if (i >= DirFirst)
            {
                if (!DpadMode)
                    return false;
                if (i == DirUp || i == DirDown)
                    return _dirUpDown;
                return true;
            }

            for (int k = 0; k < _visCount; k++)
                if (_vis[k] == i)
                    return true;
            return false;
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
        ///
        /// 留着这条是因为布局和手感要能在大屏幕上调 —— 每次改一个按钮间距都往手机上传包，
        /// 一轮下来光传输就够呛。真机上这一段不参与（touchCount 不为 0，或者根本不是 Forced）。
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
                // 暂停键按下即响应：等抬手指再响应的话，手指划出去就点不动了。
                // 结算界面上这颗键不响应 —— 再去暂停一局已经结束的游戏没有意义。
                if (!Over && OnPause != null)
                    OnPause();
                return;
            }

            if (Locked)
                return;   // 暂停 / 已结束：按键照旧画着（画面不重排），但不接输入

            for (int k = 0; k < _visCount; k++)
            {
                int i = _vis[k];
                if (_btnFinger[i] >= 0 || !_btn[i].Contains(p))
                    continue;
                _btnFinger[i] = finger;
                _btnNext[i] = Time.unscaledTime + (i == SoftDropBtn ? 0.16f : 0f);
                Fire(i);
                return;
            }

            // 十字键：先判它们，落在箭头上就不算拖动（滑动模式下这组不出现）
            for (int d = 0; d < 4; d++)
            {
                int i = DirFirst + d;
                if (_btnFinger[i] >= 0 || !IsVisible(i) || !_dirRect[d].Contains(p))
                    continue;
                _btnFinger[i] = finger;
                _btnNext[i] = Time.unscaledTime + DirFirstDelay;
                Fire(i);
                return;
            }

            if (!DpadMode && _pad.Contains(p))
            {
                _padFinger = finger;
                _padLast = p;
                _accX = 0f;
                _accY = 0f;
            }
        }

        void Drag(int finger, Vector2 p)
        {
            if (finger != _padFinger || DpadMode)
                return;

            float dx = p.x - _padLast.x;
            float dy = p.y - _padLast.y;   // GUI 坐标：往下是正，往上划 dy 为负
            _padLast = p;
            _accX += dx;
            // 取反之后「往上划 = 正数」。往上划是把方块往远处推：相机在 z 负半轴，
            // 远离镜头是 z 变大，对应的是 Down 那条指令（Up / Down 是按 z 减 / z 增定义的）。
            // 第一版按注释想当然写成「往上划 → Up」，结果推上去方块反而钻到镜头跟前。
            _accY -= dy;

            // 步长按**拖动区自身**的尺寸算，不按屏宽。
            // 按屏宽算的话：竖屏拖动区占 42% 宽，横屏只占 28%，同一个「屏宽 7%」
            // 在横屏上要走 1.5 倍的像素才动一格 —— 手感就是横屏特别涩、推不动。
            // 0.18 / 0.22 这两档正好等于原来竖屏的绝对像素（42% × 0.18 ≈ 屏宽 7.5%）。
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
            for (int i = 0; i < BtnCount; i++)
            {
                if (_btnFinger[i] == finger)
                    _btnFinger[i] = -1;
            }
        }

        void ReleaseAll()
        {
            _padFinger = -1;
            for (int i = 0; i < BtnCount; i++)
                _btnFinger[i] = -1;
        }

        void Fire(int i)
        {
            switch (i)
            {
                case RotL: _out.RotateCCW = true; break;
                case RotR: _out.RotateCW = true; break;
                case YawL: _out.RotateYawCCW = true; break;
                case YawR: _out.RotateYawCW = true; break;
                case PitchUp: _out.RotatePitchCW = true; break;
                case PitchDown: _out.RotatePitchCCW = true; break;
                case SoftDropBtn: _out.SoftDrop = true; break;
                case HardDropBtn: _out.HardDrop = true; break;
                // 注意语义：InputState 里 Up 是「往近处」（z 减小）、Down 是「往远处」（z 增大）。
                // 屏幕上「上」箭头应该把方块往远处推，所以对应的是 Down。
                case DirUp: _out.Down = true; break;
                case DirDown: _out.Up = true; break;
                case DirLeft: _out.Left = true; break;
                case DirRight: _out.Right = true; break;
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
            for (int i = 0; i < BtnCount; i++)
            {
                if (_btnFinger[i] == finger)
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

            // 操作条照旧画出来 —— 暂停时画面不该重排，只是压一层暗色表示这会儿按不动。
            // （暂停面板由 Hud 画在中间，这层暗色只盖住底部这一条。）
            Rect strip = new Rect(0f, Screen.height - _stripH, Screen.width, _stripH);
            Fill(strip, UiSkin.StripFill);
            Fill(new Rect(0f, strip.y, Screen.width, 1f), UiSkin.AccentSoft);

            DrawPad();
            DrawDpad();
            for (int k = 0; k < _visCount; k++)
                DrawButton(_vis[k]);

            if (Locked)
                Fill(strip, UiSkin.FreezeOverlay);

            DrawPause();
        }

        void DrawPad()
        {
            // 滑动模式：这块是拖动区，画个淡框提示「可以在这里划」
            // 十字模式：它是箭头的底板，也画
            bool active = _padFinger >= 0;
            Fill(_pad, Light
                ? (active ? new Color(0.74f, 0.86f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.45f))
                : (active ? new Color(0.12f, 0.17f, 0.24f, 0.55f) : new Color(0.09f, 0.11f, 0.15f, 0.5f)));
            Border(_pad, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, active ? 0.65f : 0.22f));

            // 中间那块现在摆着十字键，原来那两行说明字只能让位：
            // 拖动的提示缩成十字中心一个「拖」字（见 DrawDpad），
            // 方向键自己写着「上/下/左/右」，不用再解释。
        }

        /// <summary>十字键：比右下角那组按键更淡一点，它们是常驻的方向盘，不该抢眼。</summary>
        void DrawDpad()
        {
            for (int d = 0; d < 4; d++)
            {
                int i = DirFirst + d;
                if (!IsVisible(i))
                    continue;

                bool down = _btnFinger[i] >= 0;
                Fill(_dirRect[d], down
                    ? (Light ? new Color(0.62f, 0.80f, 1f, 0.95f) : new Color(0.18f, 0.44f, 0.62f, 0.9f))
                    : (Light ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.13f, 0.17f, 0.23f, 0.85f)));
                Border(_dirRect[d], new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, down ? 0.9f : 0.34f));
                GUI.Label(_dirRect[d], Labels[i], _dirStyle);
            }

            // 滑动模式下中心那块空着，写个「拖」提示这块区域能按住拖动；
            // 十字模式下整块都是箭头，不写。
            if (!DpadMode)
            {
                float cx = _pad.x + _pad.width * 0.5f;
                float cy = _pad.y + _pad.height * 0.5f;
                float t = _dirRect[0].width;
                GUI.Label(new Rect(cx - t * 0.5f, cy - t * 0.5f, t, t), "拖", _dirStyle);
            }
        }

        void DrawButton(int i)
        {
            bool down = _btnFinger[i] >= 0;
            Color fill = down
                ? (Light ? new Color(0.70f, 0.84f, 1f, 0.95f) : new Color(0.20f, 0.48f, 0.68f, 0.92f))
                : (Light ? new Color(1f, 1f, 1f, 0.86f) : new Color(0.11f, 0.13f, 0.18f, 0.88f));

            // 硬降是最高频的一下，给个暖色，一眼能找到
            if (i == HardDropBtn)
                fill = down ? new Color(0.85f, 0.45f, 0.18f, 0.95f)
                    : (Light ? new Color(1f, 0.92f, 0.86f, 0.95f) : new Color(0.28f, 0.16f, 0.09f, 0.9f));

            Color border = i == HardDropBtn
                ? new Color(1f, 0.62f, 0.32f, down ? 0.95f : 0.5f)
                : new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, down ? 0.9f : 0.28f);

            Fill(_btn[i], fill);
            Border(_btn[i], border);
            GUI.Label(_btn[i], Labels[i], _labelStyle);
        }

        void DrawPause()
        {
            Fill(_pauseRect, Paused
                ? (Light ? new Color(0.72f, 0.86f, 1f, 0.95f) : new Color(0.16f, 0.26f, 0.34f, 0.92f))
                : (Light ? new Color(1f, 1f, 1f, 0.88f) : new Color(0.11f, 0.13f, 0.18f, 0.8f)));
            Border(_pauseRect, new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, Paused ? 0.7f : 0.3f));
            // 暂停中这颗键就是「继续」——字不换的话，玩家会以为它点不动
            GUI.Label(_pauseRect, Paused ? "继续" : "暂停", _pauseStyle);
        }

        int _styleTheme = -1;
        bool _styleWide;
        GUIStyle _pauseStyle;

        void EnsureStyles()
        {
            // 字号同时受行高和按钮宽度约束：只按行高算的话，z = 1 时按钮变高、
            // 字号跟着涨，「面内 左」会被挤成两行（实测过）。
            float byHeight = _btn[0].height * 0.34f;
            float byWidth = _btn[0].width * 0.19f;
            int size = Mathf.RoundToInt(Mathf.Clamp(Mathf.Min(byHeight, byWidth), 14f, 40f));
            bool wide = Screen.width > Screen.height;
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
            _labelStyle.normal.textColor = Light
                ? new Color(0.10f, 0.12f, 0.16f, 0.95f)
                : new Color(0.88f, 0.94f, 1f, 0.95f);

            // 暂停键上的「暂停 / 继续」单独一档：它是方键，字太大就折成两行
            //（横屏 44 宽时「暂停」被竖着排成了两行，真机截图上很明显）
            _pauseStyle = new GUIStyle(_labelStyle);
            _pauseStyle.fontSize = Mathf.Max(10, Mathf.RoundToInt(Hud.MobilePauseSize * 0.34f));

            _dirStyle = new GUIStyle(_labelStyle);
            _dirStyle.fontSize = Mathf.Clamp(Mathf.RoundToInt(_dirRect[0].width * 0.48f), 10, 22);

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
