using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 水和岩浆的流动。
    ///
    /// 规则（2026-10-26 和用户确认过的版本）：
    /// - 水/岩浆方块落定后开始流动：先往下流到支撑面，再向四周蔓延；
    ///   扩散有上限（水 1.5 秒 / 60 格，岩浆 0.8 秒 / 30 格），不会铺满全场。
    /// - 流体只往空格里流，不会穿过方块。
    /// - 方块落下来时可以压在流体上（流体不算实体），落定后那一格流体消失；
    ///   如果被压的是源头或者把水路截断了，受影响的那部分流体逐格退掉。
    ///
    /// 实现方式：每次"应不应该有水"变化时，从所有源头重新 BFS 一遍应存在的水格，
    /// 然后每 tick 只填几格、退几格，所以看起来是一格一格漫过去 / 退回来。
    /// </summary>
    public static class Fluids
    {
        static int _waterId = -1;
        static int _lavaId = -1;

        const float WaterLife = 1.5f;
        const int WaterMax = 60;
        const float LavaLife = 0.8f;
        const int LavaMax = 30;

        const float FillPerSecond = 24f;   // 每秒最多铺几格（看起来是漫过去）
        const float DrainPerSecond = 12f;  // 每秒最多退几格

        struct Source
        {
            public int X, Y, Z;
            public int Id;
            public float Life;     // 还能流多久
            public float MaxLife;
            public int MaxCells;
        }

        static readonly List<Source> _sources = new List<Source>();
        static readonly HashSet<Vector3Int> _wanted = new HashSet<Vector3Int>();
        static readonly List<Vector3Int> _cellsToFill = new List<Vector3Int>();
        static readonly List<Vector3Int> _cellsToDrain = new List<Vector3Int>();
        static float _fillCredit, _drainCredit;

        public static int WaterId { get { return _waterId; } }
        public static int LavaId { get { return _lavaId; } }

        /// <summary>从方块表里认出水和岩浆的编号（名字从 jar 里来，永远是 water / lava）。</summary>
        public static void Init()
        {
            _waterId = FindByName("water");
            _lavaId = FindByName("lava");
            Debug.Log("[fluids] water=" + _waterId + " lava=" + _lavaId);
        }

        /// <summary>按英文名找方块编号（返回 1 起的 blockId，找不到返回 -1）。</summary>
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

        public static void Reset()
        {
            _sources.Clear();
            _wanted.Clear();
            _cellsToFill.Clear();
            _cellsToDrain.Clear();
            _fillCredit = 0f;
            _drainCredit = 0f;
        }

        /// <summary>流体方块落定：登记成新的源头，并立刻重算一次流动范围。</summary>
        public static void AddSource(Grid grid, int x, int y, int z, int blockId)
        {
            bool lava = blockId == _lavaId;
            _sources.Add(new Source
            {
                X = x, Y = y, Z = z,
                Id = blockId,
                Life = lava ? LavaLife : WaterLife,
                MaxLife = lava ? LavaLife : WaterLife,
                MaxCells = lava ? LavaMax : WaterMax,
            });
            Rebuild(grid);
        }

        /// <summary>固体方块压到某个格子上：这一格如果是源头就不再有水。</summary>
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
            // 不管压的是不是源头都重算：可能正好把一条水路截断了
            Rebuild(grid);
            if (removed)
                Debug.Log("[fluids] 源头被压住: " + x + "," + y + "," + z);
        }

        /// <summary>每帧推进：填几格、退几格。返回 true 表示画面需要刷新。</summary>
        public static bool Tick(Grid grid, float dt)
        {
            bool changed = false;

            // 源头还有寿命，过期就不再往外扩（已经铺出去的水留着）
            for (int i = 0; i < _sources.Count; i++)
            {
                Source s = _sources[i];
                if (s.Life > 0f)
                {
                    s.Life -= dt;
                    _sources[i] = s;
                }
            }

            // 该铺没铺的
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

            // 该退没退的
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

        static readonly Dictionary<Vector3Int, int> _wantedIds = new Dictionary<Vector3Int, int>();

        static int WantedId(Vector3Int cell)
        {
            int id;
            return _wantedIds.TryGetValue(cell, out id) ? id : _waterId;
        }

        /// <summary>从所有源头重算"应该有哪些流体格"。</summary>
        public static void Rebuild(Grid grid)
        {
            _wanted.Clear();
            _wantedIds.Clear();

            for (int i = 0; i < _sources.Count; i++)
            {
                Source s = _sources[i];
                if (s.Life <= 0f)
                    continue;
                Bfs(grid, s);
            }

            // 和当前格子对一遍：缺的排队填，多的排队退
            _cellsToFill.Clear();
            _cellsToDrain.Clear();

            foreach (Vector3Int cell in _wanted)
            {
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
                        if (!_wanted.Contains(cell) && !_cellsToDrain.Contains(cell))
                            _cellsToDrain.Add(cell);
                    }
        }

        /// <summary>从源头铺开：先往下，再往四周，最多 maxCells 格。</summary>
        static void Bfs(Grid grid, Source src)
        {
            var start = new Vector3Int(src.X, src.Y, src.Z);
            var queue = new Queue<Vector3Int>();
            var seen = new HashSet<Vector3Int>();
            queue.Enqueue(start);
            seen.Add(start);
            _wanted.Add(start);
            _wantedIds[start] = src.Id;

            int budget = src.MaxCells;
            // 让水先往下走：用两个队列，先把"下落"这条链走完再向侧面铺
            var down = new Queue<Vector3Int>();
            down.Enqueue(start);

            while (down.Count > 0)
            {
                Vector3Int p = down.Dequeue();
                var below = new Vector3Int(p.x, p.y - 1, p.z);
                if (below.y < 0 || !grid.InBounds(below.x, below.y, below.z))
                    continue;
                if (seen.Contains(below) || !grid.IsFree(below.x, below.y, below.z))
                    continue;
                if (--budget <= 0)
                    return;
                seen.Add(below);
                _wanted.Add(below);
                _wantedIds[below] = src.Id;
                down.Enqueue(below);
                queue.Enqueue(below);
            }

            // 再向四周扩散（广度优先，一层一层）
            var spread = new Queue<Vector3Int>();
            foreach (Vector3Int c in seen)
                spread.Enqueue(c);

            Vector3Int[] dirs =
            {
                new Vector3Int(1, 0, 0), new Vector3Int(-1, 0, 0),
                new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1),
            };

            while (spread.Count > 0)
            {
                if (budget <= 0)
                    return;
                Vector3Int p = spread.Dequeue();
                for (int i = 0; i < dirs.Length; i++)
                {
                    var n = p + dirs[i];
                    if (!grid.InBounds(n.x, n.y, n.z))
                        continue;
                    if (seen.Contains(n) || !grid.IsFree(n.x, n.y, n.z))
                        continue;
                    if (--budget <= 0)
                        return;
                    seen.Add(n);
                    _wanted.Add(n);
                    _wantedIds[n] = src.Id;
                    spread.Enqueue(n);

                    // 侧向铺出去以后，落下去的那一列也算水
                    var below = new Vector3Int(n.x, n.y - 1, n.z);
                    while (below.y >= 0 && grid.InBounds(below.x, below.y, below.z)
                           && !seen.Contains(below) && grid.IsFree(below.x, below.y, below.z))
                    {
                        if (--budget <= 0)
                            return;
                        seen.Add(below);
                        _wanted.Add(below);
                        _wantedIds[below] = src.Id;
                        spread.Enqueue(below);
                        below = new Vector3Int(below.x, below.y - 1, below.z);
                    }
                }
            }
        }
    }
}
