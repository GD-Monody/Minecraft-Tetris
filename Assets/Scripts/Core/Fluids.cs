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
            grid.Set(x, y, z, id);
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

                        // 1) 正下方是空的：整格落下去（份数不变）
                        if (y > 0 && _amount[x, y - 1, z] == 0 && grid.Get(x, y - 1, z) == 0)
                        {
                            _amount[x, y - 1, z] = a;
                            _id[x, y - 1, z] = _id[x, y, z];
                            _amount[x, y, z] = 0;
                            _id[x, y, z] = 0;
                            grid.Set(x, y - 1, z, _id[x, y - 1, z]);
                            grid.Set(x, y, z, 0);
                            changed = true;
                            continue;
                        }

                        // 2) 落不下去：在四个邻居里找份数最少的那格，把差的一半分过去
                        int bestX = -1, bestZ = -1, best = a;
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
                            int transfer = (a - best + 1) / 2;
                            if (transfer > 0)
                            {
                                _amount[bestX, y, bestZ] += transfer;
                                _id[bestX, y, bestZ] = _id[x, y, z];
                                _amount[x, y, z] -= transfer;
                                if (_amount[x, y, z] <= 0)
                                {
                                    _amount[x, y, z] = 0;
                                    _id[x, y, z] = 0;
                                }
                                grid.Set(bestX, y, bestZ, _id[bestX, y, bestZ]);
                                grid.Set(x, y, z, _id[x, y, z]);
                                changed = true;
                            }
                        }
                    }
                }
            }

            return changed;
        }

        /// <summary>
        /// 方块压到某个格子上：这一格原来的流体顶到上面一格去（份数不变，被挤起来），
        /// 上面也是方块或者到顶了才真消失。
        /// </summary>
        public static void OnSolidPlaced(Grid grid, int x, int y, int z)
        {
            Ensure(grid);
            if (!InBounds(x, y, z)) return;

            // 方块落在水面上的情况：落点这一格是空的，水在它下面一格。
            // 这时方块并没有"压到"水，但视觉上方块占住了水的位置，水被挤到方块上方去。
            // 落点下方「连着的一串水」：从紧贴方块那格往下数，连续几格水都算被方块压住。
            // 只要中间断一格（空格或方块）就停，免得把隔着方块、埋得很深的水也翻上来。
            int total = 0;
            int ftype = 0;
            int top = -1;
            for (int ty = y - 1; ty >= 0; ty--)
            {
                if (_amount[x, ty, z] <= 0)
                    break;
                if (ftype == 0)
                    ftype = _id[x, ty, z];
                total += _amount[x, ty, z];
                top = ty;
            }

            if (total > 0)
            {
                // 先把这串水清掉（含 grid 里的水标记），方块下方不留半点占位
                for (int ty = top; ty < y; ty++)
                {
                    _amount[x, ty, z] = 0;
                    _id[x, ty, z] = 0;
                    grid.Set(x, ty, z, 0);
                }

                // 再从方块正上方往上，一个空格一个空格地摊回去（每格最多 MaxAmount 份）
                int left = total;
                for (int ty = y + 1; ty < _sy && left > 0; ty++)
                {
                    if (grid.Get(x, ty, z) != 0)
                        continue;
                    int room = MaxAmount - _amount[x, ty, z];
                    if (room <= 0)
                        continue;
                    int put = left < room ? left : room;
                    _amount[x, ty, z] += put;
                    _id[x, ty, z] = ftype;
                    grid.Set(x, ty, z, ftype);
                    left -= put;
                }

                // 正上方塞不下的，往侧面挪
                if (left > 0)
                {
                    if (TryPush(grid, x - 1, y, z, ftype, left)) left = 0;
                    else if (TryPush(grid, x + 1, y, z, ftype, left)) left = 0;
                    else if (TryPush(grid, x, y, z - 1, ftype, left)) left = 0;
                    else if (TryPush(grid, x, y, z + 1, ftype, left)) left = 0;
                }

                Debug.Log("[fluids] 方块下方 " + total + " 份水挪到上方，剩 " + left + " 份没放下: " + x + "," + y + "," + z);
            }

            int a = _amount[x, y, z];
            if (a <= 0)
                return;

            int id = _id[x, y, z];
            string where = "none";

            // 1) 正上方空着：直接顶上去
            if (TryPush(grid, x, y + 1, z, id, a))
                where = "up";

            // 2) 正上方是方块（水被压在方块底下）：渗到方块上面那一格去
            if (where == "none" && y + 1 < _sy && grid.Get(x, y + 1, z) != 0 && TryPush(grid, x, y + 2, z, id, a))
                where = "over";

            // 3) 侧面找得到空位就往两边渗
            if (where == "none")
            {
                if (TryPush(grid, x - 1, y, z, id, a)) where = "left";
                else if (TryPush(grid, x + 1, y, z, id, a)) where = "right";
                else if (TryPush(grid, x, y, z - 1, id, a)) where = "back";
                else if (TryPush(grid, x, y, z + 1, id, a)) where = "front";
            }

            _amount[x, y, z] = 0;
            _id[x, y, z] = 0;

            if (where == "none")
                Debug.Log("[fluids] 流体被压没（四面都放不下）: " + x + "," + y + "," + z);
            else
                Debug.Log("[fluids] 流体被挤到 " + where + ": " + x + "," + y + "," + z);
        }

        /// <summary>把 a 份流体塞进某一格（那边是空或同类流体才塞得进去）。</summary>
        static bool TryPush(Grid grid, int x, int y, int z, int id, int a)
        {
            if (!InBounds(x, y, z)) return false;
            int cell = grid.Get(x, y, z);
            if (cell != 0 && cell != id) return false;
            if (_amount[x, y, z] + a > MaxAmount * 2) return false;

            _amount[x, y, z] += a;
            _id[x, y, z] = id;
            grid.Set(x, y, z, id);
            return true;
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
