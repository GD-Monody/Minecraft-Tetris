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
    }
}
