using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 一次射线命中的结果：格子坐标 + 被射中的那个面的外法线。
    /// 法线是关键——新的方块就长在这个法线所指的那一格上。
    /// </summary>
    public struct VoxelHit
    {
        public bool Valid;
        public Vector3Int Cell;
        /// <summary>被命中的那个面的「朝外」法线，例如 (0,1,0) 表示打在顶面上。</summary>
        public Vector3Int Face;
        /// <summary>起点到命中点的距离。</summary>
        public float Distance;
    }

    /// <summary>
    /// 体素射线拾取：给一条屏幕射线，算出它最先撞到的那一格是哪一个，以及撞的是哪个面。
    ///
    /// 用 Amanatides &amp; Woo 的体素步进（DDA）而不是给 125 个格子各挂一个碰撞体：
    /// - 纯算术，几十行，能塞进自检里做穷举对比；
    /// - 天生带着「刚跨过的是哪个轴方向的面」，这正是我们要的吸附信息；
    /// - 不碰物理系统，也不用等物理世界更新。
    ///
    /// 算法：先用 slab 法算出射线与建造盒的交区间 [tEnter, tExit]，从入射点所在的那一格出发，
    /// 每步选「下一次穿面」最近的那个轴跨过去，一路走到有方块的那格或者出了盒子为止。
    /// </summary>
    public static class VoxelRaycast
    {
        /// <summary>入射点往里挪一点点再算，免得正好踩在整数格面上时 floor 出歧义。</summary>
        const float Eps = 1e-4f;

        /// <summary>盒子的三条边最低都是 1，所以起点在世界原点、(0,0,0) 起，(0,0,0) 那一格。</summary>
        public static bool Trace(
            Vector3 origin, Vector3 dir, Vector3Int size,
            System.Func<Vector3Int, bool> isSolid,
            out VoxelHit hit,
            float maxDistance = 500f)
        {
            hit = default(VoxelHit);

            if (isSolid == null || size.x <= 0 || size.y <= 0 || size.z <= 0)
                return false;

            Vector3 d = dir.normalized;
            if (d.sqrMagnitude < 0.5f)
                return false;

            // 射线和建造盒的交区间：完全错过就不用往下走了
            float tEnter, tExit;
            Vector3Int enterFace;
            if (!BoxRange(origin, d, size, out tEnter, out tExit, out enterFace))
                return false;
            if (tEnter > maxDistance)
                return false;
            // 盒子整个都在射线背后：slab 法照样算得出一对 t，但都是负的
            if (tExit < 0f)
                return false;
            if (tEnter < 0f)
            {
                // 起点在盒子里面：没有「入射面」这一说，从当前位置这一格开始往外卖
                tEnter = 0f;
                enterFace = Vector3Int.zero;
            }

            float t = tEnter + Eps;
            Vector3 p = origin + d * t;

            int ix = Mathf.FloorToInt(p.x);
            int iy = Mathf.FloorToInt(p.y);
            int iz = Mathf.FloorToInt(p.z);
            if (ix < 0) ix = 0; else if (ix >= size.x) ix = size.x - 1;
            if (iy < 0) iy = 0; else if (iy >= size.y) iy = size.y - 1;
            if (iz < 0) iz = 0; else if (iz >= size.z) iz = size.z - 1;

            int stepX = d.x > 0f ? 1 : (d.x < 0f ? -1 : 0);
            int stepY = d.y > 0f ? 1 : (d.y < 0f ? -1 : 0);
            int stepZ = d.z > 0f ? 1 : (d.z < 0f ? -1 : 0);

            // 下一次穿过 x / y / z 方向格面的距离（tMax）和每次跨一格的增量（tDelta）
            float dx = stepX != 0 ? 1f / Mathf.Abs(d.x) : float.PositiveInfinity;
            float dy = stepY != 0 ? 1f / Mathf.Abs(d.y) : float.PositiveInfinity;
            float dz = stepZ != 0 ? 1f / Mathf.Abs(d.z) : float.PositiveInfinity;

            float nextX = stepX == 0 ? float.PositiveInfinity
                : ((stepX > 0 ? ix + 1 - p.x : p.x - ix) * dx);
            float nextY = stepY == 0 ? float.PositiveInfinity
                : ((stepY > 0 ? iy + 1 - p.y : p.y - iy) * dy);
            float nextZ = stepZ == 0 ? float.PositiveInfinity
                : ((stepZ > 0 ? iz + 1 - p.z : p.z - iz) * dz);

            // 入射那一格先答一遍：这就是对象的「正面」格子，Minecraft 也是这么定的
            var cell = new Vector3Int(ix, iy, iz);
            if (isSolid(cell))
                return Fill(ref hit, cell, enterFace, t);

            // 之后每跨一个面就问一次；出了盒子就不再追
            int guard = (size.x + size.y + size.z + 4) * 2;
            while (guard-- > 0)
            {
                if (nextX <= nextY && nextX <= nextZ)
                {
                    t += nextX;
                    ix += stepX;
                    nextY -= nextX;
                    nextZ -= nextX;
                    nextX = dx;
                    if (stepX == 0 || ix < 0 || ix >= size.x)
                        return false;
                    enterFace = new Vector3Int(-stepX, 0, 0);
                }
                else if (nextY <= nextZ)
                {
                    t += nextY;
                    iy += stepY;
                    nextX -= nextY;
                    nextZ -= nextY;
                    nextY = dy;
                    if (stepY == 0 || iy < 0 || iy >= size.y)
                        return false;
                    enterFace = new Vector3Int(0, -stepY, 0);
                }
                else
                {
                    t += nextZ;
                    iz += stepZ;
                    nextX -= nextZ;
                    nextY -= nextZ;
                    nextZ = dz;
                    if (stepZ == 0 || iz < 0 || iz >= size.z)
                        return false;
                    enterFace = new Vector3Int(0, 0, -stepZ);
                }

                if (t > maxDistance || t > tExit + Eps)
                    return false;

                cell = new Vector3Int(ix, iy, iz);
                if (isSolid(cell))
                    return Fill(ref hit, cell, enterFace, t);
            }

            return false;
        }

        static bool Fill(ref VoxelHit hit, Vector3Int cell, Vector3Int face, float t)
        {
            hit.Valid = true;
            hit.Cell = cell;
            hit.Face = face;
            hit.Distance = t;
            return true;
        }

        /// <summary>
        /// 射线和轴对齐盒子的交区间（slab 法）。enterFace 是射线「从哪个面进来」的外法线，
        /// 起点在盒子里时为零向量。
        /// </summary>
        public static bool BoxRange(Vector3 origin, Vector3 dir, Vector3Int size,
            out float tEnter, out float tExit, out Vector3Int enterFace)
        {
            tEnter = float.NegativeInfinity;
            tExit = float.PositiveInfinity;
            enterFace = Vector3Int.zero;

            if (size.x <= 0 || size.y <= 0 || size.z <= 0)
                return false;

            float[] o = { origin.x, origin.y, origin.z };
            float[] d = { dir.x, dir.y, dir.z };
            int[] s = { size.x, size.y, size.z };

            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-9f)
                {
                    if (o[i] < 0f || o[i] > s[i])
                        return false;
                    continue;
                }

                float t1 = (0f - o[i]) / d[i];
                float t2 = (s[i] - o[i]) / d[i];
                float sign = -Mathf.Sign(d[i]);   // 从低侧进来 => 法线朝负方向
                if (t1 > t2)
                {
                    float tmp = t1; t1 = t2; t2 = tmp;
                    sign = -sign;
                }

                if (t1 > tEnter)
                {
                    tEnter = t1;
                    enterFace = new Vector3Int(
                        i == 0 ? (int)sign : 0,
                        i == 1 ? (int)sign : 0,
                        i == 2 ? (int)sign : 0);
                }
                if (t2 < tExit)
                    tExit = t2;

                if (tEnter > tExit)
                    return false;
            }

            return true;
        }
    }
}
