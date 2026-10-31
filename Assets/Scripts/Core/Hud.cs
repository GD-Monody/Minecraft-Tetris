using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// HUD：左上分数面板 + 「下一个」3D 预览面板 + 暂停 / 结束界面。
    /// 面板是 MC 风格的深色半透明方块 + 一条细边框。
    /// </summary>
    public class Hud : MonoBehaviour
    {
        public GameLoop Loop;
        public NextPreview Preview;
        public bool Paused;
        public bool Hidden;
        public System.Action OnRestartClick;
        public System.Action OnResumeClick;
        public System.Action OnQuitToTitleClick;

        int _best;
        int _lastScore;
        float _scoreFlash;

        /// <summary>最高分刷新过、但还没落盘。</summary>
        bool _bestDirty;
        float _bestSaveTimer;

        /// <summary>最高分最多多久落盘一次（秒）。</summary>
        const float BestSaveInterval = 2f;

        static readonly Color PanelFill = new Color(0.10f, 0.11f, 0.15f, 0.90f);
        static readonly Color PanelBorder = new Color(0.35f, 0.37f, 0.44f, 0.95f);
        static readonly Color Accent = new Color(0.52f, 0.78f, 0.30f, 1f);
        static readonly Color LabelColor = new Color(0.66f, 0.70f, 0.76f, 1f);
        static readonly Color ValueColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color FlashColor = new Color(0.55f, 0.95f, 0.35f, 1f);
        static readonly Color ShadowColor = new Color(0f, 0f, 0f, 0.85f);

        GUIStyle _label;
        GUIStyle _labelShadow;
        GUIStyle _value;
        GUIStyle _valueShadow;
        GUIStyle _valueFlash;
        GUIStyle _valueSmall;
        GUIStyle _valueSmallShadow;
        GUIStyle _center;
        GUIStyle _centerShadow;
        GUIStyle _pauseTitleBig;
        GUIStyle _pauseBtnBig;
        GUIStyle _gameOverTitle;
        GUIStyle _gameOverTitleShadow;
        GUIStyle _labelCenter;
        GUIStyle _btnStyle;
        Texture2D _btnNormal;
        Texture2D _btnHover;
        Texture2D _btnActive;

        void Awake()
        {
            _best = PlayerPrefs.GetInt("best_score", 0);
            _lastScore = 0;
        }

        void Update()
        {
            if (Loop != null && Loop.Score != _lastScore)
            {
                if (Loop.Score > _lastScore)
                    _scoreFlash = 0.4f;
                _lastScore = Loop.Score;
            }

            if (_scoreFlash > 0f)
                _scoreFlash -= Time.deltaTime;

            // 最高分放在这里写，而不是 OnGUI 里（以前就是写在 OnGUI 里的）：
            // OnGUI 不是「一帧一次」，鼠标动一下就再来一趟，而 PlayerPrefs.Save() 是同步
            // 落盘，一趟几毫秒被放大到十几趟，正好撞在拖窗口那几秒上。这里一帧最多一次，
            // 而且攒够间隔才真写一次盘 —— 分数是一路往上涨的，每帧都写没有意义。
            if (Loop != null && Loop.Score > _best)
            {
                _best = Loop.Score;
                _bestDirty = true;
            }

            if (_bestDirty)
            {
                _bestSaveTimer -= Time.unscaledDeltaTime;
                if (_bestSaveTimer <= 0f)
                    SaveBest();
            }
        }

        void SaveBest()
        {
            if (!_bestDirty)
                return;

            _bestDirty = false;
            _bestSaveTimer = BestSaveInterval;
            PlayerPrefs.SetInt("best_score", _best);
            PlayerPrefs.Save();
        }

        /// <summary>退出前补一次：攒着的那次还没到间隔，别把最后的记录丢了。</summary>
        void OnApplicationQuit()
        {
            SaveBest();
        }

        void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = MakeStyle(15, LabelColor);
            _labelShadow = MakeStyle(15, ShadowColor);
            _value = MakeStyle(30, ValueColor);
            _valueShadow = MakeStyle(30, ShadowColor);
            // 分数跳动时用的那一档：只改它的文字颜色，不每帧 new 一个样式。
            _valueFlash = MakeStyle(30, FlashColor);
            _valueSmall = MakeStyle(18, ValueColor);
            _valueSmallShadow = MakeStyle(18, ShadowColor);
            _center = MakeStyle(26, ValueColor);
            _centerShadow = MakeStyle(26, ShadowColor);

            _gameOverTitle = MakeStyle(40, ValueColor);
            _gameOverTitle.alignment = TextAnchor.MiddleCenter;
            _gameOverTitle.fontStyle = FontStyle.Bold;
            _gameOverTitleShadow = MakeStyle(40, ShadowColor);
            _gameOverTitleShadow.alignment = TextAnchor.MiddleCenter;
            _gameOverTitleShadow.fontStyle = FontStyle.Bold;
            _labelCenter = MakeStyle(16, LabelColor);
            _labelCenter.alignment = TextAnchor.MiddleCenter;

            _btnNormal = SolidTexture(new Color(0.13f, 0.15f, 0.19f, 0.96f));
            _btnHover = SolidTexture(new Color(0.20f, 0.24f, 0.30f, 0.98f));
            _btnActive = SolidTexture(new Color(0.10f, 0.12f, 0.15f, 0.98f));

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = UiSkin.Size(18);
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.background = _btnNormal;
            _btnStyle.hover.background = _btnHover;
            _btnStyle.active.background = _btnActive;
            _btnStyle.normal.textColor = Color.white;
            _btnStyle.hover.textColor = Color.white;
            _btnStyle.active.textColor = new Color(0.8f, 0.85f, 0.9f, 1f);
            _btnStyle.border = new RectOffset(0, 0, 0, 0);
            // 不显式清掉 padding / contentOffset 的话，文字会贴着按钮上半部（继承 GUI.skin.button 的默认值）
            _btnStyle.padding = new RectOffset(0, 0, 0, 0);
            _btnStyle.contentOffset = new Vector2(0f, UiSkin.P(3f));

            // 暂停面板专用样式。字号在这里不定死：DrawPausePanel 每帧按实际
            // 缩放系数设置，屏幕装不下两倍时会自动折减，字号跟着面板走。
            _pauseTitleBig = MakeStyle(34, ValueColor);
            _pauseTitleBig.alignment = TextAnchor.MiddleCenter;
            // 拷贝构造会把按钮底图、文字色、padding 这些一并带上。
            _pauseBtnBig = new GUIStyle(_btnStyle);
        }

        static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        GUIStyle MakeStyle(int size, Color color)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = UiSkin.Size(size);
            s.normal.textColor = color;
            s.alignment = TextAnchor.UpperLeft;
            return s;
        }

        /// <summary>先画一层偏移的黑色阴影，再画正体。</summary>
        void Text(Rect r, string text, GUIStyle main, GUIStyle shadow)
        {
            GUI.Label(new Rect(r.x + 1f, r.y + 2f, r.width, r.height), text, shadow);
            GUI.Label(r, text, main);
        }

        void OnGUI()
        {
            if (Hidden)
                return;

            // 鼠标每动一下 IMGUI 就再跑一趟，拖窗口时一帧能跑十几趟，
            // 界面画的是绝对位置、结果一模一样，这种白跑的趟数直接挡掉。
            if (!UiSkin.WorthDrawing())
                return;

            UiSkin.Apply();

            EnsureStyles();

            DrawScorePanel();
            DrawNextPanel();

            if (Paused && !Loop.GameOver)
                DrawPausePanel();

            if (Loop.GameOver)
                DrawGameOverPanel();

            // 必须放在最后：光标拖尾要压在暂停 / 结束面板之上
            CursorTrail.DrawLast();
        }

        void DrawGameOverPanel()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Clamp(Screen.width * 0.34f, UiSkin.P(380f), UiSkin.P(560f));
            float h = UiSkin.P(312f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            DrawPanel(panel);

            Text(new Rect(panel.x, panel.y + UiSkin.P(22f), panel.width, UiSkin.P(58f)),
                "游戏结束", _gameOverTitle, _gameOverTitleShadow);
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(90f), panel.width, UiSkin.P(28f)), "按 R 重开", _labelCenter);

            float bw = Mathf.Min(w - UiSkin.P(100f), UiSkin.P(220f));
            var btn = new Rect(panel.x + (panel.width - bw) * 0.5f, panel.y + UiSkin.P(150f), bw, UiSkin.P(46f));
            if (GUI.Button(btn, "再来一局", _btnStyle))
            {
                if (OnRestartClick != null)
                    OnRestartClick();
            }
            var btn2 = new Rect(panel.x + (panel.width - bw) * 0.5f, panel.y + UiSkin.P(212f), bw, UiSkin.P(46f));
            if (GUI.Button(btn2, "退出到主界面", _btnStyle))
            {
                if (OnQuitToTitleClick != null)
                    OnQuitToTitleClick();
            }
        }

        void DrawScorePanel()
        {
            var panel = new Rect(UiSkin.P(14f), UiSkin.P(14f), UiSkin.P(246f), UiSkin.P(158f));
            DrawPanel(panel);

            float x = panel.x + UiSkin.P(16f);
            float y = panel.y + UiSkin.P(18f);

            Text(new Rect(x, y, UiSkin.P(80f), UiSkin.P(30f)), "分数", _label, _labelShadow);

            float scale = 1f;
            if (_scoreFlash > 0f)
                scale = 1f + _scoreFlash * 0.4f;
            Rect valueRect = new Rect(x + UiSkin.P(46f), y - UiSkin.P(6f), UiSkin.P(200f), UiSkin.P(40f));
            GUI.matrix = Matrix4x4.TRS(new Vector3(valueRect.x, valueRect.y, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            // 以前这里每帧 new GUIStyle(30, 渐变的颜色)：一帧一个垃圾，拖窗口时被放大到
            // 十几个。改成只改已有样式的文字颜色，样式本身建一次就够。
            GUIStyle scoreStyle = _value;
            if (_scoreFlash > 0f)
            {
                _valueFlash.normal.textColor = Color.Lerp(ValueColor, FlashColor, _scoreFlash / 0.4f);
                scoreStyle = _valueFlash;
            }
            GUI.Label(new Rect(1f, 2f, UiSkin.P(200f), UiSkin.P(40f)), Loop.Score.ToString(), _valueShadow);
            GUI.Label(new Rect(0f, 0f, UiSkin.P(200f), UiSkin.P(40f)), Loop.Score.ToString(), scoreStyle);
            GUI.matrix = Matrix4x4.identity;

            y += UiSkin.P(46f);
            Text(new Rect(x, y, UiSkin.P(80f), UiSkin.P(30f)), "消除", _label, _labelShadow);
            // 层消模式下 LinesCleared 数的是层，单位跟着变一下，不然「12 行」其实是 12 层
            string unit = GameSettings.ElimMode == GameSettings.ElimLayer ? " 层" : " 行";
            Text(new Rect(x + UiSkin.P(46f), y - UiSkin.P(3f), UiSkin.P(200f), UiSkin.P(26f)),
                Loop.LinesCleared + unit, _valueSmall, _valueSmallShadow);

            y += UiSkin.P(34f);
            Text(new Rect(x, y, UiSkin.P(80f), UiSkin.P(30f)), "最高", _label, _labelShadow);
            Text(new Rect(x + UiSkin.P(46f), y - UiSkin.P(3f), UiSkin.P(200f), UiSkin.P(26f)),
                _best.ToString(), _valueSmall, _valueSmallShadow);

            DrawAccentLine(panel);
        }

        void DrawNextPanel()
        {
            var panel = new Rect(UiSkin.P(14f), UiSkin.P(180f), UiSkin.P(246f), UiSkin.P(184f));
            DrawPanel(panel);

            Text(new Rect(panel.x + UiSkin.P(16f), panel.y + UiSkin.P(16f), UiSkin.P(200f), UiSkin.P(30f)),
                "下一个", _label, _labelShadow);

            var previewRect = new Rect(panel.x + UiSkin.P(14f), panel.y + UiSkin.P(40f),
                UiSkin.P(112f), UiSkin.P(112f));
            if (Preview != null && Preview.Texture != null)
                GUI.DrawTexture(previewRect, Preview.Texture, ScaleMode.ScaleToFit, true);

            Text(new Rect(panel.x + UiSkin.P(134f), panel.y + UiSkin.P(84f), UiSkin.P(106f), UiSkin.P(44f)),
                Loop.Next != null ? Loop.Next.Label : "", _valueSmall, _valueSmallShadow);

            DrawAccentLine(panel);
        }

        /// <summary>暂停面板的目标放大倍数。屏幕装不下时按屏高自动折减，见 DrawPausePanel。</summary>
        const float PauseScale = 2f;

        void DrawPausePanel()
        {
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 目标放大 PauseScale 倍；窗口高度装不下就折减倍数。字号、面板、按钮
            // 全部跟同一个 scale 走，任何分辨率下面板都完整、比例都一致。
            // 上一版踩的坑：面板高按 ×1 的设计值传了 Ps(262)，×2 的内容塞进一半高的
            // 面板，按钮整排跑出去；根因是布局值和容器尺寸用了两套基准，现在统一。
            float scale = Mathf.Min(PauseScale, (Screen.height - UiSkin.P(40f)) / UiSkin.P(262f));
            float Ps(float design) => UiSkin.P(design * scale);

            _pauseTitleBig.fontSize = UiSkin.Size(Mathf.RoundToInt(34f * scale));
            _pauseBtnBig.fontSize = UiSkin.Size(Mathf.RoundToInt(18f * scale));
            _pauseBtnBig.contentOffset = new Vector2(0f, UiSkin.P(3f * scale));

            float w = Mathf.Clamp(Screen.width * 0.48f, Ps(300f), Ps(440f));
            w = Mathf.Min(w, Screen.width - UiSkin.P(40f));    // 宽度同样保底，小窗不出屏
            float h = Ps(262f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Ps(16f), panel.width, Ps(42f)), "已暂停", _pauseTitleBig);

            float bw = panel.width - Ps(160f);
            float bx = panel.x + (panel.width - bw) * 0.5f;
            float by = panel.y + Ps(74f);

            if (GUI.Button(new Rect(bx, by, bw, Ps(40f)), "继续游戏", _pauseBtnBig))
            {
                if (OnResumeClick != null)
                    OnResumeClick();
            }
            by += Ps(52f);
            if (GUI.Button(new Rect(bx, by, bw, Ps(40f)), "重新开始", _pauseBtnBig))
            {
                if (OnRestartClick != null)
                    OnRestartClick();
            }
            by += Ps(52f);
            if (GUI.Button(new Rect(bx, by, bw, Ps(40f)), "退出到主界面", _pauseBtnBig))
            {
                if (OnQuitToTitleClick != null)
                    OnQuitToTitleClick();
            }
        }

        void DrawCenterText(string text)
        {
            GUI.Label(new Rect(Screen.width / 2f - UiSkin.P(200f), Screen.height / 2f - UiSkin.P(40f),
                UiSkin.P(400f), UiSkin.P(40f)), text, _centerShadow);
            GUI.Label(new Rect(Screen.width / 2f - UiSkin.P(200f), Screen.height / 2f - UiSkin.P(42f),
                UiSkin.P(400f), UiSkin.P(40f)), text, _center);
        }

        static string TypeLabel(PieceType t)
        {
            switch (t)
            {
                case PieceType.I: return "I 长条";
                case PieceType.O: return "O 方块";
                case PieceType.T: return "T 形";
                case PieceType.S: return "S 形";
                case PieceType.Z: return "Z 形";
                case PieceType.J: return "J 形";
                case PieceType.L: return "L 形";
                default: return t.ToString();
            }
        }

        /// <summary>MC 风格的深色半透明面板 + 细边框。俯视图那块也用同一套，别各画一份。</summary>
        public static void DrawPanel(Rect r)
        {
            // 面板下方稍微加一点深色投影，让面板浮在场景上面
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 3f, r.width, r.height), Texture2D.whiteTexture);

            GUI.color = PanelFill;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = PanelBorder;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 1f, r.width, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        public static void DrawAccentLine(Rect panel)
        {
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(panel.x + 1f, panel.yMax - 3f, panel.width * 0.42f, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
