using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 水和岩浆：定体积模型（不是"无限蔓延"）。
    ///
    /// 每格流体按"份"记，一格最多 8 份：
    /// - 一满格方块 = 8 份；摊开以后 1 份就是 1/8 高的一层薄水。
    /// - 每 0.1 秒推进一次：能往下落就整格落下去（份数不变），落不下去就在四邻之间均分
    ///   （把两边差的一半分过去），所以总份数始终不变，水不会无中生有。
    /// - 画的时候按份数给高度，8 份满格、1 份就薄薄一层，看起来就是水摊开变浅。
    /// </summary>
    public static class Fluids
    {
        /// <summary>一格最多 8 份（8 份 = 满格）。</summary>
        public const int MaxAmount = 8;

        /// <summary>兼容旧代码：8 份算 level 0，1 份算 level 7。</summary>
        public const int MaxSpread = 7;

        const float TickInterval = 0.1f;

        static int _waterId = -1;
        static int _lavaId = -1;

        static int[,,] _amount;   // 每格几份（0 表示没有流体）
        static int[,,] _id;       // 每格是哪种流体（0 表示没有）
        static int _sx, _sy, _sz;
        static float _timer;

        public static int WaterId { get { return _waterId; } }
        public static int LavaId { get { return _lavaId; } }

        public static void Init()
        {
            _waterId = FindByName("water");
            _lavaId = FindByName("lava");
            Debug.Log("[fluids] water=" + _waterId + " lava=" + _lavaId);
        }

        public static bool IsFluid(int blockId)
        {
            return blockId > 0 && (blockId == _waterId || blockId == _lavaId);
        }

        public static void Reset()
        {
            _amount = null;
            _id = null;
            _timer = 0f;
        }

        static void Ensure(Grid grid)
        {
            if (_amount != null && _sx == grid.SizeX && _sy == grid.SizeY && _sz == grid.SizeZ)
                return;

            _sx = grid.SizeX;
            _sy = grid.SizeY;
            _sz = grid.SizeZ;
            _amount = new int[_sx, _sy, _sz];
            _id = new int[_sx, _sy, _sz];
        }

        static bool InBounds(int x, int y, int z)
        {
            return _amount != null && x >= 0 && y >= 0 && z >= 0 && x < _sx && y < _sy && z < _sz;
        }

        /// <summary>这一格有几份流体（0~8）。渲染用。</summary>
        public static int AmountAt(int x, int y, int z)
        {
            if (!InBounds(x, y, z)) return 0;
            return _amount[x, y, z];
        }

        /// <summary>直接设某格的份数（只给自检用，正常流程走 AddSource / Step）。</summary>
        public static void SetAmount(int x, int y, int z, int amount, int id)
        {
            if (_amount == null || x < 0 || y < 0 || z < 0 || x >= _sx || y >= _sy || z >= _sz)
                return;
            if (amount < 0) amount = 0;
            if (amount > MaxAmount) amount = MaxAmount;
            _amount[x, y, z] = amount;
            _id[x, y, z] = amount > 0 ? id : 0;
        }

        /// <summary>把一格的水直接清掉（消行时用）。</summary>
        public static void ClearCell(int x, int y, int z)
        {
            if (_id == null || x < 0 || y < 0 || z < 0 || x >= _sx || y >= _sy || z >= _sz)
                return;
            _amount[x, y, z] = 0;
            _id[x, y, z] = 0;
        }

        /// <summary>某格是哪种流体（不是流体返回 0）。水不写 grid，渲染要靠这个判断画什么。</summary>
        public static int IdAt(int x, int y, int z)
        {
            if (_id == null || x < 0 || y < 0 || z < 0 || x >= _sx || y >= _sy || z >= _sz)
                return 0;
            return _id[x, y, z];
        }

        /// <summary>旧接口：没流体返回 -1，有的话返回 0~7（8 份算 0，1 份算 7）。</summary>
        public static int DepthAt(int x, int y, int z)
        {
            int a = AmountAt(x, y, z);
            if (a <= 0) return -1;
            return Mathf.Clamp(MaxAmount - a, 0, MaxSpread);
        }

        /// <summary>往这一格放一满格流体（8 份）。</summary>
        public static void AddSource(Grid grid, int x, int y, int z, int id)
        {
            Ensure(grid);
            if (!InBounds(x, y, z)) return;

            _amount[x, y, z] = MaxAmount;
            _id[x, y, z] = id;
        }

        /// <summary>每帧调用；真的推进了一步就返回 true（外面据此重画）。</summary>
        public static bool Tick(Grid grid, float dt)
        {
            Ensure(grid);
            if (_amount == null) return false;

            _timer += dt;
            if (_timer < TickInterval) return false;
            _timer = 0f;

            bool changed = Step(grid);
            return changed;
        }

        static bool Step(Grid grid)
        {
            bool changed = false;

            // 从下往上扫：先把能落下去的都落下去，落不下去的再往两边分
            for (int y = 0; y < _sy; y++)
            {
                for (int x = 0; x < _sx; x++)
                {
                    for (int z = 0; z < _sz; z++)
                    {
                        int a = _amount[x, y, z];
                        if (a <= 0)
                            continue;

                        // 1) 正下方不是方块：把水往下面灌，灌满后剩下的才留在这格。
                        //    这样上下叠着的几个 1/8 会自己并成一柱（3 个 1/8 就是 3/8 高），
                        //    而不是每层各留一片 1/8 的薄水。
                        if (y > 0 && grid.Get(x, y - 1, z) == 0)
                        {
                            int room = MaxAmount - _amount[x, y - 1, z];
                            if (room > 0)
                            {
                                int move = a < room ? a : room;
                                _amount[x, y - 1, z] += move;
                                _id[x, y - 1, z] = _id[x, y, z];
                                _amount[x, y, z] -= move;
                                if (_amount[x, y, z] <= 0)
                                {
                                    _amount[x, y, z] = 0;
                                    _id[x, y, z] = 0;
                                }
                                changed = true;
                                continue;
                            }
                        }

                        // 2) 横向只从「已经落到底」的水往外铺：下方是方块、或者已经是最底层。
                        //    这样叠着的水只有最底那一层会往旁边散，上面几层靠往下落来补。
                        bool resting = (y == 0) || grid.Get(x, y - 1, z) != 0;
                        if (!resting)
                            continue;

                        // 3) 吸附：邻居最多涨到本格的一半。挑最少、且比一半还少的那个邻居，
                        //    一个 tick 只给 1 份 —— 水是慢慢渗开的，不是一下摊平。
                        //    1 份的水一半是 0，谁都满足不了，所以边上的薄水就停在那儿了。
                        int half = a / 2;
                        if (half <= 0)
                            continue;

                        int bestX = -1, bestZ = -1, best = half;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? -1 : k == 1 ? 1 : 0);
                            int nz = z + (k == 2 ? -1 : k == 3 ? 1 : 0);
                            if (!InBounds(nx, y, nz))
                                continue;
                            int cell = grid.Get(nx, y, nz);
                            if (cell != 0 && cell != _id[x, y, z])
                                continue;   // 那边是方块，进不去
                            int na = _amount[nx, y, nz];
                            if (na < best)
                            {
                                best = na;
                                bestX = nx;
                                bestZ = nz;
                            }
                        }

                        if (bestX >= 0)
                        {
                            _amount[bestX, y, bestZ] += 1;
                            _id[bestX, y, bestZ] = _id[x, y, z];
                            _amount[x, y, z] = a - 1;
                            if (_amount[x, y, z] <= 0)
                            {
                                _amount[x, y, z] = 0;
                                _id[x, y, z] = 0;
                            }
                            changed = true;
                        }
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// 方块落定在某一格：水不占格子，方块落下来就直接占住这一格，
        /// 原来那格水被搬到上方去 —— 先和正上方原有的水合成一滴，装满一格再往上一格；
        /// 上面全满才试四个侧面，都满才丢掉。
        /// </summary>
        public static void OnSolidPlaced(Grid grid, int x, int y, int z)
        {
            Ensure(grid);
            if (!InBounds(x, y, z)) return;

            if (_amount[x, y, z] > 0)
            {
                int a = _amount[x, y, z];
                int ftype = _id[x, y, z];
                _amount[x, y, z] = 0;
                _id[x, y, z] = 0;

                // 把这一格的水往上搬：和上方原本的水合成一滴（一格最多 8 份），
                // 装满一格就再往上找一格。竖着落下来的方块（I 形、转过的 L）自己占着上面几格，
                // 所以只认「网格里是空的」那格，等于把水送到方块堆的最上面。
                int left = a;
                for (int ty = y + 1; ty < _sy && left > 0; ty++)
                {
                    if (grid.Get(x, ty, z) != 0)
                        continue;
                    left = PushInto(grid, x, ty, z, ftype, left);
                }

                // 上面全满了，再试四个侧面
                if (left > 0)
                {
                    left = PushInto(grid, x + 1, y, z, ftype, left);
                    left = PushInto(grid, x - 1, y, z, ftype, left);
                    left = PushInto(grid, x, y, z + 1, ftype, left);
                    left = PushInto(grid, x, y, z - 1, ftype, left);
                }

                if (left > 0)
                    Debug.Log("[fluids] 方块压住水，四处都满了，丢掉 " + left + " 份: " + x + "," + y + "," + z);

            }
        }

        /// <summary>往那一格塞最多 a 份流体，返回还剩几份没塞进去。</summary>
        static int PushInto(Grid grid, int x, int y, int z, int id, int a)
        {
            if (a <= 0) return 0;
            if (!InBounds(x, y, z)) return a;
            int cell = grid.Get(x, y, z);
            if (cell != 0 && cell != id) return a;
            int room = MaxAmount - _amount[x, y, z];
            if (room <= 0) return a;

            int put = a < room ? a : room;
            _amount[x, y, z] += put;
            _id[x, y, z] = id;
            return a - put;
        }

        static int FindByName(string name)
        {
            var blocks = BlockCatalog.Blocks;
            for (int i = 0; i < blocks.Length; i++)
            {
                if (blocks[i].Name == name)
                    return i + 1;
            }
            return -1;
        }
    }
}
