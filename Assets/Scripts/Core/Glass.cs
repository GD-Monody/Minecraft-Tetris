using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 玻璃被压碎：玻璃**后来**被一块方块压在上面才会碎。
    ///
    /// 判定规则（设置页的「玻璃被压碎」开关）：
    /// - 只看正上方那格：它必须是实心方块，而且**比这格玻璃更晚出现**（`Grid.Stamp` 更大）。
    ///   玻璃落定那一刻，上方本来就有东西（比如塞进一个悬空方块底下、或者叠在一块旧玻璃上）
    ///   不算「被压」，所以玻璃落下不会莫名其妙碎一片；
    /// - 上面那块也是玻璃就不算压：玻璃压玻璃会连锁碎一大片，太吵，也不符合「易碎品被重物压」的直觉；
    /// - 之后不管是谁落上来都算：玩家新摆的方块、沙子掉下来的、TNT 炸完剩下的、结构方块还原搭回去的
    ///   都是新写入的格子，序号更新，于是压碎下面这格玻璃；
    /// - 碎掉之后上方的东西失去支撑，会下落的东西接着掉，可能再压到下面一层玻璃，
    ///   见 GameLoop.CrushGlass 的循环；
    /// - 碎哪几格记在 LastCrushed 里，视图层照着播音效、撒碎块。
    ///
    /// 为什么还是一遍场地扫描：方块落到玻璃上的来源太多（落定、重力、行消下移、层消下移、
    /// 结构方块还原），逐个埋点总会漏掉一条；而且现在判定要看「先后」，扫描时拿两个序号比一下
    /// 就够了，不用在每条路径上维护额外状态。
    /// </summary>
    public static class Glass
    {
        /// <summary>最近一次 Crush 碎掉的格子。</summary>
        public static readonly List<Vector3Int> LastCrushed = new List<Vector3Int>();

        public static bool IsGlass(int blockId)
        {
            return blockId > 0 && blockId == BlockCatalog.GlassId;
        }

        /// <summary>扫一遍场地，把被压着的玻璃打碎。返回碎了几格（结果也写进 LastCrushed）。</summary>
        public static int Crush(Grid grid)
        {
            LastCrushed.Clear();
            return Crush(grid, LastCrushed);
        }

        /// <summary>
        /// 扫描并打碎被压着的玻璃，碎掉的格子追加进 outCells（不清空它，调用方可能想攒多轮）。
        /// 返回这一轮碎了几格。
        /// </summary>
        public static int Crush(Grid grid, List<Vector3Int> outCells)
        {
            if (grid == null || !GameSettings.GlassCrushes)
                return 0;

            int glassId = BlockCatalog.GlassId;
            if (glassId <= 0)
                return 0;

            int count = 0;

            // 最上面一层上面不会有方块，少扫一排
            for (int y = 0; y < grid.SizeY - 1; y++)
            {
                for (int z = 0; z < grid.SizeZ; z++)
                {
                    for (int x = 0; x < grid.SizeX; x++)
                    {
                        if (grid.Get(x, y, z) != glassId)
                            continue;

                        int above = grid.Get(x, y + 1, z);
                        // 上面是空的，或者又是一块玻璃：都不算被压
                        if (above <= 0 || above == glassId)
                            continue;
                        // 先来后到：只有比这格玻璃更晚落上来的才算压着。
                        // 相等/更早的都是玻璃落定之前就在那儿的，不该把玻璃压碎。
                        if (grid.Stamp(x, y + 1, z) <= grid.Stamp(x, y, z))
                            continue;

                        grid.Set(x, y, z, 0);
                        count++;
                        if (outCells != null)
                            outCells.Add(new Vector3Int(x, y, z));
                    }
                }
            }

            return count;
        }
    }
}
