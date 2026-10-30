using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 海绵吸水：干海绵把周围一圈的水吸进肚子，自己变成湿海绵；湿海绵就吸不动了。
    ///
    /// 跟原版一致的地方：
    /// - 吸水范围是以海绵为中心的 7×7×7（切比雪夫半径 3），从海绵向外逐格找水；
    /// - 一次最多吸 65 格，吸满就停（原版也是 65）；
    /// - 实心方块挡路：水隔着墙是吸不到的（原版海绵的 BFS 走不过不透水方块）；
    /// - 一格水都没吸到的海绵保持干燥，下次还能接着吸；吸到了哪怕一格也变湿。
    /// - 半格水（流动中的水）也算水，照样吸走。
    ///
    /// 跟原版不一样的地方：
    /// - 原版吸掉水后海绵会「记住自己湿了」，放在熔炉里能烤干；这里没有熔炉，
    ///   湿海绵就一直是湿的（也不会自己变干）。
    /// - 原版水「流进」海绵所在的那格也算接触；这里水不写网格，海绵只能吸
    ///   六向邻居之外的流体层里的水 —— 反正水贴着海绵的时候就在范围里，效果一样。
    /// </summary>
    public static class Sponge
    {
        /// <summary>一次最多吸多少格水（原版同为 65）。</summary>
        public const int MaxCells = 65;

        /// <summary>吸水半径：以海绵为中心的切比雪夫距离（3 = 7×7×7）。</summary>
        public const int Reach = 3;

        /// <summary>吸掉一格水得多少分。排水能帮层消腾地方，给一点分但不值钱。</summary>
        public const int ScorePerWater = 1;

        public struct Soak
        {
            /// <summary>海绵自己的位置。</summary>
            public int X, Y, Z;
            /// <summary>这一下有没有吸到水（吸到了才变湿）。</summary>
            public bool BecameWet;
            /// <summary>这次吸掉的水格。</summary>
            public List<Vector3Int> Cells;
        }

        /// <summary>最近一次扫描的结果，视图层照着刷格子、撒水花。</summary>
        public static readonly List<Soak> LastSoaks = new List<Soak>();

        // 六向邻居：下、上、左、右、后、前。和 Fluids / Melt / Fire 里的顺序一致。
        static readonly int[] Dx = { 0, 0, -1, 1, 0, 0 };
        static readonly int[] Dy = { -1, 1, 0, 0, 0, 0 };
        static readonly int[] Dz = { 0, 0, 0, 0, -1, 1 };

        static int _spongeId = -1;
        static int _wetSpongeId = -1;
        static bool _ready;

        static void Init()
        {
            if (_ready)
                return;
            _ready = true;

            BlockDef[] blocks = BlockCatalog.Blocks;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Name == "sponge")
                    _spongeId = i + 1;
                else if (blocks[i].Name == "wet_sponge")
                    _wetSpongeId = i + 1;
            }

            if (_spongeId <= 0)
                Debug.LogWarning("[sponge] 方块表里没有 sponge");
            if (_wetSpongeId <= 0)
                Debug.LogWarning("[sponge] 方块表里没有 wet_sponge");
        }

        public static int SpongeId { get { Init(); return _spongeId; } }
        public static int WetSpongeId { get { Init(); return _wetSpongeId; } }

        public static bool IsSponge(int blockId) { Init(); return blockId > 0 && blockId == _spongeId; }
        public static bool IsWet(int blockId) { Init(); return blockId > 0 && blockId == _wetSpongeId; }

        /// <summary>清掉上一次的结果（重开一局 / 演示重摆时调）。</summary>
        public static void Reset()
        {
            LastSoaks.Clear();
        }

        /// <summary>
        /// 全场扫一遍，让每个干海绵吸自己周围的水。返回总共吸掉几格水。
        /// 结果（哪些海绵变湿了、各自吸了哪些格）记在 LastSoaks 里。
        /// </summary>
        public static int SoakAll(Grid grid)
        {
            if (grid == null || !GameSettings.SpongeSoaks)
                return 0;
            Init();
            if (_spongeId <= 0 || _wetSpongeId <= 0 || Fluids.WaterId <= 0)
                return 0;

            int total = 0;
            for (int x = 0; x < grid.SizeX; x++)
            {
                for (int y = 0; y < grid.SizeY; y++)
                {
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        if (grid.Get(x, y, z) != _spongeId)
                            continue;

                        var soak = new Soak
                        {
                            X = x,
                            Y = y,
                            Z = z,
                            BecameWet = false,
                            Cells = new List<Vector3Int>(),
                        };
                        SoakAround(grid, x, y, z, soak.Cells);

                        // 一格水都没吸到：保持干燥（原版也是这样），下次还能再吸
                        if (soak.Cells.Count == 0)
                            continue;

                        // 吸到了水就变湿。要赶在记 Cells 之后换 —— 换完它就不是干海绵了
                        grid.Set(x, y, z, _wetSpongeId);
                        soak.BecameWet = true;
                        LastSoaks.Add(soak);
                        total += soak.Cells.Count;
                    }
                }
            }
            return total;
        }

        /// <summary>
        /// 从海绵出发向外找水：BFS 逐格扩展，只在切比雪夫半径 3 的盒子里走，
        /// 碰到实心方块就绕不过去，走到的水格全部吸干，最多 MaxCells 格。
        /// </summary>
        static void SoakAround(Grid grid, int sx, int sy, int sz, List<Vector3Int> cells)
        {
            var queue = new Queue<Vector3Int>();
            var seen = new HashSet<Vector3Int>();
            var start = new Vector3Int(sx, sy, sz);
            queue.Enqueue(start);
            seen.Add(start);

            while (queue.Count > 0 && cells.Count < MaxCells)
            {
                Vector3Int c = queue.Dequeue();

                for (int k = 0; k < 6 && cells.Count < MaxCells; k++)
                {
                    int nx = c.x + Dx[k];
                    int ny = c.y + Dy[k];
                    int nz = c.z + Dz[k];
                    if (nx < 0 || ny < 0 || nz < 0 || nx >= grid.SizeX || ny >= grid.SizeY || nz >= grid.SizeZ)
                        continue;
                    if (Mathf.Abs(nx - sx) > Reach || Mathf.Abs(ny - sy) > Reach || Mathf.Abs(nz - sz) > Reach)
                        continue;

                    var n = new Vector3Int(nx, ny, nz);
                    if (!seen.Add(n))
                        continue;

                    // 实心方块挡路：这一格既不吸（水也不会在实心格里）也不穿过去
                    if (grid.Get(nx, ny, nz) > 0)
                        continue;

                    // 有水（不管满格还是半格）就吸走；吸完这一格变空，还能继续往外走
                    if (Fluids.AmountAt(nx, ny, nz) > 0 && Fluids.IdAt(nx, ny, nz) == Fluids.WaterId)
                    {
                        Fluids.ClearCell(nx, ny, nz);
                        cells.Add(n);
                    }

                    queue.Enqueue(n);
                }
            }
        }
    }
}
