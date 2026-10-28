using System;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>一次 Tick 里读到的输入。按下瞬间为 true，不做按住加速。</summary>
    public struct InputState
    {
        public bool Left;
        public bool Right;
        public bool Up;      // 往远处（z 减小）
        public bool Down;    // 往近处（z 增大）
        public bool SoftDrop;
        public bool HardDrop;
        public bool RotateCW;       // 面内顺时针（屏幕里转）
        public bool RotateCCW;      // 面内逆时针
        public bool RotateYawCW;    // 俯视顺时针（绕竖直轴，从上往下看）
        public bool RotateYawCCW;   // 俯视逆时针
        public bool RotatePitchCW;  // 往背面翻（顶面朝画面深处倒）
        public bool RotatePitchCCW; // 往正面翻
    }

    /// <summary>绕哪个轴转。三个轴对应屏幕上三个方向，手感完全不同。</summary>
    public enum RotationAxis
    {
        /// <summary>面内旋转：绕屏幕法线（z）转，方块还是贴着玩家。</summary>
        Screen = 0,
        /// <summary>俯视旋转：绕世界竖直轴（y）转，方块整块转过去朝别的方向。</summary>
        Yaw = 1,
        /// <summary>前后翻：绕世界水平轴（x）转，顶着玩家那一面往上掀或往下倒。</summary>
        Pitch = 2,
    }

    public class Piece
    {
        public PieceType Type;
        public string Label;   // HUD 上显示的名字：默认方块是 "I" 这种，模具是玩家起的名字
        public int BlockId;   // 材质编号（1 起），每个新方块随机挑一种完整方块
        public int OriginX;
        public int OriginY;
        public int OriginZ;

        /// <summary>形状（方块本地 x-y 平面里的格子，最小角在 0）。读档会直接改它，改完要调 RebuildOffsets。</summary>
        public Vector2Int[] Cells;

        PieceRotation _rot;
        Vector3Int[] _offsets;

        public Piece(Vector2Int[] cells, string label, PieceType type, int blockId, int x, int y, int z)
        {
            Cells = (Vector2Int[])cells.Clone();
            Label = label;
            Type = type;
            BlockId = blockId;
            OriginX = x;
            OriginY = y;
            OriginZ = z;
            _rot = PieceRotation.Identity;
            RebuildOffsets();
        }

        public Piece(PieceType type, int blockId, int x, int y, int z)
            : this(Tetromino.GetShape(type), type.ToString(), type, blockId, x, y, z)
        {
        }

        /// <summary>
        /// 整块朝向。赋值会立刻重算所有格子的偏移，所以改朝向只需要这一行。
        /// 用矩阵而不是三个角度：三组旋转键可以按任意顺序按，结果都能正确表示。
        /// </summary>
        public PieceRotation Rot
        {
            get { return _rot; }
            set
            {
                _rot = value;
                RebuildOffsets();
            }
        }

        /// <summary>
        /// 按当前朝向算好每格的偏移，并整体平移到非负——让"原点"永远是整块包围盒的最小角。
        /// 这样绕屏幕法线旋转时原点不动，方块就是原地转，和经典俄罗斯方块手感一致。
        /// </summary>
        public void RebuildOffsets()
        {
            if (_offsets == null || _offsets.Length != Cells.Length)
                _offsets = new Vector3Int[Cells.Length];

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            for (int i = 0; i < Cells.Length; i++)
            {
                Vector3Int o = _rot.Apply(Cells[i]);
                _offsets[i] = o;
                if (o.x < minX) minX = o.x;
                if (o.y < minY) minY = o.y;
                if (o.z < minZ) minZ = o.z;
            }

            for (int i = 0; i < _offsets.Length; i++)
                _offsets[i] = new Vector3Int(_offsets[i].x - minX, _offsets[i].y - minY, _offsets[i].z - minZ);
        }

        /// <summary>第 i 格相对原点的偏移（已经按朝向转好、平移到非负）。</summary>
        public Vector3Int CellOffset(int i)
        {
            return _offsets[i];
        }

        public Vector3Int WorldCell(int i)
        {
            Vector3Int o = _offsets[i];
            return new Vector3Int(OriginX + o.x, OriginY + o.y, OriginZ + o.z);
        }
    }

    /// <summary>
    /// 纯逻辑的游戏循环，不依赖 MonoBehaviour。Runner 每帧喂 dt 和输入。
    /// </summary>
    public class GameLoop
    {
        public Grid Grid;
        public Piece Current;
        public ShapeEntry Next;
        public int NextBlockId;
        public int Score;
        public int LinesCleared;
        public bool GameOver;
        /// <summary>1 倍速时每格下落间隔（秒）。实际间隔 = BaseFallInterval / FallSpeed。</summary>
        public const float BaseFallInterval = 0.8f;

        /// <summary>实际下落间隔。调试场景可以临时改成很大让它停住。</summary>
        public float FallInterval = BaseFallInterval;

        float _fallSpeed = 1f;

        /// <summary>
        /// 下落速度倍率，范围 (0, 2]，1 = 标准。赋值会立刻换算成 FallInterval。
        /// 3D 场地里玩家要前后找落点，1 倍速常常来不及，所以这一项开放给玩家调。
        /// </summary>
        public float FallSpeed
        {
            get { return _fallSpeed; }
            set
            {
                _fallSpeed = GameSettings.ClampFallSpeed(value);
                FallInterval = BaseFallInterval / _fallSpeed;
            }
        }

        readonly PieceBag _bag;
        float _fallTimer;

        public event Action<Piece> PieceLocked;
        public event Action<int> LinesClearedEvent;
        /// <summary>消除时触发：参数是被清的行号列表 + 每行取一个代表材质（粒子用）。</summary>
        public event Action<System.Collections.Generic.List<int>, System.Collections.Generic.List<int>> RowsCleared;
        public event Action GameOverEvent;
        public event Action GravitySettled;
        /// <summary>水/岩浆的流动范围变了，视图层要重画。</summary>
        public event Action FluidChanged;
        /// <summary>水岩浆结晶了（黑曜石 / 原石），视图层刷那几格并撒碎屑。</summary>
        public event Action<System.Collections.Generic.List<Fluids.Reaction>> FluidReacted;
        public event Action Spawned;
        /// <summary>TNT 被点着了：参数是那一格的坐标。视图层播引信音、开始闪白。</summary>
        public event Action<Vector3Int> TntPrimed;
        /// <summary>TNT 炸了：参数是这一处的爆心与被炸掉的格子。</summary>
        public event Action<Tnt.Blast> TntExploded;
        /// <summary>原木长出了树叶：视图层照着 Foliage.LastGrown 刷那几格。</summary>
        public event Action FoliageGrew;
        /// <summary>方块转了个 90°：参数是绕哪个轴 + 是否顺时针。视图层用它播旋转动画。</summary>
        public event Action<RotationAxis, bool> PieceRotated;

        /// <summary>最近一次重力结算里每一格怎么挪的，视图层照着播滑落。</summary>
        public readonly System.Collections.Generic.List<GravityStep> LastGravityMoves
            = new System.Collections.Generic.List<GravityStep>();

        public GameLoop(int sizeX, int sizeY, int sizeZ, int seed = 0)
        {
            Grid = new Grid(sizeX, sizeY, sizeZ);
            _bag = new PieceBag(seed);
            Next = _bag.Next();
            NextBlockId = RandomBlockId();
            SpawnNext();
        }

        public void Tick(float dt, InputState input)
        {
            if (GameOver)
                return;

            // 结构方块的还原动画：被消掉之后先把那一片清空，再一格一格搭回来
            Structure.Tick(Grid, dt);

            // 水/岩浆一格一格漫开、一格一格退掉；相遇的会结晶成石头
            if (Fluids.Tick(Grid, dt))
            {
                if (Fluids.LastReactions.Count > 0)
                    FluidReacted?.Invoke(Fluids.LastReactions);
                FluidChanged?.Invoke();

                // 岩浆淌到 TNT 旁边就点着。流体不写网格，落定时那一次扫描看不见，
                // 只能等它动过之后再补扫一遍（水面挨着不算，Tnt 里只认岩浆）。
                Tnt.PrimeContact(Grid);
            }

            // TNT：引信烧完就炸。炸完上面的沙 / 沙砾要重新塌一次，
            // 所以这里不能只当视觉事件，得再跑一遍重力结算。
            if (Tnt.Tick(Grid, dt))
            {
                for (int i = 0; i < Tnt.LastBlasts.Count; i++)
                {
                    Tnt.Blast b = Tnt.LastBlasts[i];
                    Score += b.Cells.Count * Tnt.ScorePerBlock;
                    TntExploded?.Invoke(b);
                }

                // 爆炸范围里的水被吹掉了，视图层要重画
                FluidChanged?.Invoke();

                LastGravityMoves.Clear();
                if (Gravity.Settle(Grid, LastGravityMoves))
                    GravitySettled?.Invoke();
            }
            FlushTntPrimed();

            if (input.Left)
                TryMove(-1, 0, 0);
            if (input.Right)
                TryMove(1, 0, 0);
            if (input.Up)
                TryMove(0, 0, -1);
            if (input.Down)
                TryMove(0, 0, 1);

            if (input.RotateCW)
                TryRotate(true);
            if (input.RotateCCW)
                TryRotate(false);
            if (input.RotateYawCW)
                TryRotateYaw(true);
            if (input.RotateYawCCW)
                TryRotateYaw(false);
            if (input.RotatePitchCW)
                TryRotatePitch(true);
            if (input.RotatePitchCCW)
                TryRotatePitch(false);

            if (input.SoftDrop && !TryMove(0, -1, 0))
                LockPiece();

            if (input.HardDrop)
            {
                HardDrop();
                return;
            }

            _fallTimer += dt;
            while (_fallTimer >= FallInterval)
            {
                _fallTimer -= FallInterval;
                if (!TryMove(0, -1, 0))
                {
                    LockPiece();
                    break;
                }
            }
        }

        public bool TryMove(int dx, int dy, int dz)
        {
            if (Current == null || !CanPlace(Current, dx, dy, dz))
                return false;

            Current.OriginX += dx;
            Current.OriginY += dy;
            Current.OriginZ += dz;
            return true;
        }

        /// <summary>面内旋转（绕屏幕法线的 z 轴）：方块还是贴着玩家，只是转个圈。</summary>
        public bool TryRotate(bool clockwise)
        {
            return TryRotateAbout(RotationAxis.Screen, clockwise);
        }

        /// <summary>俯视旋转（绕竖直的 y 轴）：方块整块转过去朝别的方向。</summary>
        public bool TryRotateYaw(bool clockwise)
        {
            return TryRotateAbout(RotationAxis.Yaw, clockwise);
        }

        /// <summary>前后翻（绕水平的 x 轴）：true = 往背面倒，false = 往正面倒。</summary>
        public bool TryRotatePitch(bool clockwise)
        {
            return TryRotateAbout(RotationAxis.Pitch, clockwise);
        }

        /// <summary>
        /// 绕世界轴转 90°。三种转法共用这一套：
        ///
        /// 1. 记下转之前整块占据的绝对包围盒；
        /// 2. 换朝向（只改 Piece.Rot，形状 Cells 一个字不动）；
        /// 3. 让新旧包围盒在「变化了的那两个轴」上中心重合——绕 z 转只动 x/y 范围，
        ///    绕 y 转只动 x/z，绕 x 转只动 y/z，所以对齐的轴也按这个来；
        /// 4. 转不下就依次往四周挪 1~2 格、最后试着往上顶一格；全不行就还原，静默返回 false。
        ///
        /// 顺带一提：面内旋转故意不做中心对齐，保持原点不动——宽 I 贴着地 rotate 成竖 I 时，
        /// 它会从原位置往上长，这才是俄罗斯方块该有的手感，对齐中心反而会往下沉进地里。
        /// </summary>
        bool TryRotateAbout(RotationAxis axis, bool clockwise)
        {
            if (Current == null)
                return false;

            PieceRotation backup = Current.Rot;

            // 转之前：绝对坐标的包围盒（存 min+max，省掉取整误差）
            int sx = 0, sy = 0, sz = 0;
            BoundsSum(true, ref sx, ref sy, ref sz);

            // 换朝向。CellOffset 会随之变，所以后面的包围盒是转完的形状
            Current.Rot = backup.Rotate(axis, clockwise);

            int rx = 0, ry = 0, rz = 0;
            BoundsSum(false, ref rx, ref ry, ref rz);

            int baseX = Current.OriginX;
            int baseY = Current.OriginY;
            int baseZ = Current.OriginZ;

            // 绕哪个轴转，就对齐另外两个轴；面内旋转不对齐（原点不动）
            if (axis == RotationAxis.Yaw)
            {
                baseX = Mathf.RoundToInt((sx - rx) * 0.5f);
                baseZ = Mathf.RoundToInt((sz - rz) * 0.5f);
            }
            else if (axis == RotationAxis.Pitch)
            {
                baseY = Mathf.RoundToInt((sy - ry) * 0.5f);
                baseZ = Mathf.RoundToInt((sz - rz) * 0.5f);
            }

            if (CommitRotated(baseX, baseY, baseZ))
            {
                PieceRotated?.Invoke(axis, clockwise);
                return true;
            }

            // 踢墙：先横向前后，再上下
            for (int d = 1; d <= 2; d++)
            {
                if (CommitRotated(baseX + d, baseY, baseZ) ||
                    CommitRotated(baseX - d, baseY, baseZ) ||
                    CommitRotated(baseX, baseY, baseZ + d) ||
                    CommitRotated(baseX, baseY, baseZ - d))
                {
                    PieceRotated?.Invoke(axis, clockwise);
                    return true;
                }
            }

            // 实在挤，抬一格 / 两格试试（相当于踢墙里的往上顶）
            if (CommitRotated(baseX, baseY + 1, baseZ) ||
                CommitRotated(baseX, baseY + 2, baseZ))
            {
                PieceRotated?.Invoke(axis, clockwise);
                return true;
            }

            Current.Rot = backup;
            return false;
        }

        /// <summary>整块在各轴上占的 (min + max)；absolute = true 取绝对坐标，否则取相对原点的偏移。</summary>
        void BoundsSum(bool absolute, ref int sx, ref int sy, ref int sz)
        {
            int minX = int.MaxValue, maxX = int.MinValue;
            int minY = int.MaxValue, maxY = int.MinValue;
            int minZ = int.MaxValue, maxZ = int.MinValue;
            for (int i = 0; i < Current.Cells.Length; i++)
            {
                Vector3Int c = absolute ? Current.WorldCell(i) : Current.CellOffset(i);
                if (c.x < minX) minX = c.x;
                if (c.x > maxX) maxX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.y > maxY) maxY = c.y;
                if (c.z < minZ) minZ = c.z;
                if (c.z > maxZ) maxZ = c.z;
            }
            sx = minX + maxX;
            sy = minY + maxY;
            sz = minZ + maxZ;
        }

        bool CommitRotated(int x, int y, int z)
        {
            if (!CanPlaceAt(Current, x, y, z))
                return false;

            Current.OriginX = x;
            Current.OriginY = y;
            Current.OriginZ = z;
            return true;
        }

        public void HardDrop()
        {
            if (Current == null)
                return;

            while (TryMove(0, -1, 0))
            {
            }

            LockPiece();
        }

        void LockPiece()
        {
            int blockId = Current.BlockId;

            // 落定时还有格子在场地顶部之外，说明已经堆到顶了，这才是真正的游戏结束。
            for (int i = 0; i < Current.Cells.Length; i++)
            {
                if (Current.WorldCell(i).y >= Grid.SizeY)
                {
                    GameOver = true;
                    GameOverEvent?.Invoke();
                    return;
                }
            }

            bool fluidPiece = Fluids.IsFluid(blockId);

            // 第一步：整块方块先占位（水方块只登记流体，不占格）。
            for (int i = 0; i < Current.Cells.Length; i++)
            {
                Vector3Int c = Current.WorldCell(i);
                if (!Grid.InBounds(c.x, c.y, c.z))
                    continue;

                if (fluidPiece)
                {
                    // 水方块落定后只作为流体存在（份数记在 Fluids 里）。
                    // 不能再往网格里写一份：网格一块 + 流体一块，渲染就是两份水，
                    // 看起来「体积凭空翻倍」。
                    Grid.Set(c.x, c.y, c.z, 0);
                    Fluids.AddSource(Grid, c.x, c.y, c.z, blockId);
                }
                else
                {
                    Grid.Set(c.x, c.y, c.z, blockId);
                }
            }

            // 第二步：整块都占好位之后再挤走被压住的流体。
            // 顺序反了的话，先挤出来的流体会落进还没占位的其它格子里，
            // 等那些格被填上，流体就卡在方块内部了（岩浆那块就是这么来的）。
            if (!fluidPiece)
            {
                for (int i = 0; i < Current.Cells.Length; i++)
                {
                    Vector3Int c = Current.WorldCell(i);
                    if (Grid.InBounds(c.x, c.y, c.z))
                        Fluids.OnSolidPlaced(Grid, c.x, c.y, c.z);
                }
            }

            PieceLocked?.Invoke(Current);

            // 结构方块落定 → 记住周围这一片的构造，之后被消掉时还原回来
            if (!fluidPiece)
                Structure.RememberFromPiece(Grid, Current);

            // 沙子、沙砾这类会往下掉：先落到位，再判定满行
            LastGravityMoves.Clear();
            if (Gravity.Settle(Grid, LastGravityMoves))
                GravitySettled?.Invoke();

            // 原木长树叶。放在消行之前：树叶补满的行当场就消，
            // 否则那片行要等下一块落定才消，看起来像卡住了。
            if (!fluidPiece && Foliage.Grow(Grid, Current) > 0)
                FoliageGrew?.Invoke();

            // 先记下这一轮要清的行和每行的代表材质，清完再派事件给粒子效果
            var fullRows = new System.Collections.Generic.List<int>();
            var rowBlocks = new System.Collections.Generic.List<int>();
            bool layerMode = GameSettings.ElimMode == GameSettings.ElimLayer;

            if (layerMode)
            {
                for (int y = 0; y < Grid.SizeY; y++)
                {
                    if (Grid.IsLayerFullPublic(y))
                    {
                        fullRows.Add(y);
                        rowBlocks.Add(Grid.FirstBlockInLayer(y));
                    }
                }
            }
            else
            {
                for (int y = 0; y < Grid.SizeY; y++)
                    for (int z = 0; z < Grid.SizeZ; z++)
                        if (Grid.IsRowFullPublic(y, z))
                        {
                            fullRows.Add(y);
                            rowBlocks.Add(Grid.FirstBlockInRow(y, z));
                        }
            }

            int cleared = layerMode ? Grid.ClearFullLayers() : Grid.ClearFullRows();
            if (cleared > 0)
            {
                LinesCleared += cleared;
                Score += cleared switch
                {
                    1 => 100,
                    2 => 300,
                    3 => 500,
                    _ => 800,
                };
                LinesClearedEvent?.Invoke(cleared);
                if (fullRows.Count > 0)
                    RowsCleared?.Invoke(fullRows, rowBlocks);
            }

            // 这一块落定之后再扫 TNT 有没有被红石挨着。放在消行之后：
            // 消行会把整排往下挪，先点火的话记下的坐标就错位了。
            Tnt.PrimeContact(Grid);
            FlushTntPrimed();

            SpawnNext();
        }

        /// <summary>把这一帧新点着的 TNT 派给视图层，然后清掉待发列表。</summary>
        void FlushTntPrimed()
        {
            if (Tnt.LastPrimed.Count == 0)
                return;
            for (int i = 0; i < Tnt.LastPrimed.Count; i++)
                TntPrimed?.Invoke(Tnt.LastPrimed[i]);
            Tnt.LastPrimed.Clear();
        }

        void SpawnNext()
        {
            ShapeEntry entry = Next;
            int blockId = NextBlockId;
            Next = _bag.Next();
            NextBlockId = RandomBlockId();

            Vector2Int[] cells = entry.Cells;
            int maxX = 0;
            for (int i = 0; i < cells.Length; i++)
                maxX = Mathf.Max(maxX, cells[i].x);

            int minY = 0;
            for (int i = 0; i < cells.Length; i++)
                minY = Mathf.Min(minY, cells[i].y);

            int width = maxX + 1;
            int ox = (Grid.SizeX - width) / 2;
            // 在框外生成：让方块最低的那一格正好落在场地顶部（y = SizeY），整块都在框上方，
            // 然后往下掉进来。IsFree 把顶部以上算作空，所以这一段不会判成放不下。
            int oy = Grid.SizeY - minY;
            int oz = (Grid.SizeZ - 1) / 2;
            Current = new Piece(cells, entry.Label, entry.Type, blockId, ox, oy, oz);
            _fallTimer = 0f;

            if (!CanPlace(Current, 0, 0, 0))
            {
                GameOver = true;
                GameOverEvent?.Invoke();
            }
            else
            {
                Spawned?.Invoke();
            }
        }

        /// <summary>存档用的可序列化快照。</summary>
        [System.Serializable]
        public class SaveData
        {
            public int[] cells;
            public int score;
            public int lines;
            public int nextType;      // -1 表示玩家模具
            public string nextLabel;
            public int[] nextCellsX;
            public int[] nextCellsY;
            public int nextBlockId;
            public int curType;
            public int curBlockId;
            public int ox, oy, oz;
            public int facing;   // 老字段（只有绕竖直轴的朝向），新存档不用它，只为兼容旧档
            public int rot;      // 朝向编码 + 1（0 = 这个字段不存在，退回读 facing）
            public int[] curCellsX;
            public int[] curCellsY;
            public float fallTimer;
            // 流体（水/岩浆）每 5 个数一组：x, y, z, 份数, 类型
            public int[] fluids;
            // 结构方块的记忆：每条 10 个头 + 一串方块 id，见 Structure.SerializeMemories
            public int[] structures;
        }

        public string SerializeState()
        {
            var d = new SaveData
            {
                cells = Grid.Export(),
                score = Score,
                lines = LinesCleared,
                nextType = Next.IsMold ? -1 : (int)Next.Type,
                nextLabel = Next.Label,
                nextBlockId = NextBlockId,
                curType = Current != null ? (int)Current.Type : -1,
                curBlockId = Current != null ? Current.BlockId : 0,
                ox = Current != null ? Current.OriginX : 0,
                oy = Current != null ? Current.OriginY : 0,
                oz = Current != null ? Current.OriginZ : 0,
                rot = Current != null ? Current.Rot.Code + 1 : 0,
                fallTimer = _fallTimer,
            };

            if (Current != null)
            {
                d.curCellsX = new int[Current.Cells.Length];
                d.curCellsY = new int[Current.Cells.Length];
                for (int i = 0; i < Current.Cells.Length; i++)
                {
                    d.curCellsX[i] = Current.Cells[i].x;
                    d.curCellsY[i] = Current.Cells[i].y;
                }
            }

            if (Next != null)
            {
                d.nextCellsX = new int[Next.Cells.Length];
                d.nextCellsY = new int[Next.Cells.Length];
                for (int i = 0; i < Next.Cells.Length; i++)
                {
                    d.nextCellsX[i] = Next.Cells[i].x;
                    d.nextCellsY[i] = Next.Cells[i].y;
                }
            }

            // 流体也要一并存下来，否则退出到主界面再“继续游戏”时水/岩浆会凭空消失
            var fluidList = new System.Collections.Generic.List<int>();
            for (int y = 0; y < Grid.SizeY; y++)
            {
                for (int z = 0; z < Grid.SizeZ; z++)
                {
                    for (int x = 0; x < Grid.SizeX; x++)
                    {
                        int amt = Fluids.AmountAt(x, y, z);
                        if (amt <= 0)
                            continue;
                        fluidList.Add(x);
                        fluidList.Add(y);
                        fluidList.Add(z);
                        fluidList.Add(amt);
                        fluidList.Add(Fluids.IdAt(x, y, z));
                    }
                }
            }
            d.fluids = fluidList.ToArray();

            // 结构方块的记忆也要存：只存网格的话，读档回来结构方块就成了个哑方块
            d.structures = Structure.SerializeMemories();

            return JsonUtility.ToJson(d);
        }

        public bool RestoreState(string json)
        {
            SaveData d;
            try
            {
                d = JsonUtility.FromJson<SaveData>(json);
            }
            catch
            {
                return false;
            }

            if (d == null || d.cells == null || d.cells.Length != Grid.SizeX * Grid.SizeY * Grid.SizeZ)
                return false;

            Grid.Import(d.cells);

            // 把存档里的流体还原（老存档没有 fluids 字段，那就当场上没流体）
            Fluids.Reset();
            Fluids.Prepare(Grid);
            if (d.fluids != null)
            {
                for (int i = 0; i + 4 < d.fluids.Length; i += 5)
                    Fluids.SetAmount(d.fluids[i], d.fluids[i + 1], d.fluids[i + 2], d.fluids[i + 3], d.fluids[i + 4]);
            }

            Score = d.score;
            LinesCleared = d.lines;
            GameOver = false;
            Next = MakeEntry(d.nextType, d.nextLabel, d.nextCellsX, d.nextCellsY);
            NextBlockId = d.nextBlockId <= 0 ? RandomBlockId() : d.nextBlockId;

            if (d.curType >= 0 && d.curType <= 6)
            {
                Current = new Piece((PieceType)d.curType, d.curBlockId, d.ox, d.oy, d.oz);
                if (d.curCellsX != null && d.curCellsY != null && d.curCellsX.Length == Current.Cells.Length)
                {
                    for (int i = 0; i < Current.Cells.Length; i++)
                        Current.Cells[i] = new Vector2Int(d.curCellsX[i], d.curCellsY[i]);
                }
                // 形状先落地，再套朝向（赋 Rot 会按新形状重算偏移）
                Current.Rot = d.rot > 0 ? PieceRotation.FromCode(d.rot - 1) : PieceRotation.FromYaw(d.facing);
            }
            else
            {
                Current = null;
            }

            _fallTimer = d.fallTimer;

            // 结构方块的记忆。老存档没有这个字段 → 当场没有记忆（读作 null）
            Structure.DeserializeMemories(d.structures);

            return true;
        }

        /// <summary>从存档里的类型编号 / 名字 / 坐标重建一个出块池条目。</summary>
        static ShapeEntry MakeEntry(int type, string label, int[] xs, int[] ys)
        {
            PieceType t = (type >= 0 && type <= 6) ? (PieceType)type : PieceType.I;

            if (xs != null && ys != null && xs.Length == ys.Length && xs.Length > 0)
            {
                var cells = new Vector2Int[xs.Length];
                for (int i = 0; i < xs.Length; i++)
                    cells[i] = new Vector2Int(xs[i], ys[i]);
                return new ShapeEntry
                {
                    Cells = cells,
                    Label = string.IsNullOrEmpty(label) ? t.ToString() : label,
                    Type = t,
                    IsMold = type < 0,
                };
            }

            return new ShapeEntry
            {
                Cells = Tetromino.GetShape(t),
                Label = t.ToString(),
                Type = t,
            };
        }

        public void Restart()
        {
            Grid.Clear();
            Tnt.Reset();
            Structure.Reset();
            Score = 0;
            LinesCleared = 0;
            GameOver = false;
            _fallTimer = 0f;
            Next = _bag.Next();
            NextBlockId = RandomBlockId();
            SpawnNext();
        }

        public static int RandomBlockId()
        {
            return BlockPools.NextRandomId();
        }

        bool CanPlace(Piece p, int dx, int dy, int dz)
        {
            return CanPlaceAt(p, p.OriginX + dx, p.OriginY + dy, p.OriginZ + dz);
        }

        /// <summary>按绝对原点位置判定能不能放下（俯视旋转要试好几个落点，用这个）。</summary>
        bool CanPlaceAt(Piece p, int ox, int oy, int oz)
        {
            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector3Int o = p.CellOffset(i);
                if (!Grid.IsFree(ox + o.x, oy + o.y, oz + o.z))
                    return false;
            }
            return true;
        }
    }
}
