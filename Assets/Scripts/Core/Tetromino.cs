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
        /// 生成形状，x 向右、y 向上。默认七种全部躺在 z = 0 平面里。
        ///
        /// 表本身已经是三维坐标：创意工坊的模具能占据任意 z，标准方块只是「恰好 z 都为 0」
        /// 的那一种情况，两条路走同一条管道，下游不必区分。
        /// </summary>
        static readonly Vector3Int[][] Spawn =
        {
            new[] { new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector3Int(3, 1, 0) }, // I
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0) }, // O
            new[] { new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0), new Vector3Int(1, 2, 0) }, // T
            new[] { new Vector3Int(1, 0, 0), new Vector3Int(2, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0) }, // S
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(1, 0, 0), new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0) }, // Z
            new[] { new Vector3Int(0, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0) }, // J
            new[] { new Vector3Int(2, 0, 0), new Vector3Int(0, 1, 0), new Vector3Int(1, 1, 0), new Vector3Int(2, 1, 0) }, // L
        };

        public static Vector3Int[] GetShape(PieceType type)
        {
            return (Vector3Int[])Spawn[(int)type].Clone();
        }

    }
}
