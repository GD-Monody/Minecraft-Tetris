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

        enum SettingsPage { Main, Keys, Size, Workshop, Speed, Music, Guide }

        /// <summary>「特殊方块」说明页在 SettingsPage 里的下标，调试开关要用。</summary>
        public const int PageGuide = (int)SettingsPage.Guide;

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
        /// <summary>大号数值（下落速度页那个「1.00×」）。</summary>
        GUIStyle _bigValueStyle;
        /// <summary>提示行专用：不折行。折行会把固定的 Rect 撑破、压到下面的按钮上。</summary>
        GUIStyle _hintStyle;
        /// <summary>「特殊方块」页：方块名、机制说明、页脚小字。</summary>
        GUIStyle _guideNameStyle;
        GUIStyle _guideDescStyle;
        GUIStyle _guideNoteStyle;
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
            if (show)
                _t = 0f;
        }

        /// <summary>跳过开场动画，直接从暂停回到主界面时用。</summary>
        public void SkipIntro()
        {
            _t = 99f;
        }

        /// <summary>调试用：直接打开某个设置页（0 主页面 / 1 按键 / 2 尺寸）。</summary>
        public void OpenSettingsPage(int page)
        {
            ShowSettings = true;
            _page = (SettingsPage)Mathf.Clamp(page, 0, (int)SettingsPage.Guide);
            _tmpSpeed = GameSettings.FallSpeed;
            _tmpMusic = GameSettings.MusicVolume;
            _capturingAction = -1;
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
            items.Add(new KeyValuePair<string, Action>("特殊方块", () => { ShowSettings = true; _page = SettingsPage.Guide; }));
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
            }
        }

        void DrawSettingsMain()
        {
            float w = UiSkin.P(420f);
            // 按钮数 × 54 + 上下留白；加一项就跟着涨，不然会压到「返回」上
            float h = UiSkin.P(602f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(16f), w, UiSkin.P(44f)), "设置", _panelTitleStyle);

            float bw = UiSkin.P(300f);
            float bh = UiSkin.P(42f);
            float step = UiSkin.P(54f);
            float bx = panel.x + (w - bw) * 0.5f;
            float y = panel.y + UiSkin.P(80f);

            if (GUI.Button(new Rect(bx, y, bw, bh), SfxEnabled ? "音效：开" : "音效：关", _btnStyle))
            {
                bool next = !SfxEnabled;
                PlayerPrefs.SetInt(SfxKey, next ? 1 : 0);
                PlayerPrefs.Save();
                if (OnSfxChanged != null)
                    OnSfxChanged(next);
            }
            y += step;

            string elimName = GameSettings.ElimMode == GameSettings.ElimLayer ? "消除：层消" : "消除：行消";
            if (GUI.Button(new Rect(bx, y, bw, bh), elimName, _btnStyle))
            {
                int next = GameSettings.ElimMode == GameSettings.ElimLayer
                    ? GameSettings.ElimRows
                    : GameSettings.ElimLayer;
                GameSettings.SetElimMode(next);
            }
            y += step;

            string speedName = "下落速度：" + GameSettings.FallSpeed.ToString("0.00") + "×";
            if (GUI.Button(new Rect(bx, y, bw, bh), speedName, _btnStyle))
            {
                _tmpSpeed = GameSettings.FallSpeed;
                _page = SettingsPage.Speed;
            }
            y += step;

            string musicName = GameSettings.MusicVolume <= 0.005f
                ? "音乐：关"
                : "音乐：" + Mathf.RoundToInt(GameSettings.MusicVolume * 100f) + "%";
            if (GUI.Button(new Rect(bx, y, bw, bh), musicName, _btnStyle))
            {
                _tmpMusic = GameSettings.MusicVolume;
                _page = SettingsPage.Music;
            }
            y += step;

            if (GUI.Button(new Rect(bx, y, bw, bh), "按键设置", _btnStyle))
            {
                _page = SettingsPage.Keys;
                _capturingAction = -1;
            }
            y += step;

            if (GUI.Button(new Rect(bx, y, bw, bh), "场地尺寸", _btnStyle))
            {
                _tmpX = GameSettings.SizeX;
                _tmpY = GameSettings.SizeY;
                _tmpZ = GameSettings.SizeZ;
                _page = SettingsPage.Size;
            }
            y += step;

            // 基岩不掉落，落下来就是一根永久柱子，默认关；想玩就自己开
            string bedrockName = GameSettings.BedrockInPool ? "基岩进池：开" : "基岩进池：关";
            if (GUI.Button(new Rect(bx, y, bw, bh), bedrockName, _btnStyle))
                GameSettings.SetBedrockInPool(!GameSettings.BedrockInPool);
            y += step;

            // 结构方块会记住一片构造、被消时还原，对局面的影响比普通方块大，默认关
            string structureName = GameSettings.StructureInPool ? "结构方块进池：开" : "结构方块进池：关";
            if (GUI.Button(new Rect(bx, y, bw, bh), structureName, _btnStyle))
                GameSettings.SetStructureInPool(!GameSettings.StructureInPool);
            y += UiSkin.P(62f);

            if (GUI.Button(new Rect(bx, y, bw, bh), "返回", _btnStyle))
                ShowSettings = false;
        }

        void DrawSettingsKeys()
        {
            // 12 组键 × 每行 P(40)+P(4)：放大字号后仍要能塞进 1440×900 的窗口
            float rowH = UiSkin.P(40f);
            float rowGap = UiSkin.P(4f);

            float w = UiSkin.P(480f);
            // 面板高度跟着键位条数走，不然每加一组键（比如俯视旋转）最后一行就会压到底部按钮上
            float h = UiSkin.P(62f) + AllActions.Length * (rowH + rowGap) + UiSkin.P(80f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(12f), w, UiSkin.P(44f)), "按键设置", _panelTitleStyle);

            float rowW = UiSkin.P(380f);
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + UiSkin.P(62f);

            for (int i = 0; i < AllActions.Length; i++)
            {
                GameAction action = AllActions[i];
                var row = new Rect(bx, y, rowW, rowH);

                GUI.Label(new Rect(row.x, row.y, UiSkin.P(190f), rowH), ActionLabel(action), _rowLabelStyle);

                string keyName = _capturingAction == (int)action
                    ? "按任意键…"
                    : KeyName(KeyboardInput.GetPrimaryKey(action));

                if (GUI.Button(new Rect(row.x + UiSkin.P(200f), row.y, UiSkin.P(180f), rowH), keyName, _btnStyle))
                {
                    _capturingAction = (int)action;
                }

                y += rowH + rowGap;
            }

            if (_capturingAction >= 0)
            {
                GUI.Label(new Rect(panel.x, panel.y + h - UiSkin.P(96f), w, UiSkin.P(30f)), "按 Esc 取消", _rowLabelStyle);
            }

            float bottomY = panel.y + h - UiSkin.P(62f);
            if (GUI.Button(new Rect(bx, bottomY, UiSkin.P(180f), UiSkin.P(42f)), "恢复默认", _btnStyle))
            {
                ResetKeys();
                if (OnKeysChanged != null)
                    OnKeysChanged();
            }
            if (GUI.Button(new Rect(bx + UiSkin.P(200f), bottomY, UiSkin.P(180f), UiSkin.P(42f)), "返回", _btnStyle))
            {
                _capturingAction = -1;
                _page = SettingsPage.Main;
            }
        }

        void DrawSettingsSize()
        {
            float w = UiSkin.P(460f);
            float h = UiSkin.P(500f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(14f), w, UiSkin.P(44f)), "场地尺寸", _panelTitleStyle);

            float rowW = UiSkin.P(360f);
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + UiSkin.P(76f);

            DrawSizeRow(bx, y, "宽度 X", ref _tmpX, GameSettings.MinSizeX, GameSettings.MaxSizeX);
            y += UiSkin.P(58f);
            DrawSizeRow(bx, y, "高度 Y", ref _tmpY, GameSettings.MinSizeY, GameSettings.MaxSizeY);
            y += UiSkin.P(58f);
            DrawSizeRow(bx, y, "深度 Z", ref _tmpZ, GameSettings.MinSizeZ, GameSettings.MaxSizeZ);
            y += UiSkin.P(54f);

            GUI.Label(new Rect(bx, y, rowW, UiSkin.P(40f)), "深度大于 1 时用 W / S 前后移动方块", _rowLabelStyle);
            y += UiSkin.P(44f);

            if (GUI.Button(new Rect(bx, y, rowW, UiSkin.P(44f)), "应用并重启", _btnStyle))
            {
                GameSettings.SetSize(_tmpX, _tmpY, _tmpZ);
                if (OnSizeApplied != null)
                    OnSizeApplied();
            }
            y += UiSkin.P(54f);

            if (GUI.Button(new Rect(bx, y, rowW, UiSkin.P(42f)), "返回", _btnStyle))
                _page = SettingsPage.Main;
        }

        void DrawSizeRow(float x, float y, string label, ref int value, int min, int max)
        {
            GUI.Label(new Rect(x, y, UiSkin.P(160f), UiSkin.P(40f)), label, _rowLabelStyle);

            bool canMinus = value > min;
            bool canPlus = value < max;

            GUI.enabled = canMinus;
            if (GUI.Button(new Rect(x + UiSkin.P(160f), y, UiSkin.P(46f), UiSkin.P(40f)), "−", _btnStyle))
                value = Mathf.Max(min, value - 1);
            GUI.enabled = true;

            GUI.Label(new Rect(x + UiSkin.P(214f), y, UiSkin.P(60f), UiSkin.P(40f)), value.ToString(), _valueStyle);

            GUI.enabled = canPlus;
            if (GUI.Button(new Rect(x + UiSkin.P(282f), y, UiSkin.P(46f), UiSkin.P(40f)), "+", _btnStyle))
                value = Mathf.Min(max, value + 1);
            GUI.enabled = true;
        }

        void DrawSettingsSpeed()
        {
            float w = UiSkin.P(500f);
            float h = UiSkin.P(380f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(14f), w, UiSkin.P(44f)), "下落速度", _panelTitleStyle);

            float rowW = UiSkin.P(400f);
            float bx = panel.x + (w - rowW) * 0.5f;

            // 当前值放大居中，下面一排按钮：粗调 ±0.10、细调 ±0.01
            float v = GameSettings.ClampFallSpeed(_tmpSpeed);
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(66f), w, UiSkin.P(56f)),
                v.ToString("0.00") + "×", _bigValueStyle);

            bool changed = false;
            float btnW = UiSkin.P(88f);
            float gap = UiSkin.P(10f);
            float bh = UiSkin.P(42f);
            float rowBtnW = btnW * 4f + gap * 3f;
            float btnX = panel.x + (w - rowBtnW) * 0.5f;
            float btnY = panel.y + UiSkin.P(126f);

            if (GUI.Button(new Rect(btnX, btnY, btnW, bh), "−0.10", _btnStyle)) { v -= 0.1f; changed = true; }
            if (GUI.Button(new Rect(btnX + btnW + gap, btnY, btnW, bh), "−0.01", _btnStyle)) { v -= 0.01f; changed = true; }
            if (GUI.Button(new Rect(btnX + (btnW + gap) * 2f, btnY, btnW, bh), "+0.01", _btnStyle)) { v += 0.01f; changed = true; }
            if (GUI.Button(new Rect(btnX + (btnW + gap) * 3f, btnY, btnW, bh), "+0.10", _btnStyle)) { v += 0.1f; changed = true; }

            if (changed)
            {
                _tmpSpeed = GameSettings.ClampFallSpeed(v);
                GameSettings.SetFallSpeed(_tmpSpeed);
                if (OnFallSpeedChanged != null)
                    OnFallSpeedChanged();
            }

            // 四行提示，每行都短到不会折行（折行会撑破 Rect、压到下面的按钮上）
            float lineH = UiSkin.P(28f);
            float y = panel.y + UiSkin.P(176f);
            GUI.Label(new Rect(bx, y, rowW, lineH),
                "1.00× = 标准（每 " + GameLoop.BaseFallInterval.ToString("0.00") + " 秒落一格）", _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH),
                "当前：每 " + (GameLoop.BaseFallInterval / Mathf.Max(0.01f, _tmpSpeed)).ToString("0.00") + " 秒落一格",
                _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "范围 0.01 ~ 2.00（两位小数）", _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "3D 场地建议 0.50 ~ 0.80", _hintStyle);

            float bottomY = panel.y + h - UiSkin.P(62f);
            if (GUI.Button(new Rect(bx, bottomY, UiSkin.P(190f), bh), "恢复标准", _btnStyle))
            {
                _tmpSpeed = GameSettings.DefaultFallSpeed;
                GameSettings.SetFallSpeed(_tmpSpeed);
                if (OnFallSpeedChanged != null)
                    OnFallSpeedChanged();
            }

            if (GUI.Button(new Rect(bx + rowW - UiSkin.P(190f), bottomY, UiSkin.P(190f), bh), "返回", _btnStyle))
                _page = SettingsPage.Main;
        }

        /// <summary>
        /// 音乐音量。曲库是从 MC 原版里提的那些常速曲，随机播放；
        /// 这里只管音量，0% 就是关。
        /// </summary>
        void DrawSettingsMusic()
        {
            float w = UiSkin.P(500f);
            float h = UiSkin.P(380f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(14f), w, UiSkin.P(44f)), "音乐音量", _panelTitleStyle);

            float rowW = UiSkin.P(400f);
            float bx = panel.x + (w - rowW) * 0.5f;

            float v = GameSettings.ClampMusicVolume(_tmpMusic);
            string shown = v <= 0.005f ? "静音" : Mathf.RoundToInt(v * 100f) + "%";
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(66f), w, UiSkin.P(56f)), shown, _bigValueStyle);

            bool changed = false;
            float btnW = UiSkin.P(110f);
            float gap = UiSkin.P(16f);
            float bh = UiSkin.P(42f);
            float rowBtnW = btnW * 3f + gap * 2f;
            float btnX = panel.x + (w - rowBtnW) * 0.5f;
            float btnY = panel.y + UiSkin.P(126f);

            if (GUI.Button(new Rect(btnX, btnY, btnW, bh), "−10%", _btnStyle)) { v -= 0.1f; changed = true; }
            if (GUI.Button(new Rect(btnX + btnW + gap, btnY, btnW, bh), "静音", _btnStyle)) { v = 0f; changed = true; }
            if (GUI.Button(new Rect(btnX + (btnW + gap) * 2f, btnY, btnW, bh), "+10%", _btnStyle)) { v += 0.1f; changed = true; }

            if (changed)
            {
                _tmpMusic = GameSettings.ClampMusicVolume(v);
                GameSettings.SetMusicVolume(_tmpMusic);
                if (OnMusicChanged != null)
                    OnMusicChanged();
            }

            // 一行一句，短到不会折行（折行会撑破 Rect、压到底部按钮上）
            float lineH = UiSkin.P(28f);
            float y = panel.y + UiSkin.P(176f);
            GUI.Label(new Rect(bx, y, rowW, lineH), "原版曲库，随机播放，两首之间留一段静默", _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "标题界面用菜单曲，进游戏换成常速曲", _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "0% = 关闭音乐（放置音效另算）", _hintStyle);
            y += lineH;
            GUI.Label(new Rect(bx, y, rowW, lineH), "按 Esc 暂停时音乐一起暂停", _hintStyle);

            float bottomY = panel.y + h - UiSkin.P(62f);
            if (GUI.Button(new Rect(bx, bottomY, UiSkin.P(190f), bh), "恢复默认", _btnStyle))
            {
                _tmpMusic = GameSettings.DefaultMusicVolume;
                GameSettings.SetMusicVolume(_tmpMusic);
                if (OnMusicChanged != null)
                    OnMusicChanged();
            }

            if (GUI.Button(new Rect(bx + rowW - UiSkin.P(190f), bottomY, UiSkin.P(190f), bh), "返回", _btnStyle))
                _page = SettingsPage.Main;
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
                new GuideItem
                {
                    Ids = new[] { Tnt.TntId },
                    Title = "TNT",
                    Desc = "贴着红石块或岩浆会被点着，2.4 秒后炸掉半径 2 以内的方块，旁边的 TNT 会跟着连锁。",
                },
                new GuideItem
                {
                    Ids = new[] { Tnt.RedstoneId },
                    Title = "红石块",
                    Desc = "贴着 TNT 就点火，隔一格不算。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.StructureId },
                    Title = "结构方块",
                    Desc = "落定记住整场，被消行、消层或炸掉时，场上一格格收掉再按记忆掉回来。默认不进池。",
                },
                new GuideItem
                {
                    Ids = new[] { BlockCatalog.BedrockId },
                    Title = "基岩",
                    Desc = "不随消除下落，也炸不掉，默认不进池。",
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
                    Ids = new[] { FindBlock("oak_log") },
                    Title = "原木",
                    Desc = "落定以后，周围的空位会长出同种树叶。",
                },
            };
        }

        void DrawGuide()
        {
            EnsureGuideItems();

            float w = UiSkin.P(1060f);
            float h = UiSkin.P(710f);
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(14f), w, UiSkin.P(44f)), "特殊方块", _panelTitleStyle);
            GUI.Label(new Rect(panel.x, panel.y + UiSkin.P(60f), w, UiSkin.P(24f)),
                "只列和原版不一样、或者第一次玩容易搞错的机制", _guideNoteStyle);

            const int perCol = 5;
            float colW = UiSkin.P(470f);
            float rowH = UiSkin.P(104f);
            float top = panel.y + UiSkin.P(104f);

            for (int i = 0; i < _guideItems.Length; i++)
            {
                float x = panel.x + UiSkin.P(40f) + (i / perCol) * (colW + UiSkin.P(40f));
                DrawGuideRow(x, top + (i % perCol) * rowH, colW, _guideItems[i]);
            }

            if (GUI.Button(new Rect(panel.x + w - UiSkin.P(230f), panel.yMax - UiSkin.P(72f),
                UiSkin.P(180f), UiSkin.P(44f)), "返回", _btnStyle))
                ShowSettings = false;
        }

        void DrawGuideRow(float x, float y, float w, GuideItem item)
        {
            int n = 0;
            for (int i = 0; i < item.Ids.Length; i++)
                if (item.Ids[i] > 0)
                    n++;

            float icon = n <= 2 ? UiSkin.P(40f) : UiSkin.P(30f);
            float gap = UiSkin.P(4f);
            float iconsW = n > 0 ? n * icon + (n - 1) * gap : 0f;

            int slot = 0;
            for (int i = 0; i < item.Ids.Length; i++)
            {
                if (item.Ids[i] <= 0)
                    continue;
                DrawBlockIcon(new Rect(x + slot * (icon + gap), y + UiSkin.P(2f), icon, icon), item.Ids[i]);
                slot++;
            }

            float pad = iconsW > 0f ? iconsW + UiSkin.P(14f) : 0f;
            GUI.Label(new Rect(x + pad, y, w - pad, UiSkin.P(26f)), item.Title, _guideNameStyle);
            GUI.Label(new Rect(x + pad, y + UiSkin.P(26f), w - pad, UiSkin.P(70f)), item.Desc, _guideDescStyle);
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
            _bigValueStyle = MakeStyle(34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            _guideNameStyle = MakeStyle(19, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            _guideNameStyle.wordWrap = false;
            _guideDescStyle = MakeStyle(14, new Color(0.78f, 0.82f, 0.88f, 1f), TextAnchor.UpperLeft, FontStyle.Normal);
            _guideDescStyle.wordWrap = true;
            _guideNoteStyle = MakeStyle(17, new Color(0.68f, 0.72f, 0.80f, 1f), TextAnchor.MiddleCenter, FontStyle.Normal);
            _guideNoteStyle.wordWrap = false;

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
