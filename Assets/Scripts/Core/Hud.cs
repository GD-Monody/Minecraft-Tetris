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

        Font _font;
        int _best;
        int _lastScore;
        float _scoreFlash;

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
        GUIStyle _valueSmall;
        GUIStyle _valueSmallShadow;
        GUIStyle _center;
        GUIStyle _centerShadow;

        void Awake()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
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
        }

        void EnsureStyles()
        {
            if (_label != null)
                return;

            _label = MakeStyle(15, LabelColor);
            _labelShadow = MakeStyle(15, ShadowColor);
            _value = MakeStyle(30, ValueColor);
            _valueShadow = MakeStyle(30, ShadowColor);
            _valueSmall = MakeStyle(18, ValueColor);
            _valueSmallShadow = MakeStyle(18, ShadowColor);
            _center = MakeStyle(26, ValueColor);
            _centerShadow = MakeStyle(26, ShadowColor);
        }

        GUIStyle MakeStyle(int size, Color color)
        {
            var s = new GUIStyle(GUI.skin.label);
            s.fontSize = size;
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

            if (_font != null)
                GUI.skin.font = _font;

            EnsureStyles();

            if (Loop.Score > _best)
            {
                _best = Loop.Score;
                PlayerPrefs.SetInt("best_score", _best);
                PlayerPrefs.Save();
            }

            DrawScorePanel();
            DrawNextPanel();

            if (Paused && !Loop.GameOver)
                DrawCenterText("已暂停");

            if (Loop.GameOver)
            {
                DrawCenterText("游戏结束");
                Text(new Rect(Screen.width / 2f - 120f, Screen.height / 2f + 10f, 240f, 30f),
                    "按 R 重开", _label, _labelShadow);
                if (GUI.Button(new Rect(Screen.width / 2f - 65f, Screen.height / 2f + 46f, 130f, 38f), "再来一局"))
                {
                    if (OnRestartClick != null)
                        OnRestartClick();
                }
            }
        }

        void DrawScorePanel()
        {
            var panel = new Rect(14f, 14f, 246f, 150f);
            DrawPanel(panel);

            float x = panel.x + 16f;
            float y = panel.y + 12f;

            Text(new Rect(x, y, 80f, 20f), "分数", _label, _labelShadow);

            float scale = 1f;
            if (_scoreFlash > 0f)
                scale = 1f + _scoreFlash * 0.4f;
            Rect valueRect = new Rect(x + 46f, y - 8f, 200f, 40f);
            GUI.matrix = Matrix4x4.TRS(new Vector3(valueRect.x, valueRect.y, 0f), Quaternion.identity,
                new Vector3(scale, scale, 1f));
            GUIStyle scoreStyle = _scoreFlash > 0f
                ? MakeStyle(30, Color.Lerp(ValueColor, FlashColor, _scoreFlash / 0.4f))
                : _value;
            GUI.Label(new Rect(1f, 2f, 200f, 40f), Loop.Score.ToString(), _valueShadow);
            GUI.Label(new Rect(0f, 0f, 200f, 40f), Loop.Score.ToString(), scoreStyle);
            GUI.matrix = Matrix4x4.identity;

            y += 46f;
            Text(new Rect(x, y, 80f, 20f), "消除", _label, _labelShadow);
            Text(new Rect(x + 46f, y - 3f, 200f, 26f), Loop.LinesCleared + " 行", _valueSmall, _valueSmallShadow);

            y += 34f;
            Text(new Rect(x, y, 80f, 20f), "最高", _label, _labelShadow);
            Text(new Rect(x + 46f, y - 3f, 200f, 26f), _best.ToString(), _valueSmall, _valueSmallShadow);

            DrawAccentLine(panel);
        }

        void DrawNextPanel()
        {
            var panel = new Rect(14f, 172f, 246f, 178f);
            DrawPanel(panel);

            Text(new Rect(panel.x + 16f, panel.y + 10f, 200f, 20f), "下一个", _label, _labelShadow);

            var previewRect = new Rect(panel.x + 14f, panel.y + 34f, 112f, 112f);
            if (Preview != null && Preview.Texture != null)
                GUI.DrawTexture(previewRect, Preview.Texture, ScaleMode.ScaleToFit, true);

            Text(new Rect(panel.x + 134f, panel.y + 78f, 106f, 44f),
                TypeLabel(Loop.Next), _valueSmall, _valueSmallShadow);

            DrawAccentLine(panel);
        }

        void DrawCenterText(string text)
        {
            GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height / 2f - 40f, 400f, 40f), text, _centerShadow);
            GUI.Label(new Rect(Screen.width / 2f - 200f, Screen.height / 2f - 42f, 400f, 40f), text, _center);
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

        static void DrawPanel(Rect r)
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

        static void DrawAccentLine(Rect panel)
        {
            GUI.color = Accent;
            GUI.DrawTexture(new Rect(panel.x + 1f, panel.yMax - 3f, panel.width * 0.42f, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
