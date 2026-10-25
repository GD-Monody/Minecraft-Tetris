using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 全局设置：场地尺寸（存 PlayerPrefs）。z 轴先固定 1，
    /// 等 z 大于 1 的那套做完再放开。
    /// </summary>
    public static class GameSettings
    {
        const string SizeXKey = "size.x";
        const string SizeYKey = "size.y";

        public const int MinSizeX = 5;
        public const int MaxSizeX = 20;
        public const int MinSizeY = 10;
        public const int MaxSizeY = 40;

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
            get { return 1; }
        }

        public static void SetSize(int x, int y)
        {
            PlayerPrefs.SetInt(SizeXKey, Mathf.Clamp(x, MinSizeX, MaxSizeX));
            PlayerPrefs.SetInt(SizeYKey, Mathf.Clamp(y, MinSizeY, MaxSizeY));
            PlayerPrefs.Save();
            // 尺寸变了，旧存档的格子数对不上，直接清掉
            GameSave.Clear();
        }
    }
}
