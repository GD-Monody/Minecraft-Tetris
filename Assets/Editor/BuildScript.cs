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

                if (importer.GetOverrideSampleSettings(BuildTargetGroup.WebGL).loadType
                    == AudioClipLoadType.CompressedInMemory)
                    continue;

                importer.SetOverrideSampleSettings(BuildTargetGroup.WebGL, new AudioImporterSampleSettings
                {
                    loadType = AudioClipLoadType.CompressedInMemory,
                    compressionFormat = AudioCompressionFormat.Vorbis,
                    quality = 0.7f,
                    preloadAudioData = false,
                });
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
