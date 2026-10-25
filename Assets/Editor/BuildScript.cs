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
