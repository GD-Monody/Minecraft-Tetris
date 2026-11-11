using System.Collections;
using System.IO;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 离线逐帧录制：把游戏画面一帧一帧存成 PNG，事后交给 ffmpeg 拼成视频。
    ///
    /// 为什么不用录屏软件：
    /// - 录屏抓的是桌面，窗口被别的窗口盖住就会录进别人的画面，窗口一旦失焦还可能被系统降帧；
    /// - 屏幕只有这么大，录不出比桌面更高的分辨率。
    ///
    /// 这里的做法是让游戏自己读自己的帧缓冲（ScreenCapture.CaptureScreenshot），
    /// 因此窗口被盖住、被移到屏幕外都不影响结果，分辨率也能给到 1920×1080。
    ///
    /// 用 Time.captureFramerate 把游戏时间锁成「一帧 = 1/fps 秒」：
    /// 即使机器渲染得慢、存图又慢，录出来的动画速度依然是准的，
    /// 不会出现「这台机器快就掉得快、那台慢就慢动作」的问题。
    ///
    /// 用法：
    ///   MinecraftTetris.exe -firetest -record D:\out\fire -recordframes 300
    ///   -recordfps 30     每秒录多少帧（默认 30）
    ///   -recordframes N   录满 N 帧就存档并退出（默认 150）
    ///   -recorddelay S    先按真实时间等 S 秒再开录（默认 3.0，用来跳过 Unity 启动画面）
    ///   -recordfrom S     再等到「游戏内时间」走过 S 秒才开始录（默认 0，用来跳过演示的开场）
    ///   -recordskip N     开录后再丢掉 N 帧（窗口尺寸刚变完，头几帧可能还没刷成新尺寸）
    ///   -recordsize WxH   录制分辨率（默认 1920x1080）
    /// 录完会在目录里留一个 done.txt，方便外部脚本判断结束。
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
        string _dir;
        int _total;
        int _skip;
        int _fps;
        float _delay;
        float _from;

        /// <summary>命令行里带了 -record 就挂上录制器；没带返回 null，正常游玩不受影响。</summary>
        public static FrameRecorder Attach(GameObject host)
        {
            string[] argv = System.Environment.GetCommandLineArgs();
            int at = System.Array.IndexOf(argv, "-record");
            if (at < 0 || at + 1 >= argv.Length)
                return null;

            var rec = host.AddComponent<FrameRecorder>();
            rec._dir = argv[at + 1];
            rec._fps = ReadInt(argv, "-recordfps", 30);
            rec._total = ReadInt(argv, "-recordframes", 150);
            rec._skip = ReadInt(argv, "-recordskip", 4);
            rec._delay = ReadFloat(argv, "-recorddelay", 3f);
            rec._from = ReadFloat(argv, "-recordfrom", 0f);

            // 窗口按录制分辨率来。窗口比屏幕大也没关系：渲染缓冲是整窗大小，
            // 被屏幕裁掉的那部分只是看不见，帧缓冲里是全的。
            Vector2Int size = ReadSize(argv, "-recordsize", new Vector2Int(1920, 1080));
            Screen.SetResolution(size.x, size.y, false);
            return rec;
        }

        static int ReadInt(string[] argv, string key, int fallback)
        {
            int i = System.Array.IndexOf(argv, key);
            if (i >= 0 && i + 1 < argv.Length && int.TryParse(argv[i + 1], out int v) && v > 0)
                return v;
            return fallback;
        }

        static float ReadFloat(string[] argv, string key, float fallback)
        {
            int i = System.Array.IndexOf(argv, key);
            if (i >= 0 && i + 1 < argv.Length && float.TryParse(argv[i + 1], out float v) && v >= 0f)
                return v;
            return fallback;
        }

        static Vector2Int ReadSize(string[] argv, string key, Vector2Int fallback)
        {
            int i = System.Array.IndexOf(argv, key);
            if (i >= 0 && i + 1 < argv.Length)
            {
                string[] parts = argv[i + 1].Split('x', 'X');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                    return new Vector2Int(w, h);
            }
            return fallback;
        }

        void Start()
        {
            // 后台运行：录制时窗口不用保持在最前面，鼠标点到别处也不会停帧
            Application.runInBackground = true;
            // 关掉垂直同步，否则 captureFramerate 会被显示器刷新率拽着走
            QualitySettings.vSyncCount = 0;
            // 关键：让游戏时间按固定步长前进，存图再慢也不影响动画速度
            Time.captureFramerate = _fps;

            Directory.CreateDirectory(_dir);
            StartCoroutine(Capture());
        }

        IEnumerator Capture()
        {
            // Unity 启动画面（Made with Unity）是按真实时间走的，跟 captureFramerate 无关，
            // 所以这里得用 realtimeSinceStartup 等它走完，不能靠「丢几帧」。
            while (Time.realtimeSinceStartup < _delay)
                yield return null;

            // 演示场景的开场（摆场、点火、爆炸）是按游戏内时间推进的，
            // 所以要按 Time.timeSinceLevelLoad 等，而不是真实时间。
            while (Time.timeSinceLevelLoad < _from)
                yield return null;

            // 窗口改尺寸后头几帧可能还没刷成新尺寸，空跑掉
            for (int i = 0; i < _skip; i++)
                yield return new WaitForEndOfFrame();

            for (int i = 0; i < _total; i++)
            {
                yield return new WaitForEndOfFrame();
                string path = Path.Combine(_dir, $"f{i:0000}.png");
                ScreenCapture.CaptureScreenshot(path);
            }

            // 最后一张是异步落盘的，等一拍再收尾
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            File.WriteAllText(Path.Combine(_dir, "done.txt"),
                $"{_total} frames @ {_fps}fps, size {Screen.width}x{Screen.height}");
            Debug.Log($"[record] 完成 {_total} 帧 {Screen.width}x{Screen.height} -> {_dir}");
            Application.Quit();
        }
    }
}
