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
        const int BtnCount = 8;

        static readonly string[] Labels =
        {
            "面内 左", "面内 右",
            "俯视 左", "俯视 右",
            "上翻", "下翻",
            "软降", "硬降",
        };

        /// <summary>控制条占屏高的比例。相机取景按这个让出下面一块，见 Bootstrap.SetCameraRect。</summary>
        public const float StripRatio = 0.34f;

        /// <summary>拖动区攒够多远走一格（屏宽的倍数）。</summary>
        const float StepX = 0.070f;
        const float StepZ = 0.085f;

        /// <summary>软降按住后的连发间隔（秒）。松手就停。</summary>
        const float SoftRepeat = 0.055f;

        public bool Visible = true;

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
        Rect _pad;
        Rect _pauseRect;
        float _stripH;

        int _padFinger = -1;
        Vector2 _padLast;
        Vector2 _mousePrev;
        float _accX, _accY;

        GUIStyle _labelStyle;
        GUIStyle _hintStyle;
        int _styleSize = -1;
        int _lastW, _lastH;

        InputState _out;

        /// <summary>控制条的高度（像素）。相机用它算可视区。</summary>
        public float StripHeight { get { return _stripH; } }

        void Awake()
        {
            for (int i = 0; i < BtnCount; i++)
                _btnFinger[i] = -1;
            Layout();
        }

        void Update()
        {
            if (Screen.width != _lastW || Screen.height != _lastH || CurrentDepth != _lastSizeZ)
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
                if (_btnFinger[i] < 0 || i != SoftDropBtn)
                    continue;
                float now = Time.unscaledTime;
                while (now >= _btnNext[i])
                {
                    _out.SoftDrop = true;
                    _btnNext[i] += SoftRepeat;
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
            _stripH = Mathf.Clamp(h * StripRatio, 1f, h * 0.5f);

            // 安全区：刘海、挖孔、手势条都在这儿让开。竖屏只看上下，横屏再看左右。
            Rect safe = Screen.safeArea;
            float safeBottom = Mathf.Max(0f, safe.y);
            float safeLeft = Mathf.Max(0f, safe.x);
            float safeRight = Mathf.Max(0f, w - (safe.x + safe.width));

            float top = h - _stripH;
            float pad = Mathf.Max(6f, w * 0.016f);

            // 左边拖动区占 42%，右边按钮区占剩下的
            float padW = (w - safeLeft - safeRight) * 0.42f;
            _pad = new Rect(safeLeft + pad, top + pad,
                padW - pad * 1.5f, _stripH - pad * 2f - safeBottom);

            float btnLeft = _pad.xMax + pad;
            float btnRight = w - safeRight - pad;
            float btnW = btnRight - btnLeft;

            float gap = pad * 0.8f;
            float bw = (btnW - gap) * 0.5f;

            PickVisibleButtons(CurrentDepth);
            int rows = Mathf.Max(1, (_visCount + 1) / 2);
            float bh = (_pad.height - gap * (rows - 1)) / rows;

            for (int k = 0; k < _visCount; k++)
            {
                int row = k / 2;
                int col = k % 2;
                _btn[_vis[k]] = new Rect(btnLeft + col * (bw + gap), _pad.y + row * (bh + gap), bw, bh);
            }

            // 暂停键贴右上角，和顶部横条同高同尺寸（Hud.MobilePauseSize 是唯一的尺寸来源，
            // 两边各算一套的话窗口一变就错位）
            float ps = Hud.MobilePauseSize;
            float m = UiSkin.P(10f);
            float topInset = Screen.height - (safe.y + safe.height);
            _pauseRect = new Rect(w - safeRight - m - ps, topInset + m, ps, ps);
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
                _vis[_visCount++] = PitchUp;
                _vis[_visCount++] = PitchDown;
            }
            _vis[_visCount++] = SoftDropBtn;
            _vis[_visCount++] = HardDropBtn;

            // 藏起来的按键要松开，不然它记着上一次的手指，结算时还在发指令
            for (int i = 0; i < BtnCount; i++)
            {
                if (!IsVisible(i))
                    _btnFinger[i] = -1;
            }
        }

        bool IsVisible(int i)
        {
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
            // 第一版按注释想当然写成「往上划 → Up」，结果推上去方块反而钻到镜头跟前。
            _accY -= dy;

            float stepX = Screen.width * StepX;
            float stepZ = Screen.width * StepZ;

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
            Fill(strip, new Color(0.05f, 0.06f, 0.08f, 0.97f));
            Fill(new Rect(0f, strip.y, Screen.width, 1f), new Color(0.35f, 0.72f, 1f, 0.45f));

            DrawPad();
            for (int k = 0; k < _visCount; k++)
                DrawButton(_vis[k]);

            if (Locked)
                Fill(strip, new Color(0f, 0f, 0f, 0.55f));

            DrawPause();
        }

        void DrawPad()
        {
            bool active = _padFinger >= 0;
            Fill(_pad, active ? new Color(0.12f, 0.17f, 0.24f, 0.55f) : new Color(0.09f, 0.11f, 0.15f, 0.5f));
            Border(_pad, new Color(0.42f, 0.86f, 1f, active ? 0.65f : 0.22f));

            GUI.Label(new Rect(_pad.x, _pad.y + _pad.height * 0.36f, _pad.width, _pad.height * 0.16f),
                "拖动 移动方块", _hintStyle);
            GUI.Label(new Rect(_pad.x, _pad.y + _pad.height * 0.52f, _pad.width, _pad.height * 0.14f),
                "左右 / 前后", _hintStyle);
        }

        void DrawButton(int i)
        {
            bool down = _btnFinger[i] >= 0;
            Color fill = down
                ? new Color(0.20f, 0.48f, 0.68f, 0.92f)
                : new Color(0.11f, 0.13f, 0.18f, 0.88f);

            // 硬降是最高频的一下，给个暖色，一眼能找到
            if (i == HardDropBtn)
                fill = down ? new Color(0.85f, 0.45f, 0.18f, 0.95f) : new Color(0.28f, 0.16f, 0.09f, 0.9f);

            Color border = i == HardDropBtn
                ? new Color(1f, 0.62f, 0.32f, down ? 0.95f : 0.5f)
                : new Color(0.42f, 0.86f, 1f, down ? 0.9f : 0.28f);

            Fill(_btn[i], fill);
            Border(_btn[i], border);
            GUI.Label(_btn[i], Labels[i], _labelStyle);
        }

        void DrawPause()
        {
            Fill(_pauseRect, Paused ? new Color(0.16f, 0.26f, 0.34f, 0.92f)
                                    : new Color(0.11f, 0.13f, 0.18f, 0.8f));
            Border(_pauseRect, new Color(0.42f, 0.86f, 1f, Paused ? 0.7f : 0.3f));
            // 暂停中这颗键就是「继续」——字不换的话，玩家会以为它点不动
            GUI.Label(_pauseRect, Paused ? "继续" : "暂停", _hintStyle);
        }

        void EnsureStyles()
        {
            // 字号同时受行高和按钮宽度约束：只按行高算的话，z = 1 时按钮变高、
            // 字号跟着涨，「面内 左」会被挤成两行（实测过）。
            float byHeight = _btn[0].height * 0.34f;
            float byWidth = _btn[0].width * 0.19f;
            int size = Mathf.RoundToInt(Mathf.Clamp(Mathf.Min(byHeight, byWidth), 14f, 40f));
            if (_labelStyle != null && size == _styleSize)
                return;

            _styleSize = size;
            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.font = UiSkin.Font;
            _labelStyle.fontSize = size;
            _labelStyle.alignment = TextAnchor.MiddleCenter;
            _labelStyle.normal.textColor = new Color(0.88f, 0.94f, 1f, 0.95f);

            _hintStyle = new GUIStyle(_labelStyle);
            _hintStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(_pad.height * 0.075f, 12f, 28f));
            _hintStyle.normal.textColor = new Color(0.55f, 0.72f, 0.86f, 0.75f);
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
