using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// TNT：被红石块挨着就点着，引信烧完把周围一格范围炸掉，炸到的 TNT 会连锁。
    ///
    /// 跟 MC 的差别只有一处：原版点着之后方块变成实体（会掉、会被水推），
    /// 这里让方块留在网格里原地闪，引信烧完再炸。留在网格里意味着它占位、能挡方块，
    /// 好处是坐标不会因为重力 / 消行而错位——代价是被消行带走时这颗就哑了（Tick 里会发现并丢掉）。
    ///
    /// 只在三种时机点火：
    /// - 有方块落定之后扫一遍全场（见 PrimeContact），找「TNT 六向挨着红石块或岩浆」的格子；
    ///   扫全场而不是只看刚落下的那几格，是因为接触可能由「新放的红石」或「新放的 TNT」任一方造成，
    ///   而且重力塌落之后位置也变了，全场扫描一次（最多 2000 格）比追踪每个来源省事得多。
    /// - 岩浆淌动之后（GameLoop 在 Fluids.Tick 返回变化时再调一次 PrimeContact），
    ///   因为岩浆是流体、不占网格，只有在它漫到 TNT 旁边那一刻才看得见接触。
    /// - 爆炸时炸到别的 TNT，给一个很短的引信，形成连锁。
    /// </summary>
    public static class Tnt
    {
        /// <summary>引信时长。原版 4 秒，这里缩到 2.4 秒——一局俄罗斯方块里 4 秒已经太长了。</summary>
        public const float FuseSeconds = 2.4f;

        /// <summary>连锁时给下颗 TNT 的引信范围（秒）。</summary>
        public const float ChainFuseMin = 0.18f;
        public const float ChainFuseMax = 0.55f;

        /// <summary>
        /// 闪白的半周期：0.25 秒白、0.25 秒原色。
        /// 原版 `TntRenderer` 是按引信 tick 数算 `fuse / 5 % 2`，5 tick @ 20 TPS 正好 0.25 秒。
        /// </summary>
        public const float FlashHalfPeriod = 0.25f;

        /// <summary>引信烧了 elapsed 秒时，这一帧该不该画成白的（每 0.25 秒翻转一次，点火瞬间是白）。</summary>
        public static bool FlashOn(float elapsed)
        {
            int step = Mathf.FloorToInt(elapsed / FlashHalfPeriod);
            return (step & 1) == 0;
        }

        /// <summary>
        /// 爆炸半径（格）。按球形判距离：
        /// z = 1 的场地上只有 dz = 0 那一层，实际是一个半径 2 的圆，约 13 格；
        /// 场地深到 z ≥ 2 才会炸出完整的一团。
        /// </summary>
        public const float BlastRadius = 2.0f;

        /// <summary>炸掉一格得多少分。清一行是 100 分，这里一格 4 分，炸一圈差不多半行的收益。</summary>
        public const int ScorePerBlock = 4;

        public struct Primed
        {
            public int X, Y, Z;
            public float Fuse;
            /// <summary>点火时的引信长度（闪白频率要用它算进度）。</summary>
            public float Total;
        }

        public struct Blast
        {
            public int X, Y, Z;
            /// <summary>被炸掉的格子（不含连锁的 TNT）。</summary>
            public List<Vector3Int> Cells;
            public List<int> Blocks;
        }

        static readonly List<Primed> _primed = new List<Primed>();
        static readonly List<Blast> _blasts = new List<Blast>();
        static readonly List<Vector3Int> _newlyPrimed = new List<Vector3Int>();

        /// <summary>正在烧引信的 TNT（视图层每帧读它画闪白）。</summary>
        public static List<Primed> PrimedCells { get { return _primed; } }

        /// <summary>本帧炸掉的每一处。</summary>
        public static List<Blast> LastBlasts { get { return _blasts; } }

        /// <summary>本帧新点着的格子（播引信音、开始闪白）。</summary>
        public static List<Vector3Int> LastPrimed { get { return _newlyPrimed; } }

        public static int TntId { get { Init(); return _tntId; } }
        public static int RedstoneId { get { Init(); return _redstoneId; } }

        static int _tntId = -1;
        static int _redstoneId = -1;
        static bool _ready;

        public static void Init()
        {
            if (_ready)
                return;
            _ready = true;

            BlockDef[] blocks = BlockCatalog.Blocks;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Name == "tnt")
                    _tntId = i + 1;
                else if (blocks[i].Name == "redstone_block")
                    _redstoneId = i + 1;
            }

            if (_tntId <= 0)
                Debug.LogWarning("[tnt] 方块表里没有 tnt");
            if (_redstoneId <= 0)
                Debug.LogWarning("[tnt] 方块表里没有 redstone_block");
        }

        public static void Reset()
        {
            _primed.Clear();
            _blasts.Clear();
            _newlyPrimed.Clear();
        }

        public static bool IsTnt(int blockId)
        {
            Init();
            return blockId > 0 && blockId == _tntId;
        }

        public static bool IsRedstone(int blockId)
        {
            Init();
            return blockId > 0 && blockId == _redstoneId;
        }

        /// <summary>这颗 TNT 的引信进度 0~1（1 = 已经炸了）。没有点火返回 -1。</summary>
        public static float FuseProgress(int x, int y, int z)
        {
            for (int i = 0; i < _primed.Count; i++)
            {
                if (_primed[i].X == x && _primed[i].Y == y && _primed[i].Z == z)
                {
                    Primed p = _primed[i];
                    if (p.Total <= 0.001f)
                        return 1f;
                    return Mathf.Clamp01(1f - p.Fuse / p.Total);
                }
            }
            return -1f;
        }

        static bool IsPrimed(int x, int y, int z)
        {
            for (int i = 0; i < _primed.Count; i++)
            {
                if (_primed[i].X == x && _primed[i].Y == y && _primed[i].Z == z)
                    return true;
            }
            return false;
        }

        /// <summary>点着一颗（要求那一格确实是 TNT）。返回是否点着了。</summary>
        public static bool Prime(Grid grid, int x, int y, int z, float fuse)
        {
            Init();
            // 设置里关掉「TNT 爆炸」：点着了也不炸，连引信都不给
            if (grid == null || !GameSettings.TntExplodes || !grid.InBounds(x, y, z))
                return false;
            if (grid.Get(x, y, z) != _tntId)
                return false;
            if (IsPrimed(x, y, z))
                return false;

            _primed.Add(new Primed { X = x, Y = y, Z = z, Fuse = fuse, Total = fuse });
            _newlyPrimed.Add(new Vector3Int(x, y, z));
            return true;
        }

        /// <summary>
        /// 全场扫一遍，把所有「TNT 六向挨着红石块或岩浆（含同格）」的 TNT 点着。
        /// 方块落定后调用一次；岩浆淌动后也调用一次（见 GameLoop.Tick）。返回这一轮点着了几个。
        /// </summary>
        public static int PrimeContact(Grid grid)
        {
            Init();
            // 爆炸关了自然点不着；「接触点火」关了则红石块和岩浆都失去作用
            if (grid == null || _tntId <= 0 || !GameSettings.TntExplodes || !GameSettings.ContactIgnites)
                return 0;

            int fired = 0;
            for (int x = 0; x < grid.SizeX; x++)
            {
                for (int y = 0; y < grid.SizeY; y++)
                {
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        if (grid.Get(x, y, z) != _tntId)
                            continue;
                        if (IsPrimed(x, y, z))
                            continue;
                        if (!TouchesIgniter(grid, x, y, z))
                            continue;
                        if (Prime(grid, x, y, z, FuseSeconds))
                            fired++;
                    }
                }
            }
            return fired;
        }

        static bool TouchesIgniter(Grid grid, int x, int y, int z)
        {
            return TouchesRedstone(grid, x, y, z) || TouchesLava(x, y, z);
        }

        /// <summary>岩浆不写网格（走 Fluids 的份数模型），所以这里查 Fluids 而不是查 Grid。</summary>
        static bool TouchesLava(int x, int y, int z)
        {
            return LavaAt(x, y, z)
                || LavaAt(x - 1, y, z) || LavaAt(x + 1, y, z)
                || LavaAt(x, y - 1, z) || LavaAt(x, y + 1, z)
                || LavaAt(x, y, z - 1) || LavaAt(x, y, z + 1);
        }

        static bool LavaAt(int x, int y, int z)
        {
            return Fluids.AmountAt(x, y, z) > 0 && Fluids.IdAt(x, y, z) == Fluids.LavaId;
        }

        static bool TouchesRedstone(Grid grid, int x, int y, int z)
        {
            // 同格也算（红石和 TNT 不会真的重叠，这么写是为了「贴着」的判定宽一点）
            return RedstoneAt(grid, x, y, z)
                || RedstoneAt(grid, x - 1, y, z) || RedstoneAt(grid, x + 1, y, z)
                || RedstoneAt(grid, x, y - 1, z) || RedstoneAt(grid, x, y + 1, z)
                || RedstoneAt(grid, x, y, z - 1) || RedstoneAt(grid, x, y, z + 1);
        }

        /// <summary>越界一律当成「不是红石」——Grid.Get 不判边界，直接下标会抛越界。</summary>
        static bool RedstoneAt(Grid grid, int x, int y, int z)
        {
            if (!grid.InBounds(x, y, z))
                return false;
            return grid.Get(x, y, z) == _redstoneId;
        }

        /// <summary>
        /// 推进引信。返回本帧是否有爆炸（结果在 LastBlasts / LastPrimed 里）。
        /// </summary>
        public static bool Tick(Grid grid, float dt)
        {
            _blasts.Clear();
            _newlyPrimed.Clear();

            // 中途关掉「TNT 爆炸」：已经点着的引信直接掐灭，不会留到开关重新打开时再炸
            if (!GameSettings.TntExplodes)
            {
                if (_primed.Count > 0)
                    _primed.Clear();
                return false;
            }

            if (_primed.Count == 0)
                return false;

            bool exploded = false;
            for (int i = _primed.Count - 1; i >= 0; i--)
            {
                Primed p = _primed[i];

                // 这一格已经不是 TNT 了（被消行带走 / 被别的爆炸炸没了）：哑火，静静移除。
                // 如果它只是被重力挪走了，那一格现在仍可能是 TNT——但坐标已经对不上，
                // 宁可丢掉重新点火（下次落定时 PrimeContact 会再扫到它）。
                if (grid.Get(p.X, p.Y, p.Z) != _tntId)
                {
                    _primed.RemoveAt(i);
                    continue;
                }

                p.Fuse -= dt;
                if (p.Fuse > 0f)
                {
                    _primed[i] = p;
                    continue;
                }

                _primed.RemoveAt(i);
                Explode(grid, p);
                exploded = true;
            }

            return exploded;
        }

        static void Explode(Grid grid, Primed p)
        {
            var blast = new Blast
            {
                X = p.X,
                Y = p.Y,
                Z = p.Z,
                Cells = new List<Vector3Int>(),
                Blocks = new List<int>(),
            };

            int r = Mathf.CeilToInt(BlastRadius);
            float r2 = BlastRadius * BlastRadius;

            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (dx * dx + dy * dy + dz * dz > r2)
                            continue;

                        int x = p.X + dx;
                        int y = p.Y + dy;
                        int z = p.Z + dz;
                        if (!grid.InBounds(x, y, z))
                            continue;

                        int id = grid.Get(x, y, z);

                        // 爆心那一格就是这颗自己：直接清掉。
                        // 不特判的话它会走下面的 TNT 分支，把自己又点一次（永远炸不完）。
                        if (dx == 0 && dy == 0 && dz == 0)
                        {
                            grid.Set(x, y, z, 0);
                            continue;
                        }

                        if (id <= 0)
                        {
                            // 顺手把溅到爆炸范围里的水/岩浆吹掉
                            if (Fluids.AmountAt(x, y, z) > 0)
                                Fluids.ClearCell(x, y, z);
                            continue;
                        }

                        if (id == _tntId)
                        {
                            // 连锁：不直接炸掉，点着了让它自己再烧一小会儿（原版也是这样）。
                            // 格子里的 TNT 保留不动——Tick 每帧都要用「格子里还是 TNT」来确认这颗还有效，
                            // 提前清掉会被当成哑火。
                            float fuse = Random.Range(ChainFuseMin, ChainFuseMax);
                            Prime(grid, x, y, z, fuse);
                            continue;
                        }

                        // 原版里 TNT 炸不动的方块：基岩、黑曜石、哭泣的黑曜石。
                        // 留着不动，也不计分（没被炸掉就不该给分）。
                        if (BlockCatalog.IsBlastProof(id))
                            continue;

                        // 结构方块被炸掉了 → 排一次还原（要在清掉这一格之前判断，
                        // 清完就读不到它是什么了）
                        Structure.NotifyCleared(grid, x, y, z);

                        grid.Set(x, y, z, 0);
                        blast.Cells.Add(new Vector3Int(x, y, z));
                        blast.Blocks.Add(id);
                    }
                }
            }

            _blasts.Add(blast);
        }
    }
}
