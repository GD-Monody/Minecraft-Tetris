using UnityEditor;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    /// <summary>
    /// 强制 Assets/Resources/Music 下的 ogg 按「流式播放」导入。
    ///
    /// 默认导入是 DecompressOnLoad：一开场就把整首歌解码成 PCM 塞进内存，
    /// 一首 3 分钟的曲子大概 30 MB，十几首一起加载直接把内存吃光。
    /// Streaming 只把压缩数据放在内存里、边播边解码，代价是切换时的延迟（这里本来就留了静默间隔，无感）。
    ///
    /// quality 压到 0.7：这些曲子原版就是 Vorbis，再高只是白白变大。
    /// preloadAudioData = false 让资源在真正要播之前才加载。
    /// </summary>
    public class AudioImportSettings : AssetPostprocessor
    {
        /// <summary>网页版的音乐压缩质量（构建前会按这个值强制重导一遍）。</summary>
        public const float WebGLMusicQuality = 0.35f;

        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Music/"))
                return;

            var importer = assetImporter as AudioImporter;
            if (importer == null)
                return;

            // 直接改**默认**设置，不再只依赖平台覆盖：
            // 实测 SetOverrideSampleSettings(WebGL, …) 没有落到 .meta 里
            // （platformSettingOverrides 一直是空的），于是网页版一直在用默认的
            // Streaming —— 浏览器不支持流式播放，等于没声音；顺带质量也一直按 0.7 算。
            // 统一改成「压缩进内存 + 0.35 + 单声道」，两端一致，包体也小一截。
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.CompressedInMemory;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = WebGLMusicQuality;
            // 背景音乐降到 22.05 kHz：这叫「田园风」的曲子本来就没有高频细节，
            // 砍掉一半采样点听不出来，体积再省一半
            settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
            settings.sampleRateOverride = 22050;
            // 2022 里 preloadAudioData 挪进了 SampleSettings（AudioImporter 上那个已过时）
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;

            // 背景音乐单声道：立体声在这个游戏里听不出空间感，但体积直接砍一半
            importer.forceToMono = true;
            importer.loadInBackground = true;

            // WebGL 上 Streaming 是不支持的（浏览器没法对 ogg 做随机读取），
            // 照搬的话手机网页版会一首曲子都放不出来，只在控制台留一行警告。
            // 改成「压缩进内存」：压缩数据常驻内存、播放时由浏览器解码，
            // 内存占用和 Streaming 差不多，也是十几首歌里只留压缩包那份。
            var webgl = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.CompressedInMemory,
                compressionFormat = AudioCompressionFormat.Vorbis,
                // 27 MB 的 ogg 只当背景音乐用，0.7 在手机外放上听不出差别，但首屏要多下十几兆
                quality = WebGLMusicQuality,
                preloadAudioData = false,
            };
            importer.SetOverrideSampleSettings(BuildTargetGroup.WebGL, webgl);
        }
    }
}
