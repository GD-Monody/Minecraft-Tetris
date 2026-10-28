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
        /// <summary>整数点积（Vector3Int 在 2022.3 里没有 Dot）。</summary>
        static int Dot(Vector3Int a, Vector3Int b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

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

            // ---- 朝向：整数旋转矩阵 ----
            // 三个基本旋转的方向是一份「契约」：逻辑层的格子位置和视图层的四元数
            // 都从这几个矩阵来，改了这里两边一起变，不会出现"看起来转了但碰撞没转"。
            var probe = new Piece(PieceType.I, 1, 0, 0, 0);
            Check("新方块朝向是单位阵", probe.Rot.SameAs(PieceRotation.Identity));
            Check("朝向 0：I 型沿 x 展开", probe.CellOffset(3).x == 3 && probe.CellOffset(3).y == 0);

            PieceRotation yaw90 = PieceRotation.Identity.Rotate(RotationAxis.Yaw, true);
            Check("俯视 90°：+x 转到 +z",
                yaw90.X == new Vector3Int(0, 0, 1) && yaw90.Y == Vector3Int.up);

            PieceRotation pitch90 = PieceRotation.Identity.Rotate(RotationAxis.Pitch, true);
            Check("往背面翻 90°：+y 转到 +z（画面深处）",
                pitch90.Y == new Vector3Int(0, 0, 1) && pitch90.X == Vector3Int.right);

            PieceRotation roll90 = PieceRotation.Identity.Rotate(RotationAxis.Screen, true);
            Check("面内 90°：+x 转到 -y（和经典俄罗斯方块同向）",
                roll90.X == new Vector3Int(0, -1, 0) && roll90.Y == Vector3Int.right);

            // 转四次回原位；正反抵消
            PieceRotation spin = PieceRotation.Identity;
            for (int i = 0; i < 4; i++)
                spin = spin.Rotate(RotationAxis.Pitch, true);
            Check("往背面翻四次回到原朝向", spin.SameAs(PieceRotation.Identity));
            Check("顺时针再逆时针抵消",
                PieceRotation.Identity.Rotate(RotationAxis.Yaw, true)
                    .Rotate(RotationAxis.Yaw, false).SameAs(PieceRotation.Identity));

            // 三根轴可以叠加，结果仍是合法旋转（三轴两两垂直、右手/左手都成立的正交阵）
            PieceRotation mix = PieceRotation.Identity
                .Rotate(RotationAxis.Yaw, true)
                .Rotate(RotationAxis.Pitch, true);
            Check("俯视 + 前翻能叠加出第三种朝向",
                !mix.SameAs(yaw90) && !mix.SameAs(pitch90) && !mix.SameAs(PieceRotation.Identity));
            Check("叠加后三根轴仍然两两垂直",
                Dot(mix.X, mix.Y) == 0 && Dot(mix.Y, mix.Z) == 0 && Dot(mix.X, mix.Z) == 0 &&
                mix.X.magnitude == 1 && mix.Y.magnitude == 1 && mix.Z.magnitude == 1);

            // 三组键各转 0~3 次，应该正好覆盖立方体的 24 种朝向（多一个少一个都是错的）
            var seenRot = new System.Collections.Generic.HashSet<int>();
            for (int a = 0; a < 4; a++)
                for (int b = 0; b < 4; b++)
                    for (int c = 0; c < 4; c++)
                    {
                        PieceRotation r = PieceRotation.Identity;
                        for (int i = 0; i < a; i++) r = r.Rotate(RotationAxis.Screen, true);
                        for (int i = 0; i < b; i++) r = r.Rotate(RotationAxis.Yaw, true);
                        for (int i = 0; i < c; i++) r = r.Rotate(RotationAxis.Pitch, true);
                        seenRot.Add(r.Code);
                    }
            Check("三组键组合出全部 24 种朝向（实测 " + seenRot.Count + " 种）", seenRot.Count == 24);

            // 存档编码能原样还原
            bool codeOk = true;
            foreach (int code in seenRot)
            {
                if (!PieceRotation.FromCode(code).SameAs(PieceRotation.FromCode(code)))
                    codeOk = false;
            }
            PieceRotation roundTrip = PieceRotation.FromCode(mix.Code);
            Check("朝向编码存档往返一致", codeOk && roundTrip.SameAs(mix));

            // 视图层四元数必须和逻辑矩阵说的是同一件事
            bool quatOk = true;
            foreach (int code in seenRot)
            {
                PieceRotation r = PieceRotation.FromCode(code);
                Quaternion q = r.ToQuaternion();
                if ((q * Vector3.right - (Vector3)r.X).sqrMagnitude > 0.001f ||
                    (q * Vector3.up - (Vector3)r.Y).sqrMagnitude > 0.001f ||
                    (q * Vector3.forward - (Vector3)r.Z).sqrMagnitude > 0.001f)
                    quatOk = false;
            }
            Check("24 种朝向的四元数和逻辑矩阵完全一致", quatOk);

            // ---- 场地里转：转得动 / 转不动 ----
            // 深度只有 1 格时，俯视旋转和前后翻都必然失败（转过去要占 z ≥ 2）
            var shallow = new GameLoop(10, 20, 1, 5);
            Check("z=1 的场地转不动俯视旋转",
                !shallow.TryRotateYaw(true) && shallow.Current.Rot.SameAs(PieceRotation.Identity));
            Check("z=1 的场地也翻不动",
                !shallow.TryRotatePitch(true) && shallow.Current.Rot.SameAs(PieceRotation.Identity));

            // 够深：T 型（3 宽 2 高）往背面翻过去，应该躺平成「只占一层 y、沿 z 铺开两格」
            var deep = new GameLoop(10, 20, 6, 5);
            deep.Current = new Piece(PieceType.T, 1, 3, 10, 1);
            int[] cellsBefore = new int[8];
            for (int i = 0; i < 4; i++)
            {
                cellsBefore[i * 2] = deep.Current.CellOffset(i).x;
                cellsBefore[i * 2 + 1] = deep.Current.CellOffset(i).y;
            }

            bool pitched = deep.TryRotatePitch(true);
            Check("场地够深时能往背面翻 90°", pitched && !deep.Current.Rot.SameAs(PieceRotation.Identity));

            var ys = new System.Collections.Generic.HashSet<int>();
            var zs = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < deep.Current.Cells.Length; i++)
            {
                ys.Add(deep.Current.CellOffset(i).y);
                zs.Add(deep.Current.CellOffset(i).z);
            }
            Check("翻完躺平：四格都在同一层 y", ys.Count == 1);
            Check("翻完沿 z 铺开两格", zs.Count == 2);

            // 再翻回来，形状应该回到原样（位置可能被踢过一格，形状必须一致）
            deep.TryRotatePitch(false);
            bool shapeBack = deep.Current.Rot.SameAs(PieceRotation.Identity);
            for (int i = 0; shapeBack && i < 4; i++)
            {
                if (deep.Current.CellOffset(i).x != cellsBefore[i * 2] ||
                    deep.Current.CellOffset(i).y != cellsBefore[i * 2 + 1] ||
                    deep.Current.CellOffset(i).z != 0)
                    shapeBack = false;
            }
            Check("翻回去形状回到原样", shapeBack);

            // 俯视旋转：转完整块垂直于屏幕（四格同一个 x，沿 z 排开）
            var deep2 = new GameLoop(10, 20, 6, 9);
            deep2.Current = new Piece(PieceType.I, 1, 2, 10, 1);
            bool turned = deep2.TryRotateYaw(true);
            Check("场地够深时能俯视顺时针转 90°", turned);
            bool allSameX = turned;
            for (int i = 1; turned && i < deep2.Current.Cells.Length; i++)
            {
                if (deep2.Current.WorldCell(i).x != deep2.Current.WorldCell(0).x)
                    allSameX = false;
            }
            Check("俯视转 90° 后整块垂直于屏幕", allSameX);

            // 面内旋转不动朝向的"厚度"：还是 1 格厚
            var screen = new GameLoop(10, 20, 6, 13);
            screen.Current = new Piece(PieceType.I, 1, 2, 10, 1);
            bool rolled = screen.TryRotate(true);
            var screenZs = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; rolled && i < screen.Current.Cells.Length; i++)
                screenZs.Add(screen.Current.CellOffset(i).z);
            Check("面内旋转后仍然只有一格厚", rolled && screenZs.Count == 1);

            // 存档要保住完整朝向，否则「继续游戏」读回来方块会翻个身
            var saveLoop = new GameLoop(10, 20, 6, 17);
            saveLoop.TryRotateYaw(true);
            saveLoop.TryRotatePitch(true);
            PieceRotation savedRot = saveLoop.Current.Rot;
            var saveLoop2 = new GameLoop(10, 20, 6, 17);
            saveLoop2.RestoreState(saveLoop.SerializeState());
            Check("存档能保住完整朝向（不只是绕竖直的那一圈）",
                saveLoop2.Current.Rot.SameAs(savedRot) &&
                savedRot.SameAs(PieceRotation.Identity) == false);

            // ---- 下落速度：范围 (0, 2]，两位小数 ----
            Check("下落速度下限吃掉 0 和负数", GameSettings.ClampFallSpeed(0f) == 0.01f &&
                GameSettings.ClampFallSpeed(-3f) == 0.01f);
            Check("下落速度上限是 2（闭区间）", GameSettings.ClampFallSpeed(2f) == 2f &&
                GameSettings.ClampFallSpeed(9f) == 2f);
            Check("下落速度保留两位小数", GameSettings.ClampFallSpeed(1.234f) == 1.23f &&
                GameSettings.ClampFallSpeed(0.8765f) == 0.88f);
            Check("下落速度默认 1.00", GameSettings.DefaultFallSpeed == 1f);

            var speedLoop = new GameLoop(10, 20, 6, 21);
            Check("默认速度下每 0.8 秒落一格", Mathf.Abs(speedLoop.FallInterval - 0.8f) < 0.0001f);
            speedLoop.FallSpeed = 2f;
            Check("2 倍速 -> 0.4 秒一格", Mathf.Abs(speedLoop.FallInterval - 0.4f) < 0.0001f);
            speedLoop.FallSpeed = 0.5f;
            Check("0.5 倍速 -> 1.6 秒一格", Mathf.Abs(speedLoop.FallInterval - 1.6f) < 0.0001f);
            speedLoop.FallSpeed = 0f;
            Check("传 0 会被夹到下限 0.01（不会永远不掉）",
                speedLoop.FallSpeed == 0.01f && speedLoop.FallInterval > 10f);

            // ---- 流体：不管方块怎么转，水面必须水平 ----
            Quaternion tilted = Quaternion.Euler(37f, 128f, 254f);
            Check("流体格子一律不转（水的 up 还是竖直的）",
                (GridView.CellRotation(true, tilted) * Vector3.up - Vector3.up).sqrMagnitude < 0.001f);
            Check("固体格子照旧保留朝向",
                Quaternion.Angle(GridView.CellRotation(false, tilted), tilted) < 0.01f);

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

            // 放置音效：按 MC 原版材质分组，数据由 tools/build_place_sfx.py 生成
            Check("音效分组数和方块表对齐",
                BlockSfxData.BlockGroup.Length == BlockCatalogData.Names.Length);

            var groupSet = new System.Collections.Generic.HashSet<string>(BlockSfxData.Groups);
            int unknownGroup = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string group = BlockCatalog.Blocks[i].PlaceSfxGroup;
                if (group == null || !groupSet.Contains(group))
                    unknownGroup++;
            }
            Check("每个方块都有合法的音效组", unknownGroup == 0);

            int emptyGroup = 0;
            int totalClips = 0;
            int missingClip = 0;
            for (int g = 0; g < BlockSfxData.GroupCount; g++)
            {
                string[] clips = BlockSfxData.GroupClips[g];
                if (clips == null || clips.Length == 0)
                {
                    emptyGroup++;
                    continue;
                }
                totalClips += clips.Length;
                for (int i = 0; i < clips.Length; i++)
                {
                    if (!System.IO.File.Exists("Assets/Resources/Sfx/Place/" + clips[i] + ".ogg"))
                        missingClip++;
                }
            }
            Check("每个音效组都有候选 clip（共 " + totalClips + " 个）", emptyGroup == 0);
            Check("clip 文件都在 Sfx/Place 下", missingClip == 0);
            Check("每组音量表和组数对齐",
                BlockSfxData.GroupVolume.Length == BlockSfxData.GroupCount);

            // 文件在磁盘上 ≠ Resources 拿得到，抽样真正加载一遍（281 个全加载太慢）
            int probeCount = 0;
            int probeBad = 0;
            for (int g = 0; g < BlockSfxData.GroupCount; g += 7)
            {
                string[] clips = BlockSfxData.GroupClips[g];
                if (clips == null || clips.Length == 0)
                    continue;
                probeCount++;
                if (Resources.Load<AudioClip>("Sfx/Place/" + clips[0]) == null)
                    probeBad++;
            }
            Check("抽样 " + probeCount + " 组放置音能真正加载", probeCount > 0 && probeBad == 0);

            // 抽查几个典型方块，确认分到了原版对应的组
            System.Func<string, string> groupOf = delegate(string n)
            {
                int idx = System.Array.IndexOf(BlockCatalogData.Names, n);
                return idx < 0 ? "<没有这个方块>" : BlockSfxData.BlockGroup[idx];
            };
            Check("橡木木板 -> 木头音", groupOf("oak_planks") == "wood");
            Check("石头 -> 石头音", groupOf("stone") == "stone");
            Check("羊毛 -> 布的音", groupOf("black_wool") == "wool");
            Check("沙子 -> 沙子音", groupOf("sand") == "sand");
            Check("TNT -> 草的音（原版如此）", groupOf("tnt") == "grass");
            Check("黑曜石 -> 石头音", groupOf("obsidian") == "stone");
            Check("水 -> 倒桶音", groupOf("water") == "fluid_water");
            Check("岩浆 -> 倒桶音", groupOf("lava") == "fluid_lava");

            // 流体要用的方块编号（水、岩浆、黑曜石、原石）在方块表里查一次
            Fluids.Init();
            Check("方块表里能找到水和岩浆", Fluids.WaterId > 0 && Fluids.LavaId > 0);
            Check("方块表里能找到黑曜石和原石", Fluids.ObsidianId > 0 && Fluids.CobbleId > 0);

            // 水位的渲染块确实只有水层那么高（正方体转了看不出来，矮方块一转身就变竖墙）
            BlockDef waterDef = BlockCatalog.Blocks[Fluids.WaterId - 1];
            Mesh slab = BlockMesh.BuildFluid(waterDef, 1f / Fluids.MaxAmount);
            Check("1 份水的渲染块只有 1/8 高",
                Mathf.Abs(slab.bounds.size.y - 1f / Fluids.MaxAmount) < 0.02f
                && slab.bounds.size.x > 0.9f);
            Object.DestroyImmediate(slab);

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

            // 水岩浆成石：满格岩浆是「源」→ 黑曜石；流动岩浆 → 原石；参与反应的水整格被消耗
            var gLava = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gLava);
            Fluids.AddSource(gLava, 1, 0, 0, Fluids.LavaId);    // 满 8 份
            Fluids.SetAmount(2, 0, 0, 4, Fluids.WaterId);       // 旁边 4 份水
            Fluids.Tick(gLava, 0.2f);
            Check("满格岩浆遇水变黑曜石", gLava.Get(1, 0, 0) == Fluids.ObsidianId);
            Check("反应后那格水被消耗掉", Fluids.AmountAt(2, 0, 0) == 0);
            Check("反应后岩浆那格不剩流体", Fluids.AmountAt(1, 0, 0) == 0);

            var gLava2 = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gLava2);
            Fluids.SetAmount(1, 0, 0, 3, Fluids.LavaId);        // 只有 3 份 = 流动岩浆
            Fluids.SetAmount(1, 1, 0, 2, Fluids.WaterId);       // 正上方压着水
            Fluids.Tick(gLava2, 0.2f);
            Check("流动岩浆遇水变原石", gLava2.Get(1, 0, 0) == Fluids.CobbleId);

            var gLava3 = new Grid(5, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gLava3);
            Fluids.AddSource(gLava3, 0, 0, 0, Fluids.LavaId);
            gLava3.Set(1, 0, 0, Fluids.CobbleId);               // 中间隔一格石头
            Fluids.SetAmount(2, 0, 0, 8, Fluids.WaterId);
            Fluids.Tick(gLava3, 0.2f);
            Check("隔着一格石头就不反应", gLava3.Get(0, 0, 0) == 0);

            // 水方块直接落进岩浆那格：同格相遇也要结晶，不能把岩浆悄悄覆盖掉
            var gLava4 = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gLava4);
            Fluids.AddSource(gLava4, 1, 0, 0, Fluids.LavaId);
            int made = Fluids.AddSource(gLava4, 1, 0, 0, Fluids.WaterId);
            Check("水落进岩浆那格当场变黑曜石",
                made == Fluids.ObsidianId && gLava4.Get(1, 0, 0) == Fluids.ObsidianId);
            Check("同格结晶后不剩流体", Fluids.AmountAt(1, 0, 0) == 0);

            // TNT：红石接触点火 → 引信 → 爆炸 → 连锁
            Tnt.Init();
            Check("方块表里能找到 TNT 和红石块", Tnt.TntId > 0 && Tnt.RedstoneId > 0);
            Check("TNT 不在普通随机池里（按件数控制出现频率）",
                System.Array.IndexOf(BlockPools.Normal, Tnt.TntId) < 0);
            Check("红石块仍在普通池里（偶尔自然出现也能点火）",
                System.Array.IndexOf(BlockPools.Normal, Tnt.RedstoneId) >= 0);

            int stoneId = 1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "stone")
                {
                    stoneId = i + 1;
                    break;
                }
            }

            var gTnt0 = new Grid(6, 6, 1);
            gTnt0.Set(1, 1, 0, Tnt.TntId);
            Tnt.Reset();
            Check("没挨着红石的 TNT 不会自己烧起来",
                Tnt.PrimeContact(gTnt0) == 0 && Tnt.PrimedCells.Count == 0);

            var gTnt = new Grid(10, 20, 1);
            for (int dx = -3; dx <= 3; dx++)
            {
                for (int dy = -3; dy <= 3; dy++)
                {
                    if (dx != 0 || dy != 0)
                        gTnt.Set(4 + dx, 5 + dy, 0, stoneId);
                }
            }
            gTnt.Set(4, 5, 0, Tnt.TntId);
            gTnt.Set(5, 5, 0, Tnt.RedstoneId);
            Tnt.Reset();
            Check("TNT 和红石挨着就点火", Tnt.PrimeContact(gTnt) == 1 && Tnt.PrimedCells.Count == 1);
            Check("点着之后那一格还是 TNT（原地烧引信）", gTnt.Get(4, 5, 0) == Tnt.TntId);
            Check("引信进度从 0 开始", Mathf.Abs(Tnt.FuseProgress(4, 5, 0)) < 0.01f);

            // 数一段时间里闪白翻转了几次（1 ms 步长采样，够精确到 0.25 秒的边界）
            int CountFlashes(float seconds)
            {
                int flips = 0;
                int steps = Mathf.RoundToInt(seconds * 1000f);
                bool prev = Tnt.FlashOn(0f);
                for (int ms = 1; ms <= steps; ms++)
                {
                    bool now = Tnt.FlashOn(ms / 1000f);
                    if (now != prev)
                    {
                        flips++;
                        prev = now;
                    }
                }
                return flips;
            }

            // 闪白节奏：照原版 TntRenderer 的 fuse/5%2，每 0.25 秒翻一次，点火瞬间是白
            Check("闪白半周期是 0.25 秒（原版 5 tick）", Mathf.Approximately(Tnt.FlashHalfPeriod, 0.25f));
            Check("点火瞬间是白的", Tnt.FlashOn(0f) && Tnt.FlashOn(0.24f));
            Check("0.25 秒后翻成原色", !Tnt.FlashOn(0.25f) && !Tnt.FlashOn(0.49f));
            Check("0.5 秒后又是白的", Tnt.FlashOn(0.5f) && Tnt.FlashOn(0.74f));
            Check("一秒内只翻 4 次（不是每秒闪十几次）",
                CountFlashes(1f) == 4 && CountFlashes(2.4f) == 9);

            Check("引信没烧完不爆炸",
                !Tnt.Tick(gTnt, Tnt.FuseSeconds * 0.4f) && gTnt.Get(4, 5, 0) == Tnt.TntId);
            Check("引信烧完爆炸", Tnt.Tick(gTnt, Tnt.FuseSeconds) && Tnt.LastBlasts.Count == 1);
            Check("爆心那格被清空", gTnt.Get(4, 5, 0) == 0);
            Check("半径内的方块被炸掉", gTnt.Get(5, 5, 0) == 0 && gTnt.Get(4, 6, 0) == 0);
            Check("半径外的方块留着", gTnt.Get(7, 5, 0) == stoneId);

            // 连锁：第一颗炸到第二颗，第二颗被点着而不是直接消失
            var gChain = new Grid(10, 20, 1);
            gChain.Set(2, 5, 0, Tnt.TntId);
            gChain.Set(3, 5, 0, Tnt.TntId);
            gChain.Set(2, 6, 0, Tnt.RedstoneId);
            Tnt.Reset();
            Check("连锁测试：只有挨着红石的那颗先烧", Tnt.PrimeContact(gChain) == 1);
            Tnt.Tick(gChain, Tnt.FuseSeconds);
            Check("爆炸把隔壁的 TNT 点着（连锁）", Tnt.PrimedCells.Count == 1 && gChain.Get(3, 5, 0) == Tnt.TntId);
            Tnt.Tick(gChain, 1f);
            Check("连锁的第二颗也炸了", Tnt.LastBlasts.Count >= 1 && gChain.Get(3, 5, 0) == 0);

            // 哑火：点着的 TNT 被消行带走之后不能在原坐标炸
            var gDead = new Grid(6, 6, 1);
            gDead.Set(2, 2, 0, Tnt.TntId);
            gDead.Set(3, 2, 0, Tnt.RedstoneId);
            Tnt.Reset();
            Tnt.PrimeContact(gDead);
            gDead.Set(2, 2, 0, 0);
            Tnt.Tick(gDead, 0.05f);
            Check("被消行带走的 TNT 会哑火", Tnt.PrimedCells.Count == 0);
            Tnt.Reset();

            // 炸不掉的方块：基岩 / 黑曜石 / 哭泣的黑曜石（原版 TNT 也炸不动它们）
            int bedrockForBlast = 0, obsidianId = 0, cryingObsidianId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "bedrock") bedrockForBlast = i + 1;
                else if (n == "obsidian") obsidianId = i + 1;
                else if (n == "crying_obsidian") cryingObsidianId = i + 1;
            }
            Check("黑曜石和哭泣的黑曜石都在方块表里", obsidianId > 0 && cryingObsidianId > 0);
            Check("基岩炸不掉", BlockCatalog.IsBlastProof(bedrockForBlast));
            Check("黑曜石炸不掉", BlockCatalog.IsBlastProof(obsidianId));
            Check("哭泣的黑曜石炸不掉", BlockCatalog.IsBlastProof(cryingObsidianId));
            Check("普通方块炸得掉", !BlockCatalog.IsBlastProof(stoneId));
            Check("空格子不算炸不掉", !BlockCatalog.IsBlastProof(0));

            var gBlast = new Grid(10, 20, 1);
            gBlast.Set(4, 5, 0, Tnt.TntId);
            gBlast.Set(5, 5, 0, Tnt.RedstoneId);
            gBlast.Set(3, 5, 0, obsidianId);        // 爆炸半径内
            gBlast.Set(4, 6, 0, stoneId);            // 爆炸半径内
            gBlast.Set(4, 3, 0, cryingObsidianId);  // 爆炸半径边缘
            Tnt.Reset();
            Tnt.PrimeContact(gBlast);
            Tnt.Tick(gBlast, Tnt.FuseSeconds);
            Check("爆炸后黑曜石还在", gBlast.Get(3, 5, 0) == obsidianId);
            Check("爆炸后哭泣的黑曜石还在", gBlast.Get(4, 3, 0) == cryingObsidianId);
            Check("爆炸后普通方块照炸", gBlast.Get(4, 6, 0) == 0);
            Tnt.Reset();

            // 原木长树叶
            Foliage.Init();
            int oakLog = -1, oakLeaves = -1, strippedOak = -1, crimsonStem = -1, bamboo = -1;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "oak_log")
                    oakLog = i + 1;
                else if (n == "oak_leaves")
                    oakLeaves = i + 1;
                else if (n == "stripped_oak_log")
                    strippedOak = i + 1;
                else if (n == "crimson_stem")
                    crimsonStem = i + 1;
                else if (n == "bamboo_block")
                    bamboo = i + 1;
            }

            Check("方块表里有橡树原木和橡树树叶", oakLog > 0 && oakLeaves > 0);
            Check("橡树原木长出的是橡树树叶", Foliage.LeafIdFor(oakLog) == oakLeaves);
            Check("剥皮原木不长树叶", strippedOak > 0 && Foliage.LeafIdFor(strippedOak) == 0);
            Check("菌柄不长树叶", crimsonStem > 0 && Foliage.LeafIdFor(crimsonStem) == 0);
            Check("竹块不长树叶", bamboo > 0 && Foliage.LeafIdFor(bamboo) == 0);
            Check("普通方块一律不长", Foliage.LeafIdFor(stoneId) == 0);
            Check("树叶不进随机材质池（不会整块掉下来）",
                System.Array.IndexOf(BlockPools.Normal, oakLeaves) < 0 &&
                System.Array.IndexOf(BlockPools.Special, oakLeaves) < 0);

            var gLeaf = new Grid(9, 9, 1);
            gLeaf.Set(4, 4, 0, oakLog);
            Foliage.LastGrown.Clear();
            int grew = Foliage.GrowAround(gLeaf, 4, 4, 0, oakLog);
            Check("空地上长出 1 片树叶", grew == 1 && Foliage.LastGrown.Count == 1);
            Vector3Int leafPos = Foliage.LastGrown[0];
            Check("树叶挨着木头、不是长在自身那格",
                gLeaf.Get(leafPos.x, leafPos.y, leafPos.z) == oakLeaves &&
                Mathf.Max(Mathf.Abs(leafPos.x - 4), Mathf.Abs(leafPos.y - 4)) == 1);

            var gFull = new Grid(5, 5, 1);
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                    gFull.Set(x, y, 0, stoneId);
            }
            gFull.Set(2, 2, 0, oakLog);
            Check("周围没有空地就不长", Foliage.GrowAround(gFull, 2, 2, 0, oakLog) == 0);

            var gCorner = new Grid(3, 3, 1);
            gCorner.Set(0, 0, 0, oakLog);
            Check("贴着场地左下角也不越界", Foliage.GrowAround(gCorner, 0, 0, 0, oakLog) == 1);

            // 整块结算：先把木头写进网格（真实流程里落定时已经写好了）
            var gPiece = new Grid(9, 9, 1);
            var logPiece = new Piece(PieceType.I, oakLog, 2, 2, 0);
            for (int i = 0; i < logPiece.Cells.Length; i++)
            {
                Vector3Int c = logPiece.WorldCell(i);
                gPiece.Set(c.x, c.y, c.z, oakLog);
            }
            Check("一块 4 格原木长出 4 片树叶", Foliage.Grow(gPiece, logPiece) == 4);

            bool distinct = true;
            for (int i = 0; i < Foliage.LastGrown.Count && distinct; i++)
            {
                for (int j = i + 1; j < Foliage.LastGrown.Count; j++)
                {
                    if (Foliage.LastGrown[i] == Foliage.LastGrown[j])
                    {
                        distinct = false;
                        break;
                    }
                }
            }
            Check("每片叶子落在不同的格子里", distinct && Foliage.LastGrown.Count == 4);

            // 大模具不会一次刷出一整面叶子墙：一次落定有上限
            var bigCells = new Vector2Int[16];
            for (int i = 0; i < 16; i++)
                bigCells[i] = new Vector2Int(i % 4, i / 4);
            var gMold = new Grid(12, 12, 1);
            var moldPiece = new Piece(bigCells, "big", PieceType.I, oakLog, 3, 3, 0);
            for (int i = 0; i < moldPiece.Cells.Length; i++)
            {
                Vector3Int c = moldPiece.WorldCell(i);
                gMold.Set(c.x, c.y, c.z, oakLog);
            }
            Check("16 格的大模具也只长 " + Foliage.MaxLeavesPerLock + " 片",
                Foliage.Grow(gMold, moldPiece) == Foliage.MaxLeavesPerLock);

            // 不是原木的方块整块结算什么都不长
            var gStone = new Grid(9, 9, 1);
            var stonePiece = new Piece(PieceType.O, stoneId, 2, 2, 0);
            for (int i = 0; i < stonePiece.Cells.Length; i++)
            {
                Vector3Int c = stonePiece.WorldCell(i);
                gStone.Set(c.x, c.y, c.z, stoneId);
            }
            Check("石头方块不会长叶子",
                Foliage.Grow(gStone, stonePiece) == 0 && Foliage.LastGrown.Count == 0);

            // 基岩：消除时钉在原地不跟着下落，但自己被消掉时照样消失
            int bedrockId = BlockCatalog.BedrockId;
            Check("方块表里有基岩", bedrockId > 0 && BlockCatalog.Of(bedrockId).Name == "bedrock");
            Check("基岩不算重力方块（不会自己塌）", !Gravity.IsFalling(bedrockId));

            // 行消：底层铺满，左列第 1 格是基岩、上面压两块石头；右列一样高但没有基岩
            var gBed = new Grid(5, 7, 1);
            for (int x = 0; x < 5; x++)
                gBed.Set(x, 0, 0, stoneId);
            gBed.Set(0, 1, 0, bedrockId);
            gBed.Set(0, 2, 0, stoneId);
            gBed.Set(0, 3, 0, stoneId);
            gBed.Set(4, 1, 0, stoneId);
            gBed.Set(4, 2, 0, stoneId);
            gBed.Set(4, 3, 0, stoneId);

            Check("底层填满时判为可消行", gBed.IsRowFullPublic(0, 0));
            Check("底层确实消掉了一行", gBed.ClearFullRows() == 1);
            Check("基岩没跟着下落", gBed.Get(0, 1, 0) == bedrockId);
            Check("基岩上面的方块被它托住",
                gBed.Get(0, 2, 0) == stoneId && gBed.Get(0, 3, 0) == stoneId);
            Check("基岩下方留出了空位（它悬空了）", gBed.Get(0, 0, 0) == 0);
            Check("没有基岩的那一列整体下移一格",
                gBed.Get(4, 0, 0) == stoneId && gBed.Get(4, 1, 0) == stoneId && gBed.Get(4, 2, 0) == stoneId);
            Check("下移后那一列顶上留空", gBed.Get(4, 3, 0) == 0);

            // 基岩自己所在的行被消：它该一起消失，上面的方块落下来填坑
            var gBedGone = new Grid(5, 4, 1);
            for (int x = 0; x < 5; x++)
                gBedGone.Set(x, 0, 0, x == 2 ? bedrockId : stoneId);
            gBedGone.Set(2, 1, 0, stoneId);
            Check("含基岩的满行照样能消", gBedGone.ClearFullRows() == 1);
            Check("基岩被消掉之后不留残余", gBedGone.Get(2, 0, 0) == stoneId);
            Check("基岩原来的位置由上方方块补上", gBedGone.Get(2, 1, 0) == 0);

            // 层消：同一套规则
            var gBedLayer = new Grid(3, 6, 2);
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 2; z++)
                    gBedLayer.Set(x, 0, z, stoneId);
            gBedLayer.Set(0, 1, 0, bedrockId);
            gBedLayer.Set(0, 2, 0, stoneId);
            gBedLayer.Set(2, 1, 1, stoneId);
            Check("整层填满时判为可消层", gBedLayer.IsLayerFullPublic(0));
            Check("层消清掉了一层", gBedLayer.ClearFullLayers() == 1);
            Check("层消时基岩也不下落", gBedLayer.Get(0, 1, 0) == bedrockId);
            Check("层消时基岩上方被托住", gBedLayer.Get(0, 2, 0) == stoneId);
            Check("层消时没有基岩的那列下落一格", gBedLayer.Get(2, 0, 1) == stoneId);

            // 沙子落在基岩上要停住，不能穿过去
            // 沙子放 y=4 而不是 y=5：Gravity.Settle 只扫 y < SizeY-1，场地最顶格不参与
            int sandId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                if (BlockCatalog.Blocks[i].Name == "sand")
                {
                    sandId = i + 1;
                    break;
                }
            }
            var gBedSand = new Grid(3, 6, 1);
            gBedSand.Set(1, 0, 0, bedrockId);
            gBedSand.Set(1, 4, 0, sandId);
            Gravity.Settle(gBedSand);
            Check("沙子落到基岩上就停住", gBedSand.Get(1, 1, 0) == sandId && gBedSand.Get(1, 0, 0) == bedrockId);

            // 存档：基岩编号就是个普通 int，来回一趟不能变
            var gBedSave = new Grid(4, 4, 1);
            gBedSave.Set(2, 1, 0, bedrockId);
            gBedSave.Set(1, 1, 0, stoneId);
            var gBedLoad = new Grid(4, 4, 1);
            gBedLoad.Import(gBedSave.Export());
            Check("基岩能跟着存档来回", gBedLoad.Get(2, 1, 0) == bedrockId && gBedLoad.Get(1, 1, 0) == stoneId);
            Check("基岩不进普通 / 特殊材质池",
                System.Array.IndexOf(BlockPools.Normal, bedrockId) < 0 &&
                System.Array.IndexOf(BlockPools.Special, bedrockId) < 0);

            // 背景音乐：文件齐、是流式导入、不是空文件，音量换算对
            int missingMusic = 0;
            for (int i = 0; i < MusicPlayer.GameTrackPaths.Length; i++)
            {
                if (Resources.Load<AudioClip>(MusicPlayer.GameTrackPaths[i]) == null)
                    missingMusic++;
            }
            Check("游戏曲库 " + MusicPlayer.GameTrackPaths.Length + " 首全在", missingMusic == 0);
            for (int i = 0; i < MusicPlayer.MenuTrackPaths.Length; i++)
            {
                if (Resources.Load<AudioClip>(MusicPlayer.MenuTrackPaths[i]) == null)
                    missingMusic++;
            }
            Check("菜单曲库也在", missingMusic == 0);

            AudioClip song = Resources.Load<AudioClip>(MusicPlayer.GameTrackPaths[0]);
            Check("曲子不是空文件（时长 > 60 秒）", song != null && song.length > 60f);
            Check("音乐按流式导入（不整首解码进内存）",
                song != null && song.loadType == AudioClipLoadType.Streaming);

            Check("音乐音量夹到 0~1 且保留两位小数",
                Mathf.Approximately(GameSettings.ClampMusicVolume(0.456f), 0.46f) &&
                Mathf.Approximately(GameSettings.ClampMusicVolume(-1f), 0f) &&
                Mathf.Approximately(GameSettings.ClampMusicVolume(9f), 1f));

            // UI 字体与字号放大
            Font font = Resources.Load<Font>(UiSkin.FontPath);
            Check("内置字体 Resources/" + UiSkin.FontPath + " 能加载", font != null);
            Check("字体是动态字体（中文按需光栅化）", font != null && font.dynamic);
            Check("字号放大倍数生效", Mathf.Approximately(UiSkin.P(40f), 40f * UiSkin.TextScale));
            Check("字号取整按倍数走", UiSkin.Size(18) == Mathf.RoundToInt(18f * UiSkin.TextScale));

            // 主菜单按钮放大后的排布：目标 2 倍，装不下要自动降，且绝不能越过下边界
            float mw = UiSkin.P(280f), mh = UiSkin.P(44f), mg = UiSkin.P(10f);

            MenuLayout lay6 = MenuLayout.Compute(6, mw, mh, mg, 300f, 810f, 1238f, UiSkin.MenuScale);
            Check("6 个按钮的总高度不越过可用带", lay6.BottomY <= 810f + 0.5f);
            Check("1440×900 塞不下 2 倍时自动降档", lay6.scale < UiSkin.MenuScale);
            Check("降档后仍比现在大", lay6.scale > 1f);
            Check("按钮宽度不超过给定上限", lay6.width <= 1238f + 0.5f);
            Check("按钮整体垂直居中", Mathf.Abs((lay6.startY - 300f) - (810f - lay6.BottomY)) <= 0.5f);

            MenuLayout lay5Big = MenuLayout.Compute(5, mw, mh, mg, 346f, 990f, 1651f, UiSkin.MenuScale);
            Check("1080p 下 5 个按钮接近 2 倍",
                lay5Big.scale > 1.9f && lay5Big.scale <= UiSkin.MenuScale + 0.001f);

            MenuLayout tiny = MenuLayout.Compute(6, mw, mh, mg, 300f, 320f, 400f, UiSkin.MenuScale);
            Check("极小窗口有缩放下限，不会算出 0 或负数",
                tiny.scale >= 0.5f && tiny.width > 0f && tiny.height > 0f && tiny.gap >= 0f);

            sb.AppendLine();
            sb.AppendLine("通过 " + pass + " 项，失败 " + fail + " 项");
            Debug.Log("[MinecraftTetris 自检]\n" + sb);

            EditorApplication.Exit(fail > 0 ? 1 : 0);
        }
    }
}
