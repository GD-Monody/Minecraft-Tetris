using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>一次重力下移：从哪格挪到哪格。视图层用它播滑落动画。</summary>
    public struct GravityStep
    {
        public int FromX, FromY, FromZ;
        public int ToX, ToY, ToZ;
    }

    /// <summary>
    /// 重力方块（沙子、红沙、沙砾、可疑沙、混凝土粉末）的下落结算。
    /// 在方块落定写入网格之后、判定满行之前调用：
    /// 刚落下的沙可能正好把下面空缺补满，所以顺序不能反。
    /// </summary>
    public static class Gravity
    {
        public static bool IsFalling(int blockId)
        {
            if (blockId <= 0)
                return false;

            string n = BlockCatalog.Of(blockId).Name;
            if (string.IsNullOrEmpty(n))
                return false;

            if (n == "sand" || n == "red_sand" || n == "gravel")
                return true;
            if (n.StartsWith("suspicious_sand") || n.StartsWith("suspicious_gravel"))
                return true;
            if (n.EndsWith("_concrete_powder"))
                return true;

            return false;
        }

        /// <summary>
        /// 反复把会掉的方块往下挪一格，直到全场稳定。
        /// 返回是否动过（调用方据此重绘）；传了 moves 就把每一格的下移记进去。
        /// </summary>
        public static bool Settle(Grid grid, List<GravityStep> moves = null)
        {
            // 设置里关掉「沙子与沙砾下落」：沙类就成普通方块，停在半空不动
            if (grid == null || !GameSettings.SandFalls)
                return false;

            bool any = false;
            int maxPass = grid.SizeY + 2;

            for (int pass = 0; pass < maxPass; pass++)
            {
                bool moved = false;

                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int y = 0; y < grid.SizeY - 1; y++)
                    {
                        for (int x = 0; x < grid.SizeX; x++)
                        {
                            int b = grid.Get(x, y, z);
                            if (b <= 0 || !IsFalling(b))
                                continue;
                            if (!grid.IsFree(x, y - 1, z))
                                continue;

                            grid.Set(x, y - 1, z, b);
                            grid.Set(x, y, z, 0);
                            if (moves != null)
                            {
                                moves.Add(new GravityStep
                                {
                                    FromX = x, FromY = y, FromZ = z,
                                    ToX = x, ToY = y - 1, ToZ = z,
                                });
                            }
                            moved = true;
                            any = true;
                        }
                    }
                }

                if (!moved)
                    break;
            }

            return any;
        }
    }
}
