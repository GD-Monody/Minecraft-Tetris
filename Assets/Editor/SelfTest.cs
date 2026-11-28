using System.Collections.Generic;
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
            // 自检的每一节都需要对应机制是「开」的（有的节自己开关并还原，
            // 但只要有一节忘了还原，后面的节就会静默失败）。批量跑的时候
            // 用的是全新注册表，干脆开头把全部机制打开，跑完也不还原 ——
            // batchmode 跑完就退出，不会污染玩家的设置。
            GameSettings.SetAllMech(true);
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

            // 朝向要跟着方块走。这一条是给「幽匿催化剂每次下落都换一张脸」钉的：
            // 朝向只存在格位上的话，方块掉进下一格就会继承上一任的朝向，
            // 它的顶面（深色带荧光点）跑到正面，看着就是材质变了。
            int rotA = PieceRotation.FromYaw(1).Code;
            int rotB = PieceRotation.FromYaw(2).Code;
            var gRot = new Grid(4, 6, 1);
            for (int x = 0; x < 4; x++)
                gRot.Set(x, 0, 0, 1);
            gRot.Set(1, 2, 0, 2);
            gRot.SetRotationCode(1, 2, 0, rotA);
            gRot.Set(2, 3, 0, 3);
            gRot.SetRotationCode(2, 3, 0, rotB);
            gRot.ClearFullLines();
            Check("消行后方块还在原位上方", gRot.Get(1, 1, 0) == 2 && gRot.Get(2, 2, 0) == 3);
            Check("消行后朝向跟着方块走（不会串到下一格）",
                gRot.RotationCode(1, 1, 0) == rotA && gRot.RotationCode(2, 2, 0) == rotB);
            Check("被消掉那一格的朝向归零",
                gRot.RotationCode(1, 0, 0) == PieceRotation.Identity.Code);

            // 重力 / 消行搬格子走的都是 Move，朝向也得跟着搬
            var gMove = new Grid(4, 6, 1);
            gMove.Set(1, 3, 0, 2);
            gMove.SetRotationCode(1, 3, 0, rotB);
            gMove.Move(1, 3, 0, 1, 2, 0);
            Check("搬格子时编号和朝向一起走",
                gMove.Get(1, 2, 0) == 2 && gMove.RotationCode(1, 2, 0) == rotB);
            Check("搬走的源格清空且朝向归零",
                gMove.Get(1, 3, 0) == 0 &&
                gMove.RotationCode(1, 3, 0) == PieceRotation.Identity.Code);

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
            Vector3Int[] before = (Vector3Int[])loop2.Current.Cells.Clone();
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
            // 俯视顺时针：从上往下看，+x（屏幕右）应该转到 -z（朝玩家这边），
            // 原来断言的是 +z，方向反了 —— 游戏里表现为「按顺时针键转出来是逆时针」。
            Check("俯视 90°（顺时针）：+x 转到 -z",
                yaw90.X == new Vector3Int(0, 0, -1) && yaw90.Y == Vector3Int.up);

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
            // 钉一块标准的 T：出块池洗手后有可能会先甩一个自定义模具出来，那种模具
            // 可能整个 y 只有一行（比如默认清单里的「五连条」），前后翻过去仍然只占一层 z，
            // 在 z=1 的场地里就转得动 —— 这条断言就随机翻脸了。它是测「深度不够时转不动」，
            // 不该被出块顺序绑架，所以把方块固定下来。
            var shallow = new GameLoop(10, 20, 1, 5);
            shallow.Current = new Piece(PieceType.T, 1, 3, 10, 0);
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

            // ---- 创意工坊：三维模具 ----
            Check("旧存档 \"x,y\" 缺的那一维补 0",
                MoldData.Parse("1,2").Length == 1 && MoldData.Parse("1,2")[0] == new Vector3Int(1, 2, 0));
            Check("新存档 \"x,y,z\" 保住深度",
                MoldData.Parse("1,2,3")[0] == new Vector3Int(1, 2, 3));
            Check("同一个格子写两遍只留一格", MoldData.Parse("1,1,0;1,1,0").Length == 1);
            Check("坏片段跳过，好片段照留", MoldData.Parse("1,1,0;x;2,1,0").Length == 2);
            Check("编码再解回来是一模一样的形状",
                MoldData.Parse(MoldData.Encode(new[] { new Vector3Int(1, 0, 2), new Vector3Int(3, 4, 0) }))
                    .Length == 2);

            Check("空模具不给存", MoldData.Validate(new Vector3Int[0]) != null);
            Check("超过格数上限不给存",
                MoldData.Validate(new[]
                {
                    new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0),
                    new Vector3Int(3, 0, 0), new Vector3Int(4, 0, 0), new Vector3Int(0, 1, 0),
                    new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector3Int(3, 1, 0),
                }) != null);
            Check("出建造盒的格子不给存",
                MoldData.Validate(new[] { new Vector3Int(0, 0, 0), new Vector3Int(MoldData.Size, 0, 0) }) != null);
            Check("挨在一起的两格连通",
                MoldData.IsConnected(new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) }));
            // 只对角碰上的两格不算连通：它们之间没有公共面，一旋转就散架
            Check("只有角挨着的两格不算连通",
                !MoldData.IsConnected(new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 1, 0) }));
            Check("隔着一层空气的两格在 z 上也不算连通",
                !MoldData.IsConnected(new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 2) }));
            Check("两截不连通的模具存不下来",
                MoldData.Validate(new[]
                {
                    new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0),
                    new Vector3Int(3, 0, 0), new Vector3Int(4, 0, 0),
                }) != null);
            Check("深度跨度按占了几层算",
                MoldData.SpanZ(new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 0, 2) }) == 3 &&
                MoldData.SpanZ(new[] { new Vector3Int(0, 0, 1), new Vector3Int(0, 0, 1) }) == 1);
            Check("归一化把最小角挪到三条轴的原点",
                PieceBag.Normalize(new[] { new Vector3Int(2, 3, 4), new Vector3Int(3, 3, 4) })[0]
                    == Vector3Int.zero);

            // 三维形状真的能装进三维场地：跨两层的 L 形，四次转回来还得是它自己
            var deepCells = new[]
            {
                new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 0, 1),
            };
            var deepMold = new Piece(deepCells, "深L", PieceType.I, 1, 2, 2, 1);
            Check("三维模具每格都落在三维方向上", deepMold.CellOffset(2).z == 1);
            int justify = 0;
            for (int i = 0; i < deepMold.Cells.Length; i++)
                if (deepMold.CellOffset(i).x >= 0 && deepMold.CellOffset(i).y >= 0 && deepMold.CellOffset(i).z >= 0)
                    justify++;
            Check("朝向矩阵把整块摆到非负（原点就是最小角）", justify == deepMold.Cells.Length);
            deepMold.Rot = deepMold.Rot.Rotate(RotationAxis.Yaw, true)
                .Rotate(RotationAxis.Yaw, true)
                .Rotate(RotationAxis.Yaw, true)
                .Rotate(RotationAxis.Yaw, true);
            Check("绕竖直轴转四次回到原来的样子", deepMold.Rot.SameAs(PieceRotation.Identity));

            // ---- 三维拾取：一次点击怎么解出深度 ----
            // 参照物是一份算法完全不同的暴力实现：每个格子单独做一次射线 vs 立方体相交
            // （slab 法），取距离最小的那个。DDA 步进必须和它完全一致，否则体素遍历的
            // 「先跨哪个轴」一定是错的。
            var bSolid = new HashSet<Vector3Int>();
            bSolid.Add(new Vector3Int(1, 0, 1));
            bSolid.Add(new Vector3Int(3, 2, 2));
            bSolid.Add(new Vector3Int(0, 0, 0));
            bSolid.Add(new Vector3Int(2, 2, 2));

            int mismatchCell = 0;
            int mismatchMiss = 0;
            int traced = 0;
            var rnd = new System.Random(20261031);
            for (int k = 0; k < 4000; k++)
            {
                // 起点随机散布在盒子四周（模仿相机绕着盒子转），方向对着盒子
                Vector3 o = new Vector3(
                    (float)rnd.NextDouble() * 20f - 5f,
                    (float)rnd.NextDouble() * 20f - 5f,
                    (float)rnd.NextDouble() * 20f - 5f);
                Vector3 aim = new Vector3(
                    (float)rnd.NextDouble() * MoldData.Size,
                    (float)rnd.NextDouble() * MoldData.Size,
                    (float)rnd.NextDouble() * MoldData.Size);
                Vector3 dir = aim - o;

                VoxelHit h;
                bool hitA = VoxelRaycast.Trace(o, dir, Vector3Int.one * MoldData.Size, c => bSolid.Contains(c), out h);

                Vector3Int refCell;
                float refT;
                bool hitB = BruteFirstHit(o, dir, MoldData.Size, bSolid, out refCell, out refT);

                if (!hitA && hitB)
                    mismatchMiss++;
                else if (hitA && hitB)
                {
                    traced++;
                    if (h.Cell != refCell)
                        mismatchCell++;
                }
            }
            Check("4000 条随机射线：步进命中的格子和暴力相交完全一致",
                mismatchCell == 0 && traced > 500);
            Check("射线擦不到任何方块时两边都判「没打中」", mismatchMiss == 0);

            // 打在已知方块上时，法线必须指向来路：新方块就长在这一格的法线外侧
            VoxelHit hx;
            var oneSolid = new HashSet<Vector3Int>();
            oneSolid.Add(new Vector3Int(1, 1, 1));
            VoxelRaycast.Trace(new Vector3(-6f, 1.5f, 1.5f), Vector3.right,
                Vector3Int.one * MoldData.Size, c => oneSolid.Contains(c), out hx);
            Check("从 -x 打过来：命中 x 负方向的面",
                hx.Valid && hx.Cell == new Vector3Int(1, 1, 1) && hx.Face == new Vector3Int(-1, 0, 0));
            VoxelRaycast.Trace(new Vector3(1.5f, 1.5f, -6f), Vector3.forward,
                Vector3Int.one * MoldData.Size, c => oneSolid.Contains(c), out hx);
            Check("从 -z 打过来：新方块要长在 z+ 一侧",
                hx.Valid && hx.Face == new Vector3Int(0, 0, -1));
            VoxelRaycast.Trace(new Vector3(1.5f, 8f, 1.5f), Vector3.down,
                Vector3Int.one * MoldData.Size, c => oneSolid.Contains(c), out hx);
            Check("从上方打下来：命中顶面", hx.Valid && hx.Face == new Vector3Int(0, 1, 0));

            // 空盒子和偏掉的射线都不能给出答案
            bool emptyHit = VoxelRaycast.Trace(new Vector3(-6f, 1.5f, 1.5f), Vector3.right,
                Vector3Int.one * MoldData.Size, c => false, out hx);
            Check("空盒子里打不到东西", !emptyHit);
            bool missHit = VoxelRaycast.Trace(new Vector3(-6f, 40f, 1.5f), Vector3.right,
                Vector3Int.one * MoldData.Size, c => oneSolid.Contains(c), out hx);
            Check("射线整个擦过盒子上方：判没打中", !missHit);
            VoxelRaycast.Trace(new Vector3(-6f, 1.5f, 1.5f), Vector3.right,
                Vector3Int.one * MoldData.Size, c => oneSolid.Contains(c), out hx, 1.5f);
            Check("超过最大距离就不再追了", !hx.Valid);

            // ---- 建造台的纯几何：地板落点 / 镜像 / 居中 ----
            // 建造台本体是个 MonoBehaviour（要建相机和网格），但「点下去落在哪一格」这类
            // 换算都是静态纯函数，能直接在批处理里断言。
            var boxSize = Vector3Int.one * MoldData.Size;

            Vector3Int gc;
            bool gOk = MoldWorkbench.GroundCell(new Vector3(2.5f, 8f, 2.5f), Vector3.down, boxSize, out gc);
            Check("垂直打到地板正中：落在 (2,0,2)", gOk && gc == new Vector3Int(2, 0, 2));
            Check("射线朝上打不到地板",
                !MoldWorkbench.GroundCell(new Vector3(2.5f, 1f, 2.5f), Vector3.up, boxSize, out gc));
            Check("射线和地板平行：没有交点",
                !MoldWorkbench.GroundCell(new Vector3(2.5f, 1f, 2.5f), Vector3.right, boxSize, out gc));
            // 斜射：交点要跟着射线往前挪（dir=(0.5,-1,0) 归一化后走 5 格高、2.5 格远）
            bool gDiag = MoldWorkbench.GroundCell(new Vector3(0.5f, 5f, 0.5f),
                new Vector3(0.5f, -1f, 0f).normalized, boxSize, out gc);
            Check("斜射地板：交点跟着射线往前挪", gDiag && gc == new Vector3Int(3, 0, 0));
            Check("射线太斜、交点落到盒子外面：不给落点",
                !MoldWorkbench.GroundCell(new Vector3(0.5f, 5f, 0.5f),
                    new Vector3(1f, -0.5f, 0f).normalized, boxSize, out gc));

            var lShape = new[]
            {
                new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0),
                new Vector3Int(3, 1, 0), new Vector3Int(3, 2, 0),
            };
            Vector3Int[] mirX = MoldWorkbench.Mirrored(lShape, 0);
            Vector3Int[] back = MoldWorkbench.Mirrored(mirX, 0);
            Check("左右镜像：x 按整条边翻（不是按形状自己的包围盒）",
                mirX[0].x == MoldData.Size - 1 - 1 && mirX[3].x == MoldData.Size - 1 - 3);
            Check("镜像的 y、z 不动", mirX[3].y == 2 && mirX[3].z == 0);
            Check("镜像两次回到原样", back[0] == lShape[0] && back[3] == lShape[3]);
            Vector3Int[] mirZ = MoldWorkbench.Mirrored(new[] { new Vector3Int(1, 1, 0) }, 2);
            Check("沿 z 镜像：最外面那层翻到最里面", mirZ[0] == new Vector3Int(1, 1, MoldData.Size - 1));

            var corner = new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0) };
            Vector3Int[] mid = MoldWorkbench.Centered(corner);
            int midMin = Mathf.Min(mid[0].x, mid[1].x);
            int midMax = Mathf.Max(mid[0].x, mid[1].x);
            Check("居中：两格宽的形状在 5 格盒子里两边各留 1 格", midMin == 1 && midMax == 2);
            Vector3Int[] mid2 = MoldWorkbench.Centered(mid);
            Check("居中之后再居中：原地不动", mid2[0] == mid[0] && mid2[1] == mid[1]);
            // 存档被手改成超出盒子宽度时，居中也不能把方块甩出盒外
            var wide = new List<Vector3Int>();
            for (int i = 0; i < 7; i++)
                wide.Add(new Vector3Int(i, 0, 0));
            Vector3Int[] cw = MoldWorkbench.Centered(wide);
            bool wideInBox = true;
            for (int i = 0; i < cw.Length; i++)
            {
                if (!MoldData.InBox(cw[i]))
                    wideInBox = false;
            }
            Check("超宽形状居中后被夹回盒子里", wideInBox);

            Check("建造台找得到编辑器里用的那种方块", BlockCatalog.IdOf("crystal") > 0);

            // 贴着盒壁的方块：命中的那一面朝外，外侧那一格已经出盒 —— 建造台要判「放不下」
            VoxelHit hw;
            var wallSolid = new HashSet<Vector3Int> { new Vector3Int(0, 1, 1) };
            VoxelRaycast.Trace(new Vector3(-6f, 1.5f, 1.5f), Vector3.right, boxSize,
                c => wallSolid.Contains(c), out hw);
            Check("贴着盒壁的方块：法线外侧那一格在盒子外面",
                hw.Valid && hw.Face == new Vector3Int(-1, 0, 0) && !MoldData.InBox(hw.Cell + hw.Face));
            var innerSolid = new HashSet<Vector3Int> { new Vector3Int(2, 1, 1) };
            VoxelRaycast.Trace(new Vector3(-6f, 1.5f, 1.5f), Vector3.right, boxSize,
                c => innerSolid.Contains(c), out hw);
            Check("不贴壁的方块：法线外侧那一格能放",
                hw.Valid && hw.Cell + hw.Face == new Vector3Int(1, 1, 1)
                && MoldData.InBox(hw.Cell + hw.Face));

            // ---- 下落速度：范围 (0, 2]，两位小数 ----
            Check("下落速度下限吃掉 0 和负数", GameSettings.ClampFallSpeed(0f) == 0.01f &&
                GameSettings.ClampFallSpeed(-3f) == 0.01f);
            Check("下落速度上限是 2（闭区间）", GameSettings.ClampFallSpeed(2f) == 2f &&
                GameSettings.ClampFallSpeed(9f) == 2f);
            Check("下落速度保留两位小数", GameSettings.ClampFallSpeed(1.234f) == 1.23f &&
                GameSettings.ClampFallSpeed(0.8765f) == 0.88f);
            Check("下落速度默认 1.00", GameSettings.DefaultFallSpeed == 1f);

            // ---- 消除模式：深度 1 只能行消 ----
            Check("深度 1 时层消被落成行消",
                GameSettings.ResolveElimMode(1, GameSettings.ElimLayer) == GameSettings.ElimRows);
            Check("深度大于 1 才允许层消",
                GameSettings.ResolveElimMode(4, GameSettings.ElimLayer) == GameSettings.ElimLayer &&
                GameSettings.ResolveElimMode(4, GameSettings.ElimRows) == GameSettings.ElimRows);
            Check("乱的消除模式会被夹回两种之一",
                GameSettings.ResolveElimMode(3, 7) == GameSettings.ElimLayer &&
                GameSettings.ResolveElimMode(3, -5) == GameSettings.ElimRows);

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
                Vector3Int[] shape = Tetromino.GetShape((PieceType)i);
                Check("方块形状四格: " + (PieceType)i, shape.Length == 4);
            }

            // 方块材质表 + 图集格子范围
            Check("方块材质表: " + BlockCatalog.Blocks.Length + " 种（去 IP 版是 58 块）", BlockCatalog.Blocks.Length >= 50);
            int badTiles = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                BlockDef def = BlockCatalog.Blocks[i];
                if (def.Up.Col < 0 || def.Up.Col >= BlockCatalog.AtlasColumns ||
                    def.Up.Row < 0 || def.Up.Row >= BlockCatalog.AtlasRows)
                    badTiles++;
            }
            Check("图集格子全部在范围内", badTiles == 0);

            // ---------- 镂空贴图（铜格栅那类带洞的方块） ----------
            // 贴图里整块透明的地方要真的透过去，否则铜格栅的网格会被画成一块铜砖。
            int IdOf(string n)
            {
                for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                    if (BlockCatalog.Blocks[i].Name == n)
                        return i + 1;
                return 0;
            }

            Check("镂空表和方块表对齐",
                  BlockCatalogData.CutoutFlags.Length == BlockCatalog.Blocks.Length);

            int cutCount = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
                if (BlockCatalog.Blocks[i].Cutout)
                    cutCount++;
            // 新表里没有带洞的方块（铜格栅那类 MC 方块已移除），镂空材质暂时闲置
            Check("新表没有镂空方块（洞类方块已随 MC 表移除）", cutCount == 0);

            // 玻璃 / 冰这些走半透明；实心方块不走
            Check("玻璃走半透明材质", BlockCatalog.IsTransparentBlock(BlockCatalog.GlassId));
            Check("实心方块不走半透明", !BlockCatalog.IsTransparentBlock(IdOf("stone")));
            Check("空格子 / 越界不算半透明", !BlockCatalog.IsTransparentBlock(0) &&
                                           !BlockCatalog.IsTransparentBlock(BlockCatalog.Blocks.Length + 1));

            // 图集里那一格确实带 alpha（表和贴图对得上）
            string atlasPng = System.IO.Path.Combine(Application.dataPath, "Resources/atlas.png");
            if (System.IO.File.Exists(atlasPng))
            {
                var atlasTex = new Texture2D(2, 2);
                atlasTex.LoadImage(System.IO.File.ReadAllBytes(atlasPng));
                Color32[] apx = atlasTex.GetPixels32();
                int cell = BlockCatalog.CellPixels;
                int pad = BlockCatalog.TilePadding;

                int Holes(int blockId)
                {
                    BlockDef d = BlockCatalog.Of(blockId);
                    int holes = 0;
                    int ox = d.North.Col * cell + pad;
                    // GetPixels32 是左下原点，图集格子编号是从左上数的，行要翻过来
                    int oy = atlasTex.height - (d.North.Row * cell + pad) - 16;
                    for (int y = 0; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                            if (apx[(oy + y) * atlasTex.width + ox + x].a < 250)
                                holes++;
                    return holes;
                }

                Check("玻璃贴图确实带 alpha（侧面有非 255 的像素）",
                      Holes(BlockCatalog.GlassId) > 20);
                Check("石头贴图不透明", Holes(IdOf("stone")) == 0);
            }

            Material cutMat = Resources.Load<Material>("AtlasCutoutMat");
            Check("镂空材质挂的是方块图集 + alpha test shader",
                  cutMat != null && cutMat.shader != null &&
                  cutMat.shader.name == "MinecraftTetris/AtlasCutout" &&
                  cutMat.mainTexture != null);
            if (cutMat != null)
            {
                // 新表里没有镂空方块（铜格栅那类），玻璃系走半透明材质
                Check("玻璃用的是半透明材质而不是镂空材质",
                      BlockMesh.GetMaterial(BlockCatalog.Of(BlockCatalog.GlassId)) != cutMat);
                Check("实心方块用的是普通图集材质",
                      BlockMesh.GetMaterial(BlockCatalog.Of(IdOf("stone"))) == BlockMesh.AtlasMaterial);
            }

            // ---------- 落点提示（当前方块会停在哪，用高亮方框标出来） ----------
            bool ghostWas = GameSettings.GhostPiece;
            Check("落点提示默认开着", ghostWas);
            GameSettings.SetGhostPiece(false);
            Check("落点提示能关", !GameSettings.GhostPiece);
            GameSettings.SetGhostPiece(true);
            Check("落点提示能再开回来", GameSettings.GhostPiece);
            GameSettings.SetGhostPiece(ghostWas);

            int ghostStone = IdOf("stone");
            var gGhost = new GameLoop(5, 8, 1, 7);
            gGhost.Current = new Piece(PieceType.O, ghostStone, 0, 6, 0);
            Vector3Int land = gGhost.GhostOrigin();
            Check("空场里落点在最底层（y = 0）", land.y == 0 && land.x == 0 && land.z == 0);

            // 底下垫两块石头，落点就抬到石头上面
            var gGhost2 = new GameLoop(5, 8, 1, 7);
            gGhost2.Grid.Set(0, 0, 0, ghostStone);
            gGhost2.Grid.Set(1, 0, 0, ghostStone);
            gGhost2.Current = new Piece(PieceType.O, ghostStone, 0, 6, 0);
            Check("底下有方块时落点停在它上面", gGhost2.GhostOrigin().y == 1);

            // 落点必须和硬降结果一致：框指哪儿，按空格就得落哪儿
            var gGhost3 = new GameLoop(5, 8, 1, 7);
            gGhost3.Current = new Piece(PieceType.O, ghostStone, 2, 6, 0);
            Vector3Int preview = gGhost3.GhostOrigin();
            gGhost3.HardDrop();
            bool landed = true;
            for (int i = 0; i < 4; i++)
            {
                // 落定之后 Current 已经换下一块了，拿落点算出的四格去查场地
                Vector3Int off = new Piece(PieceType.O, ghostStone, 0, 0, 0).CellOffset(i);
                if (gGhost3.Grid.Get(preview.x + off.x, preview.y + off.y, preview.z + off.z) == 0)
                    landed = false;
            }
            Check("落点和硬降落在同一处", landed && preview.y == 0);

            Mesh frame = GridView.BuildGhostFrame(0.56f, 0.055f);
            Check("落点框是 12 根棱拼的（每根 8 顶点）",
                  frame.vertexCount == 96 && frame.triangles.Length == 12 * 36);

            // 落点框的材质按主题分两套：
            //   暗色主题用加色发光（不吃光才亮，压在黑底上正好）
            //   亮色主题必须换成普通 alpha 混合 —— 加色在浅底上只会更白，框会消失
            UiSkin.Theme themeWas = UiSkin.Current;

            UiSkin.Current = UiSkin.Theme.Dark;
            var ghostHostDark = new GameObject("GhostMatProbeDark");
            Material ghostMatDark = ghostHostDark.AddComponent<GridView>().GhostMaterial;
            Check("暗色主题下落点框是加色发光的",
                  ghostMatDark != null && ghostMatDark.shader != null &&
                  ghostMatDark.shader.name == "MinecraftTetris/GlowAdditive");
            UnityEngine.Object.DestroyImmediate(ghostHostDark);

            UiSkin.Current = UiSkin.Theme.Light;
            var ghostHostLight = new GameObject("GhostMatProbeLight");
            Material ghostMatLight = ghostHostLight.AddComponent<GridView>().GhostMaterial;
            Check("亮色主题下换成普通透明混合（加色在浅底上会消失）",
                  ghostMatLight != null && ghostMatLight.shader != null &&
                  ghostMatLight.shader.name == "MinecraftTetris/AtlasTransparent");
            UnityEngine.Object.DestroyImmediate(ghostHostLight);

            UiSkin.Current = themeWas;

            // 放置音效组：由材质生成器按新方块表写出（clip 本身还是 MC 的，第二步换素材）
            Check("音效分组数和方块表对齐",
                BlockCatalogData.SfxGroups.Length == BlockCatalogData.Names.Length);

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
                return idx < 0 ? "<没有这个方块>" : BlockCatalogData.SfxGroups[idx];
            };
            Check("木板 -> 木头音", groupOf("plank") == "wood");
            Check("石头 -> 石头音", groupOf("stone") == "stone");
            Check("沙子 -> 沙子音", groupOf("sand") == "sand");
            Check("炸药 -> 草的音（原版 TNT 就挂在草组）", groupOf("explosive") == "grass");
            Check("黑曜石 -> 石头音", groupOf("obsidian") == "stone");
            Check("水 -> 水声", groupOf("water") == "water");
            Check("岩浆 -> 岩浆声", groupOf("lava") == "lava");
            Check("酸借水声", groupOf("acid") == "water");

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

            // 关掉流动开关后，场上的水要降级成普通方块。
            // 流体平时只活在 Fluids 自己的数组里、不写网格，所以关掉流动之后它既没有碰撞
            // 也参与不了消行，就僵在原地（真机反馈：消行时那滩水不下去也不消失）。
            bool flowWaterWas = GameSettings.Mech(GameSettings.MechKind.WaterFlow);
            bool flowLavaWas = GameSettings.Mech(GameSettings.MechKind.LavaFlow);
            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, false);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, false);

            var gSolid = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gSolid);
            Fluids.SetAmount(1, 2, 0, 5, Fluids.WaterId);
            Fluids.Tick(gSolid, 0.2f);
            Check("关掉流动后水变成网格里的方块", gSolid.Get(1, 2, 0) == Fluids.WaterId);
            Check("固化后流体层清空（不会画两遍）", Fluids.AmountAt(1, 2, 0) == 0);
            Check("固化那一 tick 会通知重力重算", Fluids.LastSolidified);

            // 固化之后它就是普通方块了：能凑满一行，也就能被消掉
            var gSolidRow = new Grid(2, 2, 1);
            Fluids.Reset();
            Fluids.Prepare(gSolidRow);
            gSolidRow.Set(0, 0, 0, 1);
            Fluids.SetAmount(1, 0, 0, 3, Fluids.WaterId);
            Fluids.Tick(gSolidRow, 0.2f);
            Check("固化后的水能凑满一行（消行带得走它）", gSolidRow.IsRowFullPublic(0, 0));

            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, flowWaterWas);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, flowLavaWas);

            // 一键全开 / 全关：设置页那颗按钮靠 AllMechOn 判断该显示哪个文案
            {
                var was = new bool[GameSettings.MechCount];
                for (int i = 0; i < GameSettings.MechCount; i++)
                    was[i] = GameSettings.Mech((GameSettings.MechKind)i);

                GameSettings.SetAllMech(false);
                Check("一键全关之后全都不开", !GameSettings.AllMechOn());
                GameSettings.SetAllMech(true);
                Check("一键全开之后全部打开", GameSettings.AllMechOn());
                Check("一键全开后单项也跟着开",
                      GameSettings.Mech(GameSettings.MechKind.TntExplode) &&
                      GameSettings.Mech(GameSettings.MechKind.WaterFlow));

                // 还原成跑测试之前的样子，别把玩家自己的开关改掉
                for (int i = 0; i < GameSettings.MechCount; i++)
                    GameSettings.SetMech((GameSettings.MechKind)i, was[i]);
            }

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

            // 玻璃：普通池三百多种，抽一次要等好几百块，改成按块数定期发
            int glassPool = BlockCatalog.GlassId;
            Check("玻璃不在普通池和特殊池里（单独按块数发）",
                glassPool > 0 &&
                System.Array.IndexOf(BlockPools.Normal, glassPool) < 0 &&
                System.Array.IndexOf(BlockPools.Special, glassPool) < 0);
            Random.InitState(20261029);
            bool sawGlass = false;
            for (int i = 0; i < 2000 && !sawGlass; i++)
                sawGlass = BlockPools.NextRandomId() == glassPool;
            Check("抽 2000 块能抽到玻璃", sawGlass);

            // 雪块 / 冰：同理，埋在普通池里等于见不到，改成定期发
            int snowPool = BlockCatalog.SnowId;
            int icePool = BlockCatalog.IceId;
            Check("雪块和冰不在普通池和特殊池里（单独按块数发）",
                snowPool > 0 && icePool > 0 &&
                System.Array.IndexOf(BlockPools.Normal, snowPool) < 0 &&
                System.Array.IndexOf(BlockPools.Special, snowPool) < 0 &&
                System.Array.IndexOf(BlockPools.Normal, icePool) < 0 &&
                System.Array.IndexOf(BlockPools.Special, icePool) < 0);
            Random.InitState(20261030);
            bool sawSnow = false, sawIce = false;
            for (int i = 0; i < 3000 && !(sawSnow && sawIce); i++)
            {
                int pulled = BlockPools.NextRandomId();
                if (pulled == snowPool) sawSnow = true;
                if (pulled == icePool) sawIce = true;
            }
            Check("抽 3000 块能同时抽到雪块和冰", sawSnow && sawIce);

            // 南瓜头补发机制（MC 傀儡系统）在 Substrata 表里没有对应方块，休眠，测试一并移除。

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

            // 头顶有水：这一炸只冒烟，一块都不拆
            var gShield = new Grid(10, 20, 1);
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    gShield.Set(5 + dx, 6 + dy, 0, stoneId);
                }
            }
            gShield.Set(5, 6, 0, Tnt.TntId);
            Fluids.AddSource(gShield, 5, 7, 0, Fluids.WaterId);
            Tnt.Reset();
            Tnt.Prime(gShield, 5, 6, 0, Tnt.FuseSeconds);
            Check("头顶有水：TNT 依然会炸", Tnt.Tick(gShield, Tnt.FuseSeconds + 0.01f));
            Check("头顶有水：TNT 自己那格清掉", gShield.Get(5, 6, 0) == 0);
            Check("头顶有水：周围的石头一块不少",
                gShield.Get(5, 5, 0) == stoneId && gShield.Get(4, 6, 0) == stoneId
                && gShield.Get(6, 6, 0) == stoneId);
            Check("头顶有水：这次爆炸没有破坏记录",
                Tnt.LastBlasts.Count == 1 && Tnt.LastBlasts[0].Cells.Count == 0);
            Fluids.ClearCell(5, 7, 0);

            // —— 清格子必须连流体份数一起清（「凭空冒火」的根治）——
            // 方块格子里本不该有份数（落定时 OnSolidPlaced 会把流体挤走），
            // 但清格子的路径有十来处（爆炸 / 烧掉 / 玻璃碎 / 融化 / 傀儡退场 / 重力挪走），
            // 哪条漏清就会剩一份看不见的岩浆，隔着老远把可燃方块点着，
            // 火烧起来的时候岩浆早没了——玩家看到的就是「没有岩浆却冒火」。
            // 修法是在 Grid.Set 这个唯一写入口把住：旧格子非空、新格子为空就触发
            // OnCellCleared，外面接到 Fluids.ClearCell（正式游戏在 Bootstrap 接线，
            // 这里照着接一份，下面两条走的就是真实链路）。
            Grid.OnCellCleared = (cx, cy, cz) => Fluids.ClearCell(cx, cy, cz);

            var gResidue = new Grid(6, 6, 1);
            gResidue.Set(1, 1, 0, stoneId);
            Fluids.SetAmount(1, 1, 0, 8, Fluids.LavaId);   // 强塞一份，构造「方块格藏岩浆」的异常态
            gResidue.Set(1, 1, 0, 0);
            Check("方块被清掉时格子里的份数跟着清", Fluids.AmountAt(1, 1, 0) == 0);

            gResidue.Set(4, 1, 0, stoneId);
            Fluids.SetAmount(4, 1, 0, 8, Fluids.LavaId);
            gResidue.Set(3, 1, 0, Tnt.TntId);
            Tnt.Reset();
            Tnt.Prime(gResidue, 3, 1, 0, 0.05f);
            Tnt.Tick(gResidue, 0.1f);
            Check("爆炸炸掉的方块格不留隐形岩浆",
                gResidue.Get(4, 1, 0) == 0 && Fluids.AmountAt(4, 1, 0) == 0);

            bool fireWasOn = GameSettings.FireBurns;
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, true);
            int flamId = 0;
            for (int i = 1; i <= BlockCatalog.Blocks.Length && flamId == 0; i++)
                if (Fire.IsFlammable(i))
                    flamId = i;
            var gBurn = new Grid(4, 4, 1);
            gBurn.Set(1, 1, 0, flamId);
            Fluids.SetAmount(1, 1, 0, 2, Fluids.LavaId);
            Fire.Reset();
            Check("藏着的岩浆照样把可燃方块点着", Fire.Ignite(gBurn, 1, 1, 0));
            for (int i = 0; i < 200 && Fire.IsBurning(1, 1, 0); i++)
                Fire.Tick(gBurn, 0.05f);
            Check("方块烧光后格子空了", gBurn.Get(1, 1, 0) == 0);
            Check("烧掉的格子也不留隐形岩浆", Fluids.AmountAt(1, 1, 0) == 0);
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, fireWasOn);

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

            // 岩浆也能点着 TNT（原版如此）。岩浆不写网格，判定走 Fluids。
            var gLavaTnt = new Grid(8, 6, 1);
            gLavaTnt.Set(2, 2, 0, Tnt.TntId);
            Fluids.Reset();
            Fluids.Prepare(gLavaTnt);
            Fluids.SetAmount(3, 2, 0, 8, Fluids.LavaId);
            Tnt.Reset();
            Check("岩浆挨着 TNT 也会点火", Tnt.PrimeContact(gLavaTnt) == 1);
            Tnt.Reset();
            Fluids.Reset();
            Fluids.Prepare(gLavaTnt);
            Fluids.SetAmount(3, 2, 0, 8, Fluids.WaterId);
            Check("水挨着 TNT 不点火", Tnt.PrimeContact(gLavaTnt) == 0);
            Tnt.Reset();
            Fluids.Reset();

            // 炸不掉的方块：基岩和黑曜石
            int bedrockForBlast = BlockCatalog.BedrockId;
            int obsidianId = BlockCatalog.FindId("obsidian");
            Check("基岩和黑曜石都在方块表里", bedrockForBlast > 0 && obsidianId > 0);
            Check("基岩炸不掉", BlockCatalog.IsBlastProof(bedrockForBlast));
            Check("黑曜石炸不掉", BlockCatalog.IsBlastProof(obsidianId));
            Check("普通方块炸得掉", !BlockCatalog.IsBlastProof(stoneId));
            Check("空格子不算炸不掉", !BlockCatalog.IsBlastProof(0));

            var gBlast = new Grid(10, 20, 1);
            gBlast.Set(4, 5, 0, Tnt.TntId);
            gBlast.Set(5, 5, 0, Tnt.RedstoneId);
            gBlast.Set(3, 5, 0, obsidianId);        // 爆炸半径内
            gBlast.Set(4, 6, 0, stoneId);            // 爆炸半径内
            Tnt.Reset();
            Tnt.PrimeContact(gBlast);
            Tnt.Tick(gBlast, Tnt.FuseSeconds);
            Check("爆炸后黑曜石还在", gBlast.Get(3, 5, 0) == obsidianId);
            Check("爆炸后普通方块照炸", gBlast.Get(4, 6, 0) == 0);
            Tnt.Reset();

            // 原木长树叶
            // 前面的「机制开关」测试可能把长叶关掉了，这里显式打开（测完还原）
            bool leafGrowWas = GameSettings.Mech(GameSettings.MechKind.LeafGrow);
            GameSettings.SetMech(GameSettings.MechKind.LeafGrow, true);
            Foliage.Init();
            int oakLog = BlockCatalog.FindId("log"), oakLeaves = BlockCatalog.FindId("leaf");
            Check("方块表里有原木和树叶", oakLog > 0 && oakLeaves > 0);
            Check("原木长出的是树叶", Foliage.LeafIdFor(oakLog) == oakLeaves);
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

            // 只长面相邻的格：四个侧面和正上方都被占死，斜上方空着也不该长
            var gSide = new Grid(5, 5, 1);
            gSide.Set(2, 2, 0, oakLog);
            gSide.Set(1, 2, 0, stoneId);
            gSide.Set(3, 2, 0, stoneId);
            gSide.Set(2, 3, 0, stoneId);
            Check("只剩斜上方空着时，不长（树叶不隔空）",
                Foliage.GrowAround(gSide, 2, 2, 0, oakLog) == 0);

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
            var bigCells = new Vector3Int[16];
            for (int i = 0; i < 16; i++)
                bigCells[i] = new Vector3Int(i % 4, i / 4, 0);
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

            GameSettings.SetMech(GameSettings.MechKind.LeafGrow, leafGrowWas);

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

            // 结构方块：落定时记住整块场地 → 被消掉时先一格一格收掉，再从下往上一格一格掉回来
            int structureId = BlockCatalog.StructureId;
            Check("方块表里有结构方块", structureId > 0);
            Check("结构方块能认出来", Structure.IsStructure(structureId));
            Check("石头不是结构方块", !Structure.IsStructure(stoneId));

            Structure.Reset();
            var gStruct = new Grid(9, 9, 1);
            gStruct.Set(0, 0, 0, stoneId);      // 场地角落：记的是整场，不只是身边那一圈
            gStruct.Set(2, 4, 0, sandId);
            gStruct.Set(4, 4, 0, structureId);
            gStruct.Set(4, 3, 0, stoneId);
            Structure.Memory mem = Structure.Remember(gStruct, 4, 4, 0);
            Check("记忆覆盖整块场地", mem != null && mem.Blocks.Length == 81);
            Check("记忆范围就是整场", mem != null && mem.SizeX == 9 && mem.SizeY == 9 && mem.SizeZ == 1);
            Check("记忆里存的是当时整场的样子", mem != null &&
                Structure.BlockAt(mem, 0, 0, 0) == stoneId &&
                Structure.BlockAt(mem, 2, 4, 0) == sandId &&
                Structure.BlockAt(mem, 4, 4, 0) == structureId);
            Check("同一块只记一条", Structure.Memories.Count == 1);

            Structure.Memory again = Structure.Remember(gStruct, 4, 4, 0);
            Check("同一格再记一次，旧的那条让位", again != null && again != mem && Structure.Memories.Count == 1);

            // 一整块 4 格结构方块只记一条：消掉任意一格只触发一次
            Structure.Reset();
            var gQuad = new Grid(11, 10, 1);     // 比地面宽一格，免得地面自己先是个满行
            for (int x = 0; x < 10; x++)
                gQuad.Set(x, 0, 0, stoneId);          // 地面：消失阶段有东西可收
            var quad = new Piece(PieceType.O, structureId, 4, 2, 0);
            for (int i = 0; i < quad.Cells.Length; i++)
            {
                Vector3Int qc = quad.WorldCell(i);
                if (gQuad.InBounds(qc.x, qc.y, qc.z))
                    gQuad.Set(qc.x, qc.y, qc.z, structureId);
            }
            Structure.RememberFromPiece(gQuad, quad);
            Check("4 格结构方块只记一条记忆", Structure.Memories.Count == 1 &&
                Structure.Memories[0].Cells.Length == 12);
            Check("记忆里连地面一起拍下来了", Structure.BlockAt(Structure.Memories[0], 0, 0, 0) == stoneId);

            int finishes = 0;
            System.Action<int, int, int> countFinish = (fx, fy, fz) => { finishes++; };
            Structure.Finished += countFinish;
            for (int x = 0; x < 11; x++)
            {
                if (gQuad.Get(x, 2, 0) == 0)
                    gQuad.Set(x, 2, 0, stoneId);
            }
            Check("满行会触发结构方块的还原", gQuad.ClearFullRows() == 1);
            Check("还原已经排队", Structure.Busy);

            // 消失阶段：从下往上，一次只收一格（地面 10 格 + 还留在上面的 2 格结构方块）
            Check("触发时场上还有 12 格", CountFilled(gQuad) == 12);
            Structure.Tick(gQuad, Structure.ClearInterval * 1.01f);
            Check("消失阶段一次只收一格", CountFilled(gQuad) == 11);

            // 收完场子才轮到重建
            for (int i = 0; i < 200 && !Structure.InDropPhase && Structure.Busy; i++)
                Structure.Tick(gQuad, Structure.ClearInterval * 1.01f);
            Check("消失阶段把场子腾空了", CountFilled(gQuad) == 0);
            Check("收完就进重建阶段", Structure.InDropPhase);

            // 重建阶段：同样一次一格，从最下面往上掉
            Structure.Tick(gQuad, Structure.DropInterval * 1.01f);
            Check("重建阶段一次掉下一格", CountFilled(gQuad) == 1 && gQuad.Get(0, 0, 0) == stoneId);

            for (int i = 0; i < 200 && Structure.Busy; i++)
                Structure.Tick(gQuad, Structure.DropInterval * 1.01f);
            Structure.Finished -= countFinish;
            Check("一条记忆只还原一次", finishes == 1);
            Check("重建完场地跟记忆里一样", CountFilled(gQuad) == 10 &&
                gQuad.Get(0, 0, 0) == stoneId && gQuad.Get(9, 0, 0) == stoneId);
            Check("结构方块自己不会再回来", gQuad.Get(4, 2, 0) == 0 && gQuad.Get(5, 2, 0) == 0);
            Check("记忆和队列都清空了", Structure.Memories.Count == 0 && !Structure.Busy);

            // 回滚：记忆之后才落下来的东西，消失阶段一并收掉
            Structure.Reset();
            var gRoll = new Grid(6, 6, 1);
            gRoll.Set(1, 0, 0, stoneId);
            gRoll.Set(2, 1, 0, structureId);
            Structure.Remember(gRoll, 2, 1, 0);
            gRoll.Set(4, 0, 0, sandId);
            Structure.Trigger(gRoll, 2, 1, 0);
            RunRestore(gRoll);
            Check("还原把记忆之后补的方块抹掉", gRoll.Get(4, 0, 0) == 0);
            Check("还原把记忆里的构造放回来", gRoll.Get(1, 0, 0) == stoneId);
            Check("这条记忆删掉了", Structure.Memories.Count == 0 && !Structure.Busy);

            // 从下往上：先收下面的，再收上面的
            Structure.Reset();
            var gWave = new Grid(3, 6, 1);
            gWave.Set(1, 0, 0, stoneId);
            gWave.Set(1, 2, 0, structureId);
            Structure.Remember(gWave, 1, 2, 0);
            gWave.Set(1, 4, 0, sandId);      // 记忆之后才有的，在上方
            Structure.Trigger(gWave, 1, 2, 0);
            Structure.Tick(gWave, Structure.ClearInterval * 1.01f);
            Check("消失从下往上：最下面那格先没", gWave.Get(1, 0, 0) == 0 && gWave.Get(1, 4, 0) == sandId);
            RunRestore(gWave);
            Check("收完之后上面那格也按记忆抹掉了", gWave.Get(1, 4, 0) == 0 && !Structure.Busy);

            // 存档：新格式带 -2 前缀，快照和触发格一起存
            Structure.Reset();
            var gSave = new Grid(7, 5, 1);
            gSave.Set(6, 2, 0, stoneId);       // 场地最右边也要存下来
            gSave.Set(3, 1, 0, structureId);
            gSave.Set(3, 2, 0, sandId);
            Structure.Remember(gSave, 3, 1, 0);
            int[] dumped = Structure.SerializeMemories();
            Check("新存档带版本前缀", dumped.Length > 0 && dumped[0] == -2);
            Structure.Reset();
            Structure.DeserializeMemories(dumped);
            Check("结构方块的记忆能存档", Structure.Memories.Count == 1 &&
                Structure.Memories[0].Blocks.Length == 35 &&
                Structure.BlockAt(Structure.Memories[0], 6, 2, 0) == stoneId &&
                Structure.BlockAt(Structure.Memories[0], 3, 2, 0) == sandId);
            Check("触发格跟着记忆一起存", Structure.Memories[0].Cells.Length == 3 &&
                Structure.Memories[0].Cells[0] == 3 && Structure.Memories[0].Cells[1] == 1);

            // 老存档（没有版本前缀，范围是 3×3×3）也要能读，读回来照样能还原
            int[] legacy = new int[10 + 9];
            legacy[0] = 4; legacy[1] = 4; legacy[2] = 0;   // 中心格
            legacy[3] = 3; legacy[4] = 3; legacy[5] = 0;   // 范围起点
            legacy[6] = 3; legacy[7] = 3; legacy[8] = 1;   // 尺寸
            legacy[9] = 9;                                  // 块数
            legacy[10] = stoneId;                           // (3,3,0)
            legacy[14] = structureId;                       // (4,4,0)
            Structure.Reset();
            Structure.DeserializeMemories(legacy);
            Check("老存档的 3×3 记忆能读回来", Structure.Memories.Count == 1 &&
                Structure.Memories[0].SizeX == 3 && Structure.Memories[0].SizeZ == 1 &&
                Structure.Memories[0].Cells.Length == 3 &&
                Structure.BlockAt(Structure.Memories[0], 3, 3, 0) == stoneId &&
                Structure.BlockAt(Structure.Memories[0], 4, 4, 0) == structureId);

            var gLegacy = new Grid(9, 9, 1);
            gLegacy.Set(4, 4, 0, structureId);
            Structure.DeserializeMemories(legacy);
            Structure.Trigger(gLegacy, 4, 4, 0);
            RunRestore(gLegacy);
            Check("老记忆也能还原", gLegacy.Get(3, 3, 0) == stoneId && gLegacy.Get(4, 4, 0) == 0 &&
                !Structure.Busy);
            Structure.Reset();

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
            // 原来是「必须 Streaming」：桌面流式省内存。但浏览器不支持流式播放，
            // 而平台覆盖实测写不进 .meta，所以现在统一用「压缩进内存」——网页版才能出声。
            // 这里改成断言「不是整首解码进内存」（Streaming 或 CompressedInMemory 都算合格）。
            Check("音乐不整首解码进内存（流式或压缩进内存）",
                song != null && (song.loadType == AudioClipLoadType.Streaming
                    || song.loadType == AudioClipLoadType.CompressedInMemory));

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

            // 触屏和键盘两路输入按位或：同一帧里任一路按下都算按下，
            // 没按的字段不能被另一路「凑」出来（凑出来就是方块自己乱动）
            var kbIn = new InputState { Left = true, RotateCW = true };
            var touchIn = new InputState { Right = true, HardDrop = true };
            InputState both = InputState.Merge(kbIn, touchIn);
            Check("两路输入合并：左右同时按着两个方向都在", both.Left && both.Right);
            Check("两路输入合并：没按的字段保持 false",
                !both.Up && !both.Down && !both.SoftDrop && !both.RotateYawCW && !both.RotatePitchCCW);
            Check("两路输入合并：旋转和硬降都保留", both.RotateCW && both.HardDrop);

            // 批处理跑在桌面上，必须认成非移动端 —— 认错了会把触屏层挂进桌面版
            Check("桌面环境不判定为移动端", !MobileMode.Enabled);
            // 0.34 → 0.18：四个按键原来铺满底部三分之一屏，手机上又大又笨，
            // 压到一半之后场地多出一大截。下限放宽到 14% 兜住「别再压小了」。
            Check("控制条高度占屏比在 14%~40% 之间",
                MobileControls.StripRatio > 0.14f && MobileControls.StripRatio < 0.4f);

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

            // 设置 / 规则各页的自适应：面板必须整块装进屏幕，不能只 clamp 宽不管高
            float pageMargin = UiSkin.P(24f);
            var screens = new[]
            {
                new Vector2(2560f, 1440f), new Vector2(1920f, 1080f), new Vector2(1600f, 900f),
                new Vector2(1366f, 768f), new Vector2(1280f, 720f), new Vector2(1024f, 640f),
                new Vector2(800f, 600f), new Vector2(640f, 480f),
            };

            bool pagesFit = true;
            bool pagesPositive = true;
            for (int pi = 0; pi <= TitleScreen.PageLast; pi++)
            {
                Vector2 design = TitleScreen.PageDesign(pi);
                float target = TitleScreen.PageTargetOf(pi);

                for (int si = 0; si < screens.Length; si++)
                {
                    float availW = screens[si].x - pageMargin * 2f;
                    float availH = screens[si].y - pageMargin * 2f;
                    float s = UiSkin.FitScale(design.x, design.y, target, availW, availH, 0.35f);

                    float pw = UiSkin.P(design.x * s);
                    float ph = UiSkin.P(design.y * s);
                    if (pw > availW + 0.5f || ph > availH + 0.5f)
                        pagesFit = false;
                    if (s <= 0f || pw <= 0f || ph <= 0f)
                        pagesPositive = false;
                }
            }
            Check("设置 / 规则每一页在 640×480 ~ 2560×1440 下都装得进屏幕", pagesFit);
            Check("各页缩放系数永远是正数", pagesPositive);

            // 大屏要真吃到 2 倍，小屏要真的降下来（否则等于自适应没生效）
            Vector2 guideDesign = TitleScreen.PageDesign(TitleScreen.PageGuide);
            float bigScale = UiSkin.FitScale(guideDesign.x, guideDesign.y, TitleScreen.PageTargetOf(TitleScreen.PageGuide),
                2560f - pageMargin * 2f, 1440f - pageMargin * 2f, 0.35f);
            float smallScale = UiSkin.FitScale(guideDesign.x, guideDesign.y, TitleScreen.PageTargetOf(TitleScreen.PageGuide),
                1280f - pageMargin * 2f, 720f - pageMargin * 2f, 0.35f);
            // 1440 高装不下 596 设计高的 2 倍（要 1490px），所以只能到 1.85 倍左右
            Check("2560×1440 下规则页接近目标 2 倍", bigScale > 1.5f && bigScale <= 2f + 0.001f);
            Check("1280×720 下规则页自动折减（写死 2 倍会溢出）", smallScale < 1.5f && smallScale > 0.35f);
            Check("规则页在 1080p 下宽度不超屏",
                UiSkin.P(guideDesign.x * UiSkin.FitScale(guideDesign.x, guideDesign.y, 2f,
                    1920f - pageMargin * 2f, 1080f - pageMargin * 2f, 0.35f)) <= 1920f - pageMargin * 2f + 0.5f);

            // ---------- 特殊方块机制开关 ----------
            Check("机制开关一共十六项", GameSettings.MechCount == 16);

            int mechSand = 0, mechLog = 0, mechObsidian = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string bn = BlockCatalog.Blocks[i].Name;
                if (bn == "sand") mechSand = i + 1;
                else if (bn == "log") mechLog = i + 1;
                else if (bn == "obsidian") mechObsidian = i + 1;
            }

            // TNT 爆炸与接触点火
            var gMechTnt = new Grid(6, 6, 1);
            gMechTnt.Set(2, 0, 0, Tnt.TntId);
            gMechTnt.Set(3, 0, 0, Tnt.RedstoneId);  // 红石块的点火职责由引线接替
            Tnt.Reset();
            Check("开关全开时 TNT 挨着引线会被点着", Tnt.PrimeContact(gMechTnt) == 1);
            Tnt.Reset();

            GameSettings.SetMech(GameSettings.MechKind.TntExplode, false);
            Check("关掉 TNT 爆炸就点不着", Tnt.PrimeContact(gMechTnt) == 0);
            GameSettings.SetMech(GameSettings.MechKind.TntExplode, true);
            Tnt.Reset();

            GameSettings.SetMech(GameSettings.MechKind.ContactIgnite, false);
            Check("关掉接触点火就点不着", Tnt.PrimeContact(gMechTnt) == 0);
            GameSettings.SetMech(GameSettings.MechKind.ContactIgnite, true);
            Tnt.Reset();
            Check("开关拨回来又能点着", Tnt.PrimeContact(gMechTnt) == 1);
            Tnt.Reset();

            // 沙子下落
            var gMechSand = new Grid(5, 6, 1);
            gMechSand.Set(2, 4, 0, mechSand);
            GameSettings.SetMech(GameSettings.MechKind.SandFall, false);
            Check("关掉沙子下落，沙子留在原地",
                !Gravity.Settle(gMechSand) && gMechSand.Get(2, 4, 0) == mechSand);
            GameSettings.SetMech(GameSettings.MechKind.SandFall, true);
            Check("开关拨回来沙子会掉", Gravity.Settle(gMechSand) && gMechSand.Get(2, 0, 0) == mechSand);

            // 原木长树叶
            var gMechLeaf = new Grid(9, 9, 1);
            var mechLeafPiece = new Piece(PieceType.O, mechLog, 4, 4, 0);
            // 树叶只从「网格里还是原木」的格子长，所以先把那块原木摆进网格
            for (int i = 0; i < mechLeafPiece.Cells.Length; i++)
            {
                Vector3Int lc = mechLeafPiece.WorldCell(i);
                gMechLeaf.Set(lc.x, lc.y, lc.z, mechLog);
            }
            Foliage.Init();
            GameSettings.SetMech(GameSettings.MechKind.LeafGrow, false);
            Check("关掉原木长叶就不长", Foliage.Grow(gMechLeaf, mechLeafPiece) == 0);
            GameSettings.SetMech(GameSettings.MechKind.LeafGrow, true);
            Check("开关拨回来原木会发芽", Foliage.Grow(gMechLeaf, mechLeafPiece) > 0);

            // 水与岩浆流动
            Fluids.Reset();
            var gMechFluid = new Grid(6, 6, 1);
            Fluids.Prepare(gMechFluid);
            Fluids.SetAmount(3, 3, 0, 8, Fluids.WaterId);
            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, false);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, false);
            // 关掉之后这一 tick 会把那滩水固化成方块，所以第一次 tick 是「有变化」；
            // 变成方块之后流体层就空了，再 tick 什么都不会动。
            Fluids.Tick(gMechFluid, 1f);
            Check("关掉流动后，那滩水固化成方块", gMechFluid.Get(3, 3, 0) == Fluids.WaterId);
            Check("固化之后流体层不再动", !Fluids.Tick(gMechFluid, 1f));

            // 开关拨回来：已经固化的不会变回流体质（单向降级），但新灌的水照常流
            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, true);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, true);
            Fluids.SetAmount(1, 5, 0, 8, Fluids.WaterId);
            Check("开关拨回来，新灌进去的水会流", Fluids.Tick(gMechFluid, 1f));
            Fluids.Reset();

            // 结构方块还原
            Structure.Reset();
            var gMechStruct = new Grid(9, 9, 1);
            gMechStruct.Set(4, 4, 0, BlockCatalog.StructureId);
            gMechStruct.Set(4, 5, 0, mechSand);
            Structure.Remember(gMechStruct, 4, 4, 0);
            int mechMem = Structure.Memories.Count;
            var structPiece = new Piece(PieceType.O, BlockCatalog.StructureId, 6, 1, 0);
            GameSettings.SetMech(GameSettings.MechKind.StructureRestore, false);
            Structure.RememberFromPiece(gMechStruct, structPiece);
            Check("关掉结构方块还原就不记事", Structure.Memories.Count == mechMem);
            Check("关掉还原时被消也不触发",
                !Structure.NotifyCleared(gMechStruct, 4, 4, 0) && !Structure.Busy);
            GameSettings.SetMech(GameSettings.MechKind.StructureRestore, true);
            Structure.Reset();

            // 基岩锚定与防爆
            GameSettings.SetMech(GameSettings.MechKind.BedrockAnchor, false);
            Check("关掉基岩钉住，基岩不再锚定", !BlockCatalog.IsAnchor(BlockCatalog.BedrockId));
            GameSettings.SetMech(GameSettings.MechKind.BedrockAnchor, true);
            Check("开关拨回来基岩还是钉子", BlockCatalog.IsAnchor(BlockCatalog.BedrockId));

            GameSettings.SetMech(GameSettings.MechKind.BlastProof, false);
            Check("关掉防爆开关，黑曜石也能炸掉", mechObsidian > 0 && !BlockCatalog.IsBlastProof(mechObsidian));
            GameSettings.SetMech(GameSettings.MechKind.BlastProof, true);
            Check("开关拨回来黑曜石还是炸不掉", mechObsidian > 0 && BlockCatalog.IsBlastProof(mechObsidian));

            // ---------- 玻璃被压碎 ----------
            int glassId = BlockCatalog.GlassId;
            Check("方块表里有玻璃", glassId > 0);

            var gGlass = new Grid(5, 5, 1);
            gGlass.Set(2, 0, 0, glassId);
            Check("玻璃上面没东西就不碎", Glass.Crush(gGlass) == 0);

            gGlass.Set(2, 1, 0, mechSand);
            Check("上面压着方块，玻璃就碎", Glass.Crush(gGlass) == 1);
            Check("碎掉的格子记在 LastCrushed 里",
                Glass.LastCrushed.Count == 1 && Glass.LastCrushed[0].x == 2 && Glass.LastCrushed[0].y == 0);
            // 规则页写的是「碎完上方的东西接着往下掉」：普通方块悬空本来不会掉，
            // 所以碎掉那一刻就整列塌一格，不等重力（重力只管沙子这类）
            Check("玻璃碎完，上方的方块当场塌下来补位",
                gGlass.Get(2, 0, 0) == mechSand && gGlass.Get(2, 1, 0) == 0);

            // 新规则的核心：先来后到。玻璃落定时上方本来就有东西，不该当场碎
            var gGlassLate = new Grid(5, 5, 1);
            gGlassLate.Set(2, 2, 0, mechSand);   // 上面那块先在那儿
            gGlassLate.Set(2, 1, 0, glassId);    // 玻璃后落进来，正上方压着旧的方块
            Check("玻璃落定时上方已有的旧方块不算压（落下不碎）", Glass.Crush(gGlassLate) == 0);
            gGlassLate.Set(2, 2, 0, 0);          // 上面那块旧方块被消掉
            gGlassLate.Set(2, 2, 0, mechSand);   // 又一块新方块落到同一格
            Check("之后新落下来的方块照常压碎它", Glass.Crush(gGlassLate) == 1);

            // 玻璃压玻璃不算压：不然一次落四格玻璃，叠着的那几格会互相碎掉
            var gGlassPair = new Grid(5, 5, 1);
            gGlassPair.Set(2, 0, 0, glassId);
            gGlassPair.Set(2, 1, 0, glassId);
            Check("玻璃叠玻璃不碎（上方是玻璃不算压）", Glass.Crush(gGlassPair) == 0);
            gGlassPair.Set(2, 2, 0, mechSand);
            Check("玻璃上再压一块实心方块，两块玻璃不会一次全碎",
                Glass.Crush(gGlassPair) == 1 && gGlassPair.Get(2, 0, 0) == glassId);
            Check("碎掉那一格由上方的方块补上", gGlassPair.Get(2, 1, 0) == mechSand);

            // 读档进来的格子没有先后，不能一开局就噼里啪啦碎一片
            var gGlassLoad = new Grid(2, 2, 1);
            var loadData = new int[4];
            loadData[0] = glassId;               // (x=0, y=0)
            loadData[2] = mechSand;              // (x=0, y=1)
            gGlassLoad.Import(loadData);
            Check("读档进来的玻璃不自己碎", Glass.Crush(gGlassLoad) == 0);
            gGlassLoad.Set(0, 1, 0, mechSand);   // 读档之后又落下来一块
            Check("读档后新落下的方块照常压碎玻璃", Glass.Crush(gGlassLoad) == 1);

            var gGlassOff = new Grid(5, 5, 1);
            gGlassOff.Set(2, 0, 0, glassId);
            gGlassOff.Set(2, 1, 0, mechSand);
            GameSettings.SetMech(GameSettings.MechKind.GlassCrush, false);
            Check("关掉玻璃压碎就不碎",
                Glass.Crush(gGlassOff) == 0 && gGlassOff.Get(2, 0, 0) == glassId);
            GameSettings.SetMech(GameSettings.MechKind.GlassCrush, true);

            // ---------- 侦测器（MC 专属，Substrata 里休眠）----------
            Check("侦测器已随 MC 表移除（玩法休眠）", BlockCatalog.ObserverId == 0);

            // ---------- 混凝土（MC 专属，Substrata 里休眠）----------
            // 新表没有混凝土粉末，ConcreteFall 开关闲置；受重力的就是沙/粉尘/砾石/盐那一组
            Check("混凝土粉末已随 MC 表移除", BlockCatalog.FindId("white_concrete_powder") == 0);
            GameSettings.SetMech(GameSettings.MechKind.SandFall, true);

            var gGlassStack = new Grid(5, 6, 1);
            gGlassStack.Set(2, 0, 0, glassId);
            gGlassStack.Set(2, 1, 0, glassId);
            gGlassStack.Set(2, 2, 0, mechSand);
            int glassCrushed = Glass.Crush(gGlassStack);
            Gravity.Settle(gGlassStack);
            Check("叠起来的玻璃从被压的那块开始碎，沙落在下面那块上",
                glassCrushed == 1 && gGlassStack.Get(2, 0, 0) == glassId && gGlassStack.Get(2, 1, 0) == mechSand);
            int glassCrushed2 = Glass.Crush(gGlassStack);
            Gravity.Settle(gGlassStack);
            Check("下一轮沙把剩下那块也压碎，一路落到底",
                glassCrushed2 == 1 && CountFilled(gGlassStack) == 1 && gGlassStack.Get(2, 0, 0) == mechSand);

            // 回归：玻璃压碎后场上留下擦不掉的玻璃残影。
            // 根因是事件顺序——以前先结算重力、后发 GlassCrushed，视图层已经把沙子的
            // 显示对象搬进玻璃格、把玻璃对象从格位表里顶丢了，再清就找不到它。
            // 现在锁死顺序：每轮先发 GlassCrushed（玻璃画面还在，清得掉），再结算重力。
            var gOrder = new GameLoop(5, 8, 1, 7);
            var order = new System.Collections.Generic.List<string>();
            gOrder.GlassCrushed += () => order.Add("crush" + gOrder.LastGlassCrushed.Count);
            gOrder.GravitySettled += () => order.Add("settle");
            gOrder.Grid.Set(2, 0, 0, glassId);
            gOrder.Grid.Set(2, 1, 0, glassId);
            gOrder.Grid.Set(2, 2, 0, mechSand);
            gOrder.CrushGlass();
            // 塌陷是当场搬方块，不再靠 Gravity 落下来，所以事件里没有 settle；
            // 两层玻璃各报一次 crush
            Check("玻璃压碎的事件顺序：每碎一层先清画面、再当场塌陷",
                order.Count == 2 && order[0] == "crush1" && order[1] == "crush1");
            Check("连碎两层后沙落到底，全程没有多报格子",
                gOrder.Grid.Get(2, 0, 0) == mechSand && CountFilled(gOrder.Grid) == 1);

            // ---------- 水能灭火 ----------
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, true);
            Fire.Reset();
            Fluids.Reset();

            var gQuench = new Grid(5, 5, 1);
            Fluids.Prepare(gQuench);
            gQuench.Set(2, 0, 0, mechLog);
            Check("木头点得着", Fire.Ignite(gQuench, 2, 0, 0) && Fire.IsBurning(2, 0, 0));

            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.WaterId);
            Fire.Tick(gQuench, 0.1f);
            Check("旁边有水，火被浇灭", !Fire.IsBurning(2, 0, 0));
            Check("浇灭记在 LastDoused 里", Fire.LastDoused.Count == 1);
            Check("浇灭后方块还留着（不会连木头一起没）", gQuench.Get(2, 0, 0) == mechLog);

            Fire.Reset();
            Fluids.Reset();

            // ---------- 作弊撤销 ----------
            GameSettings.CheatUndo = false;
            var gNoCheat = new GameLoop(6, 10, 1, 21);
            gNoCheat.HardDrop();
            Check("作弊关着不记快照", !gNoCheat.CanUndo);

            GameSettings.CheatUndo = true;
            var gUndo = new GameLoop(6, 10, 1, 22);
            int filledBefore = CountFilled(gUndo.Grid);
            gUndo.HardDrop();
            Check("硬降之后场上多了方块", CountFilled(gUndo.Grid) > filledBefore);
            Check("落定之后可以撤销", gUndo.CanUndo);

            // 撤销的是「已经落下的那一块」，正在下落的方块不能被动
            Piece falling = gUndo.Current;
            Check("撤销成功", gUndo.UndoLastPlacement());
            Check("撤销后网格回到落定之前", CountFilled(gUndo.Grid) == filledBefore);
            Check("撤销不替换正在下落的方块", gUndo.Current == falling);
            Check("一步只能撤销一次", !gUndo.CanUndo);
            GameSettings.CheatUndo = false;

            // ---------- 雪与冰遇岩浆融化 ----------
            int snowId = BlockCatalog.SnowId;
            int iceId = BlockCatalog.IceId;
            Check("方块表里有雪块和冰", snowId > 0 && iceId > 0);

            // 这几个用例都靠流体层，前面可能被别人关过，先拨回全开
            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, true);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, true);
            GameSettings.SetMech(GameSettings.MechKind.SnowMelt, true);

            // 雪块正下方压着一满格岩浆
            var gMelt = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gMelt);
            gMelt.Set(1, 1, 0, snowId);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("雪块贴着岩浆就化成水", Melt.Sweep(gMelt) == 1);
            Check("化掉的那格网格里空了", gMelt.Get(1, 1, 0) == 0);
            Check("原来是雪块的那格现在是一整格水",
                Fluids.AmountAt(1, 1, 0) == Fluids.MaxAmount && Fluids.IdAt(1, 1, 0) == Fluids.WaterId);
            Check("化掉的格子记在 LastMelted 里",
                Melt.LastMelted.Count == 1 && Melt.LastMelted[0].x == 1 && Melt.LastMelted[0].y == 1);
            Check("化过一次就不再算（那格已经不是雪了）", Melt.Sweep(gMelt) == 0);

            // 流动岩浆（不满 8 份）照样能把冰化掉
            var gMeltIce = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltIce);
            gMeltIce.Set(2, 2, 0, iceId);
            Fluids.SetAmount(3, 2, 0, 3, Fluids.LavaId);
            Check("冰贴着流动中的岩浆也化",
                Melt.Sweep(gMeltIce) == 1 && gMeltIce.Get(2, 2, 0) == 0);

            // 前后方向（z 轴）也要认，不能只判同一层
            var gMeltZ = new Grid(3, 3, 3);
            Fluids.Reset();
            Fluids.Prepare(gMeltZ);
            gMeltZ.Set(1, 1, 1, iceId);
            Fluids.SetAmount(1, 1, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("z 方向挨着岩浆也化", Melt.Sweep(gMeltZ) == 1);

            // 隔着两格、或者只在斜对角，都不该化
            var gMeltFar = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltFar);
            gMeltFar.Set(1, 1, 0, snowId);
            Fluids.SetAmount(3, 3, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("离岩浆远就不化", Melt.Sweep(gMeltFar) == 0 && gMeltFar.Get(1, 1, 0) == snowId);

            var gMeltDiag = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltDiag);
            gMeltDiag.Set(2, 2, 0, snowId);
            Fluids.SetAmount(3, 3, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("斜对角挨着岩浆不算（只认六向）",
                Melt.Sweep(gMeltDiag) == 0 && gMeltDiag.Get(2, 2, 0) == snowId);

            // 别的方块挨着岩浆照旧没事
            var gMeltOther = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltOther);
            gMeltOther.Set(1, 1, 0, mechObsidian > 0 ? mechObsidian : mechSand);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("石头 / 黑曜石贴着岩浆不化", Melt.Sweep(gMeltOther) == 0);

            // 开关关掉：方块照常出现，只是不再化
            var gMeltOff = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltOff);
            gMeltOff.Set(1, 1, 0, snowId);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            GameSettings.SetMech(GameSettings.MechKind.SnowMelt, false);
            Check("关掉融化开关就不化",
                Melt.Sweep(gMeltOff) == 0 && gMeltOff.Get(1, 1, 0) == snowId);
            GameSettings.SetMech(GameSettings.MechKind.SnowMelt, true);

            // 化出来的水压在岩浆上，下一次流体推进就会被烫成黑曜石
            var gMeltBoil = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltBoil);
            gMeltBoil.Set(1, 1, 0, snowId);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Melt.Sweep(gMeltBoil);
            for (int i = 0; i < 10; i++)
                Fluids.Tick(gMeltBoil, 0.2f);
            Check("化出来的水被下面的岩浆烫成黑曜石",
                gMeltBoil.Get(1, 0, 0) == Fluids.ObsidianId);

            // 化掉之后上面压着的方块要塌下来（GameLoop.MeltSnow 里补的那次重力结算）
            var gMeltDrop = new Grid(4, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gMeltDrop);
            gMeltDrop.Set(1, 0, 0, snowId);
            gMeltDrop.Set(1, 1, 0, mechSand);
            Fluids.SetAmount(2, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            int meltedDrop = Melt.Sweep(gMeltDrop);
            Gravity.Settle(gMeltDrop);
            Check("雪块化掉之后上面压着的沙落到地上",
                meltedDrop == 1 && gMeltDrop.Get(1, 0, 0) == mechSand && gMeltDrop.Get(1, 1, 0) == 0);

            // ---------- 着火与蔓延 ----------
            int firePlanks = 0, fireLeaves = 0, fireStone = 0, firePaper = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "plank") firePlanks = i + 1;
                else if (n == "leaf") fireLeaves = i + 1;
                else if (n == "stone") fireStone = i + 1;
                else if (n == "paper_kraft") firePaper = i + 1;
            }
            Check("方块表里有木板 / 树叶 / 纸 / 石头",
                  firePlanks > 0 && fireLeaves > 0 && firePaper > 0 && fireStone > 0);
            Check("木板 / 树叶 / 纸算可燃，石头不算",
                  Fire.IsFlammable(firePlanks) && Fire.IsFlammable(fireLeaves) &&
                  Fire.IsFlammable(firePaper) && !Fire.IsFlammable(fireStone));
            Check("空格子不算可燃", !Fire.IsFlammable(0));

            GameSettings.SetMech(GameSettings.MechKind.FireBurn, true);

            // 木板下面压着一满格岩浆 → 点着
            var gFire = new Grid(4, 6, 1);
            Fluids.Reset();
            Fluids.Prepare(gFire);
            Fire.Reset();
            gFire.Set(1, 1, 0, firePlanks);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("木板贴着岩浆就被点着", Fire.IgniteContact(gFire) == 1 && Fire.IsBurning(1, 1, 0));
            Check("刚点着时进度是 0", Mathf.Abs(Fire.BurnProgress(1, 1, 0)) < 0.01f);

            // 烧到底：那一格要空掉，坐标和烧掉前的方块都要报出来。
            // 放在这里而不是所有点火用例之后：后面每个用例开头都 Fire.Reset()，
            // 会把这一处火一起清掉，再 Tick 就什么都烧不到了。
            bool burnedOut = false;
            for (int i = 0; i < 40 && !burnedOut; i++)
                burnedOut = Fire.Tick(gFire, 0.25f);
            Check("烧完之后那一格空了", burnedOut && gFire.Get(1, 1, 0) == 0);
            Check("烧完的格子和方块都报了出来",
                  Fire.LastBurnedOut.Count == 1 && Fire.LastBurnedOut[0].x == 1 &&
                  Fire.LastBurnedOut[0].y == 1 && Fire.LastBurnedBlocks.Count == 1 &&
                  Fire.LastBurnedBlocks[0] == firePlanks);
            Check("烧完之后就不再烧了（那格已经空了）", Fire.BurningCells.Count == 0);

            // 离岩浆远的不点；斜对角的也不点（只认六向）
            var gFireFar = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gFireFar);
            Fire.Reset();
            gFireFar.Set(1, 1, 0, firePlanks);
            Fluids.SetAmount(3, 3, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("离岩浆远就不着火", Fire.IgniteContact(gFireFar) == 0);

            var gFireDiag = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gFireDiag);
            Fire.Reset();
            gFireDiag.Set(2, 2, 0, firePlanks);
            Fluids.SetAmount(3, 3, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("斜对角挨着岩浆不算（只认六向）", Fire.IgniteContact(gFireDiag) == 0);

            // 石头挨着岩浆也不着
            var gFireStone = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gFireStone);
            Fire.Reset();
            gFireStone.Set(1, 1, 0, fireStone);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("石头贴着岩浆不着火", Fire.IgniteContact(gFireStone) == 0);

            // 火顺着木柱往上窜：底下点着，整根烧光
            var gSpread = new Grid(3, 6, 1);
            Fluids.Reset();
            Fluids.Prepare(gSpread);
            Fire.Reset();
            for (int y = 1; y <= 4; y++)
                gSpread.Set(1, y, 0, firePlanks);   // 纸质/羊毛类已随 MC 表移除，整根都用木板
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            Check("木柱底下那格先着", Fire.IgniteContact(gSpread) == 1);
            for (int i = 0; i < 60; i++)
                Fire.Tick(gSpread, 0.25f);
            bool allGone = true;
            for (int y = 1; y <= 4; y++)
                if (gSpread.Get(1, y, 0) != 0)
                    allGone = false;
            Check("火顺着柱子往上烧，整根烧光", allGone && Fire.BurningCells.Count == 0);

            // 水能灭火：烧着的方块旁边有水 → 火灭，方块留着
            var gDouse = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gDouse);
            Fire.Reset();
            gDouse.Set(1, 1, 0, firePlanks);
            Check("干燥的木板点得着", Fire.Ignite(gDouse, 1, 1, 0));
            Fluids.SetAmount(2, 1, 0, Fluids.MaxAmount, Fluids.WaterId);
            Fire.Tick(gDouse, 0.1f);
            Check("水漫过来火就灭了（方块还在）",
                  !Fire.IsBurning(1, 1, 0) && gDouse.Get(1, 1, 0) == firePlanks);
            Check("旁边有水的方块点不着", !Fire.Ignite(gDouse, 1, 1, 0));

            // 关掉开关：点不着，已经在烧的也直接掐灭
            var gFireOff = new Grid(4, 4, 1);
            Fluids.Reset();
            Fluids.Prepare(gFireOff);
            Fire.Reset();
            gFireOff.Set(1, 1, 0, firePlanks);
            Fluids.SetAmount(1, 0, 0, Fluids.MaxAmount, Fluids.LavaId);
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, false);
            Check("关掉着火开关就点不着", Fire.IgniteContact(gFireOff) == 0);
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, true);
            Fire.Ignite(gFireOff, 1, 1, 0);
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, false);
            Fire.Tick(gFireOff, 0.1f);
            Check("中途关掉开关，正在烧的直接掐灭（方块留着）",
                  Fire.BurningCells.Count == 0 && gFireOff.Get(1, 1, 0) == firePlanks);
            GameSettings.SetMech(GameSettings.MechKind.FireBurn, true);
            Fire.Reset();

            // 火苗贴图：原版 fire_0 / fire_1 两张 16x512 拼成的 32 帧动画条（左半 / 右半）
            string firePng = System.IO.Path.Combine(Application.dataPath, "Resources/Fire.png");
            bool firePngOk = System.IO.File.Exists(firePng);
            Check("火苗贴图在（Resources/Fire.png）", firePngOk);
            if (firePngOk)
            {
                var fireTex = new Texture2D(2, 2);
                fireTex.LoadImage(System.IO.File.ReadAllBytes(firePng));
                Check("火苗贴图是 32 帧动画条（32x512）", fireTex.width == 32 && fireTex.height == 512);

                // GetPixels32 是左下原点，图片第 f 帧那 16 行倒过来数
                Color32[] fpx = fireTex.GetPixels32();
                int opaque = 0;
                bool everyFrame = true;
                for (int f = 0; f < 32; f++)
                {
                    int frameOpaque = 0;
                    int pyTop = 512 - f * 16;
                    for (int y = pyTop - 16; y < pyTop; y++)
                        for (int x = 0; x < 32; x++)
                            if (fpx[y * 32 + x].a > 0)
                                frameOpaque++;
                    if (frameOpaque == 0)
                        everyFrame = false;
                    opaque += frameOpaque;
                }
                Check("32 帧每一帧都有火苗", everyFrame);
                // 黑底必须转成透明（原版靠 PNG 的 tRNS），不然火苗就是一整块黑方块
                Check("火苗是镂空的（黑底转成了透明）",
                      opaque > 32 * 512 * 0.2 && opaque < 32 * 512 * 0.9);

                int halves = 0;
                for (int y = 0; y < 512; y += 7)
                    for (int x = 0; x < 16; x++)
                        if (fpx[y * 32 + x].a != fpx[y * 32 + x + 16].a)
                            halves++;
                Check("左半是 fire_0、右半是 fire_1（两套火苗叠着放）", halves > 20);
            }

            Material fireMat = Resources.Load<Material>("FireMat");
            Check("火苗材质挂的是那张动画条 + alpha test shader",
                  fireMat != null && fireMat.shader != null &&
                  fireMat.shader.name == "MinecraftTetris/FireCutout" &&
                  fireMat.mainTexture != null);

            // 火苗面片：露几个面铺几片，两层（fire_0 + fire_1）
            int allSides = GridView.FireBitPx | GridView.FireBitNx | GridView.FireBitPz |
                           GridView.FireBitNz | GridView.FireBitUp;
            Mesh fireAll = GridView.BuildFireMesh(allSides);
            Check("五面全露着：铺 10 片火苗（两层 x 五面）",
                  fireAll.vertexCount == 40 && fireAll.GetTriangles(0).Length == 60);
            float hiY = 0f;
            for (int i = 0; i < fireAll.vertexCount; i++)
                if (fireAll.vertices[i].y > hiY)
                    hiY = fireAll.vertices[i].y;
            Check("上面那格空着，火苗窜得比方块高一截", Mathf.Abs(hiY - 0.9f) < 0.001f);

            float flatY = 0f;
            Mesh fireFlat = GridView.BuildFireMesh(GridView.FireBitPz);
            for (int i = 0; i < fireFlat.vertexCount; i++)
                if (fireFlat.vertices[i].y > flatY)
                    flatY = fireFlat.vertices[i].y;
            Check("上面被压着，火苗只长到和方块齐平（不插进上一格）",
                  fireFlat.vertexCount == 8 && Mathf.Abs(flatY - 0.5f) < 0.001f);
            Check("埋在方块堆里就一片都不画", GridView.BuildFireMesh(0).vertexCount == 0);

            // ---------- 海绵（MC 专属，Substrata 里休眠）----------
            Check("海绵已随 MC 表移除（吸水玩法休眠）",
                  Sponge.SpongeId <= 0 && Sponge.WetSpongeId <= 0);

            // 拨回全开，别把开关状态留给玩家
            for (int i = 0; i < GameSettings.MechCount; i++)
                GameSettings.SetMech((GameSettings.MechKind)i, true);

            sb.AppendLine();
            sb.AppendLine("通过 " + pass + " 项，失败 " + fail + " 项");
            Debug.Log("[MinecraftTetris 自检]\n" + sb);

            EditorApplication.Exit(fail > 0 ? 1 : 0);
        }

        /// <summary>
        /// 模型最高点到脚底的像素距离。算的是「关节 + 盒子中心 + 半个高度」，
        /// 不带旋转 —— 目前只有雪傀儡的手臂带旋转，而它远不是最高的部件。
        /// </summary>
        static float PartTop(GolemPartDef[] parts)
        {
            float top = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                for (int k = 0; k < parts[i].Cubes.Length; k++)
                {
                    GolemCube c = parts[i].Cubes[k];
                    float y = parts[i].Pivot.y + c.Center.y + c.H * 0.5f;
                    if (y > top)
                        top = y;
                }
            }
            return top;
        }

        /// <summary>场上还有多少格有方块（水不算，水不写网格）。</summary>
        static int CountFilled(Grid g)
        {
            int n = 0;
            for (int x = 0; x < g.SizeX; x++)
                for (int y = 0; y < g.SizeY; y++)
                    for (int z = 0; z < g.SizeZ; z++)
                        if (g.Get(x, y, z) > 0)
                            n++;
            return n;
        }

        /// <summary>
        /// 逐面核对傀儡部件的 UV 是否都落在贴图的不透明区里。
        ///
        /// 贴图直接从磁盘读，不走 Resources.Load：Resources 里的贴图默认不开 Read/Write，
        /// GetPixels 会抛异常；Texture2D.LoadImage 没有这个限制。
        ///
        /// 一个 w×h×d 的方块在 MC 实体贴图上展开成：上窄带（顶面 + 底面，宽 2w，高 d）
        /// 压在四条侧面（宽 2(w+d)，高 h）上面。哪一段差一格，那面就采到透明像素，
        /// shader 的 clip 把它整面裁掉 —— 表现出来就是「这块没贴上贴图」。
        /// </summary>
        static string CheckGolemUVs(string texName, int texW, int texH, GolemPartDef[] parts, out int faceCount)
        {
            faceCount = 0;

            string path = System.IO.Path.Combine(Application.dataPath, "Resources/Golem/" + texName + ".png");
            if (!System.IO.File.Exists(path))
                return "找不到 " + path;

            var tex = new Texture2D(2, 2);
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            Color32[] px = tex.GetPixels32();   // 左下原点

            string[] faceNames = { "顶", "底", "左", "右", "正", "背" };
            int bad = 0;
            string where = "";

            for (int i = 0; i < parts.Length; i++)
            {
                for (int k = 0; k < parts[i].Cubes.Length; k++)
                {
                    GolemCube c = parts[i].Cubes[k];

                    // 与 GolemModel.BuildCube 里的六个面一一对应（图片坐标，左上原点）
                    int[][] rects =
                    {
                        new[] { c.U + c.D,             c.V,       c.U + c.D + c.W,           c.V + c.D },
                        new[] { c.U + c.D + c.W,       c.V,       c.U + c.D + c.W * 2,       c.V + c.D },
                        new[] { c.U + c.D + c.W,       c.V + c.D, c.U + c.D + c.W + c.D,     c.V + c.D + c.H },
                        new[] { c.U,                   c.V + c.D, c.U + c.D,                 c.V + c.D + c.H },
                        new[] { c.U + c.D,             c.V + c.D, c.U + c.D + c.W,           c.V + c.D + c.H },
                        new[] { c.U + c.D + c.W + c.D, c.V + c.D, c.U + c.D + c.W * 2 + c.D, c.V + c.D + c.H },
                    };

                    for (int f = 0; f < rects.Length; f++)
                    {
                        faceCount++;
                        int empty = 0, total = 0;
                        for (int y = rects[f][1]; y < rects[f][3]; y++)
                        {
                            for (int x = rects[f][0]; x < rects[f][2]; x++)
                            {
                                total++;
                                bool outside = x < 0 || y < 0 || x >= texW || y >= texH;
                                if (outside)
                                {
                                    empty++;
                                    continue;
                                }
                                if (px[(texH - 1 - y) * texW + x].a == 0)
                                    empty++;
                            }
                        }

                        if (total == 0 || empty > 0)
                        {
                            bad++;
                            where += parts[i].Part + faceCount + faceNames[f] + " ";
                        }
                    }
                }
            }

            return bad == 0 ? "" : bad + " 个面采到空白：" + where.Trim();
        }

        /// <summary>
        /// 检查铜傀儡的眼睛贴图：不透明像素必须存在、数量对得上（原版就 8 个），
        /// 而且要全部落在头「正面」那块 UV 矩形里。返回空字符串表示没问题。
        /// </summary>
        static string CheckEyeBox(string texName, int x0, int y0, int x1, int y1)
        {
            string path = System.IO.Path.Combine(Application.dataPath, "Resources/Golem/" + texName + ".png");
            if (!System.IO.File.Exists(path))
                return "找不到 " + path;

            var tex = new Texture2D(2, 2);
            tex.LoadImage(System.IO.File.ReadAllBytes(path));
            int w = tex.width, h = tex.height;
            Color32[] px = tex.GetPixels32();   // 左下原点

            int bx0 = 9999, by0 = 9999, bx1 = -1, by1 = -1, n = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (px[(h - 1 - y) * w + x].a == 0)
                        continue;
                    n++;
                    if (x < bx0) bx0 = x;
                    if (y < by0) by0 = y;
                    if (x > bx1) bx1 = x;
                    if (y > by1) by1 = y;
                }
            }

            if (n == 0)
                return "整张全透明，没有眼睛";
            if (n > 40)
                return "不透明像素 " + n + " 个，太多，不像只有眼睛";

            if (bx0 < x0 || by0 < y0 || bx1 >= x1 || by1 >= y1)
                return "眼睛在 (" + bx0 + "," + by0 + ")-(" + bx1 + "," + by1 +
                       ")，超出了头正面 (" + x0 + "," + y0 + ")-(" + x1 + "," + y1 + ")";

            return "";
        }

        /// <summary>把一条还原动画跑完（消失 + 重建）。</summary>
        static void RunRestore(Grid g)
        {
            for (int i = 0; i < 4000 && Structure.Busy; i++)
                Structure.Tick(g, 0.05f);
        }

        /// <summary>
        /// 拾取的参照实现：不管什么步进算法，对每个格子单独做一次「射线 vs 单位立方体」
        /// 的 slab 相交，取最近的一个。写得又慢又啰嗦，就是为了跟 VoxelRaycast 的思路
        /// 完全不同——两边一致才有说服力。
        /// </summary>
        static bool BruteFirstHit(Vector3 origin, Vector3 dir, int size,
            HashSet<Vector3Int> solid, out Vector3Int cell, out float dist)
        {
            cell = Vector3Int.zero;
            dist = float.MaxValue;
            bool found = false;

            foreach (Vector3Int c in solid)
            {
                float tMin = float.NegativeInfinity;
                float tMax = float.PositiveInfinity;
                bool ok = true;

                for (int axis = 0; axis < 3 && ok; axis++)
                {
                    float oComp = axis == 0 ? origin.x : (axis == 1 ? origin.y : origin.z);
                    float dComp = axis == 0 ? dir.x : (axis == 1 ? dir.y : dir.z);
                    float loP = axis == 0 ? c.x : (axis == 1 ? c.y : c.z);

                    if (Mathf.Abs(dComp) < 1e-9f)
                    {
                        if (oComp < loP || oComp > loP + 1f)
                            ok = false;
                        continue;
                    }

                    float t1 = (loP - oComp) / dComp;
                    float t2 = (loP + 1f - oComp) / dComp;
                    if (t1 > t2)
                    {
                        float tmp = t1; t1 = t2; t2 = tmp;
                    }
                    if (t1 > tMin) tMin = t1;
                    if (t2 < tMax) tMax = t2;
                    if (tMin > tMax) ok = false;
                }

                if (!ok || tMax < 0f)
                    continue;

                // 起点已经在格子里面时入射距离是负的，按 0 算——它肯定比任何
                // 后面的格子都近，这和 DDA「入射那一格优先」的规则一致
                float t = tMin < 0f ? 0f : tMin;
                if (t < dist)
                {
                    dist = t;
                    cell = c;
                    found = true;
                }
            }

            return found;
        }
    }
}
