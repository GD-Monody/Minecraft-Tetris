using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 水和岩浆的流动，按 Minecraft 现在的规则来（不是早期那种"无限蔓延"）。
    ///
    /// MC 的水流规则（这里实现的是核心那几条）：
    /// - 每个流体格有个"水位"：0 是水源，1~7 是一格一格流出去的水，数字越大离源越远。
    /// - 水平每流一格水位 +1，到 7 就不再往外流 —— 所以一片水最多往外铺 7 格，
    ///   不会像早期版本那样顺着地形一路铺到天边。
    /// - 向下流不涨水位：水从高处落下来，落地以后还能再往外铺 7 格。
    /// - 水不会往上流。
    /// - 水源被方块压住、或者水路被截断时，没源供水的那部分会逐格退掉（干涸）。
    ///
    /// 实现方式：每次"应不应该有水"变化时，从所有水源做一遍多源 BFS 算出每格的水位，
    /// 再和场上的实际格子对一遍：缺的排队填、多的排队退，每 tick 只填几格退几格，
    /// 看起来就是水一格一格漫过去、一格一格退回来。
    /// </summary>
    public static class Fluids
    {
        static int _waterId = -1;
        static int _lavaId = -1;

        /// <summary>水平最多流 7 格（MC 的 level 1~7）。</summary>
        public const int MaxSpread = 7;

        const float FillPerSecond = 24f;   // 每秒最多铺几格（看起来是漫过去）
        const float DrainPerSecond = 12f;  // 每秒最多退几格

        struct Source
        {
            public int X, Y, Z;
            public int Id;
        }

        static readonly List<Source> _sources = new List<Source>();

        // 按 MC 规则算出来的"这里应该是几级水"（0=源，1~7=流动水）
        static readonly Dictionary<Vector3Int, int> _depth = new Dictionary<Vector3Int, int>();
        static readonly Dictionary<Vector3Int, int> _ids = new Dictionary<Vector3Int, int>();

        static readonly List<Vector3Int> _cellsToFill = new List<Vector3Int>();
        static readonly List<Vector3Int> _cellsToDrain = new List<Vector3Int>();
        static float _fillCredit, _drainCredit;

        static readonly Queue<Vector3Int> _queue = new Queue<Vector3Int>();

        public static int WaterId { get { return _waterId; } }
        public static int LavaId { get { return _lavaId; } }

        /// <summary>从方块表里认出水和岩浆的编号（名字从 jar 里来，永远是 water / lava）。</summary>
        public static void Init()
        {
            _waterId = FindByName("water");
            _lavaId = FindByName("lava");
            Debug.Log("[fluids] water=" + _waterId + " lava=" + _lavaId);
        }

        static int FindByName(string name)
        {
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Name == name)
                    return i + 1;
            return -1;
        }

        public static bool IsFluid(int blockId)
        {
            return blockId > 0 && (blockId == _waterId || blockId == _lavaId);
        }

        /// <summary>这个格子的水位（0 是源，1~7 是流动水，-1 表示这格不该有水）。给渲染用。</summary>
        public static int DepthAt(int x, int y, int z)
        {
            int d;
            return _depth.TryGetValue(new Vector3Int(x, y, z), out d) ? d : -1;
        }

        public static void Reset()
        {
            _sources.Clear();
            _depth.Clear();
            _ids.Clear();
            _cellsToFill.Clear();
            _cellsToDrain.Clear();
            _fillCredit = 0f;
            _drainCredit = 0f;
        }

        /// <summary>流体方块落定：登记成新的水源，并立刻重算一次流动范围。</summary>
        public static void AddSource(Grid grid, int x, int y, int z, int blockId)
        {
            _sources.Add(new Source { X = x, Y = y, Z = z, Id = blockId });
            Rebuild(grid);
        }

        /// <summary>固体方块压到某个格子上：这一格如果是水源就不再有水，然后重算（可能截断水路）。</summary>
        public static void OnSolidPlaced(Grid grid, int x, int y, int z)
        {
            bool removed = false;
            for (int i = _sources.Count - 1; i >= 0; i--)
            {
                Source s = _sources[i];
                if (s.X == x && s.Y == y && s.Z == z)
                {
                    _sources.RemoveAt(i);
                    removed = true;
                }
            }
            Rebuild(grid);
            if (removed)
                Debug.Log("[fluids] 水源被压住: " + x + "," + y + "," + z);
        }

        /// <summary>每帧推进：填几格、退几格。返回 true 表示画面需要刷新。</summary>
        public static bool Tick(Grid grid, float dt)
        {
            bool changed = false;

            if (_cellsToFill.Count > 0)
            {
                _fillCredit += FillPerSecond * dt;
                while (_fillCredit >= 1f && _cellsToFill.Count > 0)
                {
                    _fillCredit -= 1f;
                    int idx = _cellsToFill.Count - 1;
                    Vector3Int cell = _cellsToFill[idx];
                    _cellsToFill.RemoveAt(idx);
                    if (grid.IsFree(cell.x, cell.y, cell.z))
                    {
                        grid.Set(cell.x, cell.y, cell.z, WantedId(cell));
                        changed = true;
                    }
                }
            }

            if (_cellsToDrain.Count > 0)
            {
                _drainCredit += DrainPerSecond * dt;
                while (_drainCredit >= 1f && _cellsToDrain.Count > 0)
                {
                    _drainCredit -= 1f;
                    int idx = _cellsToDrain.Count - 1;
                    Vector3Int cell = _cellsToDrain[idx];
                    _cellsToDrain.RemoveAt(idx);
                    int cur = grid.Get(cell.x, cell.y, cell.z);
                    if (IsFluid(cur))
                    {
                        grid.Set(cell.x, cell.y, cell.z, 0);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        static int WantedId(Vector3Int cell)
        {
            int id;
            return _ids.TryGetValue(cell, out id) ? id : _waterId;
        }

        /// <summary>从所有水源重算"按 MC 规则应该有哪些流体格、各是几级水"。</summary>
        public static void Rebuild(Grid grid)
        {
            _depth.Clear();
            _ids.Clear();

            for (int i = 0; i < _sources.Count; i++)
                SpreadFrom(grid, _sources[i]);

            // 和场上实际格子对一遍：缺的排队填，多的排队退
            _cellsToFill.Clear();
            _cellsToDrain.Clear();

            foreach (KeyValuePair<Vector3Int, int> kv in _depth)
            {
                Vector3Int cell = kv.Key;
                int cur = grid.Get(cell.x, cell.y, cell.z);
                if (cur == 0)
                    _cellsToFill.Add(cell);
                else if (IsFluid(cur) && cur != WantedId(cell))
                    _cellsToDrain.Add(cell);   // 水和岩浆撞上了：先把旧的退掉，下一轮再填
            }

            for (int x = 0; x < grid.SizeX; x++)
                for (int y = 0; y < grid.SizeY; y++)
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        int cur = grid.Get(x, y, z);
                        if (!IsFluid(cur))
                            continue;
                        var cell = new Vector3Int(x, y, z);
                        if (!_depth.ContainsKey(cell) && !_cellsToDrain.Contains(cell))
                            _cellsToDrain.Add(cell);
                    }
        }

        /// <summary>水能流过的地方：空格，或者本来就是一格流体（水可以接着往前铺）。</summary>
        static bool CanFlowInto(Grid grid, int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z))
                return false;
            int cur = grid.Get(x, y, z);
            return cur == 0 || IsFluid(cur);
        }

        /// <summary>
        /// 从水源按 MC 的水位规则铺开：向下不涨水位，水平每格 +1，涨到 7 就停。
        /// 多源一起算时取"更近的那个源"的水位（这就是 MC 里水流合并的逻辑）。
        /// </summary>
        static void SpreadFrom(Grid grid, Source src)
        {
            var start = new Vector3Int(src.X, src.Y, src.Z);
            int startDepth;
            if (_depth.TryGetValue(start, out startDepth) && startDepth <= 0)
                return;   // 这里已经有更近的水源了
            _depth[start] = 0;
            _ids[start] = src.Id;
            _queue.Clear();
            _queue.Enqueue(start);

            while (_queue.Count > 0)
            {
                Vector3Int p = _queue.Dequeue();
                int d = _depth[p];
                int id = _ids[p];

                // 向下：水位不涨，可以一直落
                var below = new Vector3Int(p.x, p.y - 1, p.z);
                bool wentDown = false;
                if (CanFlowInto(grid, below.x, below.y, below.z))
                {
                    int bd;
                    if (!_depth.TryGetValue(below, out bd) || bd > d)
                    {
                        _depth[below] = d;
                        _ids[below] = id;
                        _queue.Enqueue(below);
                        wentDown = true;
                    }
                }

                // MC 里水柱下落途中不往两边铺，只有落不下去了（落地或被挡住）才横向流。
                // 不这么写的话，水从高处掉下来的每一层都会横着铺一圈，看着像一层层的水膜。
                if (wentDown)
                    continue;

                // 水平：水位 +1，超过 7 就不再往外流
                if (d >= MaxSpread)
                    continue;

                int nd = d + 1;
                for (int i = 0; i < 4; i++)
                {
                    int nx = p.x + (i == 0 ? 1 : i == 1 ? -1 : 0);
                    int nz = p.z + (i == 2 ? 1 : i == 3 ? -1 : 0);
                    if (!CanFlowInto(grid, nx, p.y, nz))
                        continue;
                    var n = new Vector3Int(nx, p.y, nz);
                    int old;
                    if (!_depth.TryGetValue(n, out old) || old > nd)
                    {
                        _depth[n] = nd;
                        _ids[n] = id;
                        _queue.Enqueue(n);
                    }
                }
            }
        }
    }
}
