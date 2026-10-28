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
        void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Music/"))
                return;

            var importer = assetImporter as AudioImporter;
            if (importer == null)
                return;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            // 2022 里 preloadAudioData 挪进了 SampleSettings（AudioImporter 上那个已过时）
            settings.preloadAudioData = false;
            importer.defaultSampleSettings = settings;

            importer.forceToMono = false;
            importer.loadInBackground = true;
        }
    }
}
