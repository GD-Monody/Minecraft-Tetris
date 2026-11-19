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
        const int RotCW = 0;      // 面内顺时针
        const int RotCCW = 1;     // 面内逆时针
        const int YawCW = 2;      // 俯视顺时针
        const int YawCCW = 3;
        const int PitchCW = 4;    // 往背面翻
        const int PitchCCW = 5;
        const int SoftDropBtn = 6;
        const int HardDropBtn = 7;
        const int BtnCount = 8;

        static readonly string[] Labels =
        {
            "面内 右", "面内 左",
            "俯视 右", "俯视 左",
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

        /// <summary>点了右上角的暂停键。</summary>
        public System.Action OnPause;

        readonly Rect[] _btn = new Rect[BtnCount];
        readonly int[] _btnFinger = new int[BtnCount];
        readonly float[] _btnNext = new float[BtnCount];
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
            if (Screen.width != _lastW || Screen.height != _lastH)
                Layout();

            _out = default(InputState);

            if (!Visible)
            {
                ReleaseAll();
                return;
            }

            TickTouches();

            // 软降按住连发：按键本身只在按下那一帧发一次，之后靠这里续
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
            float bh = (_pad.height - gap * 3f) * 0.25f;

            for (int i = 0; i < BtnCount; i++)
            {
                int row = i / 2;
                int col = i % 2;
                _btn[i] = new Rect(btnLeft + col * (bw + gap), _pad.y + row * (bh + gap), bw, bh);
            }

            // 暂停键放右上角，避开左上角那块分数面板（分数面板在 HUD 里画）
            float ps = Mathf.Clamp(w * 0.11f, 44f, 96f);
            _pauseRect = new Rect(w - safeRight - ps - pad * 0.5f, UiSkin.P(14f) + pad * 0.5f, ps, ps);
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
                // 暂停键按下即响应：等抬手指再响应的话，手指划出去就点不动了
                if (OnPause != null)
                    OnPause();
                return;
            }

            for (int i = 0; i < BtnCount; i++)
            {
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
            float dy = p.y - _padLast.y;   // GUI 坐标：往上是负
            _padLast = p;
            _accX += dx;
            _accY -= dy;                   // 往上划 = 往远处（z 变小）

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
                _out.Up = true;
                _accY -= stepZ;
            }
            while (_accY <= -stepZ)
            {
                _out.Down = true;
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
                case RotCW: _out.RotateCW = true; break;
                case RotCCW: _out.RotateCCW = true; break;
                case YawCW: _out.RotateYawCW = true; break;
                case YawCCW: _out.RotateYawCCW = true; break;
                case PitchCW: _out.RotatePitchCW = true; break;
                case PitchCCW: _out.RotatePitchCCW = true; break;
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

            // 控制条底色：把 3D 画面让出来的那块填实，不然相机 rect 缩小后
            // 下面会留一片没清过的旧像素
            Rect strip = new Rect(0f, Screen.height - _stripH, Screen.width, _stripH);
            Fill(strip, new Color(0.05f, 0.06f, 0.08f, 0.97f));
            Fill(new Rect(0f, strip.y, Screen.width, 1f), new Color(0.35f, 0.72f, 1f, 0.45f));

            DrawPad();
            for (int i = 0; i < BtnCount; i++)
                DrawButton(i);
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
            Fill(_pauseRect, new Color(0.11f, 0.13f, 0.18f, 0.8f));
            Border(_pauseRect, new Color(0.42f, 0.86f, 1f, 0.3f));
            GUI.Label(_pauseRect, "暂停", _hintStyle);
        }

        void EnsureStyles()
        {
            int size = Mathf.RoundToInt(Mathf.Clamp(_btn[0].height * 0.34f, 14f, 40f));
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
