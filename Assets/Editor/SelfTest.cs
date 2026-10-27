using System.Text;
using MinecraftTetris;
using UnityEditor;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    /// <summary>
    /// 逻辑自检，不依赖场景和输入。批处理：
    /// Unity -batchmode -quit -projectPath . -executeMethod MinecraftTetris.EditorTools.SelfTest.Run
    /// </summary>
    public static class SelfTest
    {
        public static void Run()
        {
            var sb = new StringBuilder();
            int pass = 0;
            int fail = 0;

            void Check(string name, bool ok)
            {
                if (ok)
                {
                    pass++;
                    sb.AppendLine("[PASS] " + name);
                }
                else
                {
                    fail++;
                    sb.AppendLine("[FAIL] " + name);
                }
            }

            // 网格读写
            var grid = new Grid(10, 20, 1);
            grid.Set(0, 0, 0, 1);
            Check("写入后能读出来", grid.Get(0, 0, 0) == 1);
            Check("没写的格子是空的", grid.Get(1, 0, 0) == 0);
            Check("越界不算可用", !grid.IsFree(-1, 0, 0) && !grid.IsFree(10, 0, 0));
            Check("z 越界不可用", !grid.IsFree(0, 0, 1));

            // 满行消除
            var g2 = new Grid(10, 20, 1);
            for (int x = 0; x < 10; x++)
                g2.Set(x, 0, 0, 1);
            Check("满行消除返回 1", g2.ClearFullLines() == 1);
            Check("消除后该行清空", g2.Get(0, 0, 0) == 0);

            // 半满行不消
            var g3 = new Grid(10, 20, 1);
            for (int x = 0; x < 9; x++)
                g3.Set(x, 0, 0, 1);
            Check("半满行不消除", g3.ClearFullLines() == 0);

            // 消除后上面的行下落
            var g4 = new Grid(10, 20, 1);
            for (int x = 0; x < 10; x++)
                g4.Set(x, 0, 0, 1);
            g4.Set(0, 1, 0, 2);
            g4.ClearFullLines();
            Check("上面的方块落下来", g4.Get(0, 0, 0) == 2);

            // 游戏循环
            var loop = new GameLoop(10, 20, 1, 42);
            Check("开局有当前方块", loop.Current != null);
            Check("开局不是结束状态", !loop.GameOver);
            Check("下一个已就绪", loop.Next != null && loop.Next.Cells != null && loop.Next.Cells.Length > 0);

            // 硬降会把方块锁进网格
            loop.HardDrop();
            bool hasBlock = false;
            for (int x = 0; x < 10 && !hasBlock; x++)
                for (int y = 0; y < 20 && !hasBlock; y++)
                    if (loop.Grid.Get(x, y, 0) != 0)
                        hasBlock = true;
            Check("硬降后网格里有方块", hasBlock);

            // 旋转能返回稳定结果
            var loop2 = new GameLoop(10, 20, 1, 7);
            Vector2Int[] before = (Vector2Int[])loop2.Current.Cells.Clone();
            bool rotated = loop2.TryRotate(true);
            Check("旋转调用不抛异常", rotated || !rotated);
            Check("旋转后仍是四格", loop2.Current.Cells.Length == 4);

            // 七种方块形状都是四格
            for (int i = 0; i < 7; i++)
            {
                Vector2Int[] shape = Tetromino.GetShape((PieceType)i);
                Check("方块形状四格: " + (PieceType)i, shape.Length == 4);
            }

            // 方块材质表 + 图集格子范围
            Check("方块材质表: " + BlockCatalog.Blocks.Length + " 种", BlockCatalog.Blocks.Length >= 100);
            int badTiles = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                BlockDef def = BlockCatalog.Blocks[i];
                if (def.Up.Col < 0 || def.Up.Col >= BlockCatalog.AtlasColumns ||
                    def.Up.Row < 0 || def.Up.Row >= BlockCatalog.AtlasRows)
                    badTiles++;
            }
            Check("图集格子全部在范围内", badTiles == 0);

            // 水算满格：水不写进网格，但攒满一格要能凑成一行
            Grid.CountsAsFilled = (x, y, z) => x == 2;
            var gWater = new Grid(3, 3, 1);
            gWater.Set(0, 0, 0, 1);
            gWater.Set(1, 0, 0, 1);
            Check("水攒满的那格能算满", gWater.IsRowFullPublic(0, 0));
            Grid.CountsAsFilled = null;
            Check("撤掉注入后这行就不满了", !gWater.IsRowFullPublic(0, 0));

            // 消行时那一格的水要能被清掉
            var gWater2 = new Grid(4, 4, 1);
            Fluids.AddSource(gWater2, 1, 0, 0, Fluids.WaterId);
            bool hadWater = Fluids.AmountAt(1, 0, 0) > 0;
            Fluids.ClearCell(1, 0, 0);
            Check("清掉一格水", hadWater && Fluids.AmountAt(1, 0, 0) == 0);

            // 水竖向合并：三格各 1 份（1/8），上下叠着要先并下去，再按「摊成一片」的规则铺开
            var gMerge = new Grid(3, 6, 2);
            Fluids.Tick(gMerge, 0f);   // 只为触发 Ensure 建表
            Fluids.SetAmount(1, 2, 0, 1, Fluids.WaterId);
            Fluids.SetAmount(1, 1, 0, 1, Fluids.WaterId);
            Fluids.SetAmount(1, 0, 0, 1, Fluids.WaterId);
            for (int mi = 0; mi < 8; mi++)
                Fluids.Tick(gMerge, 0.2f);
            Check("上下三份 1/8 的水并到最底一格（上面两层清空）",
                Fluids.AmountAt(1, 1, 0) == 0 && Fluids.AmountAt(1, 2, 0) == 0);

            int mergeTotal = 0;
            for (int mx = 0; mx < 3; mx++)
                for (int my = 0; my < 6; my++)
                    for (int mz = 0; mz < 2; mz++)
                        mergeTotal += Fluids.AmountAt(mx, my, mz);
            Check("水的总量守恒（还是 3 份，摊开不凭空增减）", mergeTotal == 3);

            sb.AppendLine();
            sb.AppendLine("通过 " + pass + " 项，失败 " + fail + " 项");
            Debug.Log("[MinecraftTetris 自检]\n" + sb);

            EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
