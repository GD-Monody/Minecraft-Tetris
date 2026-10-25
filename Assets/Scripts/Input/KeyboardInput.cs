using System;
using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    public enum GameAction
    {
        Left = 0,
        Right = 1,
        Forward = 2,
        Back = 3,
        SoftDrop = 4,
        HardDrop = 5,
        RotateCW = 6,
        RotateCCW = 7,
    }

    /// <summary>
    /// 默认键位（可被 PlayerPrefs 里的 keybind.N 覆盖）：
    /// A/← 左，D/→ 右，S/↓ 前（z 减小），W/↑ 后（z 增大），左Shift 软降，空格 硬降，Q 逆时针，E 顺时针。
    /// </summary>
    public class KeyboardInput : MonoBehaviour
    {
        public static readonly KeyCode[][] Defaults =
        {
            new[] { KeyCode.A, KeyCode.LeftArrow },
            new[] { KeyCode.D, KeyCode.RightArrow },
            new[] { KeyCode.S, KeyCode.DownArrow },
            new[] { KeyCode.W, KeyCode.UpArrow },
            new[] { KeyCode.LeftShift },
            new[] { KeyCode.Space },
            new[] { KeyCode.E },
            new[] { KeyCode.Q },
        };

        readonly KeyCode[][] _keys = new KeyCode[8][];

        void Awake()
        {
            Reload();
        }

        /// <summary>改完键位后调用，重新从 PlayerPrefs 读一遍。</summary>
        public void Reload()
        {
            for (int i = 0; i < _keys.Length; i++)
                _keys[i] = Load((GameAction)i);
        }

        static KeyCode[] Load(GameAction action)
        {
            KeyCode[] def = Defaults[(int)action];
            string raw = PlayerPrefs.GetString("keybind." + (int)action, string.Empty);
            if (string.IsNullOrEmpty(raw))
                return (KeyCode[])def.Clone();

            var list = new List<KeyCode>();
            foreach (string part in raw.Split(','))
            {
                if (int.TryParse(part, out int v) && Enum.IsDefined(typeof(KeyCode), v))
                    list.Add((KeyCode)v);
            }

            return list.Count > 0 ? list.ToArray() : (KeyCode[])def.Clone();
        }

        public static void Save(GameAction action, params KeyCode[] keys)
        {
            var parts = new List<string>();
            for (int i = 0; i < keys.Length; i++)
                parts.Add(((int)keys[i]).ToString());
            PlayerPrefs.SetString("keybind." + (int)action, string.Join(",", parts));
            PlayerPrefs.Save();
        }

        /// <summary>取某个动作的主键（设置界面显示用）。</summary>
        public static KeyCode GetPrimaryKey(GameAction action)
        {
            KeyCode[] keys = Load(action);
            return keys.Length > 0 ? keys[0] : KeyCode.None;
        }

        public InputState Read()
        {
            return new InputState
            {
                Left = AnyDown(GameAction.Left),
                Right = AnyDown(GameAction.Right),
                Up = AnyDown(GameAction.Forward),
                Down = AnyDown(GameAction.Back),
                SoftDrop = AnyDown(GameAction.SoftDrop),
                HardDrop = AnyDown(GameAction.HardDrop),
                RotateCW = AnyDown(GameAction.RotateCW),
                RotateCCW = AnyDown(GameAction.RotateCCW),
            };
        }

        bool AnyDown(GameAction action)
        {
            KeyCode[] keys = _keys[(int)action];
            for (int i = 0; i < keys.Length; i++)
            {
                if (Input.GetKeyDown(keys[i]))
                    return true;
            }
            return false;
        }
    }
}
