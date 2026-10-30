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
            Check("只有少数方块是镂空的（带洞的贴图）", cutCount > 0 && cutCount < 60);

            // 四种氧化程度的铜格栅都要镂空；实心铜块不能镂空
            bool gratesCut = true;
            foreach (string g in new[] { "copper_grate", "exposed_copper_grate",
                                         "weathered_copper_grate", "oxidized_copper_grate" })
            {
                int id = IdOf(g);
                if (id == 0 || !BlockCatalog.IsCutoutBlock(id))
                    gratesCut = false;
            }
            Check("四种铜格栅都按镂空渲染", gratesCut);
            Check("实心铜块不镂空", !BlockCatalog.IsCutoutBlock(IdOf("copper_block")) &&
                                    !BlockCatalog.IsCutoutBlock(IdOf("cut_copper")));
            Check("空格子 / 越界不算镂空", !BlockCatalog.IsCutoutBlock(0) &&
                                           !BlockCatalog.IsCutoutBlock(BlockCatalog.Blocks.Length + 1));

            // 图集里那一格确实有透明的像素（表和贴图对得上）
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
                            if (apx[(oy + y) * atlasTex.width + ox + x].a < 128)
                                holes++;
                    return holes;
                }

                Check("铜格栅贴图确实有洞（侧面 16x16 里有透明像素）",
                      Holes(IdOf("copper_grate")) > 20);
                Check("实心铜块贴图没有洞", Holes(IdOf("copper_block")) == 0);
            }

            Material cutMat = Resources.Load<Material>("AtlasCutoutMat");
            Check("镂空材质挂的是方块图集 + alpha test shader",
                  cutMat != null && cutMat.shader != null &&
                  cutMat.shader.name == "MinecraftTetris/AtlasCutout" &&
                  cutMat.mainTexture != null);
            if (cutMat != null)
            {
                Check("铜格栅用的是镂空材质",
                      BlockMesh.GetMaterial(BlockCatalog.Of(IdOf("copper_grate"))) == cutMat);
                Check("实心方块还是用普通图集材质",
                      BlockMesh.GetMaterial(BlockCatalog.Of(IdOf("copper_block"))) == BlockMesh.AtlasMaterial);
                // 玻璃 / 树叶本来就是半透明材质，别被镂空这条规则改回去
                Check("玻璃仍然是半透明材质（没被镂空规则改掉）",
                      BlockMesh.GetMaterial(BlockCatalog.Of(BlockCatalog.GlassId)) != cutMat);
            }

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

            // 炸不掉的方块：基岩、三种命令方块、黑曜石、哭泣的黑曜石、远古残骸、
            // 下界合金块、强化深板岩（原版 TNT 也炸不动它们）
            int bedrockForBlast = 0, obsidianId = 0, cryingObsidianId = 0;
            int ancientDebrisId = 0, netheriteBlockId = 0, reinforcedDeepslateId = 0;
            int commandBlockId = 0, chainCommandBlockId = 0, repeatingCommandBlockId = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "bedrock") bedrockForBlast = i + 1;
                else if (n == "obsidian") obsidianId = i + 1;
                else if (n == "crying_obsidian") cryingObsidianId = i + 1;
                else if (n == "ancient_debris") ancientDebrisId = i + 1;
                else if (n == "netherite_block") netheriteBlockId = i + 1;
                else if (n == "reinforced_deepslate") reinforcedDeepslateId = i + 1;
                else if (n == "command_block") commandBlockId = i + 1;
                else if (n == "chain_command_block") chainCommandBlockId = i + 1;
                else if (n == "repeating_command_block") repeatingCommandBlockId = i + 1;
            }
            Check("黑曜石和哭泣的黑曜石都在方块表里", obsidianId > 0 && cryingObsidianId > 0);
            Check("防爆的那几种都在方块表里",
                ancientDebrisId > 0 && netheriteBlockId > 0 && reinforcedDeepslateId > 0 &&
                commandBlockId > 0 && chainCommandBlockId > 0 && repeatingCommandBlockId > 0);
            Check("基岩炸不掉", BlockCatalog.IsBlastProof(bedrockForBlast));
            Check("黑曜石炸不掉", BlockCatalog.IsBlastProof(obsidianId));
            Check("哭泣的黑曜石炸不掉", BlockCatalog.IsBlastProof(cryingObsidianId));
            Check("远古残骸炸不掉", BlockCatalog.IsBlastProof(ancientDebrisId));
            Check("下界合金块炸不掉", BlockCatalog.IsBlastProof(netheriteBlockId));
            Check("强化深板岩炸不掉", BlockCatalog.IsBlastProof(reinforcedDeepslateId));
            Check("命令方块炸不掉",
                BlockCatalog.IsBlastProof(commandBlockId) &&
                BlockCatalog.IsBlastProof(chainCommandBlockId) &&
                BlockCatalog.IsBlastProof(repeatingCommandBlockId));
            Check("结构方块故意留成炸得掉", !BlockCatalog.IsBlastProof(BlockCatalog.StructureId));
            Check("普通方块炸得掉", !BlockCatalog.IsBlastProof(stoneId));
            Check("空格子不算炸不掉", !BlockCatalog.IsBlastProof(0));

            var gBlast = new Grid(10, 20, 1);
            gBlast.Set(4, 5, 0, Tnt.TntId);
            gBlast.Set(5, 5, 0, Tnt.RedstoneId);
            gBlast.Set(3, 5, 0, obsidianId);        // 爆炸半径内
            gBlast.Set(4, 6, 0, stoneId);            // 爆炸半径内
            gBlast.Set(4, 3, 0, cryingObsidianId);  // 爆炸半径边缘
            gBlast.Set(5, 4, 0, netheriteBlockId);  // 爆炸半径内
            Tnt.Reset();
            Tnt.PrimeContact(gBlast);
            Tnt.Tick(gBlast, Tnt.FuseSeconds);
            Check("爆炸后黑曜石还在", gBlast.Get(3, 5, 0) == obsidianId);
            Check("爆炸后哭泣的黑曜石还在", gBlast.Get(4, 3, 0) == cryingObsidianId);
            Check("爆炸后下界合金块还在", gBlast.Get(5, 4, 0) == netheriteBlockId);
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

            // ---------- 特殊方块机制开关 ----------
            Check("机制开关一共十四项", GameSettings.MechCount == 14);

            int mechSand = 0, mechLog = 0, mechObsidian = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string bn = BlockCatalog.Blocks[i].Name;
                if (bn == "sand") mechSand = i + 1;
                else if (bn == "oak_log") mechLog = i + 1;
                else if (bn == "obsidian") mechObsidian = i + 1;
            }

            // TNT 爆炸与接触点火
            var gMechTnt = new Grid(6, 6, 1);
            gMechTnt.Set(2, 0, 0, Tnt.TntId);
            gMechTnt.Set(3, 0, 0, Tnt.RedstoneId);
            Tnt.Reset();
            Check("开关全开时 TNT 挨着红石块会被点着", Tnt.PrimeContact(gMechTnt) == 1);
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
            Check("关掉水/岩浆流动，流体不动", !Fluids.Tick(gMechFluid, 1f));
            GameSettings.SetMech(GameSettings.MechKind.WaterFlow, true);
            GameSettings.SetMech(GameSettings.MechKind.LavaFlow, true);
            Check("开关拨回来水会流", Fluids.Tick(gMechFluid, 1f));
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
            Check("上面压着方块，玻璃就碎", Glass.Crush(gGlass) == 1 && gGlass.Get(2, 0, 0) == 0);
            Check("碎掉的格子记在 LastCrushed 里",
                Glass.LastCrushed.Count == 1 && Glass.LastCrushed[0].x == 2 && Glass.LastCrushed[0].y == 0);

            Gravity.Settle(gGlass);
            Check("玻璃碎完，上面的沙落到底", gGlass.Get(2, 0, 0) == mechSand);

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
                Glass.Crush(gGlassPair) == 1 && gGlassPair.Get(2, 1, 0) == 0 && gGlassPair.Get(2, 0, 0) == glassId);

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
            int firePlanks = 0, fireWool = 0, fireLeaves = 0, fireStone = 0;
            for (int i = 0; i < BlockCatalog.Blocks.Length; i++)
            {
                string n = BlockCatalog.Blocks[i].Name;
                if (n == "oak_planks") firePlanks = i + 1;
                else if (n == "white_wool") fireWool = i + 1;
                else if (n == "oak_leaves") fireLeaves = i + 1;
                else if (n == "stone") fireStone = i + 1;
            }
            Check("方块表里有木板 / 羊毛 / 树叶 / 石头",
                  firePlanks > 0 && fireWool > 0 && fireLeaves > 0 && fireStone > 0);
            Check("木板 / 羊毛 / 树叶算可燃，石头不算",
                  Fire.IsFlammable(firePlanks) && Fire.IsFlammable(fireWool) &&
                  Fire.IsFlammable(fireLeaves) && !Fire.IsFlammable(fireStone));
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
                gSpread.Set(1, y, 0, y % 2 == 1 ? firePlanks : fireWool);
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

            // ---------- 海绵吸水 ----------
            int fireSponge = Sponge.SpongeId;
            int fireWetSponge = Sponge.WetSpongeId;
            Check("方块表里有海绵和湿海绵", fireSponge > 0 && fireWetSponge > 0 && fireSponge != fireWetSponge);

            GameSettings.SetMech(GameSettings.MechKind.SpongeSoak, true);

            // 海绵旁边有一格水：吸掉，海绵变湿
            var gSp = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gSp);
            Sponge.Reset();
            gSp.Set(2, 2, 0, fireSponge);
            Fluids.SetAmount(1, 2, 0, Fluids.MaxAmount, Fluids.WaterId);
            Check("海绵挨着水就把水吸掉", Sponge.SoakAll(gSp) == 1);
            Check("被吸的那格没有水了", Fluids.AmountAt(1, 2, 0) == 0);
            Check("吸到水的海绵变成了湿海绵",
                  gSp.Get(2, 2, 0) == fireWetSponge && Sponge.LastSoaks.Count == 1 &&
                  Sponge.LastSoaks[0].BecameWet && Sponge.LastSoaks[0].Cells.Count == 1);

            // 周围没水：保持干燥，之后还能接着吸
            var gSpDry = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gSpDry);
            Sponge.Reset();
            gSpDry.Set(2, 2, 0, fireSponge);
            Check("周围没水就不变湿", Sponge.SoakAll(gSpDry) == 0 && gSpDry.Get(2, 2, 0) == fireSponge);

            // 湿海绵不能再吸：把它真换成湿海绵再试
            gSpDry.Set(2, 2, 0, fireWetSponge);
            Fluids.SetAmount(1, 2, 0, Fluids.MaxAmount, Fluids.WaterId);
            Check("湿海绵吸不动水",
                  Sponge.SoakAll(gSpDry) == 0 && Fluids.AmountAt(1, 2, 0) == Fluids.MaxAmount &&
                  gSpDry.Get(2, 2, 0) == fireWetSponge);

            // 半格水也算水，照样吸走
            var gSpHalf = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gSpHalf);
            Sponge.Reset();
            gSpHalf.Set(2, 2, 0, fireSponge);
            Fluids.SetAmount(3, 2, 0, 2, Fluids.WaterId);
            Check("半格的流动水也吸",
                  Sponge.SoakAll(gSpHalf) == 1 && Fluids.AmountAt(3, 2, 0) == 0);

            // 隔着一整列石墙，墙后面的水吸不到（BFS 走不过实心方块；墙只有一格高的话
            // 水的搜索会从上面绕过去，所以这里把 x=2 那一列从底堵到顶）
            var gSpWall = new Grid(7, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gSpWall);
            Sponge.Reset();
            gSpWall.Set(1, 1, 0, fireSponge);
            for (int y = 0; y < gSpWall.SizeY; y++)
                gSpWall.Set(2, y, 0, fireStone);
            Fluids.SetAmount(4, 1, 0, Fluids.MaxAmount, Fluids.WaterId);
            Check("隔着石墙的水吸不到",
                  Sponge.SoakAll(gSpWall) == 0 && Fluids.AmountAt(4, 1, 0) == Fluids.MaxAmount &&
                  gSpWall.Get(1, 1, 0) == fireSponge);

            // 吸水量有上限：原版一次最多 65 格
            var gSpMax = new Grid(20, 5, 5);
            Fluids.Reset();
            Fluids.Prepare(gSpMax);
            Sponge.Reset();
            gSpMax.Set(10, 0, 2, fireSponge);
            int spWater = 0;
            for (int x = 0; x < gSpMax.SizeX; x++)
                for (int y = 1; y < 4; y++)
                    for (int z = 0; z < gSpMax.SizeZ; z++)
                    {
                        Fluids.SetAmount(x, y, z, Fluids.MaxAmount, Fluids.WaterId);
                        spWater++;
                    }
            int soakedMax = Sponge.SoakAll(gSpMax);
            Check("一次最多吸 65 格水",
                  soakedMax == Sponge.MaxCells && soakedMax < spWater &&
                  gSpMax.Get(10, 0, 2) == fireWetSponge);

            // 开关关掉就不吸
            var gSpOff = new Grid(5, 5, 1);
            Fluids.Reset();
            Fluids.Prepare(gSpOff);
            Sponge.Reset();
            gSpOff.Set(2, 2, 0, fireSponge);
            Fluids.SetAmount(1, 2, 0, Fluids.MaxAmount, Fluids.WaterId);
            GameSettings.SetMech(GameSettings.MechKind.SpongeSoak, false);
            Check("关掉海绵开关就不吸",
                  Sponge.SoakAll(gSpOff) == 0 && Fluids.AmountAt(1, 2, 0) == Fluids.MaxAmount &&
                  gSpOff.Get(2, 2, 0) == fireSponge);
            GameSettings.SetMech(GameSettings.MechKind.SpongeSoak, true);
            Sponge.Reset();

            // ---------- 傀儡召唤 ----------
            int golemHead = BlockCatalog.CarvedPumpkinId > 0
                ? BlockCatalog.CarvedPumpkinId
                : BlockCatalog.PumpkinId;
            Check("方块表里有南瓜 / 雪块 / 铁块 / 铜块",
                golemHead > 0 && BlockCatalog.SnowId > 0 &&
                BlockCatalog.IronBlockId > 0 && BlockCatalog.CopperBlockId > 0);

            GameSettings.SetMech(GameSettings.MechKind.Golem, true);
            Golem.Reset();

            // 雪傀儡：南瓜头 + 竖着两格雪
            var gSnow = new Grid(4, 5, 1);
            gSnow.Set(1, 1, 0, BlockCatalog.SnowId);
            gSnow.Set(1, 2, 0, BlockCatalog.SnowId);
            gSnow.Set(1, 3, 0, golemHead);
            Check("南瓜头压着两格雪，召出雪傀儡",
                Golem.Check(gSnow) == 1 && Golem.LastSummoned[0].Kind == GolemKind.Snow);
            Check("雪傀儡消耗三格（南瓜头 + 两格雪）",
                Golem.LastSummoned[0].CellCount == 3 &&
                gSnow.Get(1, 1, 0) == 0 && gSnow.Get(1, 2, 0) == 0 && gSnow.Get(1, 3, 0) == 0);
            Check("召出来的傀儡记进收藏",
                Golem.Collection.Count == 1 && Golem.Collection[0].Kind == GolemKind.Snow);

            // 只有一格雪不够
            var gSnowOne = new Grid(4, 4, 1);
            gSnowOne.Set(1, 1, 0, BlockCatalog.SnowId);
            gSnowOne.Set(1, 2, 0, golemHead);
            Check("只垫一格雪召不出雪傀儡",
                Golem.Check(gSnowOne) == 0 && gSnowOne.Get(1, 2, 0) == golemHead);

            // 铁傀儡：倒 T（竖两格 + 上面那格左右各一块）
            var gIron = new Grid(6, 6, 1);
            gIron.Set(2, 1, 0, BlockCatalog.IronBlockId);
            gIron.Set(2, 2, 0, BlockCatalog.IronBlockId);
            gIron.Set(1, 2, 0, BlockCatalog.IronBlockId);
            gIron.Set(3, 2, 0, BlockCatalog.IronBlockId);
            gIron.Set(2, 3, 0, golemHead);
            Check("南瓜头压着倒 T 的四块铁，召出铁傀儡",
                Golem.Check(gIron) == 1 && Golem.LastSummoned[0].Kind == GolemKind.Iron);
            Check("铁傀儡消耗五格",
                Golem.LastSummoned[0].CellCount == 5 && CountFilled(gIron) == 0);

            // 少了左右那两块就只是「两根柱子」
            var gIronBare = new Grid(6, 6, 1);
            gIronBare.Set(2, 1, 0, BlockCatalog.IronBlockId);
            gIronBare.Set(2, 2, 0, BlockCatalog.IronBlockId);
            gIronBare.Set(2, 3, 0, golemHead);
            Check("只有竖着的两格铁召不出铁傀儡",
                Golem.Check(gIronBare) == 0 && gIronBare.Get(2, 3, 0) == golemHead);

            // 铜傀儡：一格铜就够，氧化阶段跟着那块铜走
            var gCopper = new Grid(4, 4, 1);
            gCopper.Set(1, 1, 0, BlockCatalog.CopperBlockId);
            gCopper.Set(1, 2, 0, golemHead);
            Check("南瓜头压着一格铜，召出铜傀儡",
                Golem.Check(gCopper) == 1 && Golem.LastSummoned[0].Kind == GolemKind.Copper);
            Check("普通铜块召出来的傀儡是没氧化的",
                Golem.LastSummoned[0].Oxidation == 0);

            if (BlockCatalog.WeatheredCopperId > 0)
            {
                var gWeathered = new Grid(4, 4, 1);
                gWeathered.Set(1, 1, 0, BlockCatalog.WeatheredCopperId);
                gWeathered.Set(1, 2, 0, golemHead);
                Check("用风化铜块召唤，傀儡从第二阶段开始",
                    Golem.Check(gWeathered) == 1 && Golem.LastSummoned[0].Oxidation == 2);
            }

            var gStoneHead = new Grid(4, 4, 1);
            gStoneHead.Set(1, 0, 0, mechSand > 0 ? mechSand : 1);
            gStoneHead.Set(1, 1, 0, golemHead);
            Check("南瓜头压在别的方块上什么都不发生",
                Golem.Check(gStoneHead) == 0 && gStoneHead.Get(1, 0, 0) != 0 && gStoneHead.Get(1, 1, 0) == golemHead);

            var gFloatHead = new Grid(4, 4, 1);
            gFloatHead.Set(1, 2, 0, golemHead);
            Check("南瓜头底下是空的也召不出来",
                Golem.Check(gFloatHead) == 0 && gFloatHead.Get(1, 2, 0) == golemHead);

            var gGolemOff = new Grid(4, 4, 1);
            gGolemOff.Set(1, 0, 0, BlockCatalog.SnowId);
            gGolemOff.Set(1, 1, 0, BlockCatalog.SnowId);
            gGolemOff.Set(1, 2, 0, golemHead);
            GameSettings.SetMech(GameSettings.MechKind.Golem, false);
            Check("关掉傀儡开关就不召唤",
                Golem.Check(gGolemOff) == 0 && gGolemOff.Get(1, 2, 0) == golemHead);
            GameSettings.SetMech(GameSettings.MechKind.Golem, true);

            // 傀儡模型的部件数：这次照原版 26.2 的模型类重抄了一遍，顺便把「部件里有几个立方体」
            // 也钉住 —— 铁傀儡的鼻子、腰和铜傀儡的天线以前就是漏在这儿的。
            {
                GolemPartDef[] pSnow = GolemModel.Snow();
                GolemPartDef[] pIron = GolemModel.Iron();
                GolemPartDef[] pCopper = GolemModel.Copper();

                Check("雪傀儡有底下雪球 / 上面雪球 / 头 / 双臂（5 个部件）", pSnow.Length == 5);
                Check("雪傀儡的底下那个 12³ 雪球没被漏掉",
                      pSnow[0].Cubes.Length == 1 && pSnow[0].Cubes[0].W == 12 &&
                      pSnow[0].Cubes[0].H == 12 && pSnow[0].Cubes[0].D == 12);

                Check("铁傀儡有头 / 身体 / 双臂 / 双腿（6 个部件）", pIron.Length == 6);
                Check("铁傀儡的头带鼻子（头上两个立方体，鼻子 2×4×2）",
                      pIron[0].Cubes.Length == 2 && pIron[0].Cubes[1].W == 2 &&
                      pIron[0].Cubes[1].H == 4 && pIron[0].Cubes[1].D == 2);
                Check("铁傀儡的身体带腰（身体两个立方体，腰 9×5×6）",
                      pIron[1].Cubes.Length == 2 && pIron[1].Cubes[1].W == 9 &&
                      pIron[1].Cubes[1].H == 5 && pIron[1].Cubes[1].D == 6);

                Check("铜傀儡有身体 / 头 / 双臂 / 双腿（6 个部件）", pCopper.Length == 6);
                Check("铜傀儡的头带鼻子 + 天线杆 + 天线球（四个立方体）",
                      pCopper[1].Cubes.Length == 4);
                Check("铜傀儡的头是 8×5×10（前后比左右长一倍）",
                      pCopper[1].Cubes[0].W == 8 && pCopper[1].Cubes[0].H == 5 &&
                      pCopper[1].Cubes[0].D == 10);
            }

            // 雪傀儡的手臂：得是「上面接肩膀、往外下斜」。
            //
            // 这个只能算，不能看形状 —— 反过来做的时候，12 像素的杆照样是一根斜杆，
            // 从正面看也能「像手臂」，只是近端落在身体下缘、远端翘到肩膀以上，
            // 玩家一眼看出来的「手臂接反了」就是这么回事。
            // 判据：越往外（|x| 越大）越往下，外端的 y 必须明显低于内端的 y。
            {
                GolemPartDef[] arms = { GolemModel.Snow()[3], GolemModel.Snow()[4] };
                for (int a = 0; a < arms.Length; a++)
                {
                    Mesh m = GolemModel.BuildPart(arms[a], 64, 64);
                    Vector3[] vs = m.vertices;
                    // 网格顶点是关节空间的（没带旋转），旋转在 Transform 上，这里得手动套一遍
                    Quaternion rot = Quaternion.Euler(arms[a].Rot);

                    float minAx = 999f, maxAx = -1f;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 p = rot * vs[i];
                        float ax = Mathf.Abs(p.x) * GolemModel.PixelsPerBlock;
                        minAx = Mathf.Min(minAx, ax);
                        maxAx = Mathf.Max(maxAx, ax);
                    }

                    float sumIn = 0f, sumOut = 0f;
                    int nIn = 0, nOut = 0;
                    for (int i = 0; i < vs.Length; i++)
                    {
                        Vector3 p = rot * vs[i];
                        float ax = Mathf.Abs(p.x) * GolemModel.PixelsPerBlock;
                        float y = p.y * GolemModel.PixelsPerBlock;
                        if (ax <= minAx + 0.5f) { sumIn += y; nIn++; }
                        if (ax >= maxAx - 0.5f) { sumOut += y; nOut++; }
                    }
                    float yIn = nIn > 0 ? sumIn / nIn : 0f;
                    float yOut = nOut > 0 ? sumOut / nOut : 0f;

                    Check("雪傀儡" + (a == 0 ? "左" : "右") + "臂是上面接肩膀、往外下斜" +
                          "（内端 y " + yIn.ToString("0.0") + "、外端 y " + yOut.ToString("0.0") + " 像素）",
                          yOut < yIn - 4f);
                }
            }

            // 铜傀儡的眼睛：原版是 LivingEntityEmissiveLayer 单独刷的一层，
            // 贴图 copper_golem_eyes*.png 里只有那几个像素不透明。
            // 它们必须正好落在头「正面」那块的 UV 矩形里 —— 偏移一格就跑到侧面，
            // 或者干脆采不到、眼睛就不见了。
            {
                GolemCube head = GolemModel.Copper()[1].Cubes[0];
                int fx0 = head.U + head.D, fy0 = head.V + head.D;
                int fx1 = fx0 + head.W, fy1 = fy0 + head.H;

                string[] eyeTex =
                {
                    "copper_golem_eyes", "copper_golem_eyes_exposed",
                    "copper_golem_eyes_weathered", "copper_golem_eyes_oxidized",
                };
                string badEye = "";
                for (int i = 0; i < eyeTex.Length; i++)
                {
                    string r = CheckEyeBox(eyeTex[i], fx0, fy0, fx1, fy1);
                    if (r.Length > 0)
                        badEye += eyeTex[i] + "（" + r + "）";
                }
                Check("铜傀儡四张眼睛贴图都有眼睛、且都落在头的正面" +
                      (badEye.Length == 0 ? "" : "：" + badEye), badEye.Length == 0);
            }

            // 整体高度得对上原版：雪 28 像素、铁 43、铜 24（天线顶），换算成格是 /16
            {
                Check("铁傀儡模型高 43 像素（原版 2.69 格）",
                      Mathf.Abs(PartTop(GolemModel.Iron()) - 43f) < 0.01f);
                Check("雪傀儡模型高 28 像素（原版 1.75 格）",
                      Mathf.Abs(PartTop(GolemModel.Snow()) - 28f) < 0.01f);
                Check("铜傀儡模型高 24 像素（连天线，原版 1.5 格）",
                      Mathf.Abs(PartTop(GolemModel.Copper()) - 24f) < 0.01f);
            }

            // 网格尺寸：一根 18×12×11 像素的方块，换算成格应该是 1.125 × 0.75 × 0.6875
            var cubeMesh = GolemModel.BuildCube(
                new GolemCube(0, 40, 18, 12, 11, Vector3.zero), 128, 128);
            Vector3 csize = cubeMesh.bounds.size;
            Check("傀儡网格的尺寸按「像素 / 16」换算",
                Mathf.Abs(csize.x - 18f / 16f) < 0.001f &&
                Mathf.Abs(csize.y - 12f / 16f) < 0.001f &&
                Mathf.Abs(csize.z - 11f / 16f) < 0.001f);
            Check("傀儡网格是六个面（24 个顶点）", cubeMesh.vertexCount == 24);

            // 绕向到底朝哪边，别自己推公式 —— 让 Unity 说。
            // Mesh.RecalculateNormals() 就是按三角形绕向生成顶点法线的：算出来的方向和
            // BuildCube 里显式给的法线同向，才说明这一面按 Unity 的规矩是「朝外」的。
            // 反过来就是整块模型内外翻了，看到的是盒子内壁（贴图像是贴在里面）。
            {
                Mesh m2 = GolemModel.BuildCube(GolemModel.Iron()[1].Cubes[0], 128, 128);
                Vector3[] given = m2.normals;
                m2.RecalculateNormals();
                Vector3[] calc = m2.normals;
                int mismatch = 0;
                for (int i = 0; i < calc.Length; i++)
                    if (Vector3.Dot(given[i], calc[i]) <= 0f)
                        mismatch++;
                Check("网格绕向与法线同向（Unity 按绕向算出的法线朝外）", mismatch == 0);
            }

            // 贴图不能上下颠倒：侧面里位置更高的那个顶点，UV 的 v 也必须更大
            // （Unity 的 UV 原点在左下，v 大 = 图片上方）。
            // 顶点顺序按「下左→下右→上右→上左」写的时候，四个侧面会一起上下翻，
            // 看着就是整张贴图被倒过来 —— 这个只能靠算，肉眼看形状是看不出来的。
            {
                Mesh m3 = GolemModel.BuildCube(GolemModel.Iron()[1].Cubes[0], 128, 128);
                Vector3[] vv = m3.vertices;
                Vector2[] uv3 = m3.uv;
                int upsideDown = 0;
                for (int f = 0; f < vv.Length; f += 4)
                {
                    float dy = vv[f].y - vv[f + 2].y;       // a 与 c 是同一面的对角
                    if (Mathf.Abs(dy) < 0.001f)
                        continue;                            // 顶面 / 底面，跳过
                    float dv = uv3[f].y - uv3[f + 2].y;
                    if (dy * dv <= 0f)
                        upsideDown++;
                }
                Check("侧面贴图没有上下颠倒（高处顶点的 UV.v 更大）", upsideDown == 0);
            }

            // 每一面都得贴在贴图的不透明区里。26.2 的实体贴图是「展开的纸盒」，
            // 没用到的地方 alpha=0；UV 差一格就会采到空白，shader 的 clip 把这面裁掉，
            // 看起来就是「这块没贴上贴图」。光看截图分不清是哪个部件哪一面，所以逐面核对。
            int faces;
            string uvSnow = CheckGolemUVs("snow_golem", 64, 64, GolemModel.Snow(), out faces);
            Check("snow_golem 的 " + faces + " 个面全都贴在贴图上" + (uvSnow.Length == 0 ? "" : "（" + uvSnow + "）"),
                  uvSnow.Length == 0);

            string uvIron = CheckGolemUVs("iron_golem", 128, 128, GolemModel.Iron(), out faces);
            Check("iron_golem 的 " + faces + " 个面全都贴在贴图上" + (uvIron.Length == 0 ? "" : "（" + uvIron + "）"),
                  uvIron.Length == 0);

            string uvCopper = CheckGolemUVs("copper_golem", 64, 64, GolemModel.Copper(), out faces);
            Check("copper_golem 的 " + faces + " 个面全都贴在贴图上" + (uvCopper.Length == 0 ? "" : "（" + uvCopper + "）"),
                  uvCopper.Length == 0);

            // 各部件的实际世界高度：光看代码里的 pivot / center 算不出渲染出来多高，
            // 直接建一个傀儡量包围盒最实在。原版铁傀儡 2.7 格、雪 1.9、铜 0.98。
            {
                string log = "";
                foreach (GolemKind k in new[] { GolemKind.Snow, GolemKind.Iron, GolemKind.Copper })
                {
                    var sizeProbe = new GameObject("golem_probe");
                    var pet = sizeProbe.AddComponent<GolemPet>();
                    pet.Build(k, 0, Vector3.zero, 0f, Vector3.zero, Vector3.zero);

                    var rs = sizeProbe.GetComponentsInChildren<MeshRenderer>();
                    float lo = 999f, hi = -999f;
                    for (int i = 0; i < rs.Length; i++)
                    {
                        lo = Mathf.Min(lo, rs[i].bounds.min.y);
                        hi = Mathf.Max(hi, rs[i].bounds.max.y);
                    }

                    log += "\n  " + k + "：" + rs.Length + " 个部件，y " +
                           lo.ToString("0.000") + " ~ " + hi.ToString("0.000") +
                           "，高 " + (hi - lo).ToString("0.000") + " 格";

                    for (int i = 0; i < rs.Length; i++)
                    {
                        Bounds b = rs[i].bounds;
                        log += "\n      " + rs[i].gameObject.name + "  y " +
                               b.min.y.ToString("0.000") + "~" + b.max.y.ToString("0.000") +
                               "  x " + b.min.x.ToString("0.000") + "~" + b.max.x.ToString("0.000") +
                               "  z " + b.min.z.ToString("0.000") + "~" + b.max.z.ToString("0.000");
                    }

                    // 铜傀儡多的那一层眼睛：得真的建出来，而且贴图是 eyes 那张，
                    // 不是被身体那张盖掉（氧化换贴图时很容易顺手把眼睛也改了）。
                    if (k == GolemKind.Copper)
                    {
                        bool hasEyes = false;
                        for (int i = 0; i < rs.Length; i++)
                        {
                            if (rs[i].gameObject.name != "eyes")
                                continue;
                            Material mat = rs[i].sharedMaterial;
                            hasEyes = mat != null && mat.mainTexture != null &&
                                      mat.mainTexture.name.StartsWith("copper_golem_eyes");
                        }
                        Check("铜傀儡头上有单独一层眼睛（用的是 eyes 贴图）", hasEyes);
                    }

                    Object.DestroyImmediate(sizeProbe);
                }
                Debug.Log("[golem] 傀儡实际尺寸（原版：雪 1.9 / 铁 2.7 / 铜 0.98）" + log);
            }

            // 桌宠的出生点：连召 4 个，得彼此离得够远。
            // 以前是纯随机落点，实测一次召出雪 / 铁 / 铜三个能叠在同一点上，
            // 穿模成一团，走到随机目标之后才散开。现在用 best-candidate 采样
            // （撒十几把挑离同伴最远的），这里用固定种子把「不重叠」钉死。
            {
                Random.InitState(20261030);
                Vector3 pmin = new Vector3(0f, 0f, 0f), pmax = new Vector3(10f, 0f, 6f);
                var spots = new System.Collections.Generic.List<Vector3>();
                var probes = new System.Collections.Generic.List<GameObject>();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 pos = GolemPet.BestSpot(pmin, pmax, null);
                    spots.Add(pos);

                    var go = new GameObject("golem_spot_" + i);
                    go.AddComponent<GolemPet>()
                      .Build(GolemKind.Snow, 0, pos, 0f, pmin, pmax);
                    probes.Add(go);
                }

                float minPair = 999f;
                for (int a = 0; a < spots.Count; a++)
                    for (int b = a + 1; b < spots.Count; b++)
                        minPair = Mathf.Min(minPair,
                            Vector2.Distance(
                                new Vector2(spots[a].x, spots[a].z),
                                new Vector2(spots[b].x, spots[b].z)));

                Check("连召 4 个傀儡，出生点两两间距不小于 1.5 格（最近 " +
                      minPair.ToString("0.0") + "）", minPair >= 1.5f);

                for (int i = 0; i < probes.Count; i++)
                    Object.DestroyImmediate(probes[i]);
            }

            // 桌宠互相排斥：两个摆到完全重合，跑两秒（120 帧 × 1/60）必须分开到
            // 「半径和 + 余量」之外；而且是一帧一帧推开的，不能瞬移。
            {
                Vector3 pmin = new Vector3(0f, 0f, 0f), pmax = new Vector3(10f, 0f, 6f);
                var aGo = new GameObject("golem_push_a");
                var bGo = new GameObject("golem_push_b");
                var ga = aGo.AddComponent<GolemPet>();
                var gb = bGo.AddComponent<GolemPet>();
                ga.Build(GolemKind.Iron, 0, new Vector3(5f, 0f, 3f), 0f, pmin, pmax);
                gb.Build(GolemKind.Iron, 0, new Vector3(5f, 0f, 3f), 0f, pmin, pmax);

                float want = ga.BodyRadius + gb.BodyRadius + 0.1f;
                float firstStep = 0f;
                for (int f = 0; f < 120; f++)
                {
                    Vector3 prevPos = ga.transform.position;
                    ga.Separate(1f / 60f);
                    Vector3 moved = ga.transform.position - prevPos;
                    moved.y = 0f;
                    if (f == 0)
                        firstStep = moved.magnitude;
                }

                Vector3 gap = ga.transform.position - gb.transform.position;
                gap.y = 0f;
                Check("两个傀儡叠在一起会被挤开（间距 " + gap.magnitude.ToString("0.00") +
                      " >= " + want.ToString("0.00") + "，场上 " + GolemPet.AliveCount + " 个）",
                      gap.magnitude >= want - 0.02f);
                Check("挤开是渐进推开的、不瞬移（第一帧只挪 " + firstStep.ToString("0.000") +
                      " 格，上限 " + (2.2f / 60f).ToString("0.000") + "）",
                      firstStep > 0f && firstStep <= 2.2f / 60f + 0.001f);

                Object.DestroyImmediate(aGo);
                Object.DestroyImmediate(bGo);
            }

            // ---------- 层俯视图 ----------
            // 只在三格以上出现：两格深的中间列其实紧贴着前后两层，从正面转一下就能看全
            Check("俯视图只在 z > 2 时挂（z=2 不挂、z=3 挂）",
                  !LayerMap.UsefulFor(2) && LayerMap.UsefulFor(3));

            {
                // 层号只由玩家拖滑条决定。早先它会自己跟着当前方块跳层，结果刚翻到
                // 某一层看缺口，画面就被方块带走了 —— 这里钉死「没有跟随逻辑」。
                var go = new GameObject("selftest_layermap");
                LayerMap lm = go.AddComponent<LayerMap>();
                lm.LayerY = 5;
                Check("俯视图的层号设了就是设了（第 5 层）", lm.LayerY == 5);
                Check("俯视图不再自带跟随方块跳层的逻辑",
                      typeof(LayerMap).GetMethod("FollowPiece",
                          System.Reflection.BindingFlags.Instance |
                          System.Reflection.BindingFlags.Public |
                          System.Reflection.BindingFlags.NonPublic) == null);
                Object.DestroyImmediate(go);
            }

            {
                // 10×16×3，最底层只差一格 —— 这就是「看着铺满了却没消」的样子
                var g = new Grid(10, 16, 3);
                for (int x = 0; x < 10; x++)
                    for (int z = 0; z < 3; z++)
                        g.Set(x, 0, z, 1);
                Check("整层填满时俯视图报「还差 0」", LayerMap.MissingInLayer(g, 0) == 0);

                g.Set(7, 0, 1, 0);       // 中间列挖一个洞：正面完全看不出来
                Check("中间列差一格时俯视图报「还差 1」", LayerMap.MissingInLayer(g, 0) == 1);
                Check("那一排自己的差数也是 1（另外两排是 0）",
                      LayerMap.MissingInRow(g, 0, 1) == 1 &&
                      LayerMap.MissingInRow(g, 0, 0) == 0 &&
                      LayerMap.MissingInRow(g, 0, 2) == 0);
                Check("还差一格时整层不算满（不会消）", !g.IsLayerFullPublic(0));

                // 水：只有攒满一格才算填满，半格水必须照样报「差」。
                // CountsAsFilled 平时是 Bootstrap 启动时挂上去的，自检里得自己挂一遍，
                // 不然「水算不算填满」永远是 false，这条就测了个寂寞。
                System.Func<int, int, int, bool> savedFill = Grid.CountsAsFilled;
                Grid.CountsAsFilled = (x, y, z) => Fluids.AmountAt(x, y, z) >= Fluids.MaxAmount;
                Fluids.Prepare(g);
                Fluids.SetAmount(7, 0, 1, Fluids.MaxAmount, Fluids.WaterId);
                Check("水攒满一格就算填满（还差 0，这一层可以消）",
                      LayerMap.MissingInLayer(g, 0) == 0 && g.IsLayerFullPublic(0));

                Fluids.SetAmount(7, 0, 1, Fluids.MaxAmount - 1, Fluids.WaterId);
                Check("半格水不算填满（还差 1，这一层不消）",
                      LayerMap.MissingInLayer(g, 0) == 1 && !g.IsLayerFullPublic(0));

                Fluids.SetAmount(7, 0, 1, 0, 0);
                Grid.CountsAsFilled = savedFill;
            }

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
    }
}
