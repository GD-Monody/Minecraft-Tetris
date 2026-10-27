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
        public bool RotateCW;
        public bool RotateCCW;
    }

    public class Piece
    {
        public PieceType Type;
        public string Label;   // HUD 上显示的名字：默认方块是 "I" 这种，模具是玩家起的名字
        public int BlockId;   // 材质编号（1 起），每个新方块随机挑一种完整方块
        public int OriginX;
        public int OriginY;
        public int OriginZ;
        public Vector2Int[] Cells;

        public Piece(Vector2Int[] cells, string label, PieceType type, int blockId, int x, int y, int z)
        {
            Cells = (Vector2Int[])cells.Clone();
            Label = label;
            Type = type;
            BlockId = blockId;
            OriginX = x;
            OriginY = y;
            OriginZ = z;
        }

        public Piece(PieceType type, int blockId, int x, int y, int z)
            : this(Tetromino.GetShape(type), type.ToString(), type, blockId, x, y, z)
        {
        }

        public Vector3Int WorldCell(int i)
        {
            return new Vector3Int(OriginX + Cells[i].x, OriginY + Cells[i].y, OriginZ);
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
        public float FallInterval = 0.8f;

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
        public event Action Spawned;
        /// <summary>方块转了个 90°（true = 顺时针）。视图层用它播旋转动画。</summary>
        public event Action<bool> PieceRotated;

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

            // 水/岩浆一格一格漫开、一格一格退掉
            if (Fluids.Tick(Grid, dt))
                FluidChanged?.Invoke();

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

        public bool TryRotate(bool clockwise)
        {
            if (Current == null)
                return false;

            Vector2Int[] backup = Current.Cells;
            Vector2Int[] rotated = Tetromino.Rotate(Current.Cells, clockwise);
            Current.Cells = rotated;

            if (CanPlace(Current, 0, 0, 0))
            {
                PieceRotated?.Invoke(clockwise);
                return true;
            }

            // 简单踢墙：左右各试一格
            for (int k = -1; k <= 1; k += 2)
            {
                if (CanPlace(Current, k, 0, 0))
                {
                    Current.OriginX += k;
                    PieceRotated?.Invoke(clockwise);
                    return true;
                }
            }

            Current.Cells = backup;
            return false;
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
            for (int i = 0; i < Current.Cells.Length; i++)
            {
                Vector3Int c = Current.WorldCell(i);
                if (Grid.InBounds(c.x, c.y, c.z))
                {
                    if (Fluids.IsFluid(blockId))
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
                        Fluids.OnSolidPlaced(Grid, c.x, c.y, c.z);
                    }
                }
            }

            PieceLocked?.Invoke(Current);

            // 沙子、沙砾这类会往下掉：先落到位，再判定满行
            LastGravityMoves.Clear();
            if (Gravity.Settle(Grid, LastGravityMoves))
                GravitySettled?.Invoke();

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

            SpawnNext();
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

            int width = maxX + 1;
            int ox = (Grid.SizeX - width) / 2;
            int oy = Grid.SizeY - 3;
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
            public int[] curCellsX;
            public int[] curCellsY;
            public float fallTimer;
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
            }
            else
            {
                Current = null;
            }

            _fallTimer = d.fallTimer;
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
            for (int i = 0; i < p.Cells.Length; i++)
            {
                Vector2Int c = p.Cells[i];
                if (!Grid.IsFree(p.OriginX + c.x + dx, p.OriginY + c.y + dy, p.OriginZ + dz))
                    return false;
            }
            return true;
        }
    }
}
