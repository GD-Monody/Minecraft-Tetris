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

        enum SettingsPage { Main, Keys, Size, Workshop }

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
        GUIStyle _listStyle;
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
            _page = (SettingsPage)Mathf.Clamp(page, 0, (int)SettingsPage.Workshop);
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

            DrawPortalMotes();

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
            items.Add(new KeyValuePair<string, Action>("创意工坊", OpenWorkshop));
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
                case SettingsPage.Workshop:
                    DrawWorkshop();
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
                GUI.Label(new Rect(panel.x, panel.y + h - 96f, w, 30f), "按 Esc 取消", _rowLabelStyle);
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
            float h = 500f;
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

            GUI.Label(new Rect(bx, y, rowW, 40f), "深度大于 1 时用 W / S 前后移动方块", _rowLabelStyle);
            y += 44f;

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
            float w = 680f;
            float h = 640f;
            Rect panel = CenterPanel(w, h);
            DrawPanel(panel);

            GUI.Label(new Rect(panel.x, panel.y + 12f, w, 44f), "创意工坊", _panelTitleStyle);

            // 左边的模具列表
            float listX = panel.x + 24f;
            float listY = panel.y + 96f;
            const float itemH = 38f;
            const float itemGap = 6f;

            GUI.Label(new Rect(listX, listY - 30f, 280f, 30f), "模具列表（点一下选中编辑）", _rowLabelStyle);

            int count = MoldData.Items.Count;
            for (int i = 0; i < count && i < 7; i++)
            {
                MoldEntry m = MoldData.Items[i];
                string label = (i == _moldSel ? "▸ " : "   ") + m.name + (m.enabled != 0 ? "     ✓ 启用" : "     ○ 未启用");
                var rect = new Rect(listX, listY + i * (itemH + itemGap), 260f, itemH);
                if (i == _moldSel)
                    DrawRect(rect, new Color(0.42f, 0.86f, 1f, 0.22f));
                if (GUI.Button(rect, label, _listStyle) && i != _moldSel)
                {
                    _moldSel = i;
                    LoadMoldEdit();
                }
            }

            if (count > 7)
                GUI.Label(new Rect(listX, listY + 7 * (itemH + itemGap), 260f, 30f), "（只显示前 7 个）", _rowLabelStyle);

            // 右边的 5x5 画布
            const float cell = 46f;
            float gridX = panel.x + 348f;
            float gridY = panel.y + 96f;

            GUI.Label(new Rect(gridX, gridY - 30f, 300f, 30f), "点格子摆形状（再点一次取消）", _rowLabelStyle);

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
            float bx = panel.x + 40f;
            float by = panel.y + 430f;
            const float bw2 = 110f;
            const float bh2 = 40f;
            const float bgap = 12f;

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

            if (GUI.Button(new Rect(bx + (bw2 + bgap) * 3f, by, 200f, bh2), enableLabel, _btnStyle))
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
                float ry = by - 46f;
                GUI.Label(new Rect(bx, ry + 2f, 56f, 30f), "名字", _rowLabelStyle);
                _renameText = GUI.TextField(new Rect(bx + 58f, ry, 300f, 32f), _renameText, 16, _btnStyle);
                if (GUI.Button(new Rect(bx + 370f, ry, 70f, 32f), "确定", _btnStyle))
                {
                    MoldData.Rename(_moldSel, _renameText);
                    _renaming = false;
                    LoadMoldEdit();
                }
                if (GUI.Button(new Rect(bx + 452f, ry, 70f, 32f), "取消", _btnStyle))
                    _renaming = false;
            }

            GUI.Label(new Rect(panel.x + 40f, by + bh2 + 20f, 520f, 30f),
                "启用的模具会和 7 种默认方块一起随机出现", _rowLabelStyle);

            if (GUI.Button(new Rect(panel.x + w - 170f, by + bh2 + 22f, 130f, bh2), "返回", _btnStyle))
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
            _listStyle = new GUIStyle(_btnStyle);
            _listStyle.fontSize = 17;
            _listStyle.alignment = TextAnchor.MiddleLeft;
            _listStyle.padding = new RectOffset(14, 10, 0, 0);
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
