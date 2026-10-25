using UnityEngine;

namespace MinecraftTetris
{
    public enum PieceType
    {
        I = 0,
        O = 1,
        T = 2,
        S = 3,
        Z = 4,
        J = 5,
        L = 6
    }

    public static class Tetromino
    {
        /// <summary>
        /// 生成形状，x 向右、y 向上。初版全部在 z = 0 平面里。
        /// </summary>
        static readonly Vector2Int[][] Spawn =
        {
            new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(3, 1) }, // I
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }, // O
            new[] { new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(1, 2) }, // T
            new[] { new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) }, // S
            new[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // Z
            new[] { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // J
            new[] { new Vector2Int(2, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) }, // L
        };

        public static Vector2Int[] GetShape(PieceType type)
        {
            return (Vector2Int[])Spawn[(int)type].Clone();
        }

        /// <summary>
        /// 屏幕内 90° 旋转（z 坐标不动），转完平移到左上角贴回原点。
        /// </summary>
        public static Vector2Int[] Rotate(Vector2Int[] cells, bool clockwise)
        {
            var result = new Vector2Int[cells.Length];
            int minX = int.MaxValue;
            int minY = int.MaxValue;

            for (int i = 0; i < cells.Length; i++)
            {
                Vector2Int p = cells[i];
                Vector2Int r = clockwise ? new Vector2Int(p.y, -p.x) : new Vector2Int(-p.y, p.x);
                result[i] = r;
                minX = Mathf.Min(minX, r.x);
                minY = Mathf.Min(minY, r.y);
            }

            for (int i = 0; i < result.Length; i++)
                result[i] = new Vector2Int(result[i].x - minX, result[i].y - minY);

            return result;
        }
    }
}
