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
            PlayerPrefs.SetString(Key, "v2|" + loop.SerializeState());
            PlayerPrefs.Save();
        }

        public static bool TryLoad(GameLoop loop)
        {
            if (loop == null || !HasSave)
                return false;
            string raw = PlayerPrefs.GetString(Key);
            // 方块表从 354 个 MC 方块换成 58 个自绘方块，id 全变了。
            // 旧存档（没有 v2 前缀）直接作废，恢复失败时顺手清掉，免得「继续游戏」按钮一直挂着。
            if (!raw.StartsWith("v2|"))
            {
                PlayerPrefs.DeleteKey(Key);
                PlayerPrefs.Save();
                return false;
            }
            return loop.RestoreState(raw.Substring(3));
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
