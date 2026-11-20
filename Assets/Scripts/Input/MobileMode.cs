using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 「现在是不是移动端」的唯一判定口。
    ///
    /// 为什么要判好几路：同一份 WebGL 包，电脑浏览器和手机浏览器跑的是同一个二进制，
    /// <c>Application.isMobilePlatform</c> 在手机浏览器上仍然是 false。只认它的话，
    /// 手机上打开网页版会拿到一套键盘操作 —— 一块砖都动不了，还看不出是哪儿错了。
    ///
    /// 所以按可信度从高到低试：
    /// 1. 网址带 mobile=0：明确要桌面版式，直接否掉，别的都不用看；
    /// 2. 真机（Android / iOS）：isMobilePlatform 直接为真；
    /// 3. 命令行 -mobile：桌面版也能拉出触屏界面，方便在电脑上先用鼠标拖一拖；
    /// 4. 网址带 mobile=1 **且这台设备真有触屏**：同一个网址手机电脑都会打开，
    ///    只认参数的话，电脑上用手机那条链接会跑出一套点不动的触屏界面；
    /// 5. 设备类型是 Handheld：兜底，部分手机浏览器认这个。
    ///
    /// 判定只做一次（Detect），结果缓存下来。中途不再变 —— 面板尺寸、控制条高度、
    /// 相机取景都按它算，跑到一半翻脸会让界面跳一下。
    /// </summary>
    public static class MobileMode
    {
        static bool _decided;
        static bool _enabled;

        /// <summary>是不是移动端（含网页版手机浏览器、以及 -mobile 强制开启）。</summary>
        public static bool Enabled
        {
            get
            {
                if (!_decided)
                    Detect();
                return _enabled;
            }
        }

        /// <summary>竖屏。移动端默认竖屏：场地 10×20 本来就是竖长的。</summary>
        public static bool Portrait
        {
            get { return Screen.height >= Screen.width; }
        }

        /// <summary>
        /// 在电脑上被 -mobile 强行拉进移动模式的。此时没有真触屏，
        /// 操作层会把鼠标当成一根手指，方便用鼠标点虚拟键调布局。
        /// </summary>
        public static bool Forced
        {
            get { return Enabled && !Application.isMobilePlatform; }
        }

        public static void Detect()
        {
            _decided = true;

            // mobile=0 是明确的「我就要桌面版」，优先级最高：手机上想用键盘版式、
            // 或者横屏平板想当电脑玩，都靠它。
            if (UrlSays("mobile=0"))
            {
                _enabled = false;
                return;
            }

            _enabled = Application.isMobilePlatform
                       || SystemInfo.deviceType == DeviceType.Handheld
                       || HasArg("-mobile")
                       || UrlSaysMobile();

            if (_enabled)
                Debug.Log("[mobile] 移动端模式：屏幕 " + Screen.width + "x" + Screen.height
                    + "，dpi " + Screen.dpi.ToString("0") + "，触屏 " + Input.touchSupported);
        }

        static bool HasArg(string name)
        {
            string[] argv = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < argv.Length; i++)
            {
                if (argv[i] == name)
                    return true;
            }
            return false;
        }

        static bool UrlSays(string key)
        {
            string url = Application.absoluteURL;
            return !string.IsNullOrEmpty(url) && url.Contains(key);
        }

        static bool UrlSaysMobile()
        {
            if (!UrlSays("mobile=1"))
                return false;

            // 光有参数不算数。同一个网址在手机和电脑上是一模一样的，从手机那边
            // 复制过来的链接贴到电脑浏览器里打开，参数照样在 —— 只认参数的话
            // 电脑上会跑出一套触屏界面（左侧拖动区 + 8 个虚拟键），占满屏还点不动。
            // 所以再看一眼这台设备到底有没有触屏；没有就还是桌面版式。
            if (Input.touchSupported || SystemInfo.deviceType == DeviceType.Handheld)
                return true;

            // 少数手机内嵌浏览器（某些 App 的 WebView）不报触屏，兜底再看视口：
            // 竖着的窄屏基本就是手机，别让它落到键盘版式里（那种情况一块砖都动不了）
            return Screen.height > Screen.width && Screen.width < 1200;
        }
    }
}
