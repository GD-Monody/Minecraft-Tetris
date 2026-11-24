using System.Text;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 屏幕角上的性能角标（调试用）：帧率 / 帧间隔 / 卡顿帧数 + 各段耗时。
    ///
    /// 手机上「动画不丝滑」有两种完全不同的成因，得分开看：
    ///   · 帧率本来就不够 —— 要砍开销；
    ///   · 帧率够但帧间隔忽长忽短 —— 出帧节奏的问题，砍开销没用。
    /// 所以第一行给 FPS、平均帧间隔、以及最近一秒里超过 20ms 的帧数
    /// （20ms≈50fps，超过就会漏刷新，肉眼能看出顿）。
    ///
    /// 第二行把一帧拆成几段（见 PerfCounters）：逻辑 / 视图 / 界面 / 按键 / 俯视，
    /// 「其余」= 帧时间减掉这些，也就是渲染 + 引擎本身 —— 它大就是 GPU 扛不住，
    /// 界面那几项大就是 IMGUI 拖的，两种情况的解法完全不同。
    ///
    /// 手机上默认开着；桌面上带 -fps 参数才开（截图核对用）。
    /// </summary>
    public class PerfOverlay : MonoBehaviour
    {
        bool _show;

        int _frames;
        float _elapsed;
        int _slow;

        float _fpsShown;
        float _msShown;
        int _slowShown;
        float _loop, _view, _hud, _ctl, _map;
        string _hzInfo = "";

        GUIStyle _style;
        readonly StringBuilder _sb = new StringBuilder(96);

        void Awake()
        {
            // 平时不显示：手机上一直挂着太碍事。要量性能时用命令行带 -fps 启动
            // （桌面版：MinecraftTetris.exe -fps）。计时本身几乎不花钱，留着随时能用。
            _show = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-fps") >= 0;
        }

        void Update()
        {
            if (!_show)
                return;

            float dt = Time.unscaledDeltaTime;
            _frames++;
            _elapsed += dt;
            // 20ms ≈ 50fps：超过这个数，60Hz 上就会漏一次刷新
            if (dt > 0.02f)
                _slow++;

            if (_elapsed >= 1f)
            {
                _fpsShown = _frames / _elapsed;
                _msShown = _elapsed * 1000f / _frames;
                _slowShown = _slow;

                float n = Mathf.Max(1, _frames);
                _loop = (float)(PerfCounters.LoopMs / n);
                _view = (float)(PerfCounters.ViewMs / n);
                _hud = (float)(PerfCounters.HudMs / n);
                _ctl = (float)(PerfCounters.CtlMs / n);
                _map = (float)(PerfCounters.MapMs / n);
                PerfCounters.Reset();

                // 屏幕刷新率 vs 我们锁的目标帧率：90Hz 屏锁 60 会退成 45，这个对照一眼能看出来
                _hzInfo = "屏" + Mathf.RoundToInt(Screen.currentResolution.refreshRate)
                          + " / 目标" + Application.targetFrameRate;

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
                _style.fontSize = Mathf.Max(11, Mathf.RoundToInt(Screen.height * 0.019f));
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.92f);
            }

            float rest = Mathf.Max(0f, _msShown - (_loop + _view + _hud + _ctl + _map));

            _sb.Length = 0;
            _sb.Append(Mathf.RoundToInt(_fpsShown)).Append(" fps   ")
               .Append(_msShown.ToString("0.0")).Append(" ms   卡顿 ").Append(_slowShown)
               .Append("   ").Append(_hzInfo)
               .Append('\n')
               .Append("逻辑 ").Append(_loop.ToString("0.0"))
               .Append("  视图 ").Append(_view.ToString("0.0"))
               .Append("  界面 ").Append(_hud.ToString("0.0"))
               .Append("  按键 ").Append(_ctl.ToString("0.0"))
               .Append("  俯视 ").Append(_map.ToString("0.0"))
               .Append("  其余 ").Append(rest.ToString("0.0"));

            float w = Mathf.Min(Screen.width * 0.94f, _style.fontSize * 26f);
            float h = _style.fontSize * 3.0f;
            var box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.008f, w, h);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(box, _sb.ToString(), _style);
        }
    }
}
