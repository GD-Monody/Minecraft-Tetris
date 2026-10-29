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
    /// 傀儡召唤：按原版的摆法搭好方块，最上面压一个南瓜头，就召出对应傀儡。
    ///
    /// 三种的摆法（南瓜头永远在最上面，下面按原版形状铺）：
    /// - **雪傀儡**：南瓜头 + 竖着两格雪块（原版就是两个雪块 + 南瓜头）
    /// - **铁傀儡**：南瓜头 + 竖着两格铁块，顶上那格左右各再来一块 —— 也就是「倒 T」的四个铁块
    /// - **铜傀儡**：南瓜头 + 一格铜块（原版就这么简单）
    ///
    /// 铜块用哪个氧化阶段（普通 / 斑驳 / 风化 / 氧化），召出来的铜傀儡就从那个阶段开始，
    /// 之后像原版一样慢慢继续氧化，见 GolemPet.TickOxidation。
    ///
    /// 召唤会**把这几个格子一起消掉**：傀儡是由这些方块「变成」的，不是白送的。
    /// 抽掉之后上面压着的东西可能悬空，调用方（GameLoop）要补一次重力结算。
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
            /// <summary>铜傀儡的初始氧化等级 0~3（其它傀儡恒为 0）。</summary>
            public int Oxidation;
            /// <summary>一共消耗掉几格方块（含南瓜头），播粒子用。</summary>
            public int CellCount;
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

        /// <summary>这块铜是第几氧化阶段（0~3），不是铜返回 -1。</summary>
        public static int CopperStage(int blockId)
        {
            if (blockId <= 0)
                return -1;
            if (blockId == BlockCatalog.CopperBlockId)
                return 0;
            if (blockId == BlockCatalog.ExposedCopperId)
                return 1;
            if (blockId == BlockCatalog.WeatheredCopperId)
                return 2;
            if (blockId == BlockCatalog.OxidizedCopperId)
                return 3;
            return -1;
        }

        public static void Reset()
        {
            LastSummoned.Clear();
            Collection.Clear();
        }

        /// <summary>
        /// 扫一遍场地，把摆好的傀儡结构召出来。
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

            // 南瓜头下面至少要有一格垫脚，所以从 y = 1 开始扫
            for (int y = 1; y < grid.SizeY; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        if (!IsPumpkin(grid.Get(x, y, z)))
                            continue;

                        Summon s;
                        if (TrySummon(grid, x, y, z, out s))
                        {
                            count++;
                            if (outList != null)
                                outList.Add(s);
                            Collection.Add(s);
                        }
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// 以 (x,y,z) 的南瓜头为顶，看下面是不是某个傀儡的摆法。
        /// 是的话把那几格清空并填好记录。
        /// </summary>
        static bool TrySummon(Grid grid, int x, int y, int z, out Summon summon)
        {
            summon = default(Summon);

            // 铜傀儡最省事：正下方一格铜块就行，氧化阶段跟着那块铜走
            int below = grid.Get(x, y - 1, z);
            int stage = CopperStage(below);
            if (stage >= 0)
            {
                summon = Make(GolemKind.Copper, x, y, z, below, stage, 2);
                grid.Set(x, y, z, 0);
                grid.Set(x, y - 1, z, 0);
                return true;
            }

            int snowId = BlockCatalog.SnowId;
            int ironId = BlockCatalog.IronBlockId;

            // 雪傀儡：竖着两格雪块
            if (snowId > 0 && below == snowId && y >= 2 && grid.Get(x, y - 2, z) == snowId)
            {
                summon = Make(GolemKind.Snow, x, y, z, snowId, 0, 3);
                grid.Set(x, y, z, 0);
                grid.Set(x, y - 1, z, 0);
                grid.Set(x, y - 2, z, 0);
                return true;
            }

            // 铁傀儡：竖着两格铁块，顶上那格左右各再来一块（倒 T）
            if (ironId > 0 && below == ironId && y >= 2 && grid.Get(x, y - 2, z) == ironId &&
                x >= 1 && x + 1 < grid.SizeX &&
                grid.Get(x - 1, y - 1, z) == ironId && grid.Get(x + 1, y - 1, z) == ironId)
            {
                summon = Make(GolemKind.Iron, x, y, z, ironId, 0, 5);
                grid.Set(x, y, z, 0);
                grid.Set(x, y - 1, z, 0);
                grid.Set(x, y - 2, z, 0);
                grid.Set(x - 1, y - 1, z, 0);
                grid.Set(x + 1, y - 1, z, 0);
                return true;
            }

            return false;
        }

        static Summon Make(GolemKind kind, int x, int y, int z, int bodyId, int oxidation, int cells)
        {
            return new Summon
            {
                Kind = kind,
                X = x, Y = y, Z = z,
                BodyBlockId = bodyId,
                Oxidation = oxidation,
                CellCount = cells,
            };
        }
    }
}
