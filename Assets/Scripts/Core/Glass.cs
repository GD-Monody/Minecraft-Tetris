using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 玻璃被压碎：玻璃正上方只要有方块压着，玻璃就碎。
    ///
    /// 判定规则（设置页的「玻璃被压碎」开关）：
    /// - 上方那格有方块就算压着，不管它是沙掉下来的、消行后整排落下来的，还是玩家刚摆上去的；
    /// - 碎掉之后上方的东西失去支撑，会下落的东西（沙、沙砾、混凝土粉末）接着掉，
    ///   于是可能再压到下面一层玻璃，见 GameLoop.CrushGlass 的循环；
    /// - 碎哪几格记在 LastCrushed 里，视图层照着播音效、撒碎块。
    ///
    /// 为什么是一遍场地扫描，而不是在每条「方块落地」的路径上埋判定：
    /// 方块落到玻璃上的来源太多（落定、重力、行消下移、层消下移、结构方块还原），
    /// 逐个埋点总会漏掉一条；默认场地 10×20×1 只有 200 格，扫一遍比维护五处重复判定便宜。
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
                        if (grid.Get(x, y + 1, z) <= 0)
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
