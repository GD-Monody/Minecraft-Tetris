using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>一个立方体：贴图上的一块展开图 + 它在部件里的位置。</summary>
    public struct GolemCube
    {
        /// <summary>贴图左上角的像素坐标（对应 MC 的 texOffs）。</summary>
        public int U, V;
        /// <summary>方块尺寸（像素）：宽、高、深。</summary>
        public int W, H, D;
        /// <summary>立方体中心相对部件关节的偏移（像素，y 向上）。</summary>
        public Vector3 Center;

        public GolemCube(int u, int v, int w, int h, int d, Vector3 center)
        {
            U = u; V = v; W = w; H = h; D = d;
            Center = center;
        }
    }

    /// <summary>部件类型，决定走路时怎么摆。</summary>
    public enum GolemPart
    {
        Head, Body, ArmLeft, ArmRight, LegLeft, LegRight,
    }

    /// <summary>
    /// 一个部件（头 / 身体 / 手臂 / 腿）。一个部件可以挂好几个立方体 ——
    /// 原版就是这么干的：铁傀儡的鼻子、腰，铜傀儡的天线，都是各自部件里额外的 addBox。
    /// </summary>
    public struct GolemPartDef
    {
        public GolemPart Part;
        /// <summary>关节位置（像素，y 向上，原点在脚底中心）。</summary>
        public Vector3 Pivot;
        /// <summary>关节的初始旋转（度）。原版雪傀儡的手臂是斜着伸出来的。</summary>
        public Vector3 Rot;
        public GolemCube[] Cubes;
    }

    /// <summary>
    /// 傀儡模型：把 MC 的实体贴图按官方 UV 布局切成六个面，拼成一个个立方体部件。
    ///
    /// 数据不是估的：下面三张表的每一个数字，都是从本机 `versions/26.2/26.2.jar` 里
    /// 的模型类反编译抄出来的（`net.minecraft.client.model.animal.golem.*Model`），
    /// 贴图也是同一个 jar 里的 `assets/minecraft/textures/entity/**`，一一对得上。
    /// 抄法：MC 用 CubeListBuilder.texOffs(u,v).addBox(x,y,z,w,h,d) 描述一个盒子，
    /// PartPose.offset(px,py,pz) 是关节位置；MC 的 y 轴向下、脚底在 y=24，
    /// 这里统一换成「y 向上、脚底在 y=0」，即 myY = 24 - mcY。
    ///
    /// 为什么不干脆按贴图的连通块反解尺寸（以前那套做法）：反解只能量出「这块贴图多大」，
    /// 量不出它挂在哪、朝哪边、是不是和别的盒子叠在一起 —— 于是就会出现
    /// 「少一块身体」「鼻子没了」「腿被截断」这类问题。
    /// </summary>
    public static class GolemModel
    {
        public const float PixelsPerBlock = 16f;

        /// <summary>
        /// 雪傀儡。贴图 64×64。原版 SnowGolemModel.createBodyLayer：
        ///   lower_body  (0,36) 12³  y 0~12   底下的雪球
        ///   upper_body  (0,16) 10³  y 11~21  上面的雪球
        ///   head        (0,0)  8³   y 20~28  脸就画在贴图 (0,8) 那一圈四个侧面上
        ///   left/right_arm (32,0) 12×2×2     两根棕色木棍，绕 z 轴斜伸出去
        /// 以前只做了 upper_body + head，缺了底下那个 12³ 雪球，所以看着「少一块身体」。
        ///
        /// 手臂：原版 texOffs(32,0)、addBox(-1,0,-1,12,2,2)，pivot 在 (±5,18,∓1)，
        /// 左臂 rot z=+1rad、右臂 yRot=π + zRot=-1rad。这里换成本地等价写法：
        /// 盒子从关节往下伸 12 像素，关节绕 z 轴外倾 57.3°（1 弧度），左右对称。
        /// </summary>
        public static GolemPartDef[] Snow()
        {
            return new[]
            {
                Part(GolemPart.Body, new Vector3(0f, 0f, 0f), Vector3.zero,
                    Cube(0, 36, 12, 12, 12, 0f, 6f, 0f)),

                Part(GolemPart.Body, new Vector3(0f, 11f, 0f), Vector3.zero,
                    Cube(0, 16, 10, 10, 10, 0f, 5f, 0f)),

                Part(GolemPart.Head, new Vector3(0f, 20f, 0f), Vector3.zero,
                    Cube(0, 0, 8, 8, 8, 0f, 4f, 0f)),

                Part(GolemPart.ArmLeft, new Vector3(5f, 18f, -1f), new Vector3(0f, 0f, 57.3f),
                    Cube(32, 0, 12, 2, 2, 0f, -6f, 0f)),

                Part(GolemPart.ArmRight, new Vector3(-5f, 18f, 1f), new Vector3(0f, 0f, -57.3f),
                    Cube(32, 0, 12, 2, 2, 0f, -6f, 0f)),
            };
        }

        /// <summary>
        /// 铁傀儡。贴图 128×128。原版 IronGolemModel.createBodyLayer：
        ///   head  (0,0)  8×10×8 + 鼻子 (24,0) 2×4×2   y 33~43
        ///   body  (0,40) 18×12×11 + 腰 (0,70) 9×5×6  y 21~33、腰 16~21
        ///   arms  (60,21)/(60,58) 4×30×6             y 3.5~33.5
        ///   legs  (37,0)/(60,0)   6×16×5             y 0~16
        /// 以前少了鼻子（24,0 那块）和腰（0,70 那块），总高只有 38 像素，比原版 43 矮一截，
        /// 于是腿显得被身体压住了 —— 「腿部被截断」就是这么来的。
        /// </summary>
        public static GolemPartDef[] Iron()
        {
            return new[]
            {
                // 头挂在 z=-2：原版 PartPose.offset(0,-7,-2)，脸比身体更靠前
                Part(GolemPart.Head, new Vector3(0f, 31f, -2f), Vector3.zero,
                    Cube(0, 0, 8, 10, 8, 0f, 7f, -1.5f),
                    Cube(24, 0, 2, 4, 2, 0f, 3f, -6.5f)),

                Part(GolemPart.Body, new Vector3(0f, 31f, 0f), Vector3.zero,
                    Cube(0, 40, 18, 12, 11, 0f, -4f, -0.5f),
                    Cube(0, 70, 9, 5, 6, 0f, -12.5f, 0f)),

                // 手臂的关节和身体在同一个点上（原版都挂在 (0,-7,0)），盒子自己往外偏 11 像素
                Part(GolemPart.ArmLeft, new Vector3(0f, 31f, 0f), Vector3.zero,
                    Cube(60, 58, 4, 30, 6, 11f, -12.5f, 0f)),

                Part(GolemPart.ArmRight, new Vector3(0f, 31f, 0f), Vector3.zero,
                    Cube(60, 21, 4, 30, 6, -11f, -12.5f, 0f)),

                Part(GolemPart.LegLeft, new Vector3(5f, 13f, 0f), Vector3.zero,
                    Cube(60, 0, 6, 16, 5, -0.5f, -5f, 0f)),

                Part(GolemPart.LegRight, new Vector3(-4f, 13f, 0f), Vector3.zero,
                    Cube(37, 0, 6, 16, 5, -0.5f, -5f, 0f)),
            };
        }

        /// <summary>
        /// 铜傀儡。贴图 64×64。原版 CopperGolemModel.createBodyLayer，外面还套了一层
        /// `MeshDefinition.transformed(p -&gt; p.translated(0,24,0))` —— 它的模型是贴着 y=0 建的，
        /// 整体下移 24 像素后才和别的实体对齐脚底。下面已经是换好的数：
        ///   body  (0,15) 8×6×6                       y 5~11
        ///   head  (0,0)  8×5×10 + 鼻子 (56,0) 2×3×2   y 11~16
        ///               + 天线杆 (37,8) 2×4×2 + 球 (37,0) 4×4×4  y 16~24
        ///   arms  (36,16)/(50,16) 3×10×4            y 2~12
        ///   legs  (0,27)/(16,27)  4×5×4             y 0~5
        /// 注意头是「宽 8 高 5 深 10」—— 前后比左右长一倍，鼻子和天线都在头里面，
        /// 以前按贴图反解成「8×6×6 的方头」，所以看着不像。
        /// </summary>
        public static GolemPartDef[] Copper()
        {
            return new[]
            {
                Part(GolemPart.Body, new Vector3(0f, 5f, 0f), Vector3.zero,
                    Cube(0, 15, 8, 6, 6, 0f, 3f, 0f)),

                Part(GolemPart.Head, new Vector3(0f, 11f, 0f), Vector3.zero,
                    Cube(0, 0, 8, 5, 10, 0f, 2.5f, 0f),
                    Cube(56, 0, 2, 3, 2, 0f, 0.5f, -5f),
                    Cube(37, 8, 2, 4, 2, 0f, 7f, 0f),
                    Cube(37, 0, 4, 4, 4, 0f, 11f, 0f)),

                Part(GolemPart.ArmLeft, new Vector3(4f, 11f, 0f), Vector3.zero,
                    Cube(50, 16, 3, 10, 4, 1.5f, -4f, 0f)),

                Part(GolemPart.ArmRight, new Vector3(-4f, 11f, 0f), Vector3.zero,
                    Cube(36, 16, 3, 10, 4, -1.5f, -4f, 0f)),

                Part(GolemPart.LegLeft, new Vector3(0f, 5f, 0f), Vector3.zero,
                    Cube(16, 27, 4, 5, 4, 2f, -2.5f, 0f)),

                Part(GolemPart.LegRight, new Vector3(0f, 5f, 0f), Vector3.zero,
                    Cube(0, 27, 4, 5, 4, -2f, -2.5f, 0f)),
            };
        }

        static GolemCube Cube(int u, int v, int w, int h, int d, float cx, float cy, float cz)
        {
            return new GolemCube(u, v, w, h, d, new Vector3(cx, cy, cz));
        }

        static GolemPartDef Part(GolemPart part, Vector3 pivot, Vector3 rot, params GolemCube[] cubes)
        {
            return new GolemPartDef { Part = part, Pivot = pivot, Rot = rot, Cubes = cubes };
        }

        /// <summary>整个部件（可能好几个立方体）合成一个 mesh，单位格，以关节为原点。</summary>
        public static Mesh BuildPart(GolemPartDef def, int texW, int texH)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            for (int i = 0; i < def.Cubes.Length; i++)
                AppendCube(verts, norms, uvs, tris, def.Cubes[i], def.Cubes[i].Center / PixelsPerBlock,
                           texW, texH);

            var mesh = new Mesh();
            mesh.name = "GolemPart_" + def.Part;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>生成一块立方体的 mesh（单位格），以立方体中心为原点。</summary>
        public static Mesh BuildCube(GolemCube c, int texW, int texH)
        {
            var verts = new List<Vector3>(24);
            var norms = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);

            AppendCube(verts, norms, uvs, tris, c, Vector3.zero, texW, texH);

            var mesh = new Mesh();
            mesh.name = "GolemCube";
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 把一块立方体的六个面追加进去。offset 是这块立方体中心在部件里的位置（格），
        /// 一个部件好几个盒子时（铁傀儡的头 + 鼻子），就靠它把盒子摆到位。
        /// </summary>
        static void AppendCube(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                               GolemCube c, Vector3 offset, int texW, int texH)
        {
            float sx = c.W / PixelsPerBlock;
            float sy = c.H / PixelsPerBlock;
            float sz = c.D / PixelsPerBlock;

            float x0 = offset.x - sx * 0.5f, x1 = offset.x + sx * 0.5f;
            float y0 = offset.y - sy * 0.5f, y1 = offset.y + sy * 0.5f;
            float z0 = offset.z - sz * 0.5f, z1 = offset.z + sz * 0.5f;

            // MC 的展开：横坐标从 u 开始依次是 [右][正][左][背] 四条 d/w 宽的竖条，
            // 纵坐标 v 到 v+d 是上下两个面，v+d 到 v+d+h 是四个侧面。
            float uRight0 = c.U;
            float uFront0 = c.U + c.D;
            float uLeft0 = c.U + c.D + c.W;
            float uBack0 = c.U + c.D + c.W + c.D;

            float vTop0 = c.V;
            float vSide0 = c.V + c.D;
            float vSide1 = c.V + c.D + c.H;

            AddFace(verts, norms, uvs, tris,
                new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
                Vector3.up,
                uFront0, vTop0, uFront0 + c.W, vTop0 + c.D, texW, texH);          // 顶面

            AddFace(verts, norms, uvs, tris,
                new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0),
                Vector3.down,
                uFront0 + c.W, vTop0, uFront0 + c.W * 2f, vTop0 + c.D, texW, texH); // 底面

            // 四个侧面：顶点顺序统一成「从外侧看：上左 → 上右 → 下右 → 下左」。
            // 这条必须和 UV 的分配对得上（UV 是 a=图片左上、b=右上、c=右下、d=左下），
            // 写成「下左 → 下右 → 上右 → 上左」的话贴图会上下颠倒 —— 侧面四个一起反，
            // 看着就像整张贴图被翻过来了。顶面和底面同理，只是它们的「上」是「远」那一侧。
            AddFace(verts, norms, uvs, tris,
                new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1),
                Vector3.right,
                uLeft0, vSide0, uLeft0 + c.D, vSide1, texW, texH);                 // 左面（+x）

            AddFace(verts, norms, uvs, tris,
                new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), new Vector3(x0, y0, z0),
                Vector3.left,
                uRight0, vSide0, uRight0 + c.D, vSide1, texW, texH);               // 右面（-x）

            AddFace(verts, norms, uvs, tris,
                new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0), new Vector3(x0, y0, z0),
                Vector3.back,
                uFront0, vSide0, uFront0 + c.W, vSide1, texW, texH);               // 正面（-z）

            AddFace(verts, norms, uvs, tris,
                new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
                Vector3.forward,
                uBack0, vSide0, uBack0 + c.W, vSide1, texW, texH);                 // 背面（+z）
        }

        /// <summary>
        /// 加一个面。UV 用图片像素坐标（左上原点），这里统一翻成 Unity 的左下原点。
        ///
        /// 绕向在这里自己校一遍，不靠调用点记得写对，也不靠背口诀 ——
        /// Unity 是左手坐标系，正的三角形（正面朝外）和右手叉积 cross(b-a, c-a) 是**同向**的，
        /// 这一点反直觉（右手系里正好相反），我按右手系写的结果就是整块模型内外翻过来，
        /// 从外面看到的是盒子内壁，贴图看着像"贴在里侧"。
        /// 判据用自检钉住了：Mesh.RecalculateNormals() 按绕向生成法线，
        /// 它算出来的方向必须和我们给的法线同向（见 SelfTest 的「网格绕向与法线同向」）。
        /// </summary>
        static void AddFace(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                            Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal,
                            float u0, float v0, float u1, float v1, int texW, int texH)
        {
            int i = verts.Count;

            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int k = 0; k < 4; k++)
                norms.Add(normal);

            float ua = u0 / texW;
            float ub = u1 / texW;
            float va = 1f - v0 / texH;   // 图片上方 = UV 的 v 大
            float vb = 1f - v1 / texH;

            uvs.Add(new Vector2(ua, va));
            uvs.Add(new Vector2(ub, va));
            uvs.Add(new Vector2(ub, vb));
            uvs.Add(new Vector2(ua, vb));

            // 顶点顺序和法线反向 → 这个三角形朝里，把索引倒过来（UV 不动，只翻绕向）
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0f)
            {
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }
            else
            {
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
        }
    }
}
