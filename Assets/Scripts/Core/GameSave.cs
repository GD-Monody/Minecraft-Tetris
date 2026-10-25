using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 局面存档：每落定一个方块自动存一次，开始界面「继续游戏」读回来。
    /// 存在 PlayerPrefs（Windows 注册表，WebGL 是 localStorage）。
    /// </summary>
    public static class GameSave
    {
        const string Key = "game_save_v1";

        public static bool HasSave
        {
            get { return PlayerPrefs.HasKey(Key) && !string.IsNullOrEmpty(PlayerPrefs.GetString(Key)); }
        }

        public static void Save(GameLoop loop)
        {
            if (loop == null || loop.GameOver)
                return;
            PlayerPrefs.SetString(Key, loop.SerializeState());
            PlayerPrefs.Save();
        }

        public static bool TryLoad(GameLoop loop)
        {
            if (loop == null || !HasSave)
                return false;
            return loop.RestoreState(PlayerPrefs.GetString(Key));
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
