using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 开始界面：标题、副标题、署名，按任意键或点鼠标进入游戏。
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        public bool Visible = true;

        Font _font;
        Font _titleFont;
        Texture2D _titleTex;
        GUIStyle _titleStyle;
        GUIStyle _titleShadow;
        GUIStyle _subStyle;
        GUIStyle _creditStyle;
        GUIStyle _hintStyle;

        void EnsureStyles()
        {
            if (_titleTex == null)
            {
                _titleTex = Resources.Load<Texture2D>("title");
                if (_titleTex != null)
                {
                    _titleTex.filterMode = FilterMode.Point;
                    _titleTex.wrapMode = TextureWrapMode.Clamp;
                }
            }

            if (_font == null)
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
                _titleFont = Font.CreateDynamicFontFromOSFont(
                    new[] { "Arial Black", "Impact", "Arial" }, 20);
            }

            int titleSize = Mathf.Max(30, Mathf.RoundToInt(Screen.height * 0.078f));
            int subSize = Mathf.Max(16, Mathf.RoundToInt(Screen.height * 0.028f));
            int creditSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.019f));
            int hintSize = Mathf.Max(14, Mathf.RoundToInt(Screen.height * 0.022f));

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.font = _titleFont;
            _titleStyle.fontSize = titleSize;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleCenter;
            _titleStyle.normal.textColor = Color.white;

            _titleShadow = new GUIStyle(_titleStyle);
            _titleShadow.normal.textColor = Color.black;

            _subStyle = new GUIStyle(GUI.skin.label);
            _subStyle.font = _font;
            _subStyle.fontSize = subSize;
            _subStyle.alignment = TextAnchor.MiddleCenter;
            _subStyle.normal.textColor = new Color(0.90f, 0.90f, 0.90f);

            _creditStyle = new GUIStyle(GUI.skin.label);
            _creditStyle.font = _font;
            _creditStyle.fontSize = creditSize;
            _creditStyle.alignment = TextAnchor.MiddleCenter;
            _creditStyle.normal.textColor = new Color(0.55f, 0.58f, 0.62f);

            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.font = _font;
            _hintStyle.fontSize = hintSize;
            _hintStyle.alignment = TextAnchor.MiddleCenter;
            _hintStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f);
        }

        public bool AnyInputPressed()
        {
            return Input.anyKeyDown || Input.GetMouseButtonDown(0);
        }

        void OnGUI()
        {
            if (!Visible)
                return;

            EnsureStyles();

            // 压暗游戏画面
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_titleTex != null)
            {
                // 整数倍放大，点阵才不会被插值拉歪
                float scale = Mathf.Max(3f, Mathf.FloorToInt(Screen.height * 0.145f / _titleTex.height));
                float w = _titleTex.width * scale;
                float h = _titleTex.height * scale;
                float x = (Screen.width - w) * 0.5f;
                float y = Screen.height * 0.225f;

                int off = Mathf.Max(2, Mathf.RoundToInt(scale * 0.6f));
                GUI.color = Color.black;
                GUI.DrawTexture(new Rect(x + off * 1.5f, y + off * 1.5f, w, h), _titleTex);
                GUI.DrawTexture(new Rect(x + off, y + off, w, h), _titleTex);
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(x, y, w, h), _titleTex);
            }
            else
            {
                float titleY = Screen.height * 0.235f;
                float titleH = Screen.height * 0.16f;

                // 没有像素标题图时的兜底：粗体白字 + 右下厚投影
                GUI.Label(new Rect(6f, titleY + 6f, Screen.width, titleH), "MINECRAFT X TETRIS", _titleShadow);
                GUI.Label(new Rect(4f, titleY + 4f, Screen.width, titleH), "MINECRAFT X TETRIS", _titleShadow);
                GUI.Label(new Rect(2f, titleY + 2f, Screen.width, titleH), "MINECRAFT X TETRIS", _titleShadow);
                GUI.Label(new Rect(0f, titleY, Screen.width, titleH), "MINECRAFT X TETRIS", _titleStyle);
            }

            GUI.Label(new Rect(0f, Screen.height * 0.435f, Screen.width, 40f), "Made by Monody", _subStyle);
            GUI.Label(new Rect(0f, Screen.height * 0.492f, Screen.width, 30f), "DSH辅助完成", _creditStyle);

            if (Mathf.Repeat(Time.time, 1.4f) < 0.9f)
                GUI.Label(new Rect(0f, Screen.height * 0.64f, Screen.width, 30f), "按任意键开始", _hintStyle);
        }
    }
}
