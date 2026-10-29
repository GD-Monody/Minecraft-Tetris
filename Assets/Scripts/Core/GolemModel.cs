using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>傀儡的一个部件（头 / 身体 / 手臂 / 腿），对应贴图里的一块展开图。</summary>
    public struct GolemCube
    {
        /// <summary>贴图左上角的像素坐标（MC 的 texOffs）。</summary>
        public int U, V;
        /// <summary>方块尺寸（像素）：宽、高、深。</summary>
        public int W, H, D;
        /// <summary>关节位置，单位像素，原点在脚底中心，y 向上。</summary>
        public Vector3 Pivot;
        /// <summary>立方体中心相对关节的偏移（像素）。</summary>
        public Vector3 Center;

        public GolemCube(int u, int v, int w, int h, int d, Vector3 pivot, Vector3 center)
        {
            U = u; V = v; W = w; H = h; D = d;
            Pivot = pivot; Center = center;
        }
    }

    /// <summary>部件类型，决定走路时怎么摆。</summary>
    public enum GolemPart
    {
        Head, Body, ArmLeft, ArmRight, LegLeft, LegRight,
    }

    public struct GolemPartDef
    {
        public GolemPart Part;
        public GolemCube Cube;
    }

    /// <summary>
    /// 傀儡模型：把 MC 的实体贴图按官方 UV 布局切成六个面，拼成一个个立方体部件。
    ///
    /// 为什么不沿用方块：傀儡在 MC 里是实体，皮肤是 `textures/entity/*.png` 那种"展开的纸盒"，
    /// 用方块贴图堆出来怎么都不像。这里按 MC 自己的 cube 展开规则还原：
    /// 一块 w×h×d 的立方体，贴图上占的矩形宽 2*(w+d)、高 d+h，
    /// 依次是 [右面][正面][左面][背面] 四条竖条 + 上下的 [顶面][底面]。
    ///
    /// 坐标：像素为单位，1 格 = 16 像素，y 向上（MC 模型是 y 向下，这里反过来）。
    /// </summary>
    public static class GolemModel
    {
        public const float PixelsPerBlock = 16f;

        /// <summary>
        /// 雪傀儡的部件。贴图 64×64。
        ///
        /// 这张贴图上实际只有三块不透明的展开区（用连通块量出来的）：
        ///   (0,0)  8×8×8     头
        ///   (0,16) 10×10×10  —— 26.2 的模型已经不用了，留着不动
        ///   (0,36) 12×12×12  身体
        /// 手臂在 26.2 的贴图上**没有独立区域**了（老的 (32,0) 整块透明，采过去一片空，
        /// 看起来就是"手臂没贴图"）。所以手臂借身体的侧面那条空白雪色带（y48~59）当采样区，
        /// 那一带整片是不透明的雪，取哪一格都是白的，做树枝手臂够用。
        /// </summary>
        public static GolemPartDef[] Snow()
        {
            return new[]
            {
                // 原版是「下面一个大雪球 + 上面一个小雪球（盖南瓜头）」：
                // 身体 10×10×10、头 8×8×8，两条细树枝手臂从身体两侧伸出来往下垂。
                // 之前身体用 12³，头显得太小、整体像个方箱子，不像雪人。
                Part(GolemPart.Body, 0, 16, 10, 10, 10, new Vector3(0f, 0f, 0f), new Vector3(0f, 5f, 0f)),
                Part(GolemPart.Head, 0, 0, 8, 8, 8, new Vector3(0f, 10f, 0f), new Vector3(0f, 4f, 0f)),
                Part(GolemPart.ArmLeft, 0, 48, 2, 6, 2, new Vector3(-5f, 11f, 0f), new Vector3(0f, -3f, 0f)),
                Part(GolemPart.ArmRight, 0, 48, 2, 6, 2, new Vector3(5f, 11f, 0f), new Vector3(0f, -3f, 0f)),
            };
        }

        /// <summary>铁傀儡的部件。贴图 128×128。</summary>
        public static GolemPartDef[] Iron()
        {
            return new[]
            {
                // 两条腿 6×16×5、身体 18×12×11、手臂 4×28×6（长到手垂过膝）、头 8×10×8
                Part(GolemPart.LegLeft, 37, 0, 6, 16, 5, new Vector3(-3.5f, 16f, 0f), new Vector3(0f, -8f, 0f)),
                Part(GolemPart.LegRight, 60, 0, 6, 16, 5, new Vector3(3.5f, 16f, 0f), new Vector3(0f, -8f, 0f)),
                Part(GolemPart.Body, 0, 40, 18, 12, 11, new Vector3(0f, 16f, 0f), new Vector3(0f, 6f, 0f)),
                // 手臂按 30 像素做会垂到脚底以下 2 像素，被地面切掉一截（看着像「腿被截断」）。
                // 28 正好从肩膀垂到脚底，贴图上少取末端两行，肉眼看不出。
                Part(GolemPart.ArmLeft, 60, 21, 4, 28, 6, new Vector3(-11f, 28f, 0f), new Vector3(0f, -14f, 0f)),
                Part(GolemPart.ArmRight, 60, 58, 4, 28, 6, new Vector3(11f, 28f, 0f), new Vector3(0f, -14f, 0f)),
                Part(GolemPart.Head, 0, 0, 8, 10, 8, new Vector3(0f, 28f, 0f), new Vector3(0f, 5f, 0f)),
            };
        }

        /// <summary>
        /// 铜傀儡的部件。贴图 64×64。它是个头大身子小的矮家伙（碰撞箱 0.49 × 0.98 格）。
        ///
        /// 这几组数字不是猜的：铜傀儡是 26.2 新增的实体，没有公开的 UV 资料，
        /// 所以直接按贴图上真实存在的展开块反解 —— 一个 w×h×d 的方块，
        /// 展开后上窄带宽 2w、下宽带 2(w+d)，扫一遍贴图就能对上号：
        ///   (0,0)   8×5×10   身体（前后比左右厚，它要抱东西）
        ///   (0,15)  8×6×6    头（方头，宽度和身体一样）
        ///   (0,27)  4×5×4    腿   (16,27) 同一尺寸，右腿
        ///   (36,16) 3×10×4   手臂 (50,16) 同一尺寸，右臂
        ///
        /// 腿和手臂一开始搞反了：3×10×4 那种细长条是**手臂**（垂到脚），
        /// 4×5×4 才是短腿。按「腿 5 + 身体 5 + 头 6 = 16 像素」算，正好 1 格高，
        /// 和原版 0.98 格对得上 —— 反过来的话整个人有 1.3 格，腿长身子短，很怪。
        /// </summary>
        public static GolemPartDef[] Copper()
        {
            return new[]
            {
                Part(GolemPart.LegLeft, 0, 27, 4, 5, 4, new Vector3(-2.5f, 5f, 0f), new Vector3(0f, -2.5f, 0f)),
                Part(GolemPart.LegRight, 16, 27, 4, 5, 4, new Vector3(2.5f, 5f, 0f), new Vector3(0f, -2.5f, 0f)),
                Part(GolemPart.Body, 0, 0, 8, 5, 10, new Vector3(0f, 5f, 0f), new Vector3(0f, 2.5f, 0f)),
                Part(GolemPart.ArmLeft, 36, 16, 3, 10, 4, new Vector3(-5.5f, 10f, 0f), new Vector3(0f, -5f, 0f)),
                Part(GolemPart.ArmRight, 50, 16, 3, 10, 4, new Vector3(5.5f, 10f, 0f), new Vector3(0f, -5f, 0f)),
                Part(GolemPart.Head, 0, 15, 8, 6, 6, new Vector3(0f, 10f, 0f), new Vector3(0f, 3f, 0f)),
            };
        }

        static GolemPartDef Part(GolemPart part, int u, int v, int w, int h, int d,
                                 Vector3 pivot, Vector3 center)
        {
            return new GolemPartDef
            {
                Part = part,
                Cube = new GolemCube(u, v, w, h, d, pivot, center),
            };
        }

        /// <summary>
        /// 生成一块立方体的 mesh（单位：格）。以部件中心为原点，
        /// 六个面的 UV 按 MC 的展开规则从贴图上取。
        /// </summary>
        public static Mesh BuildCube(GolemCube c, int texW, int texH)
        {
            float sx = c.W / PixelsPerBlock;
            float sy = c.H / PixelsPerBlock;
            float sz = c.D / PixelsPerBlock;

            float x0 = -sx * 0.5f, x1 = sx * 0.5f;
            float y0 = -sy * 0.5f, y1 = sy * 0.5f;
            float z0 = -sz * 0.5f, z1 = sz * 0.5f;

            var verts = new List<Vector3>(24);
            var norms = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);

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
