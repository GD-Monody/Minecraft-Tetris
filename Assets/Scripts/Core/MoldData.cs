using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>一个自定义方块模具：5x5 画布上的若干格子。</summary>
    [System.Serializable]
    public class MoldEntry
    {
        public string name;
        public string cells;   // "x,y;x,y;..." 坐标都是 0~4
        public int enabled;    // 1 = 出块池里启用
    }

    [System.Serializable]
    class MoldList
    {
        public List<MoldEntry> items = new List<MoldEntry>();
    }

    /// <summary>
    /// 创意工坊的模具数据：默认给几个模板，玩家能新建 / 改 / 删 / 启用。
    /// 存 PlayerPrefs（molds_v1），不写 Assets。
    /// </summary>
    public static class MoldData
    {
        public const int Size = 5;          // 画布 5x5
        const string Key = "molds_v1";

        static List<MoldEntry> _items;

        public static IReadOnlyList<MoldEntry> Items
        {
            get
            {
                if (_items == null)
                    Load();
                return _items;
            }
        }

        public static void Load()
        {
            _items = new List<MoldEntry>();
            string json = PlayerPrefs.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    MoldList list = JsonUtility.FromJson<MoldList>(json);
                    if (list != null && list.items != null)
                        _items = list.items;
                }
                catch
                {
                    _items = new List<MoldEntry>();
                }
            }

            if (_items.Count == 0)
            {
                _items = DefaultMolds();
                Save();
            }
        }

        public static void Save()
        {
            if (_items == null)
                return;
            var list = new MoldList { items = _items };
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(list));
            PlayerPrefs.Save();
        }

        static List<MoldEntry> DefaultMolds()
        {
            return new List<MoldEntry>
            {
                new MoldEntry { name = "五连条", cells = Encode(new[] { new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(4, 2) }), enabled = 1 },
                new MoldEntry { name = "大 T", cells = Encode(new[] { new Vector2Int(0, 2), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(1, 3), new Vector2Int(1, 1) }), enabled = 1 },
                new MoldEntry { name = "十字", cells = Encode(new[] { new Vector2Int(2, 1), new Vector2Int(1, 2), new Vector2Int(2, 2), new Vector2Int(3, 2), new Vector2Int(2, 3) }), enabled = 1 },
            };
        }

        public static Vector2Int[] Parse(string cells)
        {
            if (string.IsNullOrEmpty(cells))
                return new Vector2Int[0];

            var result = new List<Vector2Int>();
            foreach (string part in cells.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0)
                    continue;
                string[] xy = p.Split(',');
                if (xy.Length != 2)
                    continue;
                if (int.TryParse(xy[0], out int x) && int.TryParse(xy[1], out int y))
                    result.Add(new Vector2Int(x, y));
            }
            return result.ToArray();
        }

        public static string Encode(Vector2Int[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0)
                    sb.Append(';');
                sb.Append(cells[i].x).Append(',').Append(cells[i].y);
            }
            return sb.ToString();
        }

        /// <summary>至少一格才算出块池可用。</summary>
        public static List<MoldEntry> EnabledMolds()
        {
            var result = new List<MoldEntry>();
            foreach (MoldEntry m in Items)
            {
                if (m.enabled != 0 && Parse(m.cells).Length > 0)
                    result.Add(m);
            }
            return result;
        }

        public static void Add()
        {
            int n = Items.Count + 1;
            _items.Add(new MoldEntry { name = "新模具 " + n, cells = string.Empty, enabled = 1 });
            Save();
        }

        public static void Remove(int index)
        {
            if (index < 0 || index >= _items.Count)
                return;
            _items.RemoveAt(index);
            Save();
        }

        public static void SetCells(int index, Vector2Int[] cells)
        {
            if (index < 0 || index >= _items.Count)
                return;
            _items[index].cells = Encode(cells);
            Save();
        }

        public static void SetEnabled(int index, bool on)
        {
            if (index < 0 || index >= _items.Count)
                return;
            _items[index].enabled = on ? 1 : 0;
            Save();
        }

        public static void ResetDefaults()
        {
            _items = DefaultMolds();
            Save();
        }
    }
}
