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
    }
}
