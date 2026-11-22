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

        /// <summary>暂停面板里点了「设置」。</summary>
        public System.Action OnSettingsClick;

        /// <summary>设置面板盖在暂停画面上时，暂停面板先不画（免得两层按钮打架）。</summary>
        public bool HidePausePanel;
        public System.Action OnQuitToTitleClick;

        int _best;
        int _lastScore;
        float _scoreFlash;
        float _styleScale;

        /// <summary>最高分刷新过、但还没落盘。</summary>
        bool _bestDirty;
        float _bestSaveTimer;

        /// <summary>最高分最多多久落盘一次（秒）。</summary>
        const float BestSaveInterval = 2f;

        // 颜色都跟着 UiSkin.Theme 走，写属性而不是常量 —— 切主题时不用重启，
        // 下一帧重新取色就是新的（样式本身要重建，见 EnsureStyles）。
        static Color PanelFill { get { return UiSkin.PanelFill; } }
        static Color PanelBorder { get { return UiSkin.PanelBorder; } }
        // 绿色强调（分数跳动、消行提示）：亮色主题下换成更深的绿，浅底上才看得清
        static Color Accent
        {
            get { return UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.20f, 0.52f, 0.12f, 1f)
                : new Color(0.52f, 0.78f, 0.30f, 1f); }
        }
        static Color LabelColor { get { return UiSkin.TextSecondary; } }
        static Color ValueColor { get { return UiSkin.TextPrimary; } }
        static Color FlashColor
        {
            get { return UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.14f, 0.55f, 0.10f, 1f)
                : new Color(0.55f, 0.95f, 0.35f, 1f); }
        }
        // 文字投影：暗色主题压黑色，亮色主题用全透明（浅底上的深字不需要投影，
        // 硬压一层黑反而脏）
        static Color ShadowColor
        {
            get { return UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0f, 0f, 0f, 0f)
                : new Color(0f, 0f, 0f, 0.85f); }
        }

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
        GUIStyle _mobileSub;
        GUIStyle _mobileLabel;
        GUIStyle _mobileValue;
        bool _styleWide;
        GUIStyle _btnStyle;
        int _styleTheme = -1;
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
            // 手机横竖屏切换会换掉整体倍数，字号得跟着重建；桌面一直是 1.25，这里不会触发
            // 倍数或主题变了都要重建：字号和颜色都是拷进 GUIStyle 里的副本
            // 横竖屏切换时手机上的字号档位也跟着变（见下面的 wide），一并算进重建条件
            bool wide = Screen.width > Screen.height;
            if (_label != null && Mathf.Approximately(_styleScale, UiSkin.TextScale)
                && _styleTheme == (int)UiSkin.Current && _styleWide == wide)
                return;
            _styleScale = UiSkin.TextScale;
            _styleTheme = (int)UiSkin.Current;
            _styleWide = wide;

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

            // 左上角统计面板的三档字号：横屏面板矮（44dp），字也跟着小一档
            _mobileLabel = MakeStyle(wide ? 11 : 14, LabelColor);
            _mobileValue = MakeStyle(wide ? 20 : 25, ValueColor);
            _mobileSub = MakeStyle(wide ? 11 : 14, LabelColor);
            _mobileSub.alignment = TextAnchor.MiddleLeft;

            _btnNormal = SolidTexture(UiSkin.ButtonFill);
            _btnHover = SolidTexture(UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.86f, 0.92f, 1f, 0.95f)
                : new Color(0.20f, 0.24f, 0.30f, 0.98f));
            _btnActive = SolidTexture(UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.74f, 0.84f, 0.96f, 0.98f)
                : new Color(0.10f, 0.12f, 0.15f, 0.98f));

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = UiSkin.Size(18);
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.background = _btnNormal;
            _btnStyle.hover.background = _btnHover;
            _btnStyle.active.background = _btnActive;
            _btnStyle.normal.textColor = UiSkin.TextPrimary;
            _btnStyle.hover.textColor = UiSkin.TextPrimary;
            _btnStyle.active.textColor = UiSkin.TextSecondary;
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

            if (MobileMode.Enabled)
            {
                DrawMobileStats();
            }
            else
            {
                DrawScorePanel();
                DrawNextPanel();
            }

            if (Paused && !Loop.GameOver)
                DrawPausePanel();

            if (Loop.GameOver)
                DrawGameOverPanel();

            // 必须放在最后：光标拖尾要压在暂停 / 结束面板之上
            CursorTrail.DrawLast();
        }

        void DrawGameOverPanel()
        {
            GUI.color = UiSkin.FreezeOverlay;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float w = Mathf.Clamp(Screen.width * 0.34f, UiSkin.P(380f), UiSkin.P(560f));
            // 手机上 UiSkin.P(380) 可能比整个屏幕还宽（430 宽的屏算出来是 437）
            w = Mathf.Min(w, Screen.width - UiSkin.P(40f));
            float h = UiSkin.P(312f);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            DrawPanel(panel);

            Text(new Rect(panel.x, panel.y + UiSkin.P(22f), panel.width, UiSkin.P(58f)),
                "游戏结束", _gameOverTitle, _gameOverTitleShadow);
            // 手机上没键盘，提示「按 R」等于没说
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(90f), panel.width, UiSkin.P(28f)),
                MobileMode.Enabled ? "点下面的按钮重开" : "按 R 重开", _labelCenter);

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

        /// <summary>
        /// 手机顶部留出来的一条高度（含安全区）。
        ///
        /// 相机渲染区要按它往下让一块，否则刚生成的那几行会被面板压住 ——
        /// 方块正好从被挡住的地方下来，玩家看不见自己要接的是什么。
        ///
        /// 以前这块是「整条通栏」：横贯整个屏宽、只放几个数字，手机上一眼就是屏幕顶端
        /// 一大块黑板。现在只剩左上角一块统计面板 + 右上角的暂停键，留白按两者里高的那个算。
        /// </summary>
        public static float MobileBarHeight
        {
            get
            {
                Rect safe = Screen.safeArea;
                float topInset = Screen.height - (safe.y + safe.height);
                bool wide = MobileMode.Enabled && Screen.width > Screen.height;
                float m = UiSkin.P(wide ? 6f : 8f);
                return topInset + m + Mathf.Max(MobilePauseSize, MobileStatsHeight) + m;
            }
        }

        /// <summary>
        /// 左上角统计面板的高度：分数（大）+ 消除 / 最高（小）+ 右侧「下一个」预览。
        /// </summary>
        public static float MobileStatsHeight
        {
            // 38/46 → 44/54 → 50/62：手机上那行分数字号偏小，一眼看不清当前得分
            get { return UiSkin.P(MobileMode.Enabled && Screen.width > Screen.height ? 50f : 62f); }
        }

        /// <summary>
        /// 右上角暂停键的边长。Hud 算留白、MobileControls 摆位置都用它 ——
        /// 两边各算一套的话，窗口一变就会错位（之前暂停键比横条高出一截就是这么来的）。
        /// </summary>
        public static float MobilePauseSize
        {
            // 横屏矮一档：40 在 420 高的横屏里只占十分之一不到，看着太小，提到 46；
            // 竖屏 48 → 56，拇指按得准一些
            get { return UiSkin.P(MobileMode.Enabled && Screen.width > Screen.height ? 52f : 64f); }
        }

        /// <summary>
        /// 手机左上角的统计面板。
        ///
        /// 以前是横贯整屏的一条通栏（分数 / 消除 / 最高 / 下一个），在手机上一眼就是
        /// 屏幕顶端一块大黑板 —— 占着整条宽度却只放四个数字。现在收成一块矩形：
        /// 左上分数（大）+ 消除 / 最高（小），右侧「下一个」预览。
        /// </summary>
        void DrawMobileStats()
        {
            Rect safe = Screen.safeArea;
            float topInset = Screen.height - (safe.y + safe.height);
            float rightInset = Screen.width - (safe.x + safe.width);
            bool wide = Screen.width > Screen.height;
            float m = UiSkin.P(wide ? 6f : 8f);

            float h = MobileStatsHeight;
            // 宽度给到 184，但不能压住右上角的暂停键（窄屏上就按剩余宽度铺满）
            float w = Mathf.Min(UiSkin.P(214f),
                Screen.width - safe.x - rightInset - m * 2f - MobilePauseSize);
            var panel = new Rect(safe.x + m, topInset + m, w, h);
            DrawPanel(panel);
            // 不画那条绿色装饰线：桌面的大面板上它是点缀，这块小面板上正好压在
            // 「消除 / 最高」那行字底下，看着像给字划了条下划线

            // 右侧：下一个方块
            float pv = h - UiSkin.P(8f);
            var preview = new Rect(panel.xMax - pv - UiSkin.P(6f), panel.y + UiSkin.P(4f), pv, pv);
            if (Preview != null && Preview.Texture != null)
                GUI.DrawTexture(preview, Preview.Texture, ScaleMode.ScaleToFit, true);

            float x = panel.x + UiSkin.P(10f);
            float textW = Mathf.Max(UiSkin.P(40f), preview.x - UiSkin.P(6f) - x);

            // 两行：上行分数（大字），下行消除 / 最高（小字，左右各一半，
            // 挤成一行的话 420 宽的屏上「最高 5700」会直接折行 —— 上一版就是这毛病）
            float line1 = h * 0.56f;
            float line2 = h * 0.32f;
            float y1 = panel.y + (h - line1 - line2) * 0.5f;
            float y2 = y1 + line1;

            float labelW = UiSkin.P(wide ? 26f : 30f);
            GUI.Label(new Rect(x, y1 + line1 * 0.10f, labelW, line1 * 0.9f), "分数", _mobileLabel);
            GUI.Label(new Rect(x + labelW, y1 - line1 * 0.04f, textW - labelW, line1),
                Loop.Score.ToString(), _mobileValue);

            float half = textW * 0.5f;
            string unit = GameSettings.ElimMode == GameSettings.ElimLayer ? " 层" : " 行";
            GUI.Label(new Rect(x, y2, half, line2), "消除 " + Loop.LinesCleared + unit, _mobileSub);
            GUI.Label(new Rect(x + half, y2, half, line2), "最高 " + _best, _mobileSub);
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
            if (HidePausePanel)
                return;

            EnsureStyles();
            DrawPauseBlock(true);
        }

        /// <summary>
        /// 画「已暂停」那一块：暗底 + 面板 + 四颗按钮。
        ///
        /// interactive = false 时按钮只画不接点击。设置面板从暂停里打开时，
        /// 这块要垫在设置下面当背景（见 TitleScreen.PauseBackdrop）——
        /// 那段时间 HUD 是藏起来的，暂停画面得由 TitleScreen 代画一次，
        /// 否则设置滑出/滑入的这二百毫秒露出来的是亮着的场地，看着就是「闪一下」。
        /// </summary>
        public void DrawPauseBlock(bool interactive, bool withPanel = true)
        {
            GUI.color = UiSkin.FreezeOverlay;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // 只要暗底：设置面板开着的时候底下不该再露出「已暂停」那块板
            // （横屏设置只占右半屏，左边那半会漏出暂停面板的按钮，看着像没关掉）
            if (!withPanel)
                return;

            // 排版：一列 / 2x2 两种。
            // 只有**手机横屏**用 2x2 —— 那边高度只有四百来像素，四颗铺一列会顶出屏幕；
            // 电脑屏幕再宽也按一列排（用户要求：电脑就是竖着四颗，别分两列）。
            bool wide = MobileMode.Enabled && Screen.width > Screen.height;
            float designW = wide ? 460f : 320f;
            float designH = wide ? 214f : 326f;

            // 倍率先按「整块面板装得下」折减，字号、面板、按钮都跟同一个 scale 走，
            // 任何分辨率下面板都完整、比例一致。
            float scale = Mathf.Min(PauseScale,
                (Screen.height - UiSkin.P(40f)) / UiSkin.P(designH),
                Screen.width * (wide ? 0.62f : 0.56f) / UiSkin.P(designW));
            scale = Mathf.Max(scale, 0.5f);
            float Ps(float design) => UiSkin.P(design * scale);

            _pauseTitleBig.fontSize = UiSkin.Size(Mathf.RoundToInt(30f * scale));
            _pauseBtnBig.fontSize = UiSkin.Size(Mathf.RoundToInt(17f * scale));
            _pauseBtnBig.contentOffset = new Vector2(0f, UiSkin.P(3f * scale));

            float w = Ps(designW);
            float h = Ps(designH);
            var panel = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Ps(16f), w, Ps(40f)), "已暂停", _pauseTitleBig);

            // 按钮顺序横竖屏不一样：
            // 竖屏一列，用户要「设置」压在最底下；横屏 2x2，要「重新开始」在继续右边、
            // 「设置」在继续下面（也就是左下），所以两种顺序各自排。
            string[] labels = wide
                ? new[] { "继续游戏", "重新开始", "设置", "退出到主界面" }
                : new[] { "继续游戏", "重新开始", "退出到主界面", "设置" };
            System.Action[] acts = wide
                ? new[] { OnResumeClick, OnRestartClick, OnSettingsClick, OnQuitToTitleClick }
                : new[] { OnResumeClick, OnRestartClick, OnQuitToTitleClick, OnSettingsClick };

            float top = panel.y + Ps(66f);
            float gap = Ps(12f);

            // GUI.enabled = false 时按钮照样画（样式带自己的底图，不会变灰），
            // 只是不接点击 —— 正好用来当「垫在设置下面的那一层」
            bool wasEnabled = GUI.enabled;
            GUI.enabled = interactive;

            if (wide)
            {
                float sidePad = Ps(44f);
                float bw = (w - sidePad * 2f - gap) * 0.5f;
                float bh = Ps(46f);
                for (int i = 0; i < labels.Length; i++)
                {
                    int col = i % 2;
                    int row = i / 2;
                    var r = new Rect(panel.x + sidePad + col * (bw + gap), top + row * (bh + gap), bw, bh);
                    if (GUI.Button(r, labels[i], _pauseBtnBig) && acts[i] != null)
                        acts[i]();
                }
            }
            else
            {
                float bw = w - Ps(56f);
                float bh = Ps(48f);
                for (int i = 0; i < labels.Length; i++)
                {
                    var r = new Rect(panel.x + Ps(28f), top + i * (bh + gap), bw, bh);
                    if (GUI.Button(r, labels[i], _pauseBtnBig) && acts[i] != null)
                        acts[i]();
                }
            }

            GUI.enabled = wasEnabled;
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
            GUI.color = UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.20f, 0.30f, 0.45f, 0.10f)
                : new Color(0f, 0f, 0f, 0.35f);
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
