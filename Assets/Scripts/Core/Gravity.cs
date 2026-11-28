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
        /// <summary>
        /// 混凝土粉末（设置里是单独一项开关）。
        /// 只认粉末：硬化后的混凝土是成品砖，原版也不会掉（用户特意纠正过）。
        /// </summary>
        static bool IsConcretePowder(string name)
        {
            return name.EndsWith("_concrete_powder");
        }

        /// <summary>这一类方块本身会不会受重力（不管设置里开没开）。</summary>
        public static bool IsFalling(int blockId)
        {
            // 受重力由方块表的 gravity 标签决定（沙/粉尘/砾石/盐）。
            // 之前是按 MC 名字硬编码的字符串表，换方块表就得跟着改 —— 现在标签说了算。
            return blockId > 0 && BlockCatalog.HasTag(blockId, "gravity");
        }

        /// <summary>
        /// 这一格现在受不受重力：沙类是「沙子与沙砾下落」，混凝土是「混凝土受重力下落」。
        /// 两个机制的开关是分开的，所以不能只看 IsFalling。
        /// </summary>
        public static bool FallsEnabled(int blockId)
        {
            if (!IsFalling(blockId))
                return false;

            string n = BlockCatalog.Of(blockId).Name;
            return IsConcretePowder(n) ? GameSettings.ConcreteFalls : GameSettings.SandFalls;
        }

        /// <summary>
        /// 反复把会掉的方块往下挪一格，直到全场稳定。
        /// 返回是否动过（调用方据此重绘）；传了 moves 就把每一格的下移记进去。
        /// </summary>
        public static bool Settle(Grid grid, List<GravityStep> moves = null)
        {
            // 两个重力机制的开关都关掉就没什么可做的（沙类 / 混凝土各自一项）
            if (grid == null || (!GameSettings.SandFalls && !GameSettings.ConcreteFalls))
                return false;

            bool any = false;
            int maxPass = grid.SizeY + 2;

            for (int pass = 0; pass < maxPass; pass++)
            {
                bool moved = false;

                for (int z = 0; z < grid.SizeZ; z++)
                {
                    // 扫到最顶层（y < SizeY）：只扫到 SizeY-1 的话，停在顶格的沙
                    // 下面正好空了也不会掉。y=0 时 IsFree(y=-1) 返回 false，不会越界
                    for (int y = 0; y < grid.SizeY; y++)
                    {
                        for (int x = 0; x < grid.SizeX; x++)
                        {
                            int b = grid.Get(x, y, z);
                            if (b <= 0 || !FallsEnabled(b))
                                continue;
                            if (!grid.IsFree(x, y - 1, z))
                                continue;

                            // 用 Move 而不是「写目标 + 清源格」：朝向和序号得跟着方块一起走，
                            // 不然掉下来的那块会继承目标格上一任的朝向
                            grid.Move(x, y, z, x, y - 1, z);
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
