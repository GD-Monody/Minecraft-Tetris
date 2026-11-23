using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 浏览器里的文字输入。
    ///
    /// Unity WebGL 的 IMGUI TextField 收不到输入法上屏的字符（只能打英文），
    /// 创意工坊改名就卡在这儿。这里改用插件在画布上盖一个 HTML input 取字
    /// （见 Assets/Plugins/WebGL/WebGLIme.jslib），中文、标点、粘贴都能用。
    ///
    /// 编辑器 / Windows 版不走这条路：那边的 TextField 本来就正常。
    /// </summary>
    public static class WebGLTextInput
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void WBIME_Open(
            float gx, float gy, float gw, float gh, float sw, float sh, string initial);
        [DllImport("__Internal")] static extern void WBIME_Close();
        [DllImport("__Internal")] static extern int WBIME_Active();
        [DllImport("__Internal")] static extern void WBIME_Value(byte[] buf, int max);
        [DllImport("__Internal")] static extern int WBIME_ConsumeEnter();

        public static bool Supported { get { return true; } }

        public static void Open(Rect guiRect, string initial)
        {
            WBIME_Open(guiRect.x, guiRect.y, guiRect.width, guiRect.height,
                Screen.width, Screen.height, initial ?? "");
        }

        public static void Close()
        {
            WBIME_Close();
        }

        public static bool Active
        {
            get { return WBIME_Active() != 0; }
        }

        /// <summary>刚按过回车没有（读一次就清）。</summary>
        public static bool ConsumeEnter()
        {
            return WBIME_ConsumeEnter() != 0;
        }

        public static string Value()
        {
            var buf = new byte[256];
            WBIME_Value(buf, buf.Length);
            int len = 0;
            while (len < buf.Length && buf[len] != 0)
                len++;
            return Encoding.UTF8.GetString(buf, 0, len);
        }
#else
        public static bool Supported { get { return false; } }
        public static void Open(Rect guiRect, string initial) { }
        public static void Close() { }
        public static bool Active { get { return false; } }
        public static bool ConsumeEnter() { return false; }
        public static string Value() { return ""; }
#endif
    }
}
