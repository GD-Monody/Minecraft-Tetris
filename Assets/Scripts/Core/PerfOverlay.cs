using System.Text;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 屏幕角上的帧率 / 帧间隔角标（调试用）。
    ///
    /// 手机上「动画不丝滑」有两种完全不同的成因，得分开看：
    ///   · 帧率本来就不够（比如 45fps）—— 要砍渲染开销；
    ///   · 帧率够（60fps）但帧间隔忽长忽短 —— 是出帧节奏的问题，砍开销没用。
    /// 只看现象分不出来，所以直接把数字摆在屏幕上：FPS、平均帧间隔、最近一秒里
    /// 超过 20ms 的帧数（「卡顿」那栏）。后者是关键：稳定 60fps 时它应该接近 0。
    ///
    /// 手机上默认开着；桌面上带 -fps 参数才开（截图核对用）。
    /// </summary>
    public class PerfOverlay : MonoBehaviour
    {
        bool _show;

        float _fpsSmooth = 60f;
        int _frames;
        float _elapsed;
        int _slow;
        int _slowShown;
        float _fpsShown;
        float _msShown;

        GUIStyle _style;
        readonly StringBuilder _sb = new StringBuilder(48);

        void Awake()
        {
            // 手机上先一直开着，量完数据再决定要不要收进设置里
            _show = MobileMode.Enabled;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fps") >= 0)
                _show = true;
        }

        void Update()
        {
            if (!_show)
                return;

            float dt = Time.unscaledDeltaTime;
            _frames++;
            _elapsed += dt;
            // 20ms ≈ 50fps：超过这个数，60Hz 上就会漏掉一次刷新，肉眼能看出顿
            if (dt > 0.02f)
                _slow++;

            // 瞬时 FPS 抖得厉害，用指数平滑看趋势
            if (dt > 0f)
                _fpsSmooth = Mathf.Lerp(_fpsSmooth, 1f / dt, 0.05f);

            if (_elapsed >= 1f)
            {
                _fpsShown = _elapsed > 0f ? _frames / _elapsed : 0f;
                _msShown = _elapsed > 0f ? _elapsed * 1000f / _frames : 0f;
                _slowShown = _slow;
                _frames = 0;
                _slow = 0;
                _elapsed = 0f;
            }
        }

        void OnGUI()
        {
            if (!_show)
                return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label);
                _style.alignment = TextAnchor.MiddleCenter;
                _style.fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height * 0.022f));
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            }

            _sb.Length = 0;
            _sb.Append(Mathf.RoundToInt(_fpsShown)).Append(" fps   ")
               .Append(_msShown.ToString("0.0")).Append(" ms   ")
               .Append("卡顿 ").Append(_slowShown);

            float w = Screen.width * 0.62f;
            float h = _style.fontSize * 1.9f;
            var box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.012f, w, h);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(box, _sb.ToString(), _style);
        }
    }
}
