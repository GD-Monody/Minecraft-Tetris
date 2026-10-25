using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 开始界面：先提示「Made with Unity」，标题逐字从上方落下，署名从透明渐显，
    /// 最后淡入选项按钮和「DSH辅助完成」。设置面板（音效 / 按键 / 场地尺寸）内嵌在这里。
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Serializable]
        class CharMeta { public int x; public int w; public bool space; }
        [Serializable]
        class TitleMeta { public CharMeta[] chars; }

        enum SettingsPage { Main, Keys, Size }

        public bool Visible = true;
        public bool ShowSettings;
        public bool HasContinue;

        public event Action OnStartGame;
        public event Action OnContinue;
        public event Action OnQuit;
        public event Action<bool> OnSfxChanged;
        public event Action OnKeysChanged;
        public event Action OnSizeApplied;

        const string SfxKey = "sfx_enabled";

        public bool SfxEnabled
        {
            get { return PlayerPrefs.GetInt(SfxKey, 1) == 1; }
        }

        Texture2D _titleTex;
        CharMeta[] _chars;
        float _t;
        bool _stylesReady;
        GUIStyle _madeStyle;
        GUIStyle _subStyle;
        GUIStyle _creditStyle;
        GUIStyle _btnStyle;
        GUIStyle _panelTitleStyle;
        GUIStyle _rowLabelStyle;
        GUIStyle _valueStyle;
        Texture2D _btnNormal;
        Texture2D _btnHover;
        Texture2D _btnActive;

        SettingsPage _page = SettingsPage.Main;
        int _capturingAction = -1;
        int _tmpX = 10;
        int _tmpY = 20;
        int _tmpZ = 1;

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
            GameAction.SoftDrop, GameAction.HardDrop, GameAction.RotateCW, GameAction.RotateCCW
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
            _page = (SettingsPage)Mathf.Clamp(page, 0, 2);
            _capturingAction = -1;
            _tmpX = GameSettings.SizeX;
            _tmpY = GameSettings.SizeY;
            _tmpZ = GameSettings.SizeZ;
        }

        public bool AnyInputPressed()
        {
            return Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
        }

        void LoadAssets()
        {
            _titleTex = Resources.Load<Texture2D>("title");
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

            EnsureStyles();

            if (ShowSettings)
            {
                if (_page == SettingsPage.Keys)
                    HandleCapture();
                DrawSettingsPanel();
                return;
            }

            DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.55f));

            DrawMadeWith();
            DrawTitle();
            DrawSub();
            DrawButtons();
            DrawCredit();
        }

        // ---------- 动画各段 ----------

        void DrawMadeWith()
        {
            if (_t > MadeEnd)
                return;

            float a = Mathf.Clamp01(_t / 0.2f);
            if (_t > MadeEnd - 0.25f)
                a = Mathf.Clamp01((MadeEnd - _t) / 0.25f);

            _madeStyle.normal.textColor = new Color(1f, 1f, 1f, a * 0.9f);
            GUI.Label(new Rect(0f, Screen.height * 0.40f, Screen.width, 50f), "Made with Unity", _madeStyle);
        }

        void DrawTitle()
        {
            if (_t < TitleStart)
                return;

            if (_titleTex == null || _chars == null)
                return;

            int scale = Mathf.Max(2, Mathf.RoundToInt(Screen.height * 0.105f / 16f));
            int texW = _titleTex.width;
            float titleW = texW * scale;
            float titleH = 16f * scale;
            float left = (Screen.width - titleW) * 0.5f;
            float targetY = Mathf.Round(Screen.height * 0.19f);

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
            GUI.Label(new Rect(0f, Screen.height * 0.42f, Screen.width, 44f), "Made by Monody", _subStyle);
        }

        void DrawButtons()
        {
            var items = new List<KeyValuePair<string, Action>>();
            if (HasContinue)
                items.Add(new KeyValuePair<string, Action>("继续游戏", () => Fire(OnContinue)));
            items.Add(new KeyValuePair<string, Action>("开始游戏", () => Fire(OnStartGame)));
            items.Add(new KeyValuePair<string, Action>("设置", () => { ShowSettings = true; _page = SettingsPage.Main; }));
            items.Add(new KeyValuePair<string, Action>("退出", () => Fire(OnQuit)));

            const float bw = 280f;
            const float bh = 44f;
            const float gap = 10f;
            float startY = Screen.height * 0.53f;
            int n = items.Count;

            for (int i = 0; i < n; i++)
            {
                float ti = BtnStart + i * 0.1f;
                float p = Mathf.Clamp01((_t - ti) / 0.35f);
                if (p <= 0f)
                    continue;

                float y = startY + i * (bh + gap) + (1f - p) * 18f;
                var rect = new Rect((Screen.width - bw) * 0.5f, y, bw, bh);

                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, p);
                if (GUI.Button(rect, items[i].Key, _btnStyle))
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
            GUI.Label(new Rect(0f, Screen.height - 62f, Screen.width, 28f), "DSH辅助完成", _creditStyle);
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
            }
        }

        void DrawSettingsMain()
        {
            float w = 420f;
            float h = 384f;
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + 16f, w, 44f), "设置", _panelTitleStyle);

            float bx = panel.x + (w - 300f) * 0.5f;
            float y = panel.y + 80f;

            if (GUI.Button(new Rect(bx, y, 300f, 42f), SfxEnabled ? "音效：开" : "音效：关", _btnStyle))
            {
                bool next = !SfxEnabled;
                PlayerPrefs.SetInt(SfxKey, next ? 1 : 0);
                PlayerPrefs.Save();
                if (OnSfxChanged != null)
                    OnSfxChanged(next);
            }
            y += 54f;

            string elimName = GameSettings.ElimMode == GameSettings.ElimLayer ? "消除：层消" : "消除：行消";
            if (GUI.Button(new Rect(bx, y, 300f, 42f), elimName, _btnStyle))
            {
                int next = GameSettings.ElimMode == GameSettings.ElimLayer
                    ? GameSettings.ElimRows
                    : GameSettings.ElimLayer;
                GameSettings.SetElimMode(next);
            }
            y += 54f;

            if (GUI.Button(new Rect(bx, y, 300f, 42f), "按键设置", _btnStyle))
            {
                _page = SettingsPage.Keys;
                _capturingAction = -1;
            }
            y += 54f;

            if (GUI.Button(new Rect(bx, y, 300f, 42f), "场地尺寸", _btnStyle))
            {
                _tmpX = GameSettings.SizeX;
                _tmpY = GameSettings.SizeY;
                _tmpZ = GameSettings.SizeZ;
                _page = SettingsPage.Size;
            }
            y += 62f;

            if (GUI.Button(new Rect(bx, y, 300f, 42f), "返回", _btnStyle))
                ShowSettings = false;
        }

        void DrawSettingsKeys()
        {
            float w = 480f;
            float h = 620f;
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + 12f, w, 44f), "按键设置", _panelTitleStyle);

            float rowH = 44f;
            float rowGap = 6f;
            float rowW = 380f;
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + 62f;

            for (int i = 0; i < AllActions.Length; i++)
            {
                GameAction action = AllActions[i];
                var row = new Rect(bx, y, rowW, rowH);

                GUI.Label(new Rect(row.x, row.y, 190f, rowH), ActionLabel(action), _rowLabelStyle);

                string keyName = _capturingAction == (int)action
                    ? "按任意键…"
                    : KeyName(KeyboardInput.GetPrimaryKey(action));

                if (GUI.Button(new Rect(row.x + 200f, row.y, 180f, rowH), keyName, _btnStyle))
                {
                    _capturingAction = (int)action;
                }

                y += rowH + rowGap;
            }

            if (_capturingAction >= 0)
            {
                GUI.Label(new Rect(panel.x, panel.y + h - 96f, w, 24f), "按 Esc 取消", _rowLabelStyle);
            }

            float bottomY = panel.y + h - 62f;
            if (GUI.Button(new Rect(bx, bottomY, 180f, 42f), "恢复默认", _btnStyle))
            {
                ResetKeys();
                if (OnKeysChanged != null)
                    OnKeysChanged();
            }
            if (GUI.Button(new Rect(bx + 200f, bottomY, 180f, 42f), "返回", _btnStyle))
            {
                _capturingAction = -1;
                _page = SettingsPage.Main;
            }
        }

        void DrawSettingsSize()
        {
            float w = 460f;
            float h = 460f;
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + 14f, w, 44f), "场地尺寸", _panelTitleStyle);

            float rowW = 360f;
            float bx = panel.x + (w - rowW) * 0.5f;
            float y = panel.y + 76f;

            DrawSizeRow(bx, y, "宽度 X", ref _tmpX, GameSettings.MinSizeX, GameSettings.MaxSizeX);
            y += 58f;
            DrawSizeRow(bx, y, "高度 Y", ref _tmpY, GameSettings.MinSizeY, GameSettings.MaxSizeY);
            y += 58f;
            DrawSizeRow(bx, y, "深度 Z", ref _tmpZ, GameSettings.MinSizeZ, GameSettings.MaxSizeZ);
            y += 54f;

            GUI.Label(new Rect(bx, y, rowW, 24f), "深度大于 1 时用 W / S 前后移动方块", _rowLabelStyle);
            y += 36f;

            if (GUI.Button(new Rect(bx, y, rowW, 44f), "应用并重启", _btnStyle))
            {
                GameSettings.SetSize(_tmpX, _tmpY, _tmpZ);
                if (OnSizeApplied != null)
                    OnSizeApplied();
            }
            y += 54f;

            if (GUI.Button(new Rect(bx, y, rowW, 42f), "返回", _btnStyle))
                _page = SettingsPage.Main;
        }

        void DrawSizeRow(float x, float y, string label, ref int value, int min, int max)
        {
            GUI.Label(new Rect(x, y, 160f, 40f), label, _rowLabelStyle);

            bool canMinus = value > min;
            bool canPlus = value < max;

            GUI.enabled = canMinus;
            if (GUI.Button(new Rect(x + 160f, y, 46f, 40f), "−", _btnStyle))
                value = Mathf.Max(min, value - 1);
            GUI.enabled = true;

            GUI.Label(new Rect(x + 214f, y, 60f, 40f), value.ToString(), _valueStyle);

            GUI.enabled = canPlus;
            if (GUI.Button(new Rect(x + 282f, y, 46f, 40f), "+", _btnStyle))
                value = Mathf.Min(max, value + 1);
            GUI.enabled = true;
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
                case GameAction.RotateCW: return "顺时针旋转";
                case GameAction.RotateCCW: return "逆时针旋转";
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
            _valueStyle = MakeStyle(22, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 18;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.background = _btnNormal;
            _btnStyle.hover.background = _btnHover;
            _btnStyle.active.background = _btnActive;
            _btnStyle.normal.textColor = Color.white;
            _btnStyle.hover.textColor = Color.white;
            _btnStyle.active.textColor = new Color(0.8f, 0.85f, 0.9f, 1f);
            _btnStyle.border = new RectOffset(0, 0, 0, 0);
        }

        static GUIStyle MakeStyle(int size, Color color, TextAnchor anchor, FontStyle style)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = size;
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
