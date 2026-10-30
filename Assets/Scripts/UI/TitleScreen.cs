using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 开始界面：先提示「Made with Unity」，标题逐字从上方落下，署名从透明渐显，
    /// 最后淡入选项按钮和「AGENTS辅助完成」。设置面板（音效 / 按键 / 场地尺寸）内嵌在这里。
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Serializable]
        class CharMeta { public int x; public int w; public bool space; }
        [Serializable]
        class TitleMeta { public CharMeta[] chars; }

        enum SettingsPage { Main, Keys, Size, Workshop, Speed, Music, Guide, Mech }

        /// <summary>「游戏规则」页在 SettingsPage 里的下标，调试开关要用。</summary>
        public const int PageGuide = (int)SettingsPage.Guide;

        /// <summary>「特殊方块机制开关」页的下标。</summary>
        public const int PageMech = (int)SettingsPage.Mech;

        struct Mote
        {
            public float x, y;      // 归一化坐标 0..1
            public float vx, vy;    // 每秒归一化位移
            public float size;      // 像素（按屏高缩放前）
            public float phase;
            public float speed;
        }

        Mote[] _motes;
        float _moteT;

        public bool Visible = true;
        public bool ShowSettings;
        public bool HasContinue;

        /// <summary>「游戏规则」页当前看哪一栏：0 玩法、1 按键、2 特殊方块。</summary>
        int _guideTab;

        public event Action OnStartGame;
        public event Action OnContinue;
        public event Action OnQuit;
        public event Action<bool> OnSfxChanged;
        public event Action OnKeysChanged;
        public event Action OnSizeApplied;
        public event Action OnFallSpeedChanged;
        /// <summary>音乐音量改了（0 = 关），主程序据此换算成播放器的音量。</summary>
        public event Action OnMusicChanged;

        const string SfxKey = "sfx_enabled";

        public bool SfxEnabled
        {
            get { return PlayerPrefs.GetInt(SfxKey, 1) == 1; }
        }

        Texture2D _titleTex;
        /// <summary>方块图集，只给「特殊方块」页画图标用。</summary>
        Texture2D _atlasTex;
        CharMeta[] _chars;
        float _t;
        bool _stylesReady;
        GUIStyle _madeStyle;
        GUIStyle _subStyle;
        GUIStyle _creditStyle;
        GUIStyle _btnStyle;
        /// <summary>主菜单专用：字号每帧按菜单缩放系数重算（按钮放大，字也跟着放大）。</summary>
        GUIStyle _menuBtnStyle;
        GUIStyle _listStyle;
        GUIStyle _panelTitleStyle;
        GUIStyle _rowLabelStyle;
        GUIStyle _valueStyle;
        /// <summary>滑条文字的暗色影子（压在圆头上也看得清）。</summary>
        GUIStyle _sliderShadowStyle;
        /// <summary>提示行专用：不折行。折行会把固定的 Rect 撑破、压到下面的按钮上。</summary>
        GUIStyle _hintStyle;
        /// <summary>「特殊方块」页：方块名、机制说明、页脚小字。</summary>
        GUIStyle _guideNameStyle;
        GUIStyle _guideDescStyle;
        GUIStyle _guideNoteStyle;
        /// <summary>「游戏规则」按键栏：键名居中。</summary>
        GUIStyle _guideKeyStyle;

        // ---------- 设置各页的大号样式（尺寸统统 ×PageScale） ----------
        GUIStyle _btnBig;
        GUIStyle _panelTitleBig;
        GUIStyle _rowLabelBig;
        GUIStyle _hintBig;
        GUIStyle _valueBig;
        GUIStyle _sliderShadowBig;
        GUIStyle _guideNameBig;
        GUIStyle _guideDescBig;
        GUIStyle _guideNoteBig;
        GUIStyle _guideKeyBig;
        Texture2D _btnNormal;
        Texture2D _btnHover;
        Texture2D _btnActive;

        SettingsPage _page = SettingsPage.Main;
        int _capturingAction = -1;
        int _tmpX = 10;
        int _tmpY = 20;
        int _tmpZ = 1;
        float _tmpSpeed = 1f;
        /// <summary>音乐音量页里正在编辑的值（0~1）。</summary>
        float _tmpMusic = 0.45f;

        /// <summary>下滑条正在被拖的是哪一个（-1 = 没在拖）。IMGUI 没有内置拖动条，自己记。</summary>
        int _dragSlider = -1;
        const int SliderSpeed = 0;
        const int SliderMusic = 1;
        const int SliderSizeX = 2;
        const int SliderSizeY = 3;
        const int SliderSizeZ = 4;

        // 时间轴（秒）
        const float MadeEnd = 0.8f;
        const float TitleStart = 0.85f;
        const float CharDelay = 0.055f;
        const float CharFall = 0.5f;
        const float SubStart = 2.35f;
        const float BtnStart = 2.8f;
        const float CreditStart = 3.1f;

        static readonly GameAction[] AllActions =
        {
            GameAction.Left, GameAction.Right, GameAction.Forward, GameAction.Back,
            GameAction.SoftDrop, GameAction.HardDrop,
            GameAction.RotateCW, GameAction.RotateCCW,
            GameAction.RotateYawCW, GameAction.RotateYawCCW,
            GameAction.RotatePitchCW, GameAction.RotatePitchCCW
        };

        void Awake()
        {
            LoadAssets();
        }

        void OnEnable()
        {
            _t = 0f;
        }

        public void Show(bool show)
        {
            Visible = show;
            ShowSettings = false;
            _page = SettingsPage.Main;
            _capturingAction = -1;
            _dragSlider = -1;
            if (show)
                _t = 0f;
        }

        /// <summary>跳过开场动画，直接从暂停回到主界面时用。</summary>
        public void SkipIntro()
        {
            _t = 99f;
        }

        /// <summary>调试用：直接打开某个设置页（0 主页面 / 1 按键 / 2 尺寸…）。</summary>
        public void OpenSettingsPage(int page)
        {
            ShowSettings = true;
            _page = (SettingsPage)Mathf.Clamp(page, 0, (int)SettingsPage.Mech);
            _tmpSpeed = GameSettings.FallSpeed;
            _tmpMusic = GameSettings.MusicVolume;
            _capturingAction = -1;
            _dragSlider = -1;
            _tmpX = GameSettings.SizeX;
            _tmpY = GameSettings.SizeY;
            _tmpZ = GameSettings.SizeZ;
            if (_page == SettingsPage.Workshop)
            {
                if (_moldSel < 0 || _moldSel >= MoldData.Items.Count)
                    _moldSel = 0;
                LoadMoldEdit();
            }
        }

        public bool AnyInputPressed()
        {
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        }

        void LoadAssets()
        {
            _titleTex = Resources.Load<Texture2D>("title");
            _atlasTex = Resources.Load<Texture2D>("atlas");
            TextAsset meta = Resources.Load<TextAsset>("title_meta");
            if (meta != null)
            {
                try
                {
                    TitleMeta m = JsonUtility.FromJson<TitleMeta>(meta.text);
                    if (m != null)
                        _chars = m.chars;
                }
                catch
                {
                    _chars = null;
                }
            }
        }

        void Update()
        {
            if (Visible)
                _t += Time.deltaTime;
        }

        void OnGUI()
        {
            if (!Visible)
                return;

            UiSkin.Apply();
            EnsureStyles();

            if (ShowSettings)
            {
                if (_page == SettingsPage.Keys)
                    HandleCapture();
                DrawSettingsPanel();
                CursorTrail.DrawLast();   // 拖尾压在设置面板之上
                return;
            }

            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            DrawPortalMotes();

            DrawMadeWith();
            DrawTitle();
            DrawSub();
            DrawButtons();
            DrawCredit();

            CursorTrail.DrawLast();   // 永远最后画
        }

        // ---------- 动画各段 ----------

        /// <summary>标题（那张像素 logo）的缩放步长与高度。</summary>
        static int TitlePixelScale()
        {
            return Mathf.Max(2, Mathf.RoundToInt(Screen.height * 0.105f / 16f));
        }

        static float TitleHeight()
        {
            return 16f * TitlePixelScale();
        }

        /// <summary>标题落定后的上沿。0.19 改 0.15 是为了给放大的按钮腾出竖直空间。</summary>
        static float TitleTopY()
        {
            return Mathf.Round(Screen.height * 0.15f);
        }

        void DrawMadeWith()
        {
            if (_t > MadeEnd)
                return;

            float a = Mathf.Clamp01(_t / 0.2f);
            if (_t > MadeEnd - 0.25f)
                a = Mathf.Clamp01((MadeEnd - _t) / 0.25f);

            _madeStyle.normal.textColor = new Color(1f, 1f, 1f, a * 0.9f);
            GUI.Label(new Rect(0f, Screen.height * 0.40f, Screen.width, UiSkin.P(50f)), "Made with Unity", _madeStyle);
        }

        void DrawTitle()
        {
            if (_t < TitleStart)
                return;

            if (_titleTex == null || _chars == null)
                return;

            int scale = TitlePixelScale();
            int texW = _titleTex.width;
            float titleW = texW * scale;
            float titleH = TitleHeight();
            float left = (Screen.width - titleW) * 0.5f;
            float targetY = TitleTopY();

            for (int i = 0; i < _chars.Length; i++)
            {
                CharMeta ch = _chars[i];
                if (ch.space)
                    continue;

                float t0 = TitleStart + i * CharDelay;
                float p = Mathf.Clamp01((_t - t0) / CharFall);
                if (p <= 0f)
                    continue;

                float ease = EaseOutBounce(p);
                float y = Mathf.Lerp(-titleH, targetY, ease);
                float alpha = Mathf.Clamp01(p * 4f);

                var dst = new Rect(left + ch.x * scale, y, ch.w * scale, titleH);
                var uv = new Rect((float)ch.x / texW, 0f, (float)ch.w / texW, 1f);

                GUI.color = new Color(0f, 0f, 0f, alpha * 0.75f);
                GUI.DrawTextureWithTexCoords(new Rect(dst.x + scale * 0.25f, dst.y + scale * 0.25f, dst.width, dst.height), _titleTex, uv);

                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.DrawTextureWithTexCoords(dst, _titleTex, uv);
            }

            GUI.color = Color.white;
        }

        void DrawSub()
        {
            float p = Mathf.Clamp01((_t - SubStart) / 0.7f);
            if (p <= 0f)
                return;

            _subStyle.normal.textColor = new Color(1f, 1f, 1f, p);

            // 紧贴标题下方：原来固定在 0.42H，按钮放大后会和第一颗按钮打架
            GUI.Label(new Rect(0f, TitleTopY() + TitleHeight() + UiSkin.P(2f), Screen.width, UiSkin.P(44f)),
                "Made by Monody", _subStyle);
        }

        void DrawButtons()
        {
            var items = new List<KeyValuePair<string, Action>>();
            if (HasContinue)
                items.Add(new KeyValuePair<string, Action>("继续游戏", () => Fire(OnContinue)));
            items.Add(new KeyValuePair<string, Action>("开始游戏", () => Fire(OnStartGame)));
            items.Add(new KeyValuePair<string, Action>("创意工坊", OpenWorkshop));
            items.Add(new KeyValuePair<string, Action>("游戏规则", () => { ShowSettings = true; _guideTab = GuideTabPlay; _page = SettingsPage.Guide; }));
            items.Add(new KeyValuePair<string, Action>("设置", () => { ShowSettings = true; _page = SettingsPage.Main; }));
            items.Add(new KeyValuePair<string, Action>("退出", () => Fire(OnQuit)));

            int n = items.Count;

            // 可用竖直带：副标题下方 → 底部署名上方
            float bandTop = TitleTopY() + TitleHeight() + UiSkin.P(2f) + UiSkin.P(44f) + UiSkin.P(20f);
            float bandBottom = Screen.height - UiSkin.P(62f) - UiSkin.P(10f);

            MenuLayout lay = MenuLayout.Compute(
                n,
                UiSkin.P(280f), UiSkin.P(44f), UiSkin.P(10f),
                bandTop, bandBottom,
                Screen.width * 0.86f,
                UiSkin.MenuScale);

            // 字号跟着按钮一起放大，否则按钮长大字没长，反而更空
            _menuBtnStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(UiSkin.Size(18) * lay.scale));

            for (int i = 0; i < n; i++)
            {
                float ti = BtnStart + i * 0.1f;
                float p = Mathf.Clamp01((_t - ti) / 0.35f);
                if (p <= 0f)
                    continue;

                float y = lay.startY + i * (lay.height + lay.gap) + (1f - p) * UiSkin.P(18f);
                var rect = new Rect((Screen.width - lay.width) * 0.5f, y, lay.width, lay.height);

                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, p);
                if (GUI.Button(rect, items[i].Key, _menuBtnStyle))
                    items[i].Value?.Invoke();
                GUI.color = prev;
            }
        }

        void DrawCredit()
        {
            float p = Mathf.Clamp01((_t - CreditStart) / 0.55f);
            if (p <= 0f)
                return;

            _creditStyle.normal.textColor = new Color(1f, 1f, 1f, p * 0.85f);
            GUI.Label(new Rect(0f, Screen.height - UiSkin.P(62f), Screen.width, UiSkin.P(28f)), "AGENTS辅助完成", _creditStyle);
        }

        // ---------- 设置面板 ----------

        void DrawSettingsPanel()
        {
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.72f));

            switch (_page)
            {
                case SettingsPage.Main:
                    DrawSettingsMain();
                    break;
                case SettingsPage.Keys:
                    DrawSettingsKeys();
                    break;
                case SettingsPage.Size:
                    DrawSettingsSize();
                    break;
                case SettingsPage.Workshop:
                    DrawWorkshop();
                    break;
                case SettingsPage.Speed:
                    DrawSettingsSpeed();
                    break;
                case SettingsPage.Music:
                    DrawSettingsMusic();
                    break;
                case SettingsPage.Guide:
                    DrawGuide();
                    break;
                case SettingsPage.Mech:
                    DrawSettingsMech();
                    break;
            }
        }

        void DrawSettingsMain()
        {
            float w = Pg(420f);
            // 按钮数 × 54 + 上下留白；加一项就跟着涨，不然会压到「返回」上
            float h = Pg(594f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(16f), w, Pg(44f)), "设置", _panelTitleBig);

            float bw = Pg(300f);
            float bh = Pg(42f);
            float step = Pg(54f);
            float bx = panel.x + (w - bw) * 0.5f;
            float y = panel.y + Pg(80f);

            if (GUI.Button(new Rect(bx, y, bw, bh), SfxEnabled ? "音效：开" : "音效：关", _btnBig))
            {
                bool next = !SfxEnabled;
                PlayerPrefs.SetInt(SfxKey, next ? 1 : 0);
                PlayerPrefs.Save();
                if (OnSfxChanged != null)
                    OnSfxChanged(next);
            }
            y += step;

            // 深度 1 时一层就是一行，层消没有意义，按钮直接置灰
            bool canLayer = GameSettings.CanUseLayerMode;
            string elimName = !canLayer
                ? "消除：行消（Z=1 固定）"
                : (GameSettings.ElimMode == GameSettings.ElimLayer ? "消除：层消" : "消除：行消");
            GUI.enabled = canLayer;
            bool elimClicked = GUI.Button(new Rect(bx, y, bw, bh), elimName, _btnBig);
            GUI.enabled = true;
            // IMGUI 关掉 GUI.enabled 只是不吃点击，自定义贴图不会自己变灰，压一层半透明盖住
            if (!canLayer)
                DrawRect(new Rect(bx, y, bw, bh), new Color(0.05f, 0.06f, 0.08f, 0.45f));

            if (elimClicked)
            {
                int next = GameSettings.ElimMode == GameSettings.ElimLayer
                    ? GameSettings.ElimRows
                    : GameSettings.ElimLayer;
                GameSettings.SetElimMode(next);
            }
            y += step;

            string speedName = "下落速度：" + GameSettings.FallSpeed.ToString("0.00") + "×";
            if (GUI.Button(new Rect(bx, y, bw, bh), speedName, _btnBig))
            {
                _tmpSpeed = GameSettings.FallSpeed;
                _page = SettingsPage.Speed;
            }
            y += step;

            string musicName = GameSettings.MusicVolume <= 0.005f
                ? "音乐：关"
                : "音乐：" + Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";
            if (GUI.Button(new Rect(bx, y, bw, bh), musicName, _btnBig))
            {
                _tmpMusic = GameSettings.MusicVolume;
                _page = SettingsPage.Music;
            }
            y += step;

            // 落点提示：下落时把方块会停在哪儿用高亮方框标出来（3D 场地里不标很难估）
            if (GUI.Button(new Rect(bx, y, bw, bh),
                    GameSettings.GhostPiece ? "落点提示：开" : "落点提示：关", _btnBig))
                GameSettings.SetGhostPiece(!GameSettings.GhostPiece);
            y += step;

            if (GUI.Button(new Rect(bx, y, bw, bh), "按键设置", _btnBig))
            {
                _page = SettingsPage.Keys;
                _capturingAction = -1;
            }
            y += step;

            if (GUI.Button(new Rect(bx, y, bw, bh), "场地尺寸", _btnBig))
            {
                _tmpX = GameSettings.SizeX;
                _tmpY = GameSettings.SizeY;
                _tmpZ = GameSettings.SizeZ;
                _page = SettingsPage.Size;
            }
            y += step;

            // 特殊方块各机制的开关（TNT 炸不炸、沙子落不落…）都收在这一页里，
            // 原来「基岩进池」「结构方块进池」两个开关已经去掉：方块常驻池，行为由机制开关决定
            if (GUI.Button(new Rect(bx, y, bw, bh), "特殊方块开关", _btnBig))
                _page = SettingsPage.Mech;
            y += Pg(62f);

            if (GUI.Button(new Rect(bx, y, bw, bh), "返回", _btnBig))
                ShowSettings = false;
        }

        /// <summary>
        /// 特殊方块机制开关。十项排一列，关掉只是这种方块不再按特殊规则动，
        /// 方块本身还在池子里（改成不进池会让玩家在场上再也见不到它）。
        /// </summary>
        void DrawSettingsMech()
        {
            // 十一项排两列（6 + 5）：和按键页同理，一条竖列在放大一倍后会顶出屏幕。
            // 每列取 6 是为了让两列长度接近；按 5 分的话会变成 5+5+1，第三列孤零零一个按钮。
            const int perCol = 6;
            float rowH = Pg(46f);
            float rowGap = Pg(10f);
            float colW = Pg(400f);
            float colGap = Pg(50f);
            int cols = (GameSettings.MechCount + perCol - 1) / perCol;

            float w = Pg(80f) + cols * colW + (cols - 1) * colGap;
            float h = Pg(80f) + perCol * (rowH + rowGap) + Pg(150f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(12f), w, Pg(44f)), "特殊方块机制", _panelTitleBig);

            float top = panel.y + Pg(70f);
            for (int i = 0; i < GameSettings.MechCount; i++)
            {
                GameSettings.MechKind kind = (GameSettings.MechKind)i;
                bool on = GameSettings.Mech(kind);
                string label = GameSettings.MechLabel(kind) + (on ? "：开" : "：关");

                float x = panel.x + Pg(40f) + (i / perCol) * (colW + colGap);
                float y = top + (i % perCol) * (rowH + rowGap);

                if (GUI.Button(new Rect(x, y, colW, rowH), label, _btnBig))
                    GameSettings.SetMech(kind, !on);
            }

            GUI.Label(new Rect(panel.x, panel.y + h - Pg(120f), w, Pg(30f)),
                "关掉只是这种方块不再按特殊规则动，方块照常出现", _guideNoteBig);

            float bw = Pg(260f);
            if (GUI.Button(new Rect(panel.x + (w - bw) * 0.5f, panel.y + h - Pg(70f), bw, Pg(44f)), "返回", _btnBig))
                _page = SettingsPage.Main;
        }

        void DrawSettingsKeys()
        {
            // 12 组键排成两列（每列 6 行）。原来是一条竖列 12 行，整页放大一倍之后
            // 行数 × 行高会直接顶出屏幕，只能改成两列。
            const int perCol = 6;
            float rowH = Pg(44f);
            float rowGap = Pg(8f);
            float colW = Pg(400f);
            float colGap = Pg(60f);
            int cols = (AllActions.Length + perCol - 1) / perCol;

            float w = Pg(80f) + cols * colW + (cols - 1) * colGap;
            float h = Pg(62f) + perCol * (rowH + rowGap) + Pg(104f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(12f), w, Pg(44f)), "按键设置", _panelTitleBig);

            float top = panel.y + Pg(66f);
            for (int i = 0; i < AllActions.Length; i++)
            {
                GameAction action = AllActions[i];
                float x = panel.x + Pg(40f) + (i / perCol) * (colW + colGap);
                float y = top + (i % perCol) * (rowH + rowGap);

                GUI.Label(new Rect(x, y, Pg(170f), rowH), ActionLabel(action), _rowLabelBig);

                string keyName = _capturingAction == (int)action
                    ? "按任意键…"
                    : KeyName(KeyboardInput.GetPrimaryKey(action));

                if (GUI.Button(new Rect(x + Pg(175f), y, Pg(225f), rowH), keyName, _btnBig))
                    _capturingAction = (int)action;
            }

            if (_capturingAction >= 0)
                GUI.Label(new Rect(panel.x, panel.y + h - Pg(114f), w, Pg(30f)), "按 Esc 取消", _rowLabelBig);

            float bw = Pg(210f);
            float gap = Pg(40f);
            float bx = panel.x + (w - (bw * 2f + gap)) * 0.5f;
            float bottomY = panel.y + h - Pg(66f);

            if (GUI.Button(new Rect(bx, bottomY, bw, Pg(44f)), "恢复默认", _btnBig))
            {
                ResetKeys();
                if (OnKeysChanged != null)
                    OnKeysChanged();
            }
            if (GUI.Button(new Rect(bx + bw + gap, bottomY, bw, Pg(44f)), "返回", _btnBig))
            {
                _capturingAction = -1;
                _page = SettingsPage.Main;
            }
        }

        void DrawSettingsSize()
        {
            float w = Pg(460f);
            float h = Pg(500f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(14f), w, Pg(44f)), "场地尺寸", _panelTitleBig);

            // 三个轴都是拖拽滑条，和下落速度、音乐音量一个样式。
            // 这里只在内存里改 _tmpX/Y/Z，真正落盘要等下面「应用并重启」——尺寸一变就得清存档重建场地。
            float rowW = Pg(360f);
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + Pg(76f);

            DrawSizeSlider(SliderSizeX, bx, y, rowW, "宽度 X", GameSettings.MinSizeX, GameSettings.MaxSizeX, ref _tmpX);
            y += Pg(58f);
            DrawSizeSlider(SliderSizeY, bx, y, rowW, "高度 Y", GameSettings.MinSizeY, GameSettings.MaxSizeY, ref _tmpY);
            y += Pg(58f);
            DrawSizeSlider(SliderSizeZ, bx, y, rowW, "深度 Z", GameSettings.MinSizeZ, GameSettings.MaxSizeZ, ref _tmpZ);
            y += Pg(58f);

            GUI.Label(new Rect(bx, y, rowW, Pg(28f)), "拖动滑条调大小，改完点「应用并重启」", _hintBig);
            y += Pg(28f);
            GUI.Label(new Rect(bx, y, rowW, Pg(28f)), "深度大于 1 才有前后移动和层消", _hintBig);
            y += Pg(34f);

            if (GUI.Button(new Rect(bx, y, rowW, Pg(44f)), "应用并重启", _btnBig))
            {
                GameSettings.SetSize(_tmpX, _tmpY, _tmpZ);
                if (OnSizeApplied != null)
                    OnSizeApplied();
            }
            y += Pg(54f);

            if (GUI.Button(new Rect(bx, y, rowW, Pg(42f)), "返回", _btnBig))
                _page = SettingsPage.Main;
        }

        /// <summary>尺寸滑条：值是整数，拖的时候四舍五入到最近的格数。</summary>
        void DrawSizeSlider(int id, float x, float y, float w, string label, int min, int max, ref int value)
        {
            int v = Mathf.Clamp(value, min, max);
            float t = max > min ? Mathf.Clamp01((v - min) / (float)(max - min)) : 0f;

            if (McSlider(id, new Rect(x, y, w, Pg(46f)), ref t,
                    f => label + "  " + Mathf.RoundToInt(Mathf.Lerp(min, max, f))))
            {
                value = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(min, max, t)), min, max);
            }
        }

        void DrawSettingsSpeed()
        {
            float w = Pg(500f);
            float h = Pg(360f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(14f), w, Pg(44f)), "下落速度", _panelTitleBig);

            float rowW = Pg(400f);
            float bx = panel.x + (w - rowW) * 0.5f;

            // 和 MC 的设置一样：一条滑条拖到哪儿算哪儿，数值写在滑条上。
            // 原来那排 ±0.10 / ±0.01 按钮去掉了——MC 里没有这种按钮，也省得两套调法互相打架。
            float min = GameSettings.MinFallSpeed;
            float max = GameSettings.MaxFallSpeed;
            float t = Mathf.InverseLerp(min, max, GameSettings.ClampFallSpeed(_tmpSpeed));
            var sliderRect = new Rect(bx, panel.y + Pg(86f), rowW, Pg(46f));
            if (McSlider(SliderSpeed, sliderRect, ref t,
                    x => "下落速度  " + Mathf.Lerp(min, max, x).ToString("0.00") + "×"))
            {
                _tmpSpeed = GameSettings.ClampFallSpeed(Mathf.Lerp(min, max, t));
                GameSettings.SetFallSpeed(_tmpSpeed);
                if (OnFallSpeedChanged != null)
                    OnFallSpeedChanged();
            }

            // 四行提示，每行都短到不会折行（折行会撑破 Rect、压到下面的按钮上）
            float lineH = Pg(28f);
            float y = panel.y + Pg(152f);
            GUI.Label(new Rect(bx, y, rowW, lineH), "按住圆头拖动，或点滑条上任意位置", _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH),
                "1.00× = 标准（每 " + GameLoop.BaseFallInterval.ToString("0.00") + " 秒落一格）", _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH),
                "当前：每 " + (GameLoop.BaseFallInterval / Mathf.Max(0.01f, _tmpSpeed)).ToString("0.00") + " 秒落一格",
                _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "范围 0.01 ~ 2.00，3D 场地建议 0.50 ~ 0.80", _hintBig);

            float bottomY = panel.y + h - Pg(62f);
            float bh = Pg(42f);
            if (GUI.Button(new Rect(bx, bottomY, Pg(190f), bh), "恢复标准", _btnBig))
            {
                _tmpSpeed = GameSettings.DefaultFallSpeed;
                GameSettings.SetFallSpeed(_tmpSpeed);
                if (OnFallSpeedChanged != null)
                    OnFallSpeedChanged();
            }

            if (GUI.Button(new Rect(bx + rowW - Pg(190f), bottomY, Pg(190f), bh), "返回", _btnBig))
                _page = SettingsPage.Main;
        }

        /// <summary>
        /// 音乐音量。曲库是从 MC 原版里提的那些常速曲，随机播放；
        /// 这里只管音量，0% 就是关。滑条和下落速度那条共用 McSlider。
        /// </summary>
        void DrawSettingsMusic()
        {
            float w = Pg(500f);
            float h = Pg(360f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(14f), w, Pg(44f)), "音乐音量", _panelTitleBig);

            float rowW = Pg(400f);
            float bx = panel.x + (w - rowW) * 0.5f;

            float t = GameSettings.ClampMusicVolume(_tmpMusic);
            var sliderRect = new Rect(bx, panel.y + Pg(86f), rowW, Pg(46f));
            if (McSlider(SliderMusic, sliderRect, ref t, MusicLabel))
            {
                _tmpMusic = GameSettings.ClampMusicVolume(t);
                GameSettings.SetMusicVolume(_tmpMusic);
                if (OnMusicChanged != null)
                    OnMusicChanged();
            }

            // 一行一句，短到不会折行（折行会撑破 Rect、压到底部按钮上）
            float lineH = Pg(28f);
            float y = panel.y + Pg(152f);
            GUI.Label(new Rect(bx, y, rowW, lineH), "按住圆头拖动，拖到最左就是静音", _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "原版曲库，随机播放，两首之间留一段静默", _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "标题界面用菜单曲，进游戏换成常速曲", _hintBig);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "0% = 关闭音乐（放置音效另算）", _hintBig);

            float bottomY = panel.y + h - Pg(62f);
            float bh = Pg(42f);
            if (GUI.Button(new Rect(bx, bottomY, Pg(190f), bh), "恢复默认", _btnBig))
            {
                _tmpMusic = GameSettings.DefaultMusicVolume;
                GameSettings.SetMusicVolume(_tmpMusic);
                if (OnMusicChanged != null)
                    OnMusicChanged();
            }

            if (GUI.Button(new Rect(bx + rowW - Pg(190f), bottomY, Pg(190f), bh), "返回", _btnBig))
                _page = SettingsPage.Main;
        }

        /// <summary>滑条上的文字：0 就直接写「关」，别显示 0%。</summary>
        static string MusicLabel(float t)
        {
            return t <= 0.005f ? "音乐音量  关" : "音乐音量  " + Mathf.RoundToInt(t * 100f) + "%";
        }

        /// <summary>
        /// 我的世界那种设置滑条：一整条轨 + 一个圆头把手，按住拖、或者点轨上任意位置直接跳过去。
        /// t 是 0~1 的归一化位置；返回 true 表示这一帧值变了，调用方据此存盘并发事件。
        ///
        /// IMGUI 自带的 GUI.HorizontalSlider 是「细线 + 小圆点」的外观，和 MC 差得远，
        /// 所以鼠标三个事件自己接：MouseDown 认领、MouseDrag 跟随、MouseUp 交还。
        /// 认领期间 e.Use()，免得同一次点击又穿到下面的按钮上。
        /// </summary>
        bool McSlider(int id, Rect r, ref float t, Func<float, string> label)
        {
            Event e = Event.current;
            bool changed = false;

            float handle = Pg(12f);
            float travel = Mathf.Max(1f, r.width - handle);

            if (e != null)
            {
                if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
                    _dragSlider = id;

                if (_dragSlider == id && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                {
                    // 让把手中心跟着鼠标；两边各留半个把手宽，拖到头时把手正好贴边
                    float nt = Mathf.Clamp01((e.mousePosition.x - r.x - handle * 0.5f) / travel);
                    if (Mathf.Abs(nt - t) > 0.0004f)
                    {
                        t = nt;
                        changed = true;
                    }
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && _dragSlider == id)
                {
                    _dragSlider = -1;
                    e.Use();
                }
                else if (_dragSlider == id && !Input.GetMouseButton(0))
                {
                    // 鼠标在窗口外松开就收不到 MouseUp，这里兜一下，不然滑条会一直粘着鼠标
                    _dragSlider = -1;
                }
            }

            bool lit = e != null && (r.Contains(e.mousePosition) || _dragSlider == id);
            Color border = lit
                ? new Color(0.62f, 0.66f, 0.74f, 0.95f)
                : new Color(0.35f, 0.37f, 0.44f, 0.95f);

            DrawRect(r, new Color(0.13f, 0.15f, 0.19f, 0.96f));
            // 已拖过的部分淡淡的填一层，一眼能看出现在到哪儿了
            DrawRect(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * t, r.height - 2f),
                new Color(0.42f, 0.86f, 1f, 0.16f));
            DrawRect(new Rect(r.x, r.y, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.y, 1f, r.height), border);
            DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);

            // 把手：浅灰一块，右边压一道深色，看着像凸起来的塑料件
            var knob = new Rect(r.x + travel * t, r.y + 1f, handle, r.height - 2f);
            // 圆头用中灰而不是浅灰：白字要从它上面压过去，浅灰上白字会糊成一片
            DrawRect(knob, new Color(0.46f, 0.49f, 0.56f, 1f));
            DrawRect(new Rect(knob.xMax - 1f, knob.y, 1f, knob.height), new Color(0.26f, 0.28f, 0.33f, 1f));

            // 字在圆头上再盖一遍：先落一层深影子（往右下偏 1px），再写白字。
            // 圆头滑到中间时正好从字中间穿过，没有这层影子那块会糊成一片。
            string text = label(t);
            // 影子也要跟着放大：1px 的偏移在 2 倍字号下根本压不住圆头那块浅灰
            float sh = Mathf.Max(1f, Pg(1f));
            GUI.Label(new Rect(r.x + sh, r.y + sh, r.width, r.height), text, _sliderShadowBig);
            GUI.Label(r, text, _valueBig);
            return changed;
        }

        // ---------- 改键 ----------

        void HandleCapture()
        {
            if (_capturingAction < 0)
                return;

            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;

            if (e.keyCode == KeyCode.Escape)
            {
                _capturingAction = -1;
                e.Use();
                return;
            }

            if (e.keyCode != KeyCode.None)
            {
                KeyboardInput.Save((GameAction)_capturingAction, e.keyCode);
                _capturingAction = -1;
                if (OnKeysChanged != null)
                    OnKeysChanged();
                e.Use();
            }
        }

        static void ResetKeys()
        {
            for (int i = 0; i < AllActions.Length; i++)
                KeyboardInput.Save(AllActions[i], KeyboardInput.Defaults[i]);
        }

        static string ActionLabel(GameAction a)
        {
            switch (a)
            {
                case GameAction.Left: return "左移";
                case GameAction.Right: return "右移";
                case GameAction.Forward: return "前进（z 减小）";
                case GameAction.Back: return "后退（z 增大）";
                case GameAction.SoftDrop: return "软降";
                case GameAction.HardDrop: return "硬降";
                case GameAction.RotateCW: return "面内顺时针";
                case GameAction.RotateCCW: return "面内逆时针";
                case GameAction.RotateYawCW: return "俯视顺时针";
                case GameAction.RotateYawCCW: return "俯视逆时针";
                case GameAction.RotatePitchCW: return "往背面翻";
                case GameAction.RotatePitchCCW: return "往正面翻";
                default: return a.ToString();
            }
        }

        static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.None: return "未设置";
                case KeyCode.Space: return "空格";
                case KeyCode.LeftShift: return "左 Shift";
                case KeyCode.RightShift: return "右 Shift";
                case KeyCode.LeftControl: return "左 Ctrl";
                case KeyCode.RightControl: return "右 Ctrl";
                case KeyCode.LeftArrow: return "←";
                case KeyCode.RightArrow: return "→";
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
                default: return k.ToString();
            }
        }

        // ---------- 游戏规则 ----------

        /// <summary>规则页的三栏：玩法、按键、特殊方块。</summary>
        const int GuideTabPlay = 0;
        const int GuideTabKeys = 1;
        const int GuideTabBlocks = 2;
        const int GuideTabCount = 3;

        static readonly string[] GuideTabNames = { "玩法", "按键", "特殊方块" };
        static readonly string[] GuideTabNotes =
        {
            // 玩法栏不放提示行：六条本身就够直白，多一句反而显得啰嗦（玩家要求去掉）
            "",
            "这里显示的是当前键位，改键去 设置 → 按键设置。",
            "只列和原版不一样、或者第一次玩容易搞错的机制；每一项都能在设置里单独关掉。",
        };

        /// <summary>玩法栏的六段：小标题、正文各一行。</summary>
        /// <summary>
        /// 玩法栏的六段文案。左边是玩家自己写的一版，页名统一改成界面上真实的按钮名
        /// （设置里的那个叫「场地尺寸」，不是「场地设置」），免得他按字面去找找不到。
        /// </summary>
        static readonly string[][] PlayRules =
        {
            new[] { "游戏玩法", "参照经典游戏《俄罗斯方块》。" },
            new[] { "游戏方块来源", "《我的世界》。" },
            new[] { "游戏场地", "默认为（x = 10，y = 20，z = 1），可以在 设置 → 场地尺寸 里自行调节。" },
            new[] { "方向选择", "支持沿 x、y、z 轴任意旋转 90°，键位可以在 设置 → 按键设置 里自定义。" },
            new[] { "特殊方块", "一些方块拥有特殊机制，详见本页的「特殊方块」栏。" },
            new[] { "创意工坊", "玩家可以自定义下落方块的样式。" },
        };

        // ---------- 特殊方块说明 ----------

        struct GuideItem
        {
            public int[] Ids;
            public string Title;
            public string Desc;
        }

        static GuideItem[] _guideItems;

        static int FindBlock(string name)
        {
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == name)
                    return i + 1;
            return 0;
        }

        static void EnsureGuideItems()
        {
            if (_guideItems != null)
                return;

            _guideItems = new[]
            {
                // TNT 和红石块合成一格：本来就是同一条机制（红石点火），
                // 分开占两格的话加上傀儡就变成 13 格，三列排不下（4 行已是面板上限）
                new GuideItem
                {
                    Ids = new[] { Tnt.TntId, Tnt.RedstoneId },
                    Title = "TNT / 红石块",
                    Desc = "红石块贴着就点火（隔一格不算），2.4 秒后炸掉半径 2，TNT 会连锁。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.StructureId },
                    Title = "结构方块",
                    Desc = "落定记住整场，被消行、消层或炸掉时，场上一格格收掉再按记忆掉回来。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.BedrockId },
                    Title = "基岩",
                    Desc = "不随消除下落，也炸不掉。",
                },
                new GuideItem
                {
                    Ids = new[] { FindBlock("obsidian"), FindBlock("crying_obsidian") },
                    Title = "黑曜石 / 哭泣的黑曜石",
                    Desc = "炸不掉，可以拿来当挡板。",
                },
                new GuideItem
                {
                    Ids = new[]
                    {
                        FindBlock("ancient_debris"), FindBlock("netherite_block"),
                        FindBlock("reinforced_deepslate"), FindBlock("command_block"),
                    },
                    Title = "防爆方块",
                    Desc = "远古残骸、下界合金块、强化深板岩、命令方块，和原版一样炸不掉。",
                },
                new GuideItem
                {
                    Ids = new[] { FindBlock("water") },
                    Title = "水",
                    Desc = "往低处和两边流，一格最多 8 份。碰到源岩浆会变成黑曜石。",
                },
                new GuideItem
                {
                    Ids = new[] { FindBlock("lava") },
                    Title = "岩浆",
                    Desc = "流得比水慢；源岩浆碰到水变黑曜石，流动的岩浆变圆石。挨着 TNT 会把它点着。",
                },
                new GuideItem
                {
                    Ids = new[] { FindBlock("sand"), FindBlock("gravel") },
                    Title = "沙子 / 沙砾",
                    Desc = "下面是空的就往下掉，被炸出的弹坑会自己填平。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.SnowId, BlockCatalog.IceId },
                    Title = "雪块 / 冰",
                    Desc = "贴着岩浆（任意一面）就化成水，水再被岩浆烫成黑曜石。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.CarvedPumpkinId, BlockCatalog.SnowId },
                    Title = "傀儡",
                    Desc = "南瓜头压着雪块 / 铁块 / 铜块就召出傀儡，两格消失，傀儡摆到场外。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.GlassId },
                    Title = "玻璃",
                    Desc = "放下来不会碎；之后有方块压到上面才碎，碎完上方的东西接着往下掉。",
                },
                new GuideItem
                {
                    Ids = new[] { FindBlock("oak_log") },
                    Title = "原木",
                    Desc = "落定后，周围空位会长出同种树叶。",
                },
            };
        }

        /// <summary>调试开关和主菜单用：直接打开规则页的某一栏。</summary>
        public void OpenRulesPage(int tab)
        {
            ShowSettings = true;
            _page = SettingsPage.Guide;
            _guideTab = Mathf.Clamp(tab, 0, GuideTabCount - 1);
        }

        void DrawGuide()
        {
            EnsureGuideItems();

            if (_guideTab < 0 || _guideTab >= GuideTabCount)
                _guideTab = GuideTabPlay;

            // 这一页内容最多，放大一倍之后宽高都顶到屏幕边上，所以横向铺满（1000 设计单位 = 2500px）
            float w = Pg(1000f);
            // 三栏内容长短差得多，面板高度跟着栏走，免得短栏下面空一大块
            float h = _guideTab == GuideTabBlocks ? Pg(596f)
                : _guideTab == GuideTabKeys ? Pg(576f)
                : Pg(520f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(12f), w, Pg(46f)), "游戏规则", _panelTitleBig);

            float tw = Pg(200f);
            float th = Pg(40f);
            float tgap = Pg(16f);
            float tx = panel.x + (w - (tw * GuideTabCount + tgap * (GuideTabCount - 1))) * 0.5f;
            float ty = panel.y + Pg(56f);
            for (int i = 0; i < GuideTabCount; i++)
            {
                Color prev = GUI.color;
                GUI.color = _guideTab == i ? Color.white : new Color(0.7f, 0.7f, 0.7f, 1f);
                if (GUI.Button(new Rect(tx + i * (tw + tgap), ty, tw, th), GuideTabNames[i], _btnBig))
                    _guideTab = i;
                GUI.color = prev;
            }

            string tabNote = GuideTabNotes[_guideTab];
            if (!string.IsNullOrEmpty(tabNote))
                GUI.Label(new Rect(panel.x, panel.y + Pg(100f), w, Pg(28f)), tabNote, _guideNoteBig);

            if (_guideTab == GuideTabPlay)
                DrawRulesPlay(panel, w);
            else if (_guideTab == GuideTabKeys)
                DrawRulesKeys(panel, w);
            else
                DrawRulesBlocks(panel);

            if (GUI.Button(new Rect(panel.x + w - Pg(230f), panel.yMax - Pg(76f),
                Pg(180f), Pg(44f)), "返回", _btnBig))
                ShowSettings = false;
        }

        /// <summary>玩法栏：六段小标题 + 正文。放大之后一列放不下六段，改成两列每列三段。</summary>
        void DrawRulesPlay(Rect panel, float w)
        {
            const int perCol = 3;
            float colW = Pg(430f);
            float colGap = Pg(40f);
            float rowH = Pg(96f);
            // 这一栏没有提示行了，内容往上提一点，不然标签和正文之间空出一道
            float top = panel.y + Pg(112f);

            for (int i = 0; i < PlayRules.Length; i++)
            {
                float x = panel.x + Pg(40f) + (i / perCol) * (colW + colGap);
                float y = top + (i % perCol) * rowH;
                GUI.Label(new Rect(x, y, colW, Pg(30f)), PlayRules[i][0], _guideNameBig);
                GUI.Label(new Rect(x, y + Pg(30f), colW, Pg(58f)), PlayRules[i][1], _guideDescBig);
            }
        }

        /// <summary>按键栏：读当前键位，两列排开；这里只显示，改键在设置里。</summary>
        void DrawRulesKeys(Rect panel, float w)
        {
            const int perCol = 6;
            float colW = Pg(440f);
            float labelW = Pg(240f);
            float keyW = Pg(170f);
            float rowH = Pg(42f);
            float rowGap = Pg(8f);
            float left = panel.x + Pg(40f);
            float top = panel.y + Pg(128f);

            for (int i = 0; i < AllActions.Length; i++)
            {
                GameAction action = AllActions[i];
                float x = left + (i / perCol) * (colW + Pg(40f));
                float y = top + (i % perCol) * (rowH + rowGap);

                GUI.Label(new Rect(x, y, labelW, rowH), ActionLabel(action), _rowLabelBig);
                DrawRect(new Rect(x + labelW, y + Pg(3f), keyW, rowH - Pg(6f)),
                    new Color(1f, 1f, 1f, 0.07f));
                GUI.Label(new Rect(x + labelW, y, keyW, rowH),
                    KeyName(KeyboardInput.GetPrimaryKey(action)), _guideKeyBig);
            }

            // 最下面一行：视角操作不占键位，单独提一句（右键拖拽旋转、滚轮缩放）
            float noteY = top + perCol * (rowH + rowGap) + Pg(14f);
            GUI.Label(new Rect(left, noteY, w - Pg(80f), Pg(28f)),
                "右键拖拽→视角旋转，滚轮滑动→视角放大/缩小", _guideNoteBig);
        }

        /// <summary>特殊方块栏：十一项三列，图标从方块图集里切。两列六行在放大后放不下。</summary>
        void DrawRulesBlocks(Rect panel)
        {
            const int perCol = 4;
            float colW = Pg(293f);
            // 标题、切换栏、提示各占一段之后只剩这么多，行高再大就压到「返回」上了
            float rowH = Pg(96f);
            float top = panel.y + Pg(128f);

            for (int i = 0; i < _guideItems.Length; i++)
            {
                float x = panel.x + Pg(40f) + (i / perCol) * (colW + Pg(20f));
                DrawGuideRow(x, top + (i % perCol) * rowH, colW, _guideItems[i]);
            }
        }

        void DrawGuideRow(float x, float y, float w, GuideItem item)
        {
            int n = 0;
            for (int i = 0; i < item.Ids.Length; i++)
                if (item.Ids[i] > 0)
                    n++;

            float icon = n <= 2 ? Pg(36f) : Pg(26f);
            float gap = Pg(4f);
            float iconsW = n > 0 ? n * icon + (n - 1) * gap : 0f;

            int slot = 0;
            for (int i = 0; i < item.Ids.Length; i++)
            {
                if (item.Ids[i] <= 0)
                    continue;
                DrawBlockIcon(new Rect(x + slot * (icon + gap), y + Pg(2f), icon, icon), item.Ids[i]);
                slot++;
            }

            // 图标在左、文字在右。三列之后每格只剩 293 宽，图标要是单独占一行，
            // 一格就顶到 130 高，四行直接压到「返回」上；并排之后一格正好 26 + 52 = 78。
            float pad = iconsW > 0f ? iconsW + Pg(10f) : 0f;
            GUI.Label(new Rect(x + pad, y, w - pad, Pg(28f)), item.Title, _guideNameBig);
            GUI.Label(new Rect(x + pad, y + Pg(30f), w - pad, Pg(58f)), item.Desc, _guideDescBig);
        }

        /// <summary>从方块图集里切一格顶面出来画成图标；方块表里查不到就画个灰块。</summary>
        void DrawBlockIcon(Rect r, int blockId)
        {
            DrawRect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, r.height + 2f), new Color(0f, 0f, 0f, 0.55f));

            if (_atlasTex == null || blockId <= 0 || blockId > BlockCatalog.Blocks.Length)
            {
                DrawRect(r, new Color(0.25f, 0.28f, 0.33f, 1f));
                return;
            }

            Tile tile = BlockCatalog.Of(blockId).Up;
            int cell = BlockCatalog.CellPixels;
            int pad = BlockCatalog.TilePadding;
            float aw = (float)BlockCatalog.AtlasColumns * cell;
            float ah = (float)BlockCatalog.AtlasRows * cell;
            float x0 = tile.Col * cell + pad;
            float yTop = tile.Row * cell + pad;

            var uv = new Rect(
                x0 / aw,
                1f - (yTop + BlockCatalog.TilePixels) / ah,
                (float)BlockCatalog.TilePixels / aw,
                (float)BlockCatalog.TilePixels / ah);

            GUI.DrawTextureWithTexCoords(r, _atlasTex, uv);
        }

        // ---------- 工具 ----------

        void Fire(Action a)
        {
            if (a != null)
                a();
        }

        /// <summary>
        /// 设置这几页（主页 / 按键 / 场地尺寸 / 下滑速度 / 音量 / 机制开关 / 游戏规则）相对基准再放大一倍。
        ///
        /// 为什么要单独一套：界面尺寸是写死的像素（P() 只乘 1.25），不随分辨率走。
        /// 在 2560×1494 这类高分屏上整块设置面板只占屏幕中间一小块，字看着就小。
        /// 这几页内容少、改起来可控，所以整页翻倍；页内每个尺寸过 Pg()、每个字号过 Fg()，
        /// 两套数字同一个系数，等比长大不会出现「字大了框没大」的错位。
        /// </summary>
        const float PageScale = 2f;

        static float Pg(float design)
        {
            return UiSkin.P(design * PageScale);
        }

        static int Fg(int size)
        {
            return UiSkin.Size(Mathf.RoundToInt(size * PageScale));
        }

        static Rect CenterPanel(float w, float h)
        {
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        void DrawPanel(Rect r)
        {
            DrawRect(r, new Color(0.09f, 0.10f, 0.14f, 0.97f));
            Color border = new Color(0.35f, 0.37f, 0.44f, 0.95f);
            DrawRect(new Rect(r.x, r.y, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.y, 1f, r.height), border);
            DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);
        }


        // ---------- 创意工坊 ----------

        int _moldSel;
        readonly List<Vector2Int> _moldEdit = new List<Vector2Int>();
        bool _renaming;
        string _renameText = "";

        void OpenWorkshop()
        {
            _page = SettingsPage.Workshop;
            ShowSettings = true;
            if (_moldSel >= MoldData.Items.Count)
                _moldSel = 0;
            LoadMoldEdit();
        }

        void LoadMoldEdit()
        {
            _moldEdit.Clear();
            if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
            {
                Vector2Int[] cells = MoldData.Parse(MoldData.Items[_moldSel].cells);
                for (int i = 0; i < cells.Length; i++)
                    _moldEdit.Add(cells[i]);
            }
        }

        void SaveMoldEdit()
        {
            if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                MoldData.SetCells(_moldSel, _moldEdit.ToArray());
        }

        void DrawWorkshop()
        {
            float w = UiSkin.P(680f);
            float h = UiSkin.P(640f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(12f), w, UiSkin.P(44f)), "创意工坊", _panelTitleStyle);

            // 左边的模具列表
            float listX = panel.x + UiSkin.P(24f);
            float listY = panel.y + UiSkin.P(96f);
            float itemH = UiSkin.P(38f);
            float itemGap = UiSkin.P(6f);

            GUI.Label(new Rect(listX, listY - UiSkin.P(30f), UiSkin.P(280f), UiSkin.P(30f)),
                "模具列表（点一下选中编辑）", _rowLabelStyle);

            int count = MoldData.Items.Count;
            for (int i = 0; i < count && i < 7; i++)
            {
                MoldEntry m = MoldData.Items[i];
                string label = (i == _moldSel ? "▸ " : "   ") + m.name + (m.enabled != 0 ? "     ✓ 启用" : "     ○ 未启用");
                var rect = new Rect(listX, listY + i * (itemH + itemGap), UiSkin.P(260f), itemH);
                if (i == _moldSel)
                    DrawRect(rect, new Color(0.42f, 0.86f, 1f, 0.22f));
                if (GUI.Button(rect, label, _listStyle) && i != _moldSel)
                {
                    _moldSel = i;
                    LoadMoldEdit();
                }
            }

            if (count > 7)
                GUI.Label(new Rect(listX, listY + 7 * (itemH + itemGap), UiSkin.P(260f), UiSkin.P(30f)),
                    "（只显示前 7 个）", _rowLabelStyle);

            // 右边的 5x5 画布
            float cell = UiSkin.P(46f);
            float gridX = panel.x + UiSkin.P(348f);
            float gridY = panel.y + UiSkin.P(96f);

            GUI.Label(new Rect(gridX, gridY - UiSkin.P(30f), UiSkin.P(300f), UiSkin.P(30f)),
                "点格子摆形状（再点一次取消）", _rowLabelStyle);

            for (int row = 0; row < MoldData.Size; row++)
            {
                for (int col = 0; col < MoldData.Size; col++)
                {
                    int gx = col;
                    int gy = MoldData.Size - 1 - row;
                    var point = new Vector2Int(gx, gy);
                    bool on = _moldEdit.Contains(point);

                    var rc = new Rect(gridX + col * cell, gridY + row * cell, cell - 3f, cell - 3f);
                    DrawRect(rc, on ? new Color(0.42f, 0.86f, 1f, 0.92f) : new Color(1f, 1f, 1f, 0.07f));
                    if (GUI.Button(rc, "", GUIStyle.none))
                    {
                        if (on)
                            _moldEdit.Remove(point);
                        else
                            _moldEdit.Add(point);
                        SaveMoldEdit();
                    }
                }
            }

            // 底部按钮
            float bx = panel.x + UiSkin.P(40f);
            float by = panel.y + UiSkin.P(430f);
            float bw2 = UiSkin.P(110f);
            float bh2 = UiSkin.P(40f);
            float bgap = UiSkin.P(12f);

            if (GUI.Button(new Rect(bx, by, bw2, bh2), "新建模具", _btnStyle))
            {
                MoldData.Add();
                _moldSel = MoldData.Items.Count - 1;
                LoadMoldEdit();
            }

            if (GUI.Button(new Rect(bx + bw2 + bgap, by, bw2, bh2), "删除模具", _btnStyle))
            {
                MoldData.Remove(_moldSel);
                if (_moldSel >= MoldData.Items.Count)
                    _moldSel = MoldData.Items.Count - 1;
                LoadMoldEdit();
            }

            if (GUI.Button(new Rect(bx + (bw2 + bgap) * 2f, by, bw2, bh2), "重命名", _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                {
                    _renaming = true;
                    _renameText = MoldData.Items[_moldSel].name;
                }
            }

            string enableLabel = "未启用（点一下启用）";
            if (_moldSel >= 0 && _moldSel < MoldData.Items.Count && MoldData.Items[_moldSel].enabled != 0)
                enableLabel = "启用中（点一下禁用）";

            if (GUI.Button(new Rect(bx + (bw2 + bgap) * 3f, by, UiSkin.P(200f), bh2), enableLabel, _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                {
                    MoldData.SetEnabled(_moldSel, MoldData.Items[_moldSel].enabled == 0);
                    LoadMoldEdit();
                }
            }

            // 重命名输入行（在按钮上方）
            if (_renaming)
            {
                float ry = by - UiSkin.P(46f);
                float rh = UiSkin.P(32f);
                GUI.Label(new Rect(bx, ry + UiSkin.P(2f), UiSkin.P(56f), UiSkin.P(30f)), "名字", _rowLabelStyle);
                _renameText = GUI.TextField(new Rect(bx + UiSkin.P(58f), ry, UiSkin.P(300f), rh),
                    _renameText, 16, _btnStyle);
                if (GUI.Button(new Rect(bx + UiSkin.P(370f), ry, UiSkin.P(70f), rh), "确定", _btnStyle))
                {
                    MoldData.Rename(_moldSel, _renameText);
                    _renaming = false;
                    LoadMoldEdit();
                }
                if (GUI.Button(new Rect(bx + UiSkin.P(452f), ry, UiSkin.P(70f), rh), "取消", _btnStyle))
                    _renaming = false;
            }

            GUI.Label(new Rect(panel.x + UiSkin.P(40f), by + bh2 + UiSkin.P(20f), UiSkin.P(520f), UiSkin.P(30f)),
                "启用的模具会和 7 种默认方块一起随机出现", _rowLabelStyle);

            if (GUI.Button(new Rect(panel.x + w - UiSkin.P(170f), by + bh2 + UiSkin.P(22f),
                UiSkin.P(130f), bh2), "返回", _btnStyle))
            {
                _renaming = false;
                ShowSettings = false;
            }
        }

        void EnsureStyles()
        {
            if (_stylesReady)
                return;
            _stylesReady = true;

            _btnNormal = SolidTexture(new Color(0.13f, 0.15f, 0.19f, 0.96f));
            _btnHover = SolidTexture(new Color(0.20f, 0.24f, 0.30f, 0.98f));
            _btnActive = SolidTexture(new Color(0.10f, 0.12f, 0.15f, 0.98f));

            _madeStyle = MakeStyle(22, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            _subStyle = MakeStyle(30, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            _creditStyle = MakeStyle(17, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal);
            _panelTitleStyle = MakeStyle(32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rowLabelStyle = MakeStyle(18, new Color(0.72f, 0.76f, 0.82f, 1f), TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintStyle = MakeStyle(18, new Color(0.72f, 0.76f, 0.82f, 1f), TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintStyle.wordWrap = false;
            _valueStyle = MakeStyle(22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            // 滑条上的字会被圆头从中间穿过去，垫一层深色影子才读得清
            _sliderShadowStyle = MakeStyle(22, new Color(0f, 0f, 0f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold);

            _guideNameStyle = MakeStyle(19, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guideNameStyle.wordWrap = false;
            _guideDescStyle = MakeStyle(14, new Color(0.78f, 0.82f, 0.88f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
            _guideDescStyle.wordWrap = true;
            _guideNoteStyle = MakeStyle(17, new Color(0.68f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNoteStyle.wordWrap = false;
            // 按键栏里键名用的，放在淡色方块中间
            _guideKeyStyle = MakeStyle(19, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _guideKeyStyle.wordWrap = false;


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
            _listStyle = new GUIStyle(_btnStyle);
            _listStyle.fontSize = UiSkin.Size(17);
            _listStyle.alignment = TextAnchor.MiddleLeft;
            _listStyle.padding = new RectOffset(Mathf.RoundToInt(UiSkin.P(14f)), Mathf.RoundToInt(UiSkin.P(10f)), 0, 0);

            // 主菜单按钮：底色/悬停沿用上面那三张纯色贴图，字号由 DrawButtons 按缩放系数改写
            _menuBtnStyle = new GUIStyle(_btnStyle);
            _menuBtnStyle.alignment = TextAnchor.MiddleCenter;
            _menuBtnStyle.fontSize = UiSkin.Size(18);
            // 设置各页的大号版本：颜色、对齐、换行设置和上面一一对应，只是字号 ×PageScale
            _panelTitleBig = MakeStylePx(Fg(32), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rowLabelBig = MakeStylePx(Fg(18), new Color(0.72f, 0.76f, 0.82f, 1f), TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintBig = MakeStylePx(Fg(18), new Color(0.72f, 0.76f, 0.82f, 1f), TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintBig.wordWrap = false;
            _valueBig = MakeStylePx(Fg(22), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _sliderShadowBig = MakeStylePx(Fg(22), new Color(0f, 0f, 0f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Bold);
            _guideNameBig = MakeStylePx(Fg(19), Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guideNameBig.wordWrap = false;
            _guideDescBig = MakeStylePx(Fg(14), new Color(0.78f, 0.82f, 0.88f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
            _guideDescBig.wordWrap = true;
            _guideNoteBig = MakeStylePx(Fg(17), new Color(0.68f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNoteBig.wordWrap = false;
            _guideKeyBig = MakeStylePx(Fg(19), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _guideKeyBig.wordWrap = false;
            _btnBig = new GUIStyle(_btnStyle);
            _btnBig.fontSize = Fg(18);

        }

        static GUIStyle MakeStyle(int size, Color color, TextAnchor anchor, FontStyle style)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = UiSkin.Size(size);
            s.normal.textColor = color;
            s.alignment = anchor;
            s.fontStyle = style;
            return s;
        }

        /// <summary>同 MakeStyle，但字号已经是最终像素值（大号样式已经乘过 PageScale，不能再缩一次）。</summary>
        static GUIStyle MakeStylePx(int px, Color color, TextAnchor anchor, FontStyle style)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = px;
            s.normal.textColor = color;
            s.alignment = anchor;
            s.fontStyle = style;
            return s;
        }

        static Texture2D SolidTexture(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void InitMotes()
        {
            if (_motes != null)
                return;

            int count = 170;
            _motes = new Mote[count];
            var rnd = new System.Random(20221025);
            for (int i = 0; i < count; i++)
            {
                _motes[i] = new Mote
                {
                    x = (float)rnd.NextDouble(),
                    y = (float)rnd.NextDouble(),
                    vx = ((float)rnd.NextDouble() - 0.5f) * 0.016f,
                    vy = -0.008f - (float)rnd.NextDouble() * 0.030f,
                    size = 1f + (float)rnd.NextDouble() * 2.4f,
                    phase = (float)rnd.NextDouble() * 6.2831853f,
                    speed = 0.5f + (float)rnd.NextDouble() * 1.3f
                };
            }
        }

        void DrawPortalMotes()
        {
            InitMotes();

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _moteT += dt;

            float w = Screen.width;
            float h = Screen.height;
            float scale = Mathf.Max(1f, h / 900f);

            for (int i = 0; i < _motes.Length; i++)
            {
                Mote m = _motes[i];
                m.x += m.vx * dt;
                m.y += m.vy * dt;

                if (m.x < -0.02f) { m.x = 1.02f; }
                else if (m.x > 1.02f) { m.x = -0.02f; }
                if (m.y < -0.02f) { m.y = 1.02f; }
                else if (m.y > 1.02f) { m.y = -0.02f; }

                _motes[i] = m;

                float breathe = 0.5f + 0.5f * Mathf.Sin(_moteT * m.speed + m.phase);
                float a = 0.16f + 0.62f * breathe;
                Color c = (i % 9 == 0)
                    ? new Color(0.58f, 0.40f, 0.95f, a * 0.85f)
                    : new Color(0.76f, 0.93f, 1f, a);

                float sz = m.size * scale * (0.85f + 0.35f * breathe);
                GUI.color = c;
                GUI.DrawTexture(new Rect(m.x * w, m.y * h, sz, sz), Texture2D.whiteTexture);
            }

            GUI.color = Color.white;
        }

        static void DrawRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
        }

        static float EaseOutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;

            if (t < 1f / d1)
                return n1 * t * t;
            if (t < 2f / d1)
            {
                t -= 1.5f / d1;
                return n1 * t * t + 0.75f;
            }
            if (t < 2.5f / d1)
            {
                t -= 2.25f / d1;
                return n1 * t * t + 0.9375f;
            }
            t -= 2.625f / d1;
            return n1 * t * t + 0.984375f;
        }
    }
}
