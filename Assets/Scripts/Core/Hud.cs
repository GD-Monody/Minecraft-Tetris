using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>v0.1 用 OnGUI 显示分数/消除/下一个和结束界面，后面换正式 UI。</summary>
    public class Hud : MonoBehaviour
    {
        public GameLoop Loop;
        public bool Paused;
        public System.Action OnRestartClick;

        Font _font;
        int _best;

        void Awake()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
            _best = PlayerPrefs.GetInt("best_score", 0);
        }

        void OnGUI()
        {
            if (_font != null)
                GUI.skin.font = _font;

            GUI.Label(new Rect(20, 16, 320, 28), "分数: " + Loop.Score);
            GUI.Label(new Rect(20, 46, 320, 28), "消除行/层: " + Loop.LinesCleared);
            GUI.Label(new Rect(20, 76, 320, 28), "下一个: " + Loop.Next);

            if (Loop.Score > _best)
            {
                _best = Loop.Score;
                PlayerPrefs.SetInt("best_score", _best);
                PlayerPrefs.Save();
            }
            GUI.Label(new Rect(20, 106, 320, 28), "最高分: " + _best);

            if (Paused && !Loop.GameOver)
                GUI.Label(new Rect(Screen.width / 2f - 40, Screen.height / 2f - 20, 120, 40), "已暂停");

            if (Loop.GameOver)
            {
                GUI.Label(new Rect(Screen.width / 2f - 80, Screen.height / 2f - 40, 240, 40), "游戏结束");
                GUI.Label(new Rect(Screen.width / 2f - 80, Screen.height / 2f + 6, 240, 28), "按 R 重开");
                if (GUI.Button(new Rect(Screen.width / 2f - 60, Screen.height / 2f + 42, 120, 36), "再来一局"))
                {
                    if (OnRestartClick != null)
                        OnRestartClick();
                }
            }
        }
    }
}
