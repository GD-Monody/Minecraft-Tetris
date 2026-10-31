using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>一个自定义方块模具：建造盒里的若干格子。</summary>
    [System.Serializable]
    public class MoldEntry
    {
        public string name;
        public string cells;   // "x,y,z;x,y,z;..." 坐标都是 0 ~ Size-1；旧档只有 "x,y"，读的时候自动补 z=0
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
    ///
    /// 格子是三维的：x 左右、y 上下、z 往场地深处。默认七种方块和旧存档都是
    /// 「z 恒为 0」这一种特殊情况，所以读的时候两种写法都认，不用做数据迁移。
    /// </summary>
    public static class MoldData
    {
        /// <summary>建造盒每条边的格数。</summary>
        public const int Size = 5;

        /// <summary>
        /// 一个模具最多几格。往上放任会有两个后果：一是旋转后容易卡在场地里出不来，
        /// 二是比标准方块的四格大太多，对局就失去难度 —— 所以在这里卡住。
        /// </summary>
        public const int MaxCells = 8;

        const string Key = "molds_v1";

        static List<MoldEntry> _items;

        static readonly Vector3Int[] Neighbours =
        {
            new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 1, 0), new Vector3Int(0, -1, 0),
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
        };

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
                new MoldEntry { name = "五连条", cells = Encode(new[] { new Vector3Int(0, 2, 0), new Vector3Int(1, 2, 0), new Vector3Int(2, 2, 0), new Vector3Int(3, 2, 0), new Vector3Int(4, 2, 0) }), enabled = 1 },
                new MoldEntry { name = "T", cells = Encode(new[] { new Vector3Int(1, 3, 0), new Vector3Int(2, 3, 0), new Vector3Int(3, 3, 0), new Vector3Int(2, 2, 0), new Vector3Int(2, 1, 0) }), enabled = 1 },
                new MoldEntry { name = "十字", cells = Encode(new[] { new Vector3Int(2, 1, 0), new Vector3Int(1, 2, 0), new Vector3Int(2, 2, 0), new Vector3Int(3, 2, 0), new Vector3Int(2, 3, 0) }), enabled = 1 },
                // 带一层深度的示例：同一个形状往里再长一格，转起来才有「厚」的感觉
                new MoldEntry { name = "L 台阶", cells = Encode(new[] { new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector3Int(3, 1, 0), new Vector3Int(3, 2, 0), new Vector3Int(3, 2, 1) }), enabled = 1 },
            };
        }

        /// <summary>
        /// 解析坐标。"x,y" 和 "x,y,z" 都认：旧存档只有两个分量，缺的那个 z 一律补 0
        /// —— 旧模具本来就没往深处长，补 0 正好就是它原来的样子。
        /// </summary>
        public static Vector3Int[] Parse(string cells)
        {
            if (string.IsNullOrEmpty(cells))
                return new Vector3Int[0];

            var result = new List<Vector3Int>();
            var seen = new HashSet<int>();
            foreach (string part in cells.Split(';'))
            {
                string p = part.Trim();
                if (p.Length == 0)
                    continue;
                string[] xyz = p.Split(',');
                if (xyz.Length != 2 && xyz.Length != 3)
                    continue;
                if (!int.TryParse(xyz[0], out int x) || !int.TryParse(xyz[1], out int y))
                    continue;
                int z = 0;
                if (xyz.Length == 3 && !int.TryParse(xyz[2], out z))
                    continue;

                var c = new Vector3Int(x, y, z);
                // 重复坐标只留一个：编辑器里同一个格子被点两次不该变成两格
                if (seen.Add(Key3(c)))
                    result.Add(c);
            }
            return result.ToArray();
        }

        public static string Encode(IReadOnlyList<Vector3Int> cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                    sb.Append(';');
                sb.Append(cells[i].x).Append(',').Append(cells[i].y).Append(',').Append(cells[i].z);
            }
            return sb.ToString();
        }

        /// <summary>格子在建造盒里有没有出界。</summary>
        public static bool InBox(Vector3Int c)
        {
            return c.x >= 0 && c.x < Size && c.y >= 0 && c.y < Size && c.z >= 0 && c.z < Size;
        }

        /// <summary>把格子压成一个整数当 HashSet 的键。调用前先确认它在盒子里。</summary>
        static int Key3(Vector3Int c)
        {
            return (c.x * Size + c.y) * Size + c.z;
        }

        /// <summary>六连通检查：能不能从某一格出发走到每一格。</summary>
        public static bool IsConnected(IReadOnlyList<Vector3Int> cells)
        {
            if (cells.Count == 0)
                return false;

            var set = new HashSet<int>();
            foreach (Vector3Int c in cells)
                set.Add(Key3(c));

            var seen = new HashSet<int>();
            var stack = new Stack<Vector3Int>();
            seen.Add(Key3(cells[0]));
            stack.Push(cells[0]);

            while (stack.Count > 0)
            {
                Vector3Int a = stack.Pop();
                for (int n = 0; n < Neighbours.Length; n++)
                {
                    Vector3Int b = a + Neighbours[n];
                    // 两个条件的顺序是命根子：先看这一格在不在模具里，再决定要不要
                    // 登记到 seen。反过来写的话，模具外面的邻居会先被 Add 成功塞进
                    // seen 再被第二个条件跳掉 —— seen 里就多出一堆不属于模具的格子，
                    // seen.Count == cells.Count 永远不成立，什么形状都判成不连通。
                    if (!set.Contains(Key3(b)) || !seen.Add(Key3(b)))
                        continue;
                    stack.Push(b);
                }
            }

            return seen.Count == cells.Count;
        }

        /// <summary>模具在 z 方向占了几层，决定它能不能在当前场地深度里出块。</summary>
        public static int SpanZ(IReadOnlyList<Vector3Int> cells)
        {
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = 0; i < cells.Count; i++)
            {
                min = Mathf.Min(min, cells[i].z);
                max = Mathf.Max(max, cells[i].z);
            }
            if (min > max)
                return 0;
            return max - min + 1;
        }

        /// <summary>校验一整块模具。返回 null 表示合格，否则返回给人看的原因。</summary>
        public static string Validate(IReadOnlyList<Vector3Int> cells)
        {
            if (cells == null || cells.Count == 0)
                return "至少要放一格";
            if (cells.Count > MaxCells)
                return "最多只能放 " + MaxCells + " 格";

            for (int i = 0; i < cells.Count; i++)
            {
                if (!InBox(cells[i]))
                    return "有格子在建造盒外面";
            }

            // 不连通的话一旋转就会「掉块」：两截之间只有空气，转完它们对不上。
            if (!IsConnected(cells))
                return "方块必须连成一块（相邻才算）";

            return null;
        }

        /// <summary>出块池里能用的模具：勾了启用、形状合格、且塞得进当前场地深度。</summary>
        public static List<MoldEntry> EnabledMolds()
        {
            var result = new List<MoldEntry>();
            foreach (MoldEntry m in Items)
            {
                if (m.enabled == 0)
                    continue;
                Vector3Int[] cells = Parse(m.cells);
                if (Validate(cells) != null)
                    continue;
                // 场地只有一层深时，往里长出来的模具会凸到场地外面，直接不出块
                if (SpanZ(cells) > GameSettings.SizeZ)
                    continue;
                result.Add(m);
            }
            return result;
        }

        /// <summary>列表里的状态提示：合格就说没问题，不合格就说为什么。</summary>
        public static string Status(int index)
        {
            if (index < 0 || index >= Items.Count)
                return string.Empty;

            MoldEntry m = Items[index];
            if (m.enabled == 0)
                return "未启用";
            Vector3Int[] cells = Parse(m.cells);
            string bad = Validate(cells);
            if (bad != null)
                return bad;
            if (SpanZ(cells) > GameSettings.SizeZ)
                return "需要场地深度 ≥ " + SpanZ(cells);
            return "可用";
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

        public static void SetCells(int index, IReadOnlyList<Vector3Int> cells)
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

        public static void Rename(int index, string newName)
        {
            if (index < 0 || index >= _items.Count)
                return;
            newName = (newName ?? string.Empty).Trim();
            if (newName.Length == 0)
                newName = "未命名模具";
            if (newName.Length > 16)
                newName = newName.Substring(0, 16);
            _items[index].name = newName;
            Save();
        }

        public static void ResetDefaults()
        {
            _items = DefaultMolds();
            Save();
        }
    }
}
