using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 开始界面：先提示「Made with Unity」，标题逐字从上方落下，署名从透明渐显，
    /// 最后淡入选项按钮和「DSH辅助完成」。设置面板内嵌在这里。
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        [Serializable]
        class CharMeta { public int x; public int w; public bool space; }
        [Serializable]
        class TitleMeta { public CharMeta[] chars; }

        public bool Visible;
        public bool ShowSettings;
        public bool HasContinue;

        public event Action OnStartGame;
        public event Action OnContinue;
        public event Action OnQuit;
        public event Action<bool> OnSfxChanged;

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
        Texture2D _btnNormal;
        Texture2D _btnHover;
        Texture2D _btnActive;

        // 时间轴（秒）
        const float MadeEnd = 0.8f;
        const float TitleStart = 0.85f;
        const float CharDelay = 0.055f;
        const float CharFall = 0.5f;
        const float SubStart = 2.35f;
        const float BtnStart = 2.8f;
        const float CreditStart = 3.1f;

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
            if (show)
                _t = 0f;
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
            {
                _subStyle.normal.textColor = new Color(1f, 1f, 1f, 1f);
                return;
            }

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
            items.Add(new KeyValuePair<string, Action>("设置", () => { ShowSettings = true; }));
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

            float cw = 360f;
            float cx = (Screen.width - cw) * 0.5f;
            float y = Screen.height * 0.3f;

            _panelTitleStyle.normal.textColor = Color.white;
            GUI.Label(new Rect(0f, y, Screen.width, 44f), "设置", _panelTitleStyle);
            y += 64f;

            string sfxLabel = SfxEnabled ? "音效：开" : "音效：关";
            if (GUI.Button(new Rect(cx, y, cw, 42f), sfxLabel, _btnStyle))
            {
                bool next = !SfxEnabled;
                PlayerPrefs.SetInt(SfxKey, next ? 1 : 0);
                PlayerPrefs.Save();
                if (OnSfxChanged != null)
                    OnSfxChanged(next);
            }
            y += 62f;

            if (GUI.Button(new Rect(cx, y, cw, 42f), "返回", _btnStyle))
                ShowSettings = false;
        }

        // ---------- 工具 ----------

        void Fire(Action a)
        {
            if (a != null)
                a();
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
            _panelTitleStyle = MakeStyle(34, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 19;
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
