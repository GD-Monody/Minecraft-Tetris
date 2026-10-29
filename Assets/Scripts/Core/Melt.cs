using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 雪与冰遇岩浆融化：这两类方块和岩浆相邻（六向任一面）就地化成一格水。
    ///
    /// 判定规则（设置页的「雪与冰遇岩浆融化」开关）：
    /// - 只认**六向相邻**的岩浆，斜对角不算，和 MC 里「贴着岩浆就融化」的判定一致；
    /// - 岩浆不分满格还是流动的薄层，只要那格有岩浆（份数 &gt; 0）就算；
    /// - 融化时那一格**清空**再放一满格水（8 份），于是水会从原来雪块/冰块的位置往下淌，
    ///   要是下面就压着岩浆，下一 tick 就会被 Fluids.React 结晶成黑曜石/原石；
    /// - 融掉的格子记在 LastMelted 里，视图层照着刷格子、撒碎块。
    ///
    /// 为什么是一遍场地扫描，而不是在流体流动里埋判定：
    /// 岩浆是流体、不写网格，雪块被岩浆「淌过来」贴上时的位置随时在变；
    /// 加上方块落定、重力下移、消行下移、结构还原都会让雪块换位置，
    /// 逐个埋点必漏。场地最大 20×40×10 = 8000 格，扫一遍比维护五六处判定便宜，
    /// 而且只在「流体动过」和「方块落定」这两个时机扫，平时不花时间。
    /// </summary>
    public static class Melt
    {
        /// <summary>最近一次 Sweep 化掉的格子（融化前的位置）。</summary>
        public static readonly List<Vector3Int> LastMelted = new List<Vector3Int>();

        /// <summary>定期检查的间隔，和 Fluids 的推进节奏对齐。</summary>
        const float CheckInterval = 0.1f;

        static float _timer;

        // 六向邻居：下、上、左、右、后、前。和 Fluids 里的顺序保持一致。
        static readonly int[] Dx = { 0, 0, -1, 1, 0, 0 };
        static readonly int[] Dy = { -1, 1, 0, 0, 0, 0 };
        static readonly int[] Dz = { 0, 0, 0, 0, -1, 1 };

        /// <summary>这块砖会不会被岩浆化掉（雪块、冰）。</summary>
        public static bool IsMeltable(int blockId)
        {
            if (blockId <= 0)
                return false;
            return blockId == BlockCatalog.SnowId || blockId == BlockCatalog.IceId;
        }

        /// <summary>场地重建 / 演示场景重开时清一下计时器。</summary>
        public static void Reset()
        {
            _timer = 0f;
            LastMelted.Clear();
        }

        /// <summary>
        /// 每帧调用，内部按 CheckInterval 节流。返回这一拍有没有化掉东西。
        ///
        /// 为什么不能只挂在「流体现在动过」上：一桶岩浆封在石槽里、上面贴着雪块时，
        /// 岩浆既不掉也不流，Fluids.Tick 返回 false，挂在它下面等就永远等不到融化。
        /// 只有「岩浆淌过来」那次是流体动过的，静止贴着的必须靠定期扫。
        /// </summary>
        public static bool Tick(Grid grid, float dt, List<Vector3Int> outCells)
        {
            if (grid == null || !GameSettings.SnowMelts)
                return false;

            _timer += dt;
            if (_timer < CheckInterval)
                return false;
            _timer = 0f;

            return Sweep(grid, outCells) > 0;
        }

        /// <summary>扫一遍场地，把贴着岩浆的雪/冰化成水。返回化掉几格（结果也写进 LastMelted）。</summary>
        public static int Sweep(Grid grid)
        {
            LastMelted.Clear();
            return Sweep(grid, LastMelted);
        }

        /// <summary>和上面一样，但把化掉的格子追加进 outCells（不清空它，调用方可能想攒多轮）。</summary>
        public static int Sweep(Grid grid, List<Vector3Int> outCells)
        {
            if (grid == null || !GameSettings.SnowMelts)
                return 0;

            int snowId = BlockCatalog.SnowId;
            int iceId = BlockCatalog.IceId;
            int lavaId = Fluids.LavaId;
            int waterId = Fluids.WaterId;
            // 方块表里缺冰/雪，或者没有岩浆/水，这条机制就不成立
            if ((snowId <= 0 && iceId <= 0) || lavaId <= 0 || waterId <= 0)
                return 0;

            int count = 0;

            for (int y = 0; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        int id = grid.Get(x, y, z);
                        if (id != snowId && id != iceId)
                            continue;

                        if (!TouchesLava(grid, x, y, z, lavaId))
                            continue;

                        // 先清格子再放水：格子被固体占着的时候，流体那一层里不该有东西，
                        // 但真要有残留（比如压方块的路径漏了一次 OnSolidPlaced），清一下更保险。
                        grid.Set(x, y, z, 0);
                        Fluids.ClearCell(x, y, z);
                        Fluids.AddSource(grid, x, y, z, waterId);

                        count++;
                        if (outCells != null)
                            outCells.Add(new Vector3Int(x, y, z));
                    }
                }
            }

            return count;
        }

        /// <summary>这一格的六向里有没有岩浆（份数大于 0 就算）。</summary>
        static bool TouchesLava(Grid grid, int x, int y, int z, int lavaId)
        {
            for (int k = 0; k < 6; k++)
            {
                int nx = x + Dx[k];
                int ny = y + Dy[k];
                int nz = z + Dz[k];
                if (nx < 0 || ny < 0 || nz < 0 || nx >= grid.SizeX || ny >= grid.SizeY || nz >= grid.SizeZ)
                    continue;
                if (Fluids.IdAt(nx, ny, nz) == lavaId && Fluids.AmountAt(nx, ny, nz) > 0)
                    return true;
            }
            return false;
        }
    }
}
