using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 全局设置：场地尺寸与消除模式（都存 PlayerPrefs）。
    /// </summary>
    public static class GameSettings
    {
        const string SizeXKey = "size.x";
        const string SizeYKey = "size.y";
        const string SizeZKey = "size.z";
        const string ElimModeKey = "elim.mode";
        const string FallSpeedKey = "fall.speed";
        const string MusicVolumeKey = "music.volume";
        const string BedrockInPoolKey = "pool.bedrock";

        public const int MinSizeX = 5;
        public const int MaxSizeX = 20;
        public const int MinSizeY = 10;
        public const int MaxSizeY = 40;
        public const int MinSizeZ = 1;
        public const int MaxSizeZ = 10;

        /// <summary>行消：每个 (y, z) 的 x 方向一行填满就消。</summary>
        public const int ElimRows = 0;
        /// <summary>层消：整个 x × z 平面填满才消。</summary>
        public const int ElimLayer = 1;

        /// <summary>
        /// 下限取 0.01 而不是 0：0 意味着永远掉不下来，没意义；(0, 2] 里最小的两位小数就是 0.01。
        /// </summary>
        public const float MinFallSpeed = 0.01f;
        /// <summary>上限 2 倍速（0.4 秒一格）。</summary>
        public const float MaxFallSpeed = 2f;
        public const float DefaultFallSpeed = 1f;

        public static int SizeX
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeXKey, 10), MinSizeX, MaxSizeX); }
        }

        public static int SizeY
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeYKey, 20), MinSizeY, MaxSizeY); }
        }

        public static int SizeZ
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(SizeZKey, 1), MinSizeZ, MaxSizeZ); }
        }

        public static int ElimMode
        {
            get { return Mathf.Clamp(PlayerPrefs.GetInt(ElimModeKey, ElimRows), ElimRows, ElimLayer); }
        }

        /// <summary>
        /// 下落速度倍率，范围 (0, 2]，两位小数。1 = 标准（0.8 秒一格），越大掉得越快。
        /// 3D 场地要前后看落点，1 倍速经常来不及，所以做成可调。
        /// </summary>
        public static float FallSpeed
        {
            get { return ClampFallSpeed(PlayerPrefs.GetFloat(FallSpeedKey, DefaultFallSpeed)); }
        }

        /// <summary>夹到 (0, 2] 上并四舍五入到两位小数。</summary>
        public static float ClampFallSpeed(float v)
        {
            if (float.IsNaN(v))
                return DefaultFallSpeed;
            v = Mathf.Clamp(v, MinFallSpeed, MaxFallSpeed);
            return Mathf.Round(v * 100f) / 100f;
        }

        /// <summary>
        /// 改下落速度。不清存档——它不影响已有局面的合法性，中途改了立刻生效就行。
        /// </summary>
        public static void SetFallSpeed(float v)
        {
            PlayerPrefs.SetFloat(FallSpeedKey, ClampFallSpeed(v));
            PlayerPrefs.Save();
        }

        /// <summary>背景音乐音量上限（1 = 满）。默认 0.45：音乐是垫底的，不能盖过放置音。</summary>
        public const float DefaultMusicVolume = 0.45f;

        /// <summary>背景音乐音量 0 ~ 1，0 = 关。两位小数。</summary>
        public static float MusicVolume
        {
            get { return ClampMusicVolume(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume)); }
        }

        public static float ClampMusicVolume(float v)
        {
            if (float.IsNaN(v))
                return DefaultMusicVolume;
            v = Mathf.Clamp01(v);
            return Mathf.Round(v * 100f) / 100f;
        }

        /// <summary>改音乐音量。和下落速度一样不清存档——纯听感，和局面无关。</summary>
        public static void SetMusicVolume(float v)
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, ClampMusicVolume(v));
            PlayerPrefs.Save();
        }

        public static void SetSize(int x, int y, int z)
        {
            PlayerPrefs.SetInt(SizeXKey, Mathf.Clamp(x, MinSizeX, MaxSizeX));
            PlayerPrefs.SetInt(SizeYKey, Mathf.Clamp(y, MinSizeY, MaxSizeY));
            PlayerPrefs.SetInt(SizeZKey, Mathf.Clamp(z, MinSizeZ, MaxSizeZ));
            PlayerPrefs.Save();
            // 尺寸变了，旧存档的格子数对不上，直接清掉
            GameSave.Clear();
        }

        public static void SetElimMode(int mode)
        {
            PlayerPrefs.SetInt(ElimModeKey, Mathf.Clamp(mode, ElimRows, ElimLayer));
            PlayerPrefs.Save();
            // 消除规则变了，旧存档里的局面按新规则继续容易出错，清掉
            GameSave.Clear();
        }

        /// <summary>
        /// 基岩要不要进方块池。默认关：基岩消除时不下落，掉下一块就是一根永久柱子，
        /// 属于「纯增难度」的方块，不该默认塞给玩家。打开后会从特殊池里出，
        /// 大约 1% 的方块是基岩。
        /// </summary>
        public static bool BedrockInPool
        {
            get { return PlayerPrefs.GetInt(BedrockInPoolKey, 0) != 0; }
        }

        /// <summary>
        /// 改基岩开关。不改存档——开关只影响以后抽到什么，不影响已经摆好的局面。
        /// 池子是静态缓存的（BlockPools），所以这里要重建一次。
        /// </summary>
        public static void SetBedrockInPool(bool on)
        {
            PlayerPrefs.SetInt(BedrockInPoolKey, on ? 1 : 0);
            PlayerPrefs.Save();
            BlockPools.Build();
        }
    }
}
