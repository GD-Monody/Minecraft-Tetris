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
        public int OriginX;
        public int OriginY;
        public int OriginZ;
        public Vector2Int[] Cells;

        public Piece(PieceType type, int x, int y, int z)
        {
            Type = type;
            OriginX = x;
            OriginY = y;
            OriginZ = z;
            Cells = Tetromino.GetShape(type);
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
        public PieceType Next;
        public int Score;
        public int LinesCleared;
        public bool GameOver;
        public float FallInterval = 0.8f;

        readonly PieceBag _bag;
        float _fallTimer;

        public event Action<Piece> PieceLocked;
        public event Action<int> LinesClearedEvent;
        public event Action GameOverEvent;
        public event Action Spawned;

        public GameLoop(int sizeX, int sizeY, int sizeZ, int seed = 0)
        {
            Grid = new Grid(sizeX, sizeY, sizeZ);
            _bag = new PieceBag(seed);
            Next = _bag.Next();
            SpawnNext();
        }

        public void Tick(float dt, InputState input)
        {
            if (GameOver)
                return;

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
                return true;

            // 简单踢墙：左右各试一格
            for (int k = -1; k <= 1; k += 2)
            {
                if (CanPlace(Current, k, 0, 0))
                {
                    Current.OriginX += k;
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
            int blockId = (int)Current.Type + 1;
            for (int i = 0; i < Current.Cells.Length; i++)
            {
                Vector3Int c = Current.WorldCell(i);
                if (Grid.InBounds(c.x, c.y, c.z))
                    Grid.Set(c.x, c.y, c.z, blockId);
            }

            PieceLocked?.Invoke(Current);

            int cleared = Grid.ClearFullLines();
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
            }

            SpawnNext();
        }

        void SpawnNext()
        {
            PieceType type = Next;
            Next = _bag.Next();

            Vector2Int[] cells = Tetromino.GetShape(type);
            int maxX = 0;
            for (int i = 0; i < cells.Length; i++)
                maxX = Mathf.Max(maxX, cells[i].x);

            int width = maxX + 1;
            int ox = (Grid.SizeX - width) / 2;
            int oy = Grid.SizeY - 3;
            int oz = (Grid.SizeZ - 1) / 2;
            Current = new Piece(type, ox, oy, oz);
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

        public void Restart()
        {
            Grid.Clear();
            Score = 0;
            LinesCleared = 0;
            GameOver = false;
            _fallTimer = 0f;
            Next = _bag.Next();
            SpawnNext();
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
