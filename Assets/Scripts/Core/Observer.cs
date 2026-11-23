using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 侦测器点燃 TNT：只有**激发面**贴着 TNT 才会点着。
    ///
    /// 这里的方块没有朝向信息（同一格永远画同一个朝向），没法像原版那样
    /// 「脸朝哪边就检测哪边」，所以把激发面固定成**底面**：
    /// 侦测器正下方那格是 TNT 就点燃它；侧面、上面贴着 TNT 都不算。
    /// 这就是「只有激发那面接触到 TNT 才能激活」。
    ///
    /// 原版里侦测器其实是输出红石信号、由红石点 TNT；这里省掉红石那一层，
    /// 直接等价成「贴面引燃」。
    /// </summary>
    public static class Observer
    {
        public static bool IsObserver(int blockId)
        {
            return blockId > 0 && blockId == BlockCatalog.ObserverId;
        }

        /// <summary>
        /// 扫一遍场地，把「底面压着 TNT」的侦测器对应的那颗 TNT 点着。
        /// 返回这一轮点着了几个（结果也进 Tnt.LastPrimed）。
        /// </summary>
        public static int IgniteTntBelow(Grid grid)
        {
            if (grid == null || !GameSettings.ObserverIgnites)
                return 0;

            int obs = BlockCatalog.ObserverId;
            if (obs <= 0)
                return 0;

            int fired = 0;
            for (int x = 0; x < grid.SizeX; x++)
            {
                // y = 0 没有下方，跳过
                for (int y = 1; y < grid.SizeY; y++)
                {
                    for (int z = 0; z < grid.SizeZ; z++)
                    {
                        if (grid.Get(x, y, z) != obs)
                            continue;
                        if (!Tnt.IsTnt(grid.Get(x, y - 1, z)))
                            continue;
                        if (Tnt.Prime(grid, x, y - 1, z, Tnt.FuseSeconds))
                            fired++;
                    }
                }
            }

            return fired;
        }
    }
}
