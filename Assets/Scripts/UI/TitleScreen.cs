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
        /// <summary>署名用的手写签名（白色笔画 + 透明底，靠 GUI.color 染成黑/白）。</summary>
        Texture2D _signTex;
        CharMeta[] _chars;
        float _t;
        bool _stylesReady;
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

        /// <summary>「请把手机横过来」提示层用的正文样式（独立一份，别去改别处的对齐）。</summary>
        GUIStyle _rotateHintStyle;

        // ---------- 设置各页的大号样式（尺寸统统 × 当前页倍数，见 BeginPage） ----------
        GUIStyle _btnBig;
        GUIStyle _panelTitleBig;
        GUIStyle _panelHeadingBig;
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

        /// <summary>设置主页的滚动位置（加了「屏幕方向」之后一屏放不下）。</summary>
        float _mainScroll;

        /// <summary>特殊方块机制页的滚动位置。列表改成滚轮滚动后，切进页面时归零。</summary>
        Vector2 _mechScroll;

        /// <summary>规则页「特殊方块」栏的滚动位置：手机上一屏放不下十一个条目，靠它上下滑。</summary>
        float _blockScroll;

        /// <summary>手机按键设置页的滚动位置（一列十二行，同样一屏放不下）。</summary>
        float _keyScroll;
        const int SliderMusic = 1;
        const int SliderSizeX = 2;
        const int SliderSizeY = 3;
        const int SliderSizeZ = 4;

        // 时间轴（秒）
        /// <summary>横屏手机：设置页改成「左标题 / 右内容」，和标题页的版式统一。</summary>
        static bool WidePage
        {
            get { return MobileMode.Enabled && Screen.width > Screen.height; }
        }

        /// <summary>
        /// 横屏设置页统一用的设计画布。所有设置页都按这个尺寸排版 ——
        /// 之前每页各有各的（420×648 / 480×900 / 460×500 / 500×360…），
        /// 横屏时受高度限制，页面越高缩放越小，切页就一大一小、字也跟着变。
        /// 统一成一块 480×480 之后，每页的倍数、面板大小、字号完全一致。
        /// </summary>
        const float WidePageW = 480f;
        const float WidePageH = 480f;

        /// <summary>页面切换动画的时长（秒）。</summary>
        const float PageMotionTime = 0.20f;

        /// <summary>当前页面的进入动画计时，切页 / 开合设置面板时归零。</summary>
        float _pageT = 99f;

        /// <summary>
        /// 切页的滑动方向：+1 = 新页从右边滑进来（进子页），-1 = 从左边滑回来（点返回）。
        /// 一律从右边进的话，返回时画面还是往左走，看着像"又往下钻了一层"。
        /// </summary>
        int _pageDir = 1;

        /// <summary>关闭设置面板的动画计时（返回按钮不立刻收起，先滑出去再收）。</summary>
        float _closeT = 99f;
        bool _closing;

        /// <summary>玩家点掉了「请把手机横过来」的提示。改一次方向设置就重新算。</summary>
        bool _rotateHintClosed;

        /// <summary>
        /// 关设置面板。返回按钮都走这里 —— 直接置 ShowSettings = false 的话面板是「啪」一下消失，
        /// 没有切换感（真机反馈「点返回没有动画」）。
        /// </summary>
        public void BeginCloseSettings()
        {
            if (!ShowSettings || _closing)
                return;
            _closing = true;
            _closeT = 0f;
        }
        SettingsPage _pageSeen = SettingsPage.Main;
        bool _settingsSeen;

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
            _mechScroll = Vector2.zero;
            _blockScroll = 0f;
            _keyScroll = 0f;
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

        void LoadAssets()
        {
            _titleTex = Resources.Load<Texture2D>("title");
            if (_titleTex != null)
                _titleTexW = _titleTex.width;
            _atlasTex = Resources.Load<Texture2D>("atlas");
            _signTex = Resources.Load<Texture2D>("monody_sign");
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

            // 切页 / 开关设置面板时把动画计时归零（见 DrawSettingsPanel 里的滑入）
            bool on = ShowSettings;
            if (_page != _pageSeen || on != _settingsSeen)
            {
                // 设置主页是 0，子页都比它大：新页的下标变小就是「点返回回到上一层」
                _pageDir = (on && _settingsSeen && (int)_page < (int)_pageSeen) ? -1 : 1;
                _pageSeen = _page;
                _settingsSeen = on;
                _pageT = 0f;
            }
            else if (_pageT < PageMotionTime)
            {
                _pageT += Time.unscaledDeltaTime;
            }

            // 关闭动画跑完才真正收起面板
            if (_closing)
            {
                _closeT += Time.unscaledDeltaTime;
                if (_closeT >= PageMotionTime)
                {
                    _closing = false;
                    ShowSettings = false;
                }
            }
        }

        void OnGUI()
        {
            if (!Visible)
                return;

            // 标题 / 设置页是画得最多的一块（光尘、标题、按钮、滑条）。鼠标动一下
            // IMGUI 就整段重画一遍，拖窗口时一帧十几遍 —— 挡掉白跑的那几趟。
            if (!UiSkin.WorthDrawing())
                return;

            UiSkin.Apply();
            EnsureStyles();

            if (ShowSettings || _closing)
            {
                // 关闭动画期间先把标题页画出来垫在下面，面板滑出去时露出的是它，
                // 而不是一片空白
                if (_closing)
                    DrawTitleScreen();

                if (_page == SettingsPage.Keys && !_closing)
                    HandleCapture();
                DrawSettingsPanel();
                CursorTrail.DrawLast();   // 拖尾压在设置面板之上
                return;
            }

            // 选了横屏但这台设备锁不了方向时，只画提示层。
            // 这里不能先把标题页画出来再盖一层 —— IMGUI 的点击是「先画的控件先拿到
            // 热键」，底下那排按钮会照样抢走鼠标（实测点「先用竖屏」直接进了创意工坊）。
            if (MobileMode.WantsLandscapeButPortrait && !_rotateHintClosed)
            {
                DrawRotateHint();
                CursorTrail.DrawLast();
                return;
            }

            DrawTitleScreen();

            CursorTrail.DrawLast();   // 永远最后画
        }

        /// <summary>标题页整屏（雾化层 + 光尘 + 标题 + 按钮 + 署名）。</summary>
        void DrawTitleScreen()
        {
            // 亮色主题不能压黑（整屏会变成一团深灰），改成压一层浅色雾化
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height),
                UiSkin.Current == UiSkin.Theme.Light
                    ? new Color(0.93f, 0.96f, 1f, 0.55f)
                    : new Color(0f, 0f, 0f, 0.55f));

            DrawPortalMotes();

            DrawTitle();
            DrawSub();
            DrawButtons();
            DrawCredit();
        }

        // ---------- 动画各段 ----------

        /// <summary>标题（那张像素 logo）的缩放步长与高度。</summary>
        /// <summary>logo 原图的宽度，加载后写入 —— 竖屏窄的时候要靠它把倍数压下来。</summary>
        static int _titleTexW = 140;

        /// <summary>
        /// 手机横屏时的标题页版式：左边标题、右边一列放大的菜单。
        /// 竖屏和桌面仍然是「标题在上、按钮在下」那一套。
        /// </summary>
        static bool WideTitle
        {
            get { return MobileMode.Enabled && Screen.width > Screen.height; }
        }

        static int TitlePixelScale()
        {
            // 横屏时标题只占左半屏，可用宽度和高度都得按那半边算
            float availW = WideTitle ? Screen.width * 0.5f * 0.88f : Screen.width * 0.94f;
            float hRatio = WideTitle ? 0.17f : 0.105f;

            int byHeight = Mathf.Max(2, Mathf.RoundToInt(Screen.height * hRatio / 16f));
            // 只按屏高算的话，竖屏（宽高比 0.47）上 logo 会宽出屏幕一大截，
            // 两边都被切掉 —— 按可用宽度再卡一道，取小的那个
            int byWidth = Mathf.Max(2, Mathf.FloorToInt(availW / Mathf.Max(1, _titleTexW)));
            return Mathf.Min(byHeight, byWidth);
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
            // 横屏：在左半屏里居中、竖直方向也居中
            float left = WideTitle
                ? (Screen.width * 0.5f - titleW) * 0.5f
                : (Screen.width - titleW) * 0.5f;
            float targetY = WideTitle
                ? (Screen.height - titleH) * 0.42f
                : TitleTopY();

            for (int i = 0; i < _chars.Length; i++)
            {
                CharMeta ch = _chars[i];
                if (ch.space)
                    continue;

                float t0 = TitleStart + i * CharDelay;
                float p = Mathf.Clamp01((_t - t0) / CharFall);
                if (p <= 0f)
                    continue;

                // 横屏：逐字显现 —— 每个字在落点上原地淡入（带一点上浮），一个一个亮起来。
                // 竖屏 / 桌面：保留原来那套「从屏幕上方掉下来、落地弹一下」。
                // 两套要分开：横屏的标题在左半屏，从上往下掉会擦过顶部、也压不住左边那块空白；
                // 而且用户明确要求横屏用逐字显现，别跟竖屏合并。
                float y, alpha;
                if (WideTitle)
                {
                    y = targetY + (1f - p) * UiSkin.P(12f);
                    alpha = Mathf.Clamp01(p);
                }
                else
                {
                    y = Mathf.Lerp(-titleH, targetY, EaseOutBounce(p));
                    alpha = Mathf.Clamp01(p * 4f);
                }

                var dst = new Rect(left + ch.x * scale, y, ch.w * scale, titleH);
                var uv = new Rect((float)ch.x / texW, 0f, (float)ch.w / texW, 1f);

                // logo 是白色像素图，压在浅底上会糊掉：亮色主题把它整体压成深灰，
                // 投影也跟着反过来用浅色，字才浮得起来。
                bool light = UiSkin.Current == UiSkin.Theme.Light;
                GUI.color = light ? new Color(1f, 1f, 1f, alpha * 0.85f) : new Color(0f, 0f, 0f, alpha * 0.75f);
                GUI.DrawTextureWithTexCoords(new Rect(dst.x + scale * 0.25f, dst.y + scale * 0.25f, dst.width, dst.height), _titleTex, uv);

                GUI.color = light ? new Color(0.16f, 0.20f, 0.27f, alpha) : new Color(1f, 1f, 1f, alpha);
                GUI.DrawTextureWithTexCoords(dst, _titleTex, uv);
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// 署名一行：签名 LOGO + 「Made by Monody」。
        ///
        /// 原来这里只有一行字，前面还有一段「Made with Unity」（白色，亮色主题下
        /// 压在白底上几乎看不见）。现在统一成自己的署名：那段去掉，这一行换成
        /// 主文字色（浅色近黑、深色纯白），左边挂上签名图。
        /// 签名图是「白色笔画 + 透明底」，靠 GUI.color 染色，所以两个主题共用一张贴图。
        /// </summary>
        void DrawSub()
        {
            float p = Mathf.Clamp01((_t - SubStart) / 0.7f);
            if (p <= 0f)
                return;

            Color ink = UiSkin.TextPrimary;
            ink.a = p;
            _subStyle.normal.textColor = ink;

            const string text = "Made by Monody";
            Vector2 size = _subStyle.CalcSize(new GUIContent(text));

            float lineH = UiSkin.P(44f);
            float logoH = size.y * 1.15f;
            float logoW = _signTex != null ? logoH * _signTex.width / Mathf.Max(1f, _signTex.height) : 0f;
            float gap = logoW > 0f ? UiSkin.P(10f) : 0f;

            // 紧贴标题下方：原来固定在 0.42H，按钮放大后会和第一颗按钮打架
            float subY = WideTitle
                ? (Screen.height - TitleHeight()) * 0.42f + TitleHeight()
                : TitleTopY() + TitleHeight();

            // 横屏：签名跟着标题在左半屏居中；竖屏：整屏居中
            float centerX = WideTitle ? Screen.width * 0.25f : Screen.width * 0.5f;
            float rowW = logoW + gap + size.x;
            float x = centerX - rowW * 0.5f;

            if (_signTex != null)
            {
                GUI.color = ink;
                GUI.DrawTexture(
                    new Rect(x, subY + (lineH - logoH) * 0.5f, logoW, logoH),
                    _signTex, ScaleMode.ScaleToFit, true);
                GUI.color = Color.white;
            }

            GUI.Label(new Rect(x + logoW + gap, subY, size.x + 2f, lineH), text, _subStyle);
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
#if !UNITY_WEBGL
            // 网页版没有「退出」这回事：Application.Quit 在浏览器里是空操作，
            // 摆一个点了没反应的按钮比不摆更让人困惑（关标签页就行）
            items.Add(new KeyValuePair<string, Action>("退出", () => Fire(OnQuit)));
#endif

            int n = items.Count;

            // 横屏：右半屏竖排一列，上下留边；竖屏：标题下方一条竖直带
            float bandTop;
            float bandBottom;
            float maxW;
            if (WideTitle)
            {
                bandTop = UiSkin.P(46f);
                bandBottom = Screen.height - UiSkin.P(46f);
                maxW = Screen.width * 0.5f * 0.80f;
            }
            else
            {
                bandTop = TitleTopY() + TitleHeight() + UiSkin.P(2f) + UiSkin.P(44f) + UiSkin.P(20f);
                bandBottom = Screen.height - UiSkin.P(62f) - UiSkin.P(10f);
                maxW = Screen.width * 0.86f;
            }

            MenuLayout lay = MenuLayout.Compute(
                n,
                WideTitle ? UiSkin.P(320f) : UiSkin.P(280f),
                WideTitle ? UiSkin.P(52f) : UiSkin.P(44f),
                UiSkin.P(10f),
                bandTop, bandBottom,
                maxW,
                UiSkin.MenuScale);

            // 字号跟着按钮一起放大，否则按钮长大字没长，反而更空
            // 横屏的按钮又高又宽，按 18 号配上去字显得很小，单独放大一档
            int baseSize = WideTitle ? 21 : 18;
            _menuBtnStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(UiSkin.Size(baseSize) * lay.scale));

            for (int i = 0; i < n; i++)
            {
                float ti = BtnStart + i * 0.1f;
                float p = Mathf.Clamp01((_t - ti) / 0.35f);
                if (p <= 0f)
                    continue;

                float y = lay.startY + i * (lay.height + lay.gap);
                float x;
                if (WideTitle)
                {
                    // 横屏：贴在右半屏居中，并从右侧屏幕外滑进来（一个接一个）
                    x = Screen.width * 0.5f + (Screen.width * 0.5f - lay.width) * 0.5f;
                    x += (1f - p) * Screen.width * 0.45f;
                }
                else
                {
                    // 竖屏：从下方轻轻浮起
                    x = (Screen.width - lay.width) * 0.5f;
                    y += (1f - p) * UiSkin.P(18f);
                }
                var rect = new Rect(x, y, lay.width, lay.height);

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

            // 这行是淡色署名：亮色主题下白字在浅底上看不见，换成深灰
            _creditStyle.normal.textColor = UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.42f, 0.47f, 0.55f, p * 0.9f)
                : new Color(1f, 1f, 1f, p * 0.85f);
            float creditW = WideTitle ? Screen.width * 0.5f : Screen.width;
            GUI.Label(new Rect(0f, Screen.height - UiSkin.P(62f), creditW, UiSkin.P(28f)),
                "AGENTS辅助完成", _creditStyle);
        }

        // ---------- 设置面板 ----------

        void DrawSettingsPanel()
        {
            // 关闭时雾化层跟着淡出，露出来的就是下面那张标题页
            float closeK = _closing ? Mathf.Clamp01(_closeT / PageMotionTime) : 1f;
            float dim = _closing ? 1f - closeK : 1f;
            Color dimColor = UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.90f, 0.94f, 0.99f, 0.72f * dim)
                : new Color(0f, 0f, 0f, 0.72f * dim);
            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), dimColor);

            // 页面切换：从右侧轻轻滑进来一小段（34 设计像素，0.2 秒）；关闭时反向滑出去。
            // 只挪 GUI.matrix 不动布局 —— 控件的点击判定跟着矩阵走，滑完照样点得动。
            // 没做淡入：各页里到处 DrawRect（自己会把 GUI.color 复位成白色），
            // 全局淡入半路就断了，滑入这一下已经够「切换感」。
            float k = Mathf.Clamp01(_pageT / PageMotionTime);
            float ease = Mathf.SmoothStep(0f, 1f, k);
            float offset = (1f - ease) * Pg(34f) * _pageDir;
            if (_closing)
                offset = Mathf.SmoothStep(0f, 1f, closeK) * Pg(34f);

            Matrix4x4 matBack = GUI.matrix;
            if (Mathf.Abs(offset) > 0.01f)
                GUI.matrix = matBack * Matrix4x4.Translate(new Vector3(offset, 0f, 0f));

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

            GUI.matrix = matBack;
        }

        /// <summary>
        /// 设置主页一共几行。手机上是十行（含「屏幕方向」），桌面上把「屏幕方向」
        /// 换成「按键设置」—— 桌面的窗口比例由玩家自己拉，网页版也会跟着重排，
        /// 摆一个改不了任何东西的按钮只会让人以为坏了。
        /// </summary>
        static int SettingRowCount()
        {
            return 10;
        }

        void DrawSettingsMain()
        {
            Rect panel = BeginPage(420f, 648f);
            float w = panel.width;
            DrawPanel(panel);

            PageTitle(panel, "设置", 16f);

            bool wide = WidePage;
            float bw = Pg(wide ? 340f : 300f);
            float bh = Pg(wide ? 36f : 42f);
            float step = Pg(wide ? 44f : 54f);
            float bx = panel.x + (w - bw) * 0.5f;

            // 列表可滚动：加进「屏幕方向」之后已经十项，横屏的画布放不下一屏。
            // 和「特殊方块机制」页同一套做法（滚轮 + 手指拖动 + 整行吸附）。
            int count = SettingRowCount();
            float top = panel.y + Pg(wide ? 34f : 78f);
            float viewH = Mathf.Max(bh, panel.yMax - Pg(wide ? 12f : 18f) - top);
            float contentH = count * step - (step - bh);
            float maxScroll = Mathf.Max(0f, contentH - viewH);

            var clip = new Rect(bx, top, bw, viewH);
            ScrollList(clip, maxScroll, ref _mainScroll, step);

            GUI.BeginGroup(clip);
            for (int i = 0; i < count; i++)
            {
                var r = new Rect(0f, i * step - _mainScroll, bw, bh);
                if (r.y + bh < 0f || r.y > viewH)
                    continue;   // 看不见的不画

                DrawSettingRow(i, r);
            }
            GUI.EndGroup();

            if (maxScroll > 0f)
                DrawScrollBar(clip, viewH, contentH, maxScroll, _mainScroll);
        }

        /// <summary>
        /// 画设置主页的第 i 行。坐标是相对于滚动区左上角的（外面套了 BeginGroup），
        /// 所以这里直接用 0 / r 里的坐标，不要再加 panel.x。
        /// </summary>
        void DrawSettingRow(int i, Rect r)
        {
            // 手机上不显示「按键设置」：只有触屏，键盘映射没有意义；
            // 桌面上反过来，把「屏幕方向」那一格换成「按键设置」—— 桌面窗口比例
            // 自己拉就行，网页版也会跟着重排，不需要这一项。
            int idx = i;
            if (!MobileMode.Enabled && i == 6)
            {
                if (GUI.Button(r, "按键设置", _btnBig))
                {
                    _page = SettingsPage.Keys;
                    _capturingAction = -1;
                    _keyScroll = 0f;
                }
                return;
            }

            switch (idx)
            {
                case 0:
                    if (GUI.Button(r, SfxEnabled ? "音效：开" : "音效：关", _btnBig))
                    {
                        bool next = !SfxEnabled;
                        PlayerPrefs.SetInt(SfxKey, next ? 1 : 0);
                        PlayerPrefs.Save();
                        if (OnSfxChanged != null)
                            OnSfxChanged(next);
                    }
                    break;

                case 1:
                    // 背景配色：手机在户外/强光下黑底几乎看不见东西，亮色底能救回来。
                    if (GUI.Button(r, UiSkin.Current == UiSkin.Theme.Light ? "背景：亮色" : "背景：暗色", _btnBig))
                    {
                        UiSkin.Current = UiSkin.Current == UiSkin.Theme.Light
                            ? UiSkin.Theme.Dark
                            : UiSkin.Theme.Light;
                    }
                    break;

                case 2:
                {
                    // 深度 1 时一层就是一行，层消没有意义，按钮直接置灰
                    bool canLayer = GameSettings.CanUseLayerMode;
                    string elimName = !canLayer
                        ? "消除：行消（Z=1 固定）"
                        : (GameSettings.ElimMode == GameSettings.ElimLayer ? "消除：层消" : "消除：行消");
                    GUI.enabled = canLayer;
                    bool elimClicked = GUI.Button(r, elimName, _btnBig);
                    GUI.enabled = true;
                    // IMGUI 关掉 GUI.enabled 只是不吃点击，自定义贴图不会自己变灰，压一层半透明盖住
                    if (!canLayer)
                        DrawRect(r, new Color(0.05f, 0.06f, 0.08f, 0.45f));

                    if (elimClicked)
                    {
                        int next = GameSettings.ElimMode == GameSettings.ElimLayer
                            ? GameSettings.ElimRows
                            : GameSettings.ElimLayer;
                        GameSettings.SetElimMode(next);
                    }
                    break;
                }

                case 3:
                    if (GUI.Button(r, "下落速度：" + GameSettings.FallSpeed.ToString("0.00") + "×", _btnBig))
                    {
                        _tmpSpeed = GameSettings.FallSpeed;
                        _page = SettingsPage.Speed;
                    }
                    break;

                case 4:
                {
                    string musicName = GameSettings.MusicVolume <= 0.005f
                        ? "音乐：关"
                        : "音乐：" + Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";
                    if (GUI.Button(r, musicName, _btnBig))
                    {
                        _tmpMusic = GameSettings.MusicVolume;
                        _page = SettingsPage.Music;
                    }
                    break;
                }

                case 5:
                    // 落点提示：下落时把方块会停在哪儿用高亮方框标出来（3D 场地里不标很难估）
                    if (GUI.Button(r, GameSettings.GhostPiece ? "落点提示：开" : "落点提示：关", _btnBig))
                        GameSettings.SetGhostPiece(!GameSettings.GhostPiece);
                    break;

                case 6:
                    DrawScreenOrientationRow(r);
                    break;

                case 7:
                    if (GUI.Button(r, "场地尺寸", _btnBig))
                    {
                        _tmpX = GameSettings.SizeX;
                        _tmpY = GameSettings.SizeY;
                        _tmpZ = GameSettings.SizeZ;
                        _page = SettingsPage.Size;
                    }
                    break;

                case 8:
                    // 特殊方块各机制的开关都收在这一页里
                    if (GUI.Button(r, "特殊方块开关", _btnBig))
                    {
                        _mechScroll = Vector2.zero;
                        _page = SettingsPage.Mech;
                    }
                    break;

                case 9:
                    if (GUI.Button(r, "返回", _btnBig))
                        BeginCloseSettings();
                    break;
            }
        }

        /// <summary>
        /// 「屏幕方向」这一行：自动 / 横屏 / 竖屏 循环。
        ///
        /// 网页版把这个选择推给页面：横屏 / 竖屏 会让浏览器进全屏再调
        /// <c>screen.orientation.lock()</c>，系统真的转屏之后窗口尺寸变化、
        /// 模板把新尺寸写回画布，Unity 就按新方向排版（见 WebGLTemplates 里的模板）。
        /// 桌面版没有这回事，窗口比例由玩家自己拉。
        /// </summary>
        void DrawScreenOrientationRow(Rect r)
        {
            string name;
            switch (MobileMode.Orientation)
            {
                case MobileMode.ScreenMode.Landscape: name = "屏幕：横屏"; break;
                case MobileMode.ScreenMode.Portrait: name = "屏幕：竖屏"; break;
                default: name = "屏幕：自动"; break;
            }

            if (GUI.Button(r, name, _btnBig))
            {
                MobileMode.ScreenMode next;
                switch (MobileMode.Orientation)
                {
                    case MobileMode.ScreenMode.Auto: next = MobileMode.ScreenMode.Landscape; break;
                    case MobileMode.ScreenMode.Landscape: next = MobileMode.ScreenMode.Portrait; break;
                    default: next = MobileMode.ScreenMode.Auto; break;
                }
                MobileMode.SetOrientation(next);
                _rotateHintClosed = false;   // 换了一次设置，提示重新算
            }
        }

        /// <summary>
        /// 「选了横屏，但屏幕还是竖的」时压一层提示。
        ///
        /// 网页版锁方向只能靠 <c>screen.orientation.lock()</c>，iOS Safari 和微信 /
        /// QQ 内置浏览器都没实现，安卓 Chrome 也要先进全屏才行。这些环境上点了横屏
        /// 不会有任何反应 —— 与其让人以为按钮坏了，不如直接说清楚：把手机横过来就好，
        /// 转过去之后模板会把新尺寸写回画布，版式自己就切了。
        /// </summary>
        void DrawRotateHint()
        {
            if (!MobileMode.WantsLandscapeButPortrait || _rotateHintClosed)
                return;

            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height),
                UiSkin.Current == UiSkin.Theme.Light
                    ? new Color(0.93f, 0.96f, 1f, 0.88f)
                    : new Color(0f, 0f, 0f, 0.88f));

            float w = Mathf.Min(Screen.width * 0.86f, UiSkin.P(300f));
            float h = UiSkin.P(200f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            int titleSize = Mathf.Max(14, Mathf.RoundToInt(UiSkin.Size(24)));
            _panelTitleStyle.fontSize = titleSize;
            _panelTitleStyle.normal.textColor = UiSkin.TextPrimary;
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(26f), panel.width, UiSkin.P(38f)),
                "请把手机横过来", _panelTitleStyle);

            _rotateHintStyle.fontSize = Mathf.Max(11, Mathf.RoundToInt(UiSkin.Size(14)));
            _rotateHintStyle.normal.textColor = UiSkin.TextSecondary;
            GUI.Label(new Rect(panel.x + UiSkin.P(14f), panel.y + UiSkin.P(72f),
                    panel.width - UiSkin.P(28f), UiSkin.P(24f)),
                "这台设备的浏览器不支持网页锁定方向", _rotateHintStyle);
            GUI.Label(new Rect(panel.x + UiSkin.P(14f), panel.y + UiSkin.P(100f),
                    panel.width - UiSkin.P(28f), UiSkin.P(24f)),
                "横握手机后版式会自动切过去", _rotateHintStyle);

            Rect ok = new Rect(panel.x + (panel.width - UiSkin.P(160f)) * 0.5f,
                panel.yMax - UiSkin.P(56f), UiSkin.P(160f), UiSkin.P(40f));
            _menuBtnStyle.fontSize = Mathf.Max(12, Mathf.RoundToInt(UiSkin.Size(17)));
            if (GUI.Button(ok, "先用竖屏", _menuBtnStyle))
                _rotateHintClosed = true;
        }

        /// <summary>
        /// 特殊方块机制开关。十项排一列，关掉只是这种方块不再按特殊规则动，
        /// 方块本身还在池子里（改成不进池会让玩家在场上再也见不到它）。
        /// </summary>
        void DrawSettingsMech()
        {
            // 十四项改单列 + 滚轮滚动。原来排三列，界面放大一倍后宽度直接溢出屏幕；
            // 而且机制以后还会加，行数变多固定面板迟早又出事。改成滚动列表一劳永逸：
            // 面板高度钳在屏幕内，装不下的部分用滚轮看，右侧画一条自绘的滚动指示条。
            // 必须先 BeginPage：下面的 rowH / colW 都要用这一页刚算出来的倍数
            // 上下各留一截、装不下就折减倍数：标题和返回键永远在屏内
            Rect panel = BeginPage(480f, 900f);
            float w = panel.width;
            float h = panel.height;
            DrawPanel(panel);

            // 横屏画布只有 480 高：行高压到 32、间距 5，十四项能露出八行
            bool wide = WidePage;
            float rowH = Pg(wide ? 32f : 46f);
            float rowGap = Pg(wide ? 5f : 10f);
            float colW = Pg(400f);

            PageTitle(panel, "特殊方块机制", 12f);

            float top = panel.y + Pg(wide ? 44f : 66f);
            // 底下固定不滚的：说明行 + 一键开关 + 返回。
            // 比说明行本身多留 10，不然列表底边正好压在说明字上（截图里挤在一起）
            float bottomReserve = Pg(wide ? 130f : 182f);
            // 可视区取整行：不取整的话裁剪区底边会露出半行字（列表最后一行被切成两半，
            // 底下的说明文字压在上面，看着像排版坏了）
            float availH = Mathf.Max(rowH, panel.yMax - bottomReserve - top);
            int rowsFit = Mathf.Max(1, Mathf.FloorToInt((availH + rowGap) / (rowH + rowGap)));
            float viewH = rowsFit * (rowH + rowGap) - rowGap;
            float contentH = GameSettings.MechCount * (rowH + rowGap) - rowGap;
            float maxScroll = Mathf.Max(0f, contentH - viewH);

            Rect clip = new Rect(panel.x + Pg(40f), top, colW, viewH);

            // 滚轮：悬停在列表上就滚。这里手动消费 ScrollWheel 事件，步长一行多一点，
            // 不用 BeginScrollView 是因为它的滚动条样式和这套手绘 UI 对不上，
            // 隐藏滚动条之后滚轮行为又没法控制，干脆 BeginGroup 裁剪 + 自己记滚动量。
            if (Event.current.type == EventType.ScrollWheel && clip.Contains(Event.current.mousePosition))
            {
                _mechScroll.y = Mathf.Clamp(_mechScroll.y + Event.current.delta.y * Pg(48f), 0f, maxScroll);
                Event.current.Use();
            }
            // 手机上 WebGL 不把触摸转成 MouseDrag，只能自己读 touches
            if (MobileMode.Enabled)
                DragScroll(clip, ref _mechScroll.y, maxScroll);

            GUI.BeginGroup(clip);
            for (int i = 0; i < GameSettings.MechCount; i++)
            {
                GameSettings.MechKind kind = (GameSettings.MechKind)i;
                bool on = GameSettings.Mech(kind);
                string label = GameSettings.MechLabel(kind) + (on ? "：开" : "：关");

                float y = i * (rowH + rowGap) - _mechScroll.y;
                if (y + rowH < 0f || y > viewH)        // 看不见的不画，省一点开销
                    continue;

                if (GUI.Button(new Rect(0f, y, colW, rowH), label, _btnBig))
                    GameSettings.SetMech(kind, !on);
            }
            GUI.EndGroup();

            // 自绘滚动指示条（内容放得下时不画）
            if (maxScroll > 0f)
            {
                float barX = clip.xMax + Pg(10f);
                float barW = Pg(6f);
                float barH = Mathf.Max(Pg(36f), viewH * viewH / contentH);
                float barY = top + (viewH - barH) * (_mechScroll.y / maxScroll);
                DrawRect(new Rect(barX, top, barW, viewH), new Color(1f, 1f, 1f, 0.08f));
                DrawRect(new Rect(barX, barY, barW, barH), new Color(0.35f, 0.37f, 0.44f, 0.95f));
            }

            GUI.Label(new Rect(panel.x, panel.y + h - Pg(wide ? 122f : 172f), w, Pg(26f)),
                "关掉只是这种方块不再按特殊规则动，方块照常出现", _guideNoteBig);

            // 一键开关：全开时这颗键是「一键关闭」，否则是「一键开启」——
            // 十四项一个一个点太烦，想快速对比「全关 vs 全开」时用得上。
            float bw = Pg(wide ? 300f : 260f);
            bool allOn = GameSettings.AllMechOn();
            if (GUI.Button(new Rect(panel.x + (w - bw) * 0.5f, panel.y + h - Pg(wide ? 94f : 132f),
                        bw, Pg(wide ? 34f : 42f)),
                    allOn ? "一键关闭" : "一键开启", _btnBig))
                GameSettings.SetAllMech(!allOn);

            if (GUI.Button(new Rect(panel.x + (w - bw) * 0.5f, panel.y + h - Pg(wide ? 50f : 74f),
                        bw, Pg(wide ? 36f : 44f)), "返回", _btnBig))
                _page = SettingsPage.Main;
        }

        void DrawSettingsKeys()
        {
            // 手机上单列 + 上下滑：12 组键排两列的话，竖屏下整页要缩到 0.35 倍，
            // 键名只剩几个像素。单列以后行高能保住，装不下的往下滚。
            if (MobileMode.Enabled)
            {
                DrawSettingsKeysMobile();
                return;
            }

            // 12 组键排成两列（每列 6 行）。原来是一条竖列 12 行，整页放大一倍之后
            // 行数 × 行高会直接顶出屏幕，只能改成两列。
            const int perCol = 6;
            int cols = (AllActions.Length + perCol - 1) / perCol;

            // 必须先 BeginPage：下面的 rowH / colW 都要用这一页刚算出来的倍数
            // 设计值：80 + 2×400 + 60 宽；62 + 6×52 + 104 高
            Rect panel = BeginPage(940f, 478f);
            float w = panel.width;
            float h = panel.height;
            DrawPanel(panel);

            float rowH = Pg(44f);
            float rowGap = Pg(8f);
            float colW = Pg(400f);
            float colGap = Pg(60f);

            PageTitle(panel, "按键设置", 12f);

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

        /// <summary>
        /// 手机上的按键设置：一列十二行，上下滑。
        /// 行高按 52 设计像素给，面板尺寸和规则页统一（460×790），页与页之间不跳。
        /// </summary>
        void DrawSettingsKeysMobile()
        {
            Rect panel = BeginPage(460f, 790f);
            float w = panel.width;
            float h = panel.height;
            DrawPanel(panel);

            PageTitle(panel, "按键设置", 12f);

            bool wide = WidePage;
            float rowH = Pg(wide ? 34f : 52f);
            float gap = Pg(wide ? 6f : 8f);
            float top = panel.y + Pg(wide ? 44f : 66f);
            // 底下固定不滚的：提示行 + 「恢复默认 / 返回」两个按钮
            float bottomReserve = Pg(wide ? 104f : 140f);
            float gap2 = gap;
            float avail = Mathf.Max(rowH + gap2, panel.yMax - bottomReserve - top);
            // 同上：行数取整、余量摊到每行，底边不空、也不露半行
            int rows = Mathf.Max(1, Mathf.FloorToInt(avail / (rowH + gap2)));
            float viewH = avail;
            float step = avail / rows;
            float contentH = AllActions.Length * step - gap2;
            float maxScroll = Mathf.Max(0f, contentH - viewH);

            // 右边单独留一列给滚动条，不然条子直接压住键名按钮
            float rowW = w - Pg(80f) - Pg(16f);
            var clip = new Rect(panel.x + Pg(40f), top, rowW, viewH);

            ScrollList(clip, maxScroll, ref _keyScroll, step);

            float keyW = Pg(140f);
            // 行的实际高度：step 是均分后的步进，扣掉间距就是控件高度。
            // 绘制必须和 ScrollList 的吸附步进一致，否则滚完会错位半行。
            float lineH = step - gap2;
            GUI.BeginGroup(clip);
            for (int i = 0; i < AllActions.Length; i++)
            {
                float y = i * step - _keyScroll;
                if (y + lineH < 0f || y > viewH)
                    continue;   // 看不见的不画

                GameAction action = AllActions[i];
                GUI.Label(new Rect(0f, y, rowW - keyW - Pg(10f), lineH), ActionLabel(action), _rowLabelBig);

                string keyName = _capturingAction == (int)action
                    ? "按任意键…"
                    : KeyName(KeyboardInput.GetPrimaryKey(action));

                if (GUI.Button(new Rect(rowW - keyW, y, keyW, lineH), keyName, _btnBig))
                    _capturingAction = (int)action;
            }
            GUI.EndGroup();

            DrawScrollBar(clip, viewH, contentH, maxScroll, _keyScroll);

            if (_capturingAction >= 0)
                GUI.Label(new Rect(panel.x, panel.y + h - Pg(wide ? 100f : 132f), w, Pg(28f)),
                    "按 Esc 取消", _rowLabelBig);

            float bw = (w - Pg(120f)) * 0.5f;
            float bottomY = panel.y + h - Pg(wide ? 52f : 66f);
            if (GUI.Button(new Rect(panel.x + Pg(40f), bottomY, bw, Pg(44f)), "恢复默认", _btnBig))
            {
                ResetKeys();
                if (OnKeysChanged != null)
                    OnKeysChanged();
            }
            if (GUI.Button(new Rect(panel.xMax - Pg(40f) - bw, bottomY, bw, Pg(44f)), "返回", _btnBig))
            {
                _capturingAction = -1;
                _page = SettingsPage.Main;
            }
        }

        void DrawSettingsSize()
        {
            Rect panel = BeginPage(460f, 500f);
            float w = panel.width;
            DrawPanel(panel);

            PageTitle(panel, "场地尺寸", 14f);

            // 三个轴都是拖拽滑条，和下落速度、音乐音量一个样式。
            // 这里只在内存里改 _tmpX/Y/Z，真正落盘要等下面「应用并重启」——尺寸一变就得清存档重建场地。
            bool wide = WidePage;
            float rowW = Pg(wide ? 400f : 360f);
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + Pg(wide ? 52f : 76f);

            DrawSizeSlider(SliderSizeX, bx, y, rowW, "宽度 X", GameSettings.MinSizeX, GameSettings.MaxSizeX, ref _tmpX);
            y += Pg(wide ? 50f : 58f);
            DrawSizeSlider(SliderSizeY, bx, y, rowW, "高度 Y", GameSettings.MinSizeY, GameSettings.MaxSizeY, ref _tmpY);
            y += Pg(wide ? 50f : 58f);
            DrawSizeSlider(SliderSizeZ, bx, y, rowW, "深度 Z", GameSettings.MinSizeZ, GameSettings.MaxSizeZ, ref _tmpZ);
            y += Pg(wide ? 50f : 58f);

            GUI.Label(new Rect(bx, y, rowW, Pg(28f)), "拖动滑条调大小，改完点「应用」", _hintBig);
            y += Pg(wide ? 26f : 28f);
            GUI.Label(new Rect(bx, y, rowW, Pg(28f)), "深度大于 1 才有前后移动和层消", _hintBig);

            // 横屏统一画布只有 480 高，两颗按钮贴底摆（和下落速度 / 音乐页一致）
            y = wide ? panel.y + panel.height - Pg(96f) : y + Pg(34f);

            // 文案从「应用并重启」改成「应用」：它确实会重载场景（尺寸变了只能重建场地），
            // 但重载后直接回到标题页、不再播开场动画，玩家感觉不到「重启」这一步。
            if (GUI.Button(new Rect(bx, y, rowW, Pg(wide ? 38f : 44f)), "应用", _btnBig))
            {
                GameSettings.SetSize(_tmpX, _tmpY, _tmpZ);
                if (OnSizeApplied != null)
                    OnSizeApplied();
            }
            y += Pg(wide ? 46f : 54f);

            if (GUI.Button(new Rect(bx, y, rowW, Pg(wide ? 34f : 42f)), "返回", _btnBig))
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
            Rect panel = BeginPage(500f, 360f);
            float w = panel.width;
            float h = panel.height;
            DrawPanel(panel);

            PageTitle(panel, "下落速度", 14f);

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
            Rect panel = BeginPage(500f, 360f);
            float w = panel.width;
            float h = panel.height;
            DrawPanel(panel);

            PageTitle(panel, "音乐音量", 14f);

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
            // 配色跟主题走：亮色主题下轨道要浅、描边要淡，否则整条黑槽压在浅色页面上
            bool light = UiSkin.Current == UiSkin.Theme.Light;
            Color border = lit
                ? (light ? new Color(0.55f, 0.60f, 0.68f, 0.95f) : new Color(0.62f, 0.66f, 0.74f, 0.95f))
                : (light ? new Color(0.78f, 0.82f, 0.88f, 0.95f) : new Color(0.35f, 0.37f, 0.44f, 0.95f));

            DrawRect(r, light ? new Color(0.87f, 0.91f, 0.96f, 0.96f) : new Color(0.13f, 0.15f, 0.19f, 0.96f));
            // 已拖过的部分淡淡的填一层，一眼能看出现在到哪儿了
            DrawRect(new Rect(r.x + 1f, r.y + 1f, (r.width - 2f) * t, r.height - 2f),
                new Color(UiSkin.Accent.r, UiSkin.Accent.g, UiSkin.Accent.b, 0.16f));
            DrawRect(new Rect(r.x, r.y, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.y, 1f, r.height), border);
            DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);

            // 把手：浅灰一块，右边压一道深色，看着像凸起来的塑料件
            var knob = new Rect(r.x + travel * t, r.y + 1f, handle, r.height - 2f);
            // 圆头用中灰而不是浅灰：字要从它上面压过去，浅灰上文字会糊成一片
            DrawRect(knob, light ? new Color(0.55f, 0.62f, 0.72f, 1f) : new Color(0.46f, 0.49f, 0.56f, 1f));
            DrawRect(new Rect(knob.xMax - 1f, knob.y, 1f, knob.height),
                light ? new Color(0.40f, 0.46f, 0.55f, 1f) : new Color(0.26f, 0.28f, 0.33f, 1f));

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

        /// <summary>
        /// 这一栏在当前平台上显不显示。
        /// 手机只有触屏，键盘映射没有意义，按键栏整栏不出现（设置页里那个入口也一起去掉了）。
        /// </summary>
        static bool GuideTabVisible(int tab)
        {
            return !(MobileMode.Enabled && tab == GuideTabKeys);
        }
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
            new[] { "游戏场地", "默认 10 × 20 × 1，可在「设置 → 场地尺寸」里调。" },
            // 第三段是这一条在手机上的说法：手机上既没有键盘，也没有「按键设置」那一页
            new[] { "方向选择", "支持沿 x、y、z 轴任意旋转 90°，键位可以在 设置 → 按键设置 里自定义。",
                    "支持沿 x、y、z 轴任意旋转 90°，用右侧的旋转按键操作。" },
            new[] { "特殊方块", "一些方块拥有特殊机制，详见本页的「特殊方块」栏。" },
            new[] { "创意工坊", "玩家可以自定义下落方块的样式。" },
        };

        /// <summary>取第 i 段正文：手机上优先用第三段（同一件事的触屏说法）。</summary>
        static string PlayRuleDesc(int i)
        {
            string[] row = PlayRules[i];
            if (MobileMode.Enabled && row.Length > 2)
                return row[2];
            return row.Length > 1 ? row[1] : "";
        }

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
                    Desc = "远古残骸、下界合金块、强化深板岩、命令方块都炸不掉。",
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
                    Desc = "先落雪 / 铁 / 铜块，南瓜头压上去就召出傀儡。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.GlassId },
                    Title = "玻璃",
                    Desc = "放下来不会碎；之后有方块压到上面才碎，碎完上方的东西接着往下掉。",
                },
                new GuideItem
                {
                    Ids = new[] { Sponge.SpongeId, Sponge.WetSpongeId },
                    Title = "海绵",
                    Desc = "干海绵吸走周围 7×7×7 里够得着的水（最多 65 格）并变成湿海绵；湿的吸不动。",
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
            if (!GuideTabVisible(_guideTab))
                _guideTab = GuideTabPlay;   // 手机上传进来的是隐藏栏，落回玩法
            _blockScroll = 0f;      // 每次进来都从头看
        }

        void DrawGuide()
        {
            EnsureGuideItems();

            if (_guideTab < 0 || _guideTab >= GuideTabCount)
                _guideTab = GuideTabPlay;
            // 手机上不显示「按键」栏，翻页翻到那儿就落回玩法
            if (MobileMode.Enabled && _guideTab == GuideTabKeys)
                _guideTab = GuideTabPlay;

            // 这一页内容最多（设计 1000 宽），横向基本铺满；屏幕不够宽就自动折减倍数
            // 三栏内容长短差得多，面板高度跟着栏走，免得短栏下面空一大块
            //
            // 手机上换成单列（460 宽）：按 1000 宽去缩，竖屏只有 0.35 倍，字小到看不清。
            // 单列以后每栏都往下排，高度跟着内容长，字体才保得住。
            // 三栏统一用一个高度：特殊方块栏内容最长，早先给它单独加高，结果三栏面板
            // 换一下就是三个不同的宽高比，看着像三张不同的图。内容超出去靠滚动，
            // 面板本身不再跟着内容长。
            bool compact = MobileMode.Enabled;
            bool wide = WidePage;
            float designW = compact ? 460f : 1000f;
            float designH = compact ? 790f : 596f;
            Rect panel = BeginPage(designW, designH);
            float w = panel.width;
            DrawPanel(panel);

            PageTitle(panel, "游戏规则", 12f);

            // 手机上键盘映射没意义，这一栏整栏不显示：页签只画「玩法」和「特殊方块」，
            // 两个页签在面板里居中排（按可见数量算宽度，别沿用三栏的居中公式）
            float tw = Pg(wide ? 150f : (compact ? 132f : 200f));
            float th = Pg(wide ? 28f : 40f);
            float tgap = Pg(compact ? 8f : 16f);
            int tabCount = 0;
            for (int i = 0; i < GuideTabCount; i++)
                if (GuideTabVisible(i))
                    tabCount++;
            float tx = panel.x + (w - (tw * tabCount + tgap * (tabCount - 1))) * 0.5f;
            float ty = panel.y + Pg(wide ? 26f : 56f);
            int slot = 0;
            for (int i = 0; i < GuideTabCount; i++)
            {
                if (!GuideTabVisible(i))
                    continue;

                // 选中态不再靠整体调暗/调亮：亮色主题下「调白」的按钮和白色面板糊在一起，
                // 看着反倒是灰的那个像被选中（真机截图反馈「颜色反了」）。
                // 现在两颗按钮同色，选中的那颗底下压一条强调色横线，两套主题都一眼能看出。
                var tabRect = new Rect(tx + slot * (tw + tgap), ty, tw, th);
                if (GUI.Button(tabRect, GuideTabNames[i], _btnBig))
                    _guideTab = i;
                if (_guideTab == i)
                {
                    DrawRect(new Rect(tabRect.x + Pg(10f), tabRect.yMax - Pg(3f),
                        tabRect.width - Pg(20f), Pg(3f)), UiSkin.Accent);
                }
                slot++;
            }

            string tabNote = GuideTabNotes[_guideTab];
            // 手机上 460 宽放不下那句长提示（特殊方块栏那句有四十多字，会横着溢出面板），
            // 干脆不画：下面每一项都写着自己的机制，开关在设置页
            bool noteFits = !wide && (!compact || string.IsNullOrEmpty(tabNote) || tabNote.Length <= 26);
            if (!string.IsNullOrEmpty(tabNote) && noteFits)
                GUI.Label(new Rect(panel.x, panel.y + Pg(100f), w, Pg(28f)), tabNote, _guideNoteBig);

            if (_guideTab == GuideTabPlay)
                DrawRulesPlay(panel, w);
            else if (_guideTab == GuideTabKeys)
                DrawRulesKeys(panel, w);
            else
                DrawRulesBlocks(panel);

            Rect backRect = wide
                ? new Rect(panel.x + (w - Pg(200f)) * 0.5f, panel.yMax - Pg(46f), Pg(200f), Pg(34f))
                : new Rect(panel.x + w - Pg(230f), panel.yMax - Pg(76f), Pg(180f), Pg(44f));
            if (GUI.Button(backRect, "返回", _btnBig))
                BeginCloseSettings();
        }

        /// <summary>玩法栏：六段小标题 + 正文。放大之后一列放不下六段，改成两列每列三段。</summary>
        void DrawRulesPlay(Rect panel, float w)
        {
            if (MobileMode.Enabled)
            {
                // 手机单列：六段竖着排。两列在 460 宽里每列只剩 200，正文会挤成一条。
                // 横屏画布只有 480 高，行距压到 58，六段正好排完（116+6*88 会溢出面板）
                bool wide = WidePage;
                float narrowW = w - Pg(80f);
                float narrowRow = Pg(wide ? 60f : 88f);
                float narrowTop = panel.y + Pg(wide ? 62f : 116f);
                for (int i = 0; i < PlayRules.Length; i++)
                {
                    float y = narrowTop + i * narrowRow;
                    GUI.Label(new Rect(panel.x + Pg(40f), y, narrowW, Pg(28f)), PlayRules[i][0], _guideNameBig);
                    GUI.Label(new Rect(panel.x + Pg(40f), y + Pg(26f), narrowW, Pg(wide ? 32f : 56f)),
                        PlayRuleDesc(i), _guideDescBig);
                }
                return;
            }

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
                GUI.Label(new Rect(x, y + Pg(30f), colW, Pg(58f)), PlayRuleDesc(i), _guideDescBig);
            }
        }

        /// <summary>按键栏：读当前键位，两列排开；这里只显示，改键在设置里。</summary>
        void DrawRulesKeys(Rect panel, float w)
        {
            if (MobileMode.Enabled)
            {
                // 单列：十二行竖着排，键帽宽度吃满剩余空间，比两列挤在 460 里好认
                float mLeft = panel.x + Pg(40f);
                float mLabel = Pg(210f);
                float mKeyW = Mathf.Max(Pg(80f), w - Pg(80f) - mLabel);
                float mRow = Pg(34f);
                float mGap = Pg(6f);
                float mTop = panel.y + Pg(126f);

                for (int i = 0; i < AllActions.Length; i++)
                {
                    GameAction action = AllActions[i];
                    float y = mTop + i * (mRow + mGap);
                    GUI.Label(new Rect(mLeft, y, mLabel, mRow), ActionLabel(action), _rowLabelBig);
                    DrawRect(new Rect(mLeft + mLabel, y + Pg(2f), mKeyW, mRow - Pg(4f)),
                        new Color(1f, 1f, 1f, 0.07f));
                    GUI.Label(new Rect(mLeft + mLabel, y, mKeyW, mRow),
                        KeyName(KeyboardInput.GetPrimaryKey(action)), _guideKeyBig);
                }

                float mNoteY = mTop + AllActions.Length * (mRow + mGap) + Pg(10f);
                GUI.Label(new Rect(mLeft, mNoteY, w - Pg(80f), Pg(28f)),
                    "手机上：左半屏拖动移动，右侧按键旋转和降落", _guideNoteBig);
                return;
            }

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
            if (MobileMode.Enabled)
            {
                // 手机单列十一项，一屏放不下 —— 裁一块可视区，内容在里面滚。
                // 滚轮和手指拖动都认：手机上只有拖，电脑上两个都顺手。
                bool wide = WidePage;
                float narrowW = panel.width - Pg(80f);
                // 行高 78 只够标题 + 两行说明；「傀儡」那条要三行，
                // 第三行整个被裁掉（真机截图里末尾的字就是丢的）。
                float rowH = Pg(wide ? 66f : 98f);
                float top = panel.y + Pg(wide ? 62f : 126f);
                // 返回按钮的顶边在 yMax - 76，这里再多留一点：贴着按钮裁的话，
                // 最后一行字会被切一半、看着像被按钮压住。
                float bottomEdge = wide ? panel.yMax - Pg(54f) : panel.yMax - Pg(104f);
                float avail = Mathf.Max(rowH, bottomEdge - top);

                // 先把可用高度切成整数行，再把余量平均还给每一行。
                //
                // 只「向下取整」的话，列表底边会空出不满一行的一大条（实测 56px），
                // 看着像内容没加载完；不取整直接铺满，底边又永远露半行字。
                // 平均分配两头都不占：行数取整、行高略放大，边和底都是齐的。
                int rows = Mathf.Max(1, Mathf.FloorToInt(avail / rowH));
                float viewH = avail;
                float step = avail / rows;
                float contentH = _guideItems.Length * step;
                float maxScroll = Mathf.Max(0f, contentH - viewH);
                // 右边留一列给滚动条：不留的话条子直接压在说明文字最后几个字上
                float rowW = narrowW - Pg(20f);
                var clip = new Rect(panel.x + Pg(40f), top, rowW, viewH);

                ScrollList(clip, maxScroll, ref _blockScroll, step);

                GUI.BeginGroup(clip);
                for (int i = 0; i < _guideItems.Length; i++)
                {
                    float y = i * step - _blockScroll;
                    if (y + step < 0f || y > viewH)
                        continue;   // 看不见的不画
                    DrawGuideRow(0f, y, rowW, _guideItems[i]);
                }
                GUI.EndGroup();

                DrawScrollBar(clip, viewH, contentH, maxScroll, _blockScroll);
                return;
            }

            const int perCol = 4;
            float colW = Pg(293f);
            // 标题、切换栏、提示各占一段之后只剩这么多，行高再大就压到「返回」上了
            float rowH2 = Pg(96f);
            float top2 = panel.y + Pg(128f);

            for (int i = 0; i < _guideItems.Length; i++)
            {
                float x = panel.x + Pg(40f) + (i / perCol) * (colW + Pg(20f));
                DrawGuideRow(x, top2 + (i % perCol) * rowH2, colW, _guideItems[i]);
            }
        }

        // 拖动滚动：一帧只处理一次。OnGUI 一帧会跑 Layout / Repaint 好几趟，
        // 每趟都读一次位移的话，同一个像素会被算好几遍。
        static bool _dragOn;
        static Vector2 _dragLast;
        static int _dragFrame = -1;

        /// <summary>
        /// 按住拖动滚列表（手指或鼠标）。
        ///
        /// 上一版只读 Input.touches，真机上完全划不动 —— Unity WebGL 在移动浏览器里
        /// 常常既不给 MouseDrag 事件、Input.touchCount 又是 0（浏览器用 Pointer Events
        /// 上报时 Unity 拿不到 touch）。但按钮点得动，说明「触摸被模拟成鼠标」这条路
        /// 一定是通的。所以两条路都读，一帧只认一条，谁也不重复累加：
        /// 有 touch 用 touch，没有就退回鼠标位置。
        /// </summary>
        bool DragScroll(Rect clip, ref float scroll, float maxScroll)
        {
            if (Time.frameCount == _dragFrame)
                return _dragOn;             // 同一帧后面的趟次：直接用上一趟的结论
            _dragFrame = Time.frameCount;

            Vector2 p;
            bool began, held, ended;

            if (Input.touchCount > 0)
            {
                Touch t = Input.GetTouch(0);
                // 屏幕坐标原点在左下，GUI 坐标原点在左上
                p = new Vector2(t.position.x, Screen.height - t.position.y);
                began = t.phase == TouchPhase.Began;
                held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            }
            else
            {
                p = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                began = Input.GetMouseButtonDown(0);
                held = Input.GetMouseButton(0);
                ended = Input.GetMouseButtonUp(0);
            }

            if (began)
            {
                // 手指落在列表里才算开始拖；落在外面（比如返回按钮上）不抢
                _dragOn = clip.Contains(p);
                _dragLast = p;
            }
            else if (ended)
            {
                _dragOn = false;
            }
            else if (held && _dragOn)
            {
                float dy = p.y - _dragLast.y;
                _dragLast = p;
                // 手指往下拖 = 内容跟着往下走 = 滚动量减少
                if (Mathf.Abs(dy) > 0.01f)
                    scroll = Mathf.Clamp(scroll - dy, 0f, maxScroll);
            }

            return _dragOn;
        }

        void ScrollList(Rect clip, float maxScroll, ref float scroll, float snap = 0f)
        {
            if (maxScroll <= 0f)
            {
                scroll = 0f;
                return;
            }

            bool dragging = DragScroll(clip, ref scroll, maxScroll);

            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.ScrollWheel && clip.Contains(e.mousePosition))
            {
                scroll = Mathf.Clamp(scroll + e.delta.y * Pg(48f), 0f, maxScroll);
                e.Use();
            }
            else if (!dragging && e.type == EventType.MouseDrag && clip.Contains(e.mousePosition))
            {
                // 兜底：Input 那条路一点位移都拿不到时，至少还有 IMGUI 自己的 MouseDrag。
                // 拖动已经由 DragScroll 处理过的帧不进这里，否则一次拖动会滚两倍。
                scroll = Mathf.Clamp(scroll - e.delta.y, 0f, maxScroll);
                e.Use();
            }

            // 松手之后把位置吸附到整行。不吸附的话停下来的位置是随机的，
            // 列表上下两条边各露出半行字，看着像被裁坏了（真机截图里就是这样）。
            // 拖动过程中不吸附，否则手指还在动的时候画面会一跳一跳地跟手。
            if (!dragging && snap > 0f)
                scroll = Mathf.Clamp(Mathf.Round(scroll / snap) * snap, 0f, maxScroll);
        }

        /// <summary>自绘滚动指示条：内容放得下时不画。</summary>
        void DrawScrollBar(Rect clip, float viewH, float contentH, float maxScroll, float scroll)
        {
            if (maxScroll <= 0f)
                return;

            // 轨道比内容区上下各内缩一点：贴着裁剪区边画的话，
            // 条子的头和第一行字、尾和最后一行字在视觉上是挤在一起的。
            float inset = Pg(6f);
            float trackY = clip.y + inset;
            float trackH = Mathf.Max(Pg(20f), viewH - inset * 2f);

            float barW = Pg(6f);
            float barX = clip.xMax + Pg(8f);

            // 滑块长度按「能看见多少内容」算，最短给 36，太短就抓不住了；
            // 同时不能超过轨道自身长度（内容只比可视区多一点时会出现这种情况）。
            float barH = Mathf.Clamp(viewH * viewH / Mathf.Max(1f, contentH), Pg(36f), trackH);
            float barY = trackY + (trackH - barH) * Mathf.Clamp01(scroll / maxScroll);

            // 中性灰白，不用强调色：滚动条是背景设施，蓝色在那一片文字旁边太跳
            DrawRect(new Rect(barX, trackY, barW, trackH), new Color(1f, 1f, 1f, 0.07f));
            DrawRect(new Rect(barX, barY, barW, barH), new Color(0.78f, 0.82f, 0.9f, 0.55f));
        }

        void DrawGuideRow(float x, float y, float w, GuideItem item)
        {
            int n = 0;
            for (int i = 0; i < item.Ids.Length; i++)
                if (item.Ids[i] > 0)
                    n++;

            // 图标越多越要收着画：四个图标的条目（防爆方块）按 26 给的话，
            // 文字区只剩一百来像素，二十八字的说明要排四行、第三行之外就被裁掉。
            float icon = n <= 2 ? Pg(36f) : (n == 3 ? Pg(26f) : Pg(21f));
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
            // 手机上单列的行虽然更宽，但图标最多三枚、文字区反而窄，
            // 十四号字要排到三行；只给 58 高的话第三行会被裁掉。
            float descH = Pg(MobileMode.Enabled ? 68f : 58f);
            GUI.Label(new Rect(x + pad, y + Pg(30f), w - pad, descH), item.Desc, _guideDescBig);
        }

        /// <summary>从方块图集里切一格顶面出来画成图标；方块表里查不到就画个灰块。</summary>
        void DrawBlockIcon(Rect r, int blockId)
        {
            // 边框画在 rect **里面**：先铺一层深色底、再把贴图缩进 1px 盖上。
            // 原来画在 r 外面（r.x-1），而列表最左一列正好贴着滚动区的裁剪边，
            // 外扩的那 1px 被裁掉，最左边那枚图标看着就少了一条边。
            DrawRect(r, new Color(0f, 0f, 0f, 0.55f));
            Rect inner = new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f);

            if (_atlasTex == null || blockId <= 0 || blockId > BlockCatalog.Blocks.Length)
            {
                DrawRect(inner, new Color(0.25f, 0.28f, 0.33f, 1f));
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

            GUI.DrawTextureWithTexCoords(inner, _atlasTex, uv);
        }

        // ---------- 工具 ----------

        void Fire(Action a)
        {
            if (a != null)
                a();
        }

        /// <summary>
        /// 设置这几页（主页 / 按键 / 场地尺寸 / 下落速度 / 音量 / 机制开关 / 游戏规则）
        /// 相对基准想放大到的倍数。
        ///
        /// 为什么要单独一套：界面尺寸是写死的像素（P() 只乘 1.25），不随分辨率走。
        /// 在 2560×1494 这类高分屏上整块设置面板只占屏幕中间一小块，字看着就小。
        /// 页内每个尺寸过 Pg()、每个字号过 Fg()，两套数字同一个系数，
        /// 等比长大不会出现「字大了框没大」的错位。
        ///
        /// 注意这是「目标倍数」不是「一定倍数」：屏幕装不下时按可用空间自动折减，
        /// 见 BeginPage。写死 2 倍的话规则页（设计 1000×596）要 2500×1490 像素，
        /// 1080p 的窗口根本装不下，面板直接跑到屏幕外面去。
        /// </summary>
        const float PageTarget = 2f;

        /// <summary>
        /// 折减下限。再小字就看不清了 —— 但下限不能设太高：设成 0.5 的话
        /// 机制页（设计高 900）在 800×600 的窗口里会顶出屏幕。0.35 是实测
        /// 640×480 都还装得下的值。
        /// </summary>
        const float PageMin = 0.35f;

        /// <summary>面板四周至少留出的边距（设计像素）。</summary>
        const float PageMargin = 24f;

        /// <summary>当前页实际采用的放大倍数。每页画之前由 BeginPage 算好。</summary>
        static float _pageScale = PageTarget;

        static float Pg(float design)
        {
            return UiSkin.P(design * _pageScale);
        }

        static int Fg(int size)
        {
            return UiSkin.Size(Mathf.RoundToInt(size * _pageScale));
        }

        /// <summary>
        /// 每页画之前调一次：拿这一页的设计宽高跟当前屏幕比，算出最多能放大到几倍，
        /// 把字号同步到样式上，返回居中后的面板矩形。
        ///
        /// 宽高和字号共用同一个系数，所以任何窗口尺寸下面板都装得下内容、字也不出框
        /// （只缩面板不缩字、字会顶破按钮这个坑在暂停面板上踩过一次）。
        /// </summary>
        /// <param name="target">这一页想放大的倍数，默认 PageTarget；装不下由 FitScale 折减。</param>
        Rect BeginPage(float designW, float designH, float target = PageTarget)
        {
            float margin = UiSkin.P(PageMargin);
            float bandW = Mathf.Max(1f, Screen.width - margin * 2f);
            float availH = Mathf.Max(1f, Screen.height - margin * 2f);

            if (WidePage)
            {
                // 横屏手机：内容只占右半屏，左边留给标题（见 PageTitle）。
                // 画布统一成 WidePageW × WidePageH，各页传进来的尺寸在这条分支里作废 ——
                // 否则页面越高缩放越小，切页时字号会跳。
                // 宽度和缩放倍数都用统一画布算 —— 各页字号、面板宽度因此完全一致。
                // 高度还是各页自己的（但不超过统一画布高）：内容少的页（下落速度、
                // 音乐音量）面板就矮一些，不会像固定 480 高那样下半截空一大片。
                bandW *= 0.52f;      // 右半屏那条带子的宽度
                _pageScale = UiSkin.FitScale(WidePageW, WidePageH, target, bandW, availH, PageMin);
                SyncPageFonts();

                float w = Pg(WidePageW);
                float h = Pg(Mathf.Min(designH, WidePageH));
                float bandLeft = Screen.width - margin - bandW;
                return new Rect(bandLeft + (bandW - w) * 0.5f,
                    (Screen.height - h) * 0.5f, w, h);
            }

            _pageScale = UiSkin.FitScale(designW, designH, target, bandW, availH, PageMin);
            SyncPageFonts();

            return CenterPanel(Pg(designW), Pg(designH));
        }

        /// <summary>
        /// 页面标题。竖屏 / 桌面画在面板顶部；横屏手机画在**左半屏居中**，
        /// 和标题页「左标题 / 右按钮」的版式对齐。
        /// </summary>
        void PageTitle(Rect panel, string text, float topOffset)
        {
            if (!WidePage)
            {
                GUI.Label(new Rect(panel.x, panel.y + Pg(topOffset), panel.width, Pg(44f)),
                    text, _panelTitleBig);
                return;
            }

            float hh = Pg(76f);
            GUI.Label(new Rect(0f, (Screen.height - hh) * 0.5f, Screen.width * 0.5f, hh),
                text, _panelHeadingBig);
        }

        /// <summary>
        /// 自检用：某一设置页的设计宽高（下标 = SettingsPage 的值）。
        /// 改了某一页的布局，这里的数字要跟着改 —— 自检拿它在各种窗口尺寸下算装不装得下。
        /// </summary>
        public static Vector2 PageDesign(int page)
        {
            switch ((SettingsPage)page)
            {
                case SettingsPage.Main: return new Vector2(420f, 594f);
                case SettingsPage.Keys: return new Vector2(940f, 478f);
                case SettingsPage.Size: return new Vector2(460f, 500f);
                case SettingsPage.Workshop: return new Vector2(680f, 690f);
                case SettingsPage.Speed: return new Vector2(500f, 360f);
                case SettingsPage.Music: return new Vector2(500f, 360f);
                case SettingsPage.Guide: return new Vector2(1000f, 596f);   // 取三栏里最高那一栏
                case SettingsPage.Mech: return new Vector2(480f, 900f);
            }
            return new Vector2(420f, 594f);
        }

        /// <summary>自检用：某一页的目标倍数。各页统一走 PageTarget，由 FitScale 在装不下时折减。</summary>
        public static float PageTargetOf(int page)
        {
            return PageTarget;
        }

        /// <summary>
        /// 大号样式的字号按当前 _pageScale 现算。样式本身只在 EnsureStyles 里建一次，
        /// 每帧改的是 fontSize —— 建一个 GUIStyle 的开销远比改一个字段大。
        /// </summary>
        void SyncPageFonts()
        {
            _panelTitleBig.fontSize = Fg(32);
            _panelHeadingBig.fontSize = Fg(40);
            _rowLabelBig.fontSize = Fg(18);
            _hintBig.fontSize = Fg(18);
            _valueBig.fontSize = Fg(22);
            _sliderShadowBig.fontSize = Fg(22);
            _guideNameBig.fontSize = Fg(19);
            _guideDescBig.fontSize = Fg(14);
            _guideNoteBig.fontSize = Fg(17);
            _guideKeyBig.fontSize = Fg(19);
            _btnBig.fontSize = Fg(18);

            // 创意工坊用的是没放大那套样式（目标 1 倍），同样跟着折减
            _panelTitleStyle.fontSize = Fg(32);
            _rowLabelStyle.fontSize = Fg(18);
            _listStyle.fontSize = Fg(17);
            _btnStyle.fontSize = Fg(18);
        }

        static Rect CenterPanel(float w, float h)
        {
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        }

        void DrawPanel(Rect r)
        {
            DrawRect(r, UiSkin.PanelSolid);
            Color border = UiSkin.PanelBorder;
            DrawRect(new Rect(r.x, r.y, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), border);
            DrawRect(new Rect(r.x, r.y, 1f, r.height), border);
            DrawRect(new Rect(r.xMax - 1f, r.y, 1f, r.height), border);
        }


        // ---------- 创意工坊 ----------

        /// <summary>
        /// 三维建造台。由 Bootstrap 接进来，形状就存在它里面 —— 这一页不再自己留一份，
        /// 免得界面和建造台各记一套、改了这边忘了那边。
        /// </summary>
        public MoldWorkbench Workbench;

        /// <summary>这一页正开着。Bootstrap 据此决定建造台的相机要不要渲染。</summary>
        public bool WorkshopOpen
        {
            get { return ShowSettings && _page == SettingsPage.Workshop; }
        }

        int _moldSel;
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
            if (Workbench == null)
                return;
            var cells = new List<Vector3Int>();
            if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
            {
                Vector3Int[] parsed = MoldData.Parse(MoldData.Items[_moldSel].cells);
                for (int i = 0; i < parsed.Length; i++)
                    cells.Add(parsed[i]);
            }
            Workbench.SetCells(cells);
        }

        /// <summary>建造台动过一格就调一次，把当前形状写回存档。</summary>
        public void SaveMoldEdit()
        {
            if (Workbench == null)
                return;
            if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                MoldData.SetCells(_moldSel, Workbench.Cells);
        }

        void DrawWorkshop()
        {
            if (MobileMode.Enabled)
            {
                DrawWorkshopMobile();
                return;
            }

            // 这一页的设计值本来就是按 1 倍写的，目标倍数传 1：
            // 屏幕够大时和原来一模一样，装不下才跟着折减。
            Rect panel = BeginPage(680f, 690f);
            float w = panel.width;
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(12f), w, Pg(44f)), "创意工坊", _panelTitleStyle);

            if (GUI.Button(new Rect(panel.x + w - Pg(94f), panel.y + Pg(18f), Pg(70f), Pg(32f)),
                    "返回", _btnStyle))
            {
                _renaming = false;
                BeginCloseSettings();
            }

            // 左边的模具列表
            float listX = panel.x + Pg(24f);
            float listY = panel.y + Pg(92f);
            float itemH = Pg(34f);
            float itemGap = Pg(6f);

            GUI.Label(new Rect(listX, listY - Pg(28f), Pg(280f), Pg(26f)), "模具列表", _rowLabelStyle);

            int count = MoldData.Items.Count;
            for (int i = 0; i < count && i < 7; i++)
            {
                MoldEntry m = MoldData.Items[i];
                string label = (i == _moldSel ? "▸ " : "   ") + m.name + (m.enabled != 0 ? "  ✓" : "  ○");
                var rect = new Rect(listX, listY + i * (itemH + itemGap), Pg(230f), itemH);
                if (i == _moldSel)
                    DrawRect(rect, new Color(0.42f, 0.86f, 1f, 0.22f));
                if (GUI.Button(rect, label, _listStyle) && i != _moldSel)
                {
                    _moldSel = i;
                    _renaming = false;
                    LoadMoldEdit();
                }
            }

            if (count > 7)
                GUI.Label(new Rect(listX, listY + 7 * (itemH + itemGap), Pg(230f), Pg(26f)),
                    "（只显示前 7 个）", _rowLabelStyle);

            // 右边：三维建造盒。建造台自己带一台相机渲到贴图里，这里只负责给它一块矩形，
            // 鼠标在里面点哪儿由它自己换算成射线。
            float viewX = panel.x + Pg(272f);
            float viewW = Pg(372f);
            var viewRect = new Rect(viewX, listY, viewW, viewW);

            if (Workbench != null)
                Workbench.Draw(viewRect);
            else
                DrawRect(viewRect, new Color(0f, 0f, 0f, 0.45f));
            DrawViewBorder(viewRect);

            // 建造盒下方两排工具
            float rowY = viewRect.yMax + Pg(10f);
            float bh = Pg(34f);

            GUI.Label(new Rect(viewX, rowY + Pg(2f), Pg(28f), Pg(30f)), "层", _rowLabelStyle);
            // 箭头用 ← →（GB2312 里有）：◀ ▶ 楷体里没有，两个字形都被替换成同一个三角，分不出左右
            if (GUI.Button(new Rect(viewX + Pg(30f), rowY, Pg(36f), bh), "←", _btnStyle) && Workbench != null)
                Workbench.Layer = Workbench.Layer - 1;
            GUI.Label(new Rect(viewX + Pg(70f), rowY, Pg(56f), bh),
                Workbench != null ? (Workbench.Layer + 1) + " / " + MoldData.Size : "-", _rowLabelStyle);
            if (GUI.Button(new Rect(viewX + Pg(130f), rowY, Pg(36f), bh), "→", _btnStyle) && Workbench != null)
                Workbench.Layer = Workbench.Layer + 1;
            if (GUI.Button(new Rect(viewX + Pg(178f), rowY, Pg(140f), bh),
                    Workbench != null && Workbench.DimOthers ? "只亮当前层：开" : "只亮当前层：关", _btnStyle)
                && Workbench != null)
                Workbench.DimOthers = !Workbench.DimOthers;

            float row2 = rowY + Pg(40f);
            if (GUI.Button(new Rect(viewX, row2, Pg(78f), bh), "镜像", _btnStyle) && Workbench != null)
                Workbench.Mirror();
            if (GUI.Button(new Rect(viewX + Pg(86f), row2, Pg(78f), bh), "居中", _btnStyle) && Workbench != null)
                Workbench.Recenter();
            if (GUI.Button(new Rect(viewX + Pg(172f), row2, Pg(78f), bh), "清空", _btnStyle) && Workbench != null)
                Workbench.Clear();
            if (GUI.Button(new Rect(viewX + Pg(258f), row2, Pg(104f), bh), "复位视角", _btnStyle) && Workbench != null)
                Workbench.ResetView();

            // 提示行 / 状态行。重命名时这一块让给输入框，不然两排字会叠在一起
            float hintY = row2 + Pg(40f);
            float bx = panel.x + Pg(40f);

            if (_renaming)
            {
                GUI.Label(new Rect(bx, hintY + Pg(2f), Pg(56f), Pg(30f)), "名字", _rowLabelStyle);
                _renameText = GUI.TextField(new Rect(bx + Pg(58f), hintY, Pg(300f), Pg(32f)),
                    _renameText, 16, _btnStyle);
                if (GUI.Button(new Rect(bx + Pg(370f), hintY, Pg(70f), Pg(32f)), "确定", _btnStyle))
                {
                    MoldData.Rename(_moldSel, _renameText);
                    _renaming = false;
                }
                if (GUI.Button(new Rect(bx + Pg(452f), hintY, Pg(70f), Pg(32f)), "取消", _btnStyle))
                    _renaming = false;
            }
            else
            {
                string hint = Workbench != null ? Workbench.Hint : null;
                GUI.Label(new Rect(bx, hintY, Pg(600f), Pg(26f)),
                    hint ?? "左键放在方块朝外那一面 · 右键擦除 · 右键拖动转视角 · 滚轮缩放",
                    _rowLabelStyle);
                GUI.Label(new Rect(bx, hintY + Pg(28f), Pg(600f), Pg(26f)), MoldStatus(), _rowLabelStyle);
            }

            // 底部按钮（比提示行收紧 6 格：再往下那行说明字正好压在面板边框上）
            float by = hintY + Pg(56f);
            float bh2 = Pg(40f);

            if (GUI.Button(new Rect(bx, by, Pg(110f), bh2), "新建模具", _btnStyle))
            {
                MoldData.Add();
                _moldSel = MoldData.Items.Count - 1;
                _renaming = false;
                LoadMoldEdit();
            }

            if (GUI.Button(new Rect(bx + Pg(122f), by, Pg(110f), bh2), "删除模具", _btnStyle))
            {
                MoldData.Remove(_moldSel);
                if (_moldSel >= MoldData.Items.Count)
                    _moldSel = MoldData.Items.Count - 1;
                LoadMoldEdit();
            }

            if (GUI.Button(new Rect(bx + Pg(244f), by, Pg(100f), bh2), "重命名", _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                {
                    _renaming = true;
                    _renameText = MoldData.Items[_moldSel].name;
                }
            }

            bool on = _moldSel >= 0 && _moldSel < MoldData.Items.Count
                      && MoldData.Items[_moldSel].enabled != 0;
            if (GUI.Button(new Rect(bx + Pg(356f), by, Pg(110f), bh2),
                    on ? "启用中" : "未启用", _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                    MoldData.SetEnabled(_moldSel, !on);
            }

            GUI.Label(new Rect(bx, by + bh2 + Pg(10f), Pg(560f), Pg(26f)),
                "启用的模具会和 7 种默认方块一起随机出现", _rowLabelStyle);
        }

        /// <summary>
        /// 手机上的创意工坊。两种版式：
        /// 竖屏一列到底 —— 上面选模具、中间建造盒、下面两排工具；
        /// 横屏左右分栏 —— 左半屏整块留给建造盒，控件全在右半屏（见下面的 wide 分支）。
        /// 桌面那套「左边列表 + 右边盒子」照搬到竖屏会把两样都挤成一条。
        ///
        /// 交互也换了：右键擦除在手机上不存在，改成工具栏里的「擦除」开关，
        /// 点一下是放还是擦由它决定（手势在 MoldWorkbench.HandleTouch 里）。
        /// </summary>
        void DrawWorkshopMobile()
        {
            // 横屏：建造盒挪到左半屏之后，右侧这一列只需要 400 高；
            // 竖屏还是原来那块 790 高的一整列。
            bool wide = WidePage;
            Rect panel = BeginPage(460f, wide ? 400f : 790f);
            float w = panel.width;
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + Pg(12f), w, Pg(44f)), "创意工坊", _panelTitleStyle);

            if (GUI.Button(new Rect(panel.x + w - Pg(94f), panel.y + Pg(18f), Pg(70f), Pg(32f)),
                    "返回", _btnStyle))
            {
                _renaming = false;
                BeginCloseSettings();
            }

            // 内容区左右沿：横屏这一列更宽（整块右半屏），竖屏受建造盒宽度约束
            float colX = panel.x + Pg(30f);
            float viewSize = Mathf.Min(w - Pg(60f), Pg(366f));
            float colW = wide ? Mathf.Max(Pg(200f), w - Pg(60f)) : viewSize;

            // 模具选择：窄屏放不下左边那列列表，改成「← 名字 ✓ →」一行翻
            float selY = panel.y + Pg(64f);
            float selH = Pg(40f);
            int count = MoldData.Items.Count;

            if (GUI.Button(new Rect(colX, selY, Pg(44f), selH), "←", _btnStyle)
                && count > 0)
            {
                _moldSel = (_moldSel - 1 + count) % count;
                _renaming = false;
                LoadMoldEdit();
            }

            string label = count == 0
                ? "点「新建模具」开始"
                : MoldData.Items[_moldSel].name
                  + (MoldData.Items[_moldSel].enabled != 0 ? "  ✓" : "  ○")
                  + "   " + (_moldSel + 1) + "/" + count;
            GUI.Label(new Rect(colX + Pg(50f), selY, colW - Pg(110f), selH), label, _listStyle);

            if (GUI.Button(new Rect(colX + colW - Pg(44f), selY, Pg(44f), selH), "→", _btnStyle)
                && count > 0)
            {
                _moldSel = (_moldSel + 1) % count;
                _renaming = false;
                LoadMoldEdit();
            }

            // 建造盒。
            // 横屏：整块左半屏，取半屏可用区里最大的正方形、竖直居中 ——
            //   右边那一列控件不再压着它，建造盒能长到屏幕高度那么大。
            // 竖屏：正中间一块方的，宽度顶满（留 30 的边距），高度上限 366
            //   （面板 790 高，扣掉选模具行、两排工具、两行提示和底部四个按钮，
            //    再高提示行就顶到按钮上了）。
            Rect viewRect;
            float rowY;
            if (wide)
            {
                float margin = UiSkin.P(PageMargin);
                float availW = Mathf.Max(1f, Screen.width * 0.5f - margin * 2f);
                float availH = Mathf.Max(1f, Screen.height - margin * 2f);
                float side = Mathf.Min(availW, availH);
                viewRect = new Rect(margin + (availW - side) * 0.5f,
                    (Screen.height - side) * 0.5f, side, side);
                rowY = selY + selH + Pg(14f);
            }
            else
            {
                viewRect = new Rect(colX, selY + selH + Pg(10f), viewSize, viewSize);
                rowY = viewRect.yMax + Pg(12f);
            }

            if (Workbench != null)
                Workbench.Draw(viewRect);
            else
                DrawRect(viewRect, new Color(0f, 0f, 0f, 0.45f));
            DrawViewBorder(viewRect);

            float bh = Pg(40f);

            GUI.Label(new Rect(colX, rowY + Pg(5f), Pg(28f), Pg(30f)), "层", _rowLabelStyle);
            if (GUI.Button(new Rect(colX + Pg(30f), rowY, Pg(44f), bh), "←", _btnStyle) && Workbench != null)
                Workbench.Layer = Workbench.Layer - 1;
            GUI.Label(new Rect(colX + Pg(80f), rowY, Pg(60f), bh),
                Workbench != null ? (Workbench.Layer + 1) + " / " + MoldData.Size : "-", _rowLabelStyle);
            if (GUI.Button(new Rect(colX + Pg(146f), rowY, Pg(44f), bh), "→", _btnStyle) && Workbench != null)
                Workbench.Layer = Workbench.Layer + 1;
            if (GUI.Button(new Rect(colX + Pg(196f), rowY, Pg(190f), bh),
                    Workbench != null && Workbench.DimOthers ? "只亮当前层：开" : "只亮当前层：关", _btnStyle)
                && Workbench != null)
                Workbench.DimOthers = !Workbench.DimOthers;

            // 第二排工具：五个等宽按钮。最后一个「擦除」是手机专用的开关
            float row2 = rowY + bh + Pg(8f);
            float bw5 = (colW - Pg(24f)) / 5f;
            string[] names = { "镜像", "居中", "清空", "复位" };
            for (int i = 0; i < 4; i++)
            {
                if (!GUI.Button(new Rect(colX + i * (bw5 + Pg(6f)), row2, bw5, bh), names[i], _btnStyle))
                    continue;
                if (Workbench == null)
                    continue;
                if (i == 0) Workbench.Mirror();
                else if (i == 1) Workbench.Recenter();
                else if (i == 2) Workbench.Clear();
                else Workbench.ResetView();
            }

            bool erasing = Workbench != null && Workbench.EraseMode;
            if (GUI.Button(new Rect(colX + 4 * (bw5 + Pg(6f)), row2, bw5, bh),
                    erasing ? "擦除中" : "擦除", _btnStyle) && Workbench != null)
                Workbench.EraseMode = !erasing;

            // 提示两行：手势说明 + 模具状态。
            //
            // 上一版每行只给 24 高、两行只隔 26，而 _rowLabelStyle 是 18 号字 ——
            // 一行字本身就占 24 往上，文案又长到折行，结果两行字糊成一团（真机截图可见）。
            // 现在文案压到一行放得下，行高按字号给足 28，两行之间也不重叠。
            float hintY = row2 + bh + Pg(10f);
            string hint = Workbench != null ? Workbench.Hint : null;
            GUI.Label(new Rect(colX, hintY, colW, Pg(28f)),
                hint ?? (erasing
                    ? "点按擦除 · 拖动转视角 · 双指缩放"
                    : "点按放置 · 拖动转视角 · 双指缩放"),
                _rowLabelStyle);
            GUI.Label(new Rect(colX, hintY + Pg(28f), colW, Pg(28f)), MoldStatus(), _rowLabelStyle);

            // 底部四个按钮。横屏这一列只有 400 高，按钮贴着面板底边收；
            // 竖屏下面还有空，留 110 的余量（改名输入框就塞在这段里）。
            float by = wide ? panel.yMax - Pg(70f) : panel.yMax - Pg(110f);
            float bw4 = (colW - Pg(18f)) / 4f;

            // 横屏提示行下面到按钮之间还有一段空，把这条说明摆进去
            if (wide)
                GUI.Label(new Rect(colX, by - Pg(26f), colW, Pg(24f)),
                    "启用的模具会和 7 种默认方块一起随机出现", _rowLabelStyle);

            if (GUI.Button(new Rect(colX, by, bw4, bh), "新建", _btnStyle))
            {
                MoldData.Add();
                _moldSel = MoldData.Items.Count - 1;
                _renaming = false;
                LoadMoldEdit();
            }
            if (GUI.Button(new Rect(colX + bw4 + Pg(6f), by, bw4, bh), "删除", _btnStyle))
            {
                MoldData.Remove(_moldSel);
                if (_moldSel >= MoldData.Items.Count)
                    _moldSel = MoldData.Items.Count - 1;
                LoadMoldEdit();
            }
            if (GUI.Button(new Rect(colX + (bw4 + Pg(6f)) * 2f, by, bw4, bh), "改名", _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                {
                    _renaming = true;
                    _renameText = MoldData.Items[_moldSel].name;
                }
            }

            bool on = _moldSel >= 0 && _moldSel < MoldData.Items.Count
                      && MoldData.Items[_moldSel].enabled != 0;
            if (GUI.Button(new Rect(colX + (bw4 + Pg(6f)) * 3f, by, bw4, bh),
                    on ? "启用中" : "未启用", _btnStyle))
            {
                if (_moldSel >= 0 && _moldSel < MoldData.Items.Count)
                    MoldData.SetEnabled(_moldSel, !on);
            }

            // 改名输入框压在底部那一行上：手机键盘弹出时它就在眼前，不用再滚
            if (_renaming)
            {
                var box = new Rect(colX, by - Pg(48f), colW, Pg(40f));
                DrawRect(box, new Color(0f, 0f, 0f, 0.6f));
                _renameText = GUI.TextField(new Rect(box.x + Pg(6f), box.y + Pg(4f), box.width - Pg(120f), Pg(32f)),
                    _renameText, 16, _btnStyle);
                if (GUI.Button(new Rect(box.xMax - Pg(110f), box.y + Pg(4f), Pg(50f), Pg(32f)), "确定", _btnStyle))
                {
                    MoldData.Rename(_moldSel, _renameText);
                    _renaming = false;
                }
                if (GUI.Button(new Rect(box.xMax - Pg(56f), box.y + Pg(4f), Pg(50f), Pg(32f)), "取消", _btnStyle))
                    _renaming = false;
            }
        }

        /// <summary>建造盒那块矩形的框线，把「窗口」和面板背景分开。</summary>
        void DrawViewBorder(Rect r)
        {
            Color c = new Color(0.35f, 0.37f, 0.44f, 0.95f);
            DrawRect(new Rect(r.x - 1f, r.y - 1f, r.width + 2f, 1f), c);
            DrawRect(new Rect(r.x - 1f, r.yMax, r.width + 2f, 1f), c);
            DrawRect(new Rect(r.x - 1f, r.y - 1f, 1f, r.height + 2f), c);
            DrawRect(new Rect(r.xMax, r.y - 1f, 1f, r.height + 2f), c);
        }

        /// <summary>当前模具的状态：合不合格、要不要更深的场地、用了几格。</summary>
        string MoldStatus()
        {
            if (Workbench == null)
                return string.Empty;
            if (_moldSel < 0 || _moldSel >= MoldData.Items.Count)
                return "先选一个模具";

            string bad = MoldData.Validate(Workbench.Cells);
            if (bad != null)
                return bad;

            int spanZ = MoldData.SpanZ(Workbench.Cells);
            if (spanZ > GameSettings.SizeZ)
                return "需要场地深度 ≥ " + spanZ;

            string tail = " · " + Workbench.Cells.Count + "/" + MoldData.MaxCells + " 格";
            return MoldData.Items[_moldSel].enabled != 0 ? "可用" + tail : "未启用" + tail;
        }

        void EnsureStyles()
        {
            // 主题变了要重建：颜色是拷进 GUIStyle 里的副本，光改 UiSkin 不会自己变
            if (_stylesReady && _stylesTheme == (int)UiSkin.Current)
                return;
            if (_stylesReady)
                ReleaseStyles();
            _stylesReady = true;
            _stylesTheme = (int)UiSkin.Current;

            _btnNormal = SolidTexture(UiSkin.ButtonFill);
            _btnHover = SolidTexture(UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.86f, 0.92f, 1f, 0.95f)
                : new Color(0.20f, 0.24f, 0.30f, 0.98f));
            _btnActive = SolidTexture(UiSkin.Current == UiSkin.Theme.Light
                ? new Color(0.74f, 0.84f, 0.96f, 0.98f)
                : new Color(0.10f, 0.12f, 0.15f, 0.98f));

            _subStyle = MakeStyle(30, UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal);
            _creditStyle = MakeStyle(17, UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal);
            _panelTitleStyle = MakeStyle(32, UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rowLabelStyle = MakeStyle(18, UiSkin.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintStyle = MakeStyle(18, UiSkin.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintStyle.wordWrap = false;
            _valueStyle = MakeStyle(22, UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal);
            // 滑条上的字会被圆头从中间穿过去，垫一层深色影子才读得清
            _sliderShadowStyle = MakeStyle(22, UiSkin.Current == UiSkin.Theme.Light ? new Color(0f, 0f, 0f, 0f) : new Color(0f, 0f, 0f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Normal);

            _guideNameStyle = MakeStyle(19, UiSkin.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guideNameStyle.wordWrap = false;
            _guideDescStyle = MakeStyle(14, UiSkin.TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal);
            _guideDescStyle.wordWrap = true;
            _guideNoteStyle = MakeStyle(17, UiSkin.TextDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNoteStyle.wordWrap = false;
            // 按键栏里键名用的，放在淡色方块中间
            _guideKeyStyle = MakeStyle(19, UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            _guideKeyStyle.wordWrap = false;


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
            _listStyle = new GUIStyle(_btnStyle);
            _listStyle.fontSize = UiSkin.Size(17);
            _listStyle.alignment = TextAnchor.MiddleLeft;
            _listStyle.padding = new RectOffset(Mathf.RoundToInt(UiSkin.P(14f)), Mathf.RoundToInt(UiSkin.P(10f)), 0, 0);

            // 主菜单按钮：底色/悬停沿用上面那三张纯色贴图，字号由 DrawButtons 按缩放系数改写
            _menuBtnStyle = new GUIStyle(_btnStyle);
            _menuBtnStyle.alignment = TextAnchor.MiddleCenter;
            _menuBtnStyle.fontSize = UiSkin.Size(18);
            // 「请把手机横过来」提示层的正文：居中、不折行（折了行固定 Rect 会顶破）
            _rotateHintStyle = MakeStyle(15, UiSkin.TextSecondary, TextAnchor.MiddleCenter, FontStyle.Normal);
            _rotateHintStyle.wordWrap = false;
            // 设置各页的大号版本：颜色、对齐、换行设置和上面一一对应，字号每帧由 SyncPageFonts 改写
            _panelTitleBig = MakeStylePx(Fg(32), UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            // 横屏时标题挪到左半屏，独立成一块，字号可以比面板里那档大一些
            _panelHeadingBig = MakeStylePx(Fg(40), UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            _rowLabelBig = MakeStylePx(Fg(18), UiSkin.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintBig = MakeStylePx(Fg(18), UiSkin.TextSecondary, TextAnchor.MiddleLeft, FontStyle.Normal);
            _hintBig.wordWrap = false;
            // 滑条上的字不加粗：楷体加粗之后笔画糊成一片，「宽度 X 10」这种字母数字混排
            // 更难认；影子也要用同样的字重，不然影子比字胖一圈、看着发虚。
            _valueBig = MakeStylePx(Fg(22), UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Normal);
            _sliderShadowBig = MakeStylePx(Fg(22), UiSkin.Current == UiSkin.Theme.Light ? new Color(0f, 0f, 0f, 0f) : new Color(0f, 0f, 0f, 0.75f), TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNameBig = MakeStylePx(Fg(19), UiSkin.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guideNameBig.wordWrap = false;
            _guideDescBig = MakeStylePx(Fg(14), UiSkin.TextSecondary, TextAnchor.UpperLeft, FontStyle.Normal);
            _guideDescBig.wordWrap = true;
            _guideNoteBig = MakeStylePx(Fg(17), UiSkin.TextDim, TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNoteBig.wordWrap = false;
            _guideKeyBig = MakeStylePx(Fg(19), UiSkin.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            _guideKeyBig.wordWrap = false;
            _btnBig = new GUIStyle(_btnStyle);
            _btnBig.fontSize = Fg(18);

        }

        int _stylesTheme = -1;

        /// <summary>切主题时把上一套样式里的纯色贴图销毁掉，不然每切一次泄漏三张。</summary>
        void ReleaseStyles()
        {
            if (_btnNormal != null) Destroy(_btnNormal);
            if (_btnHover != null) Destroy(_btnHover);
            if (_btnActive != null) Destroy(_btnActive);
            _btnNormal = null;
            _btnHover = null;
            _btnActive = null;
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

        /// <summary>同 MakeStyle，但字号已经是最终像素值（大号样式的字号由 SyncPageFonts 现算，不能再缩一次）。</summary>
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
                // 暗色主题用浅色粒子（压在黑底上才亮）；亮色主题必须换成饱和的深色 ——
                // 原来那套淡青压在浅蓝背景上几乎看不见（真机截图里只有左边一颗紫点）。
                bool light = UiSkin.Current == UiSkin.Theme.Light;
                Color c = (i % 9 == 0)
                    ? (light ? new Color(0.62f, 0.20f, 0.86f, a * 0.95f)
                             : new Color(0.58f, 0.40f, 0.95f, a * 0.85f))
                    : (light ? new Color(0.10f, 0.34f, 0.82f, a * 0.9f)
                             : new Color(0.76f, 0.93f, 1f, a));

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
