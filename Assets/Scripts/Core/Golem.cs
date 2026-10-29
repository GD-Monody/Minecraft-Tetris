using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>傀儡种类。None 只是「不是傀儡」，不参与收集。</summary>
    public enum GolemKind
    {
        None = 0,
        Snow = 1,
        Iron = 2,
        Copper = 3,
    }

    /// <summary>
    /// 傀儡召唤：南瓜头压在雪块 / 铁块 / 铜块上就召出对应傀儡。
    ///
    /// 原版是「按形状摆好方块 + 最后放南瓜头」召唤，这里的方块是随机四格，
    /// 摆不出原版那种 T 形，所以简化成一条：**南瓜头正下方那一格是什么，就召哪种**：
    /// - 雪块 → 雪傀儡
    /// - 铁块 → 铁傀儡
    /// - 铜块 → 铜傀儡
    /// 雕刻南瓜和普通南瓜都算（原版两个都能召唤）。
    ///
    /// 召唤时**两格一起消失**（南瓜头和它踩着的方块），因为傀儡是由这两格「变成」的，
    /// 不是白送的。抽走两格之后上方可能悬空，调用方（GameLoop）要补一次重力结算。
    ///
    /// 傀儡不在场地里活动，而是挪到场外右侧摆成一排当收藏，见 Bootstrap.SpawnGolemModel。
    /// 所以这里只负责「认出来 + 清格子 + 记账」，不参与碰撞和消行。
    ///
    /// 设置里的「傀儡召唤」关掉之后，南瓜头压在什么上面都只是一块普通砖。
    /// </summary>
    public static class Golem
    {
        /// <summary>场外最多摆几个傀儡。摆满之后还能召唤，只是不再往展示区加东西。</summary>
        public const int MaxDisplay = 8;

        /// <summary>一次召唤的记录。</summary>
        public struct Summon
        {
            public GolemKind Kind;
            /// <summary>南瓜头那一格（傀儡的「脸」原来所在的位置）。</summary>
            public int X, Y, Z;
            /// <summary>被一起消掉的脚下那一格的方块编号，视图层撒碎块用。</summary>
            public int BodyBlockId;
        }

        /// <summary>最近一次 Check 召出来的傀儡。</summary>
        public static readonly List<Summon> LastSummoned = new List<Summon>();

        /// <summary>历来召出来的傀儡，按顺序排；视图层照这个下标摆展示位。</summary>
        public static readonly List<Summon> Collection = new List<Summon>();

        /// <summary>这个方块能不能当脑袋（雕刻南瓜 / 普通南瓜）。</summary>
        public static bool IsPumpkin(int blockId)
        {
            if (blockId <= 0)
                return false;
            return blockId == BlockCatalog.CarvedPumpkinId || blockId == BlockCatalog.PumpkinId;
        }

        /// <summary>这块砖压在下面能召出什么（召不出返回 None）。</summary>
        public static GolemKind KindOf(int blockId)
        {
            if (blockId <= 0)
                return GolemKind.None;
            if (blockId == BlockCatalog.SnowId)
                return GolemKind.Snow;
            if (blockId == BlockCatalog.IronBlockId)
                return GolemKind.Iron;
            if (blockId == BlockCatalog.CopperBlockId)
                return GolemKind.Copper;
            return GolemKind.None;
        }

        public static void Reset()
        {
            LastSummoned.Clear();
            Collection.Clear();
        }

        /// <summary>
        /// 扫一遍场地，把「南瓜头压着对应方块」的组合召成傀儡。
        /// 返回这一次召了几个（结果也写进 LastSummoned，同时追加进 Collection）。
        /// </summary>
        public static int Check(Grid grid)
        {
            LastSummoned.Clear();
            return Check(grid, LastSummoned);
        }

        /// <summary>同 Check，但结果追加进 outList（不清空它，调用方可能想攒着）。</summary>
        public static int Check(Grid grid, List<Summon> outList)
        {
            if (grid == null || !GameSettings.GolemsSummon)
                return 0;

            int count = 0;

            for (int y = 1; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        if (!IsPumpkin(grid.Get(x, y, z)))
                            continue;

                        // 只看正下方：傀儡是「站在」方块上的，斜着压过去不算
                        int body = grid.Get(x, y - 1, z);
                        GolemKind kind = KindOf(body);
                        if (kind == GolemKind.None)
                            continue;

                        // 两格一起消失：脑袋和脚下那块都变成傀儡了
                        grid.Set(x, y, z, 0);
                        grid.Set(x, y - 1, z, 0);

                        var s = new Summon
                        {
                            Kind = kind,
                            X = x, Y = y, Z = z,
                            BodyBlockId = body,
                        };
                        count++;
                        if (outList != null)
                            outList.Add(s);
                        Collection.Add(s);
                    }
                }
            }

            return count;
        }
    }
}
