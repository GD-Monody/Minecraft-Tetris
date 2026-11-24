using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    /// <summary>批处理构建 Windows 版，产物在 Build/MinecraftTetris.exe。</summary>
    public static class BuildScript
    {
        [MenuItem("MinecraftTetris/构建 Windows 版")]
        public static void BuildWindows()
        {
            SetupPlayer();
            SetupTextures();

            string outDir = "Build";
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Game.unity" },
                locationPathName = Path.Combine(outDir, "MinecraftTetris.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("[MinecraftTetris] 构建结果: " + summary.result
                      + " 大小: " + summary.totalSize + " 字节"
                      + " 错误: " + summary.totalErrors);

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// 批处理构建 WebGL 版，产物在 BuildWebGL/。
        ///
        /// 压缩先关掉：本地用 http.server 起服务时它不会带 Content-Encoding 头，
        /// 压缩过的包会被浏览器当成坏文件（Unity 会报 Unable to parse build/*.gz）。
        /// 真上线时再改成 Gzip 或 Brotli，让服务器带上对应的响应头。
        /// </summary>
        [MenuItem("MinecraftTetris/构建 WebGL 版")]
        public static void BuildWebGL()
        {
            SetupPlayer();
            SetupTextures();

            string outDir = "BuildWebGL";
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.memorySize = 512;
            // 自带模板带一条 Unity 页脚，手机上白占高度；这个模板是满屏画布 + 触控禁缩放
            PlayerSettings.WebGL.template = "PROJECT:MinecraftMobile";
            SetupAudioForWebGL();
            // 手机浏览器切到后台再回来，Unity 的 WebGL 会自动暂停，这里不用额外处理
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Game.unity" },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("[MinecraftTetris] WebGL 构建结果: " + summary.result
                      + " 大小: " + summary.totalSize + " 字节"
                      + " 错误: " + summary.totalErrors);

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// 批处理构建 Android APK，产物在 BuildAndroid/MinecraftTetris.apk。
        ///
        /// 脚本后端用 Mono：IL2CPP 必须要 NDK，本机没装；Mono 一样能出 ARM64 包，
        /// 先用它把「能不能装、能不能玩」跑通，要上架再换 IL2CPP。
        /// SDK / JDK 用本机现成的那套（D:\DeepSeek-Harness\tools\android），
        /// 构建时通过命令行 -androidSdkPath / -androidJdkPath 指过来。
        /// </summary>
        [MenuItem("MinecraftTetris/构建 Android 版")]
        public static void BuildAndroid()
        {
            SetupPlayer();
            SetupTextures();
            SetupAndroidIcons();
            ApplyAndroidToolPaths();

            // 先切平台：贴图会被重新按 Android 的压缩格式导一遍，不切的话
            // 构建器会用桌面那套设置，偶尔会卡在导入阶段
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[MinecraftTetris] 切不到 Android 平台：缺 Android Build Support 模块？");
                EditorApplication.Exit(1);
                return;
            }

            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.monody.minecrafttetris");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            // IL2CPP 没 NDK 跑不了；Mono 出包快，调试装机足够
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.Mono2x);
            // 没配自己的 keystore 就用 Unity 的 debug key，装得上、但上不了架
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;

            // 三端布局都做了，横竖都放开；倒着拿手机那种不放开
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            string outDir = "BuildAndroid";
            if (!Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Game.unity" },
                locationPathName = Path.Combine(outDir, "MinecraftTetris.apk"),
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("[MinecraftTetris] Android 构建结果: " + summary.result
                      + " 大小: " + summary.totalSize + " 字节"
                      + " 错误: " + summary.totalErrors);

            EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// 把命令行传进来的 SDK / JDK 路径写进 EditorPrefs（Preferences → External Tools 里那三个框）。
        ///
        /// 直接命令行给 -androidSdkPath / -androidJdkPath 参数是不够的：批处理里 Unity 也照样
        /// 只认 EditorPrefs 里存的值，第一次跑直接报 "JDK not found"。
        /// 路径不写死在这里：每台机器的 SDK 放在哪儿都不一样，仓库里只留参数名。
        /// </summary>
        static void ApplyAndroidToolPaths()
        {
            string[] argv = System.Environment.GetCommandLineArgs();
            ApplyToolPath(argv, "-androidSdkPath", "sdkRootPath");
            ApplyToolPath(argv, "-androidJdkPath", "jdkRootPath");
            ApplyToolPath(argv, "-androidNdkPath", "ndkRootPath");
        }

        static void ApplyToolPath(string[] argv, string arg, string prop)
        {
            int at = System.Array.IndexOf(argv, arg);
            if (at < 0 || at + 1 >= argv.Length)
                return;

            string path = argv[at + 1].Replace('\\', '/');

            // 老键名也写一份：Unity 2022.3 里真正决定子进程 JAVA_HOME / adb 路径的
            // 是这几条 EditorPrefs，光设置 AndroidExternalToolsSettings 的属性不够 ——
            // 属性只影响界面显示，构建时子进程还是按 EditorPrefs 走（实测 JAVA_HOME 仍是旧值）。
            if (prop == "sdkRootPath")
            {
                EditorPrefs.SetString("AndroidSdkRoot", path);
                EditorPrefs.SetString("AndroidSDKRoot", path);
            }
            else if (prop == "jdkRootPath")
            {
                EditorPrefs.SetString("JdkPath", path);
                EditorPrefs.SetString("JDKPath", path);
            }
            else if (prop == "ndkRootPath")
            {
                EditorPrefs.SetString("AndroidNdkRoot", path);
                EditorPrefs.SetString("AndroidNDKRoot", path);
            }

            System.Type t = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
                if (t != null)
                    break;
            }
            if (t == null)
            {
                Debug.LogWarning("[MinecraftTetris] 没装上 Android 模块，SDK/JDK 路径写不进去");
                return;
            }

            var p = t.GetProperty(prop,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (p == null || !p.CanWrite)
            {
                Debug.LogWarning("[MinecraftTetris] " + prop + " 拿不到，路径没写进去");
                return;
            }

            // 这个 setter 顺带会去扫一遍插着的安卓设备，扫不到就抛（批处理里没设备很正常）。
            // 路径本身在抛之前已经写进去了，所以这里吃掉异常、继续构建。
            try
            {
                p.SetValue(null, path);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MinecraftTetris] 设置 " + prop + " 时抛了异常（路径已写入）: " + e.Message);
            }

            Debug.Log("[MinecraftTetris] " + prop + " = " + path);        }

        /// <summary>
        /// 把 Assets/Art/AppIcon 里那几张 PNG 按 Unity 要的尺寸塞进图标槽。
        /// 拿不到对应尺寸就给最接近的（Unity 会自己缩），总比留一个空槽变成默认图标好。
        /// </summary>
        static void SetupAndroidIcons()
        {
            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Android);
            var icons = new Texture2D[sizes.Length];
            string[] have = { "48", "72", "96", "144", "192", "512" };

            for (int i = 0; i < sizes.Length; i++)
            {
                string pick = have[have.Length - 1];
                foreach (string s in have)
                {
                    if (int.Parse(s) >= sizes[i])
                    {
                        pick = s;
                        break;
                    }
                }

                string path = "Assets/Art/AppIcon/app_icon_" + pick + ".png";
                icons[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (icons[i] == null)
                    Debug.LogWarning("[MinecraftTetris] 图标缺失: " + path);
            }

            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, icons);
            Debug.Log("[MinecraftTetris] Android 图标尺寸: " + string.Join(",", sizes));
        }

        /// <summary>
        /// 音乐在 WebGL 上不能用 Streaming（浏览器没法对 ogg 随机读取），要改成「压缩进内存」。
        ///
        /// 光在 AssetPostprocessor 里写平台覆盖是不够的：后处理只在资源**被导入**时跑，
        /// 改完后处理代码并不会让已经导入过的 ogg 重新导入，包里的 loadType 还是旧的。
        /// 所以这里显式查一遍覆盖设置，不对才 SaveAndReimport（十几首歌重导一次要等一会儿，
        /// 每次构建都白刷一遍没必要）。
        /// </summary>
        static void SetupAudioForWebGL()
        {
            string dir = "Assets/Resources/Music";
            if (!Directory.Exists(dir))
                return;

            int changed = 0;
            foreach (string file in Directory.GetFiles(dir, "*.ogg"))
            {
                string path = file.Replace('\\', '/');
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                if (importer == null)
                    continue;

                // 直接改默认设置（平台覆盖实测写不进 .meta），质量或载入方式不对就重导
                var cur = importer.defaultSampleSettings;
                if (cur.loadType == AudioClipLoadType.CompressedInMemory
                    && Mathf.Abs(cur.quality - AudioImportSettings.WebGLMusicQuality) < 0.01f
                    && cur.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate
                    && cur.sampleRateOverride == 22050)
                    continue;

                var settings = cur;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = AudioImportSettings.WebGLMusicQuality;
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = 22050;
                settings.preloadAudioData = false;
                importer.defaultSampleSettings = settings;
                importer.SaveAndReimport();
                changed++;
            }

            if (changed > 0)
                Debug.Log("[MinecraftTetris] WebGL 音频平台覆盖已更新 " + changed + " 首");
        }

        /// <summary>窗口模式 + 可缩放 + 标准窗口按钮。</summary>
        static void SetupPlayer()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1440;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.runInBackground = true;
            PlayerSettings.companyName = "MinecraftTetris";
            PlayerSettings.productName = "MinecraftTetris";
        }

        /// <summary>像素风必须用 Point 过滤、不压缩、无 mipmap，否则小尺寸贴图会被拉糊。</summary>
        static void SetupTextures()
        {
            // 图集尺寸不是 2 的幂（144×126），Unity 默认会把它缩成 128×128，
            // 而 UV 是按 144×126 算的，一缩放就整体错位、边缘串到相邻格子。
            // 标题图同理（140×16），缩放会直接把点阵搞歪。
            string[] paths = { "Assets/Resources/atlas.png", "Assets/Resources/title.png" };
            foreach (string path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("[MinecraftTetris] 没找到 " + path);
                    continue;
                }

                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.SaveAndReimport();
                Debug.Log("[MinecraftTetris] 纹理导入设置: Point / 不压缩 / 无 mipmap / 不缩放 " + path);
            }
        }
    }
}
