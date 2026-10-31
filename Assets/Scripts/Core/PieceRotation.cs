using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 方块朝向：一个整数 3×3 旋转矩阵。三根列向量 = 方块本地 x / y / z 轴在世界里的指向，
    /// 每个分量只有 -1 / 0 / 1，全组合正好 24 种，覆盖立方体能摆出的所有朝向。
    ///
    /// 为什么不用「偏航 + 俯仰 + 翻滚」三个角度存：绕世界轴的旋转不满足交换律，
    /// 三个角度分开存就拼不出「先绕 y 转、再绕 x 转」的结果，转到某个角度还会撞上 gimbal lock
    /// （两组不同的角度指向同一个朝向，再转一下就分叉了）。
    /// 存矩阵没有这个问题：每次按键都是「左乘一个 90° 基本旋转」，三组键按任意顺序按都对得上。
    ///
    /// 三个基本旋转的方向不是推出来的，是拿 Unity 的 Quaternion 实测的（见下方 Step 注释），
    /// 这样逻辑层的格子位置和视图层的方块朝向永远不会差一个 90°。
    /// </summary>
    public struct PieceRotation
    {
        /// <summary>本地 +x 轴在世界里的方向。</summary>
        public readonly Vector3Int X;
        /// <summary>本地 +y 轴在世界里的方向。</summary>
        public readonly Vector3Int Y;
        /// <summary>本地 +z 轴在世界里的方向。</summary>
        public readonly Vector3Int Z;

        public PieceRotation(Vector3Int x, Vector3Int y, Vector3Int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>不转：本地轴就是世界轴。</summary>
        public static readonly PieceRotation Identity =
            new PieceRotation(Vector3Int.right, Vector3Int.up, new Vector3Int(0, 0, 1));

        /// <summary>绕世界轴转 90°：新的朝向 = 基本旋转 × 当前朝向（左乘 = 绕世界轴转）。</summary>
        public PieceRotation Rotate(RotationAxis axis, bool clockwise)
        {
            Vector3Int c0, c1, c2;
            Step(axis, clockwise, out c0, out c1, out c2);
            return new PieceRotation(Mul(c0, c1, c2, X), Mul(c0, c1, c2, Y), Mul(c0, c1, c2, Z));
        }

        /// <summary>
        /// 立体形状的某一格转成世界方向：本地 xyz 各乘对应的世界轴向量。
        /// 二十四朝向里每格都走得准 —— 三维模具能转起来就靠这一步。
        /// </summary>
        public Vector3Int Apply(Vector3Int cell)
        {
            return new Vector3Int(
                X.x * cell.x + Y.x * cell.y + Z.x * cell.z,
                X.y * cell.x + Y.y * cell.y + Z.y * cell.z,
                X.z * cell.x + Y.z * cell.y + Z.z * cell.z);
        }

        /// <summary>给 Unity 用的四元数。由这个矩阵反推，所以和逻辑层一定一致。</summary>
        public Quaternion ToQuaternion()
        {
            Matrix4x4 m = Matrix4x4.identity;
            m.m00 = X.x; m.m10 = X.y; m.m20 = X.z;
            m.m01 = Y.x; m.m11 = Y.y; m.m21 = Y.z;
            m.m02 = Z.x; m.m12 = Z.y; m.m22 = Z.z;
            return m.rotation;
        }

        /// <summary>存档用：9 个 -1/0/1 按三进制压成一个数（0 ~ 19682）。</summary>
        public int Code
        {
            get
            {
                int code = 0;
                // 和 FromCode 的取值顺序反过来写，保证编解码对称
                int[] v = { X.x, Y.x, Z.x, X.y, Y.y, Z.y, X.z, Y.z, Z.z };
                for (int i = 8; i >= 0; i--)
                    code = code * 3 + (v[i] + 1);
                return code;
            }
        }

        public static PieceRotation FromCode(int code)
        {
            int[] v = new int[9];
            for (int i = 0; i < 9; i++)
            {
                v[i] = code % 3 - 1;
                code /= 3;
            }
            return new PieceRotation(
                new Vector3Int(v[0], v[3], v[6]),
                new Vector3Int(v[1], v[4], v[7]),
                new Vector3Int(v[2], v[5], v[8]));
        }

        /// <summary>
        /// 老存档兼容：以前只存了一个 Facing（绕竖直轴转了几个 90°），
        /// 换算关系照搬当时的定义（Facing = 1 时 +x 转到 +z）。
        /// </summary>
        public static PieceRotation FromYaw(int facing)
        {
            switch (facing & 3)
            {
                case 1: return new PieceRotation(new Vector3Int(0, 0, 1), Vector3Int.up, new Vector3Int(-1, 0, 0));
                case 2: return new PieceRotation(new Vector3Int(-1, 0, 0), Vector3Int.up, new Vector3Int(0, 0, -1));
                case 3: return new PieceRotation(new Vector3Int(0, 0, -1), Vector3Int.up, new Vector3Int(1, 0, 0));
                default: return Identity;
            }
        }

        public bool SameAs(PieceRotation other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override string ToString()
        {
            return "X" + X + " Y" + Y + " Z" + Z;
        }

        // ---- 内部：三个基本旋转 ----

        /// <summary>
        /// 绕世界轴转 90° 的基本旋转（三根列向量）。
        /// 方向是实测 Unity 的 Quaternion 得到的，不是按手性推的：
        ///   Euler(0,0,-90)：right → -y、up → +x
        ///   Euler(0,-90,0)：right → +z、forward → -x
        ///   Euler(90,0,0) ：up → +z、forward → -y   （+z 就是画面里"往里/背面"的方向）
        /// 逆时针就是顺时针的转置（旋转矩阵的逆 = 转置）。
        /// </summary>
        static void Step(RotationAxis axis, bool clockwise, out Vector3Int c0, out Vector3Int c1, out Vector3Int c2)
        {
            int mode = (int)axis;
            if (mode == (int)RotationAxis.Pitch)
            {
                c0 = new Vector3Int(1, 0, 0);
                c1 = new Vector3Int(0, 0, 1);
                c2 = new Vector3Int(0, -1, 0);
            }
            else if (mode == (int)RotationAxis.Yaw)
            {
                c0 = new Vector3Int(0, 0, 1);
                c1 = new Vector3Int(0, 1, 0);
                c2 = new Vector3Int(-1, 0, 0);
            }
            else
            {
                c0 = new Vector3Int(0, -1, 0);
                c1 = new Vector3Int(1, 0, 0);
                c2 = new Vector3Int(0, 0, 1);
            }

            if (!clockwise)
            {
                // 转置：列变行
                Vector3Int t0 = new Vector3Int(c0.x, c1.x, c2.x);
                Vector3Int t1 = new Vector3Int(c0.y, c1.y, c2.y);
                Vector3Int t2 = new Vector3Int(c0.z, c1.z, c2.z);
                c0 = t0;
                c1 = t1;
                c2 = t2;
            }
        }

        /// <summary>矩阵乘向量：c0/c1/c2 是矩阵的三列。</summary>
        static Vector3Int Mul(Vector3Int c0, Vector3Int c1, Vector3Int c2, Vector3Int v)
        {
            return new Vector3Int(
                c0.x * v.x + c1.x * v.y + c2.x * v.z,
                c0.y * v.x + c1.y * v.y + c2.y * v.z,
                c0.z * v.x + c1.z * v.y + c2.z * v.z);
        }
    }
}
