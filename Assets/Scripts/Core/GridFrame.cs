using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地外观：底下一层 MC 石砖地台，四周一圈玻璃围挡。
    ///
    /// 地台：只铺「能放方块」的那几列，边缘就是场地边界；进深跟场地 z 一致，
    /// 并且沿 z 按「一格一块」切开（整条拉通会把贴图拉成长条，砖缝全糊）。
    ///
    /// 玻璃：方块表里原版 glass 那格贴图就是「中间全透明 + 边缘一圈浅线」，
    /// 正好是 MC 里那种看不见玻璃只有框线的效果。四面各画一层薄片（不是实心方块），
    /// 免得四层玻璃叠在一起把后面糊成毛玻璃。
    ///
    /// 上一版是蓝色发光线框（两面网格墙 + 加色光晕），读起来像科幻界面，和满场的方块
    /// 不在一个世界里，所以整体换成了方块表里的真方块。
    /// </summary>
    public class GridFrame : MonoBehaviour
    {
        /// <summary>地台用哪种方块。想换材质改这一行就行（方块表里的英文名）。</summary>
        const string FrameBlockName = "stone_bricks";

        /// <summary>四周那圈围挡用哪种方块。必须是方块表里标了透明的（玻璃 / 冰之类）。</summary>
        const string GlassBlockName = "glass";

        /// <summary>前脸比场地前沿再出来一点，别和场上最前排方块的贴面共面打架。</summary>
        const float FrontLip = 0.14f;

        /// <summary>玻璃往外让一点，别和贴着边界那一列方块的贴面 z-fighting。</summary>
        const float GlassLip = 0.03f;


        public void Build(int sizeX, int sizeY, int sizeZ)
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            int id = BlockCatalog.IdOf(FrameBlockName);
            if (id <= 0)
            {
                // 方块表被人换了名字也不至于整场没地台，只是没有地台而已
                Debug.LogWarning("[frame] 方块表里没有 " + FrameBlockName + "，这次不画地台");
                return;
            }

            BlockDef def = BlockCatalog.Of(id);
            Tile side = def.South;
            Tile top = def.Up;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // 进深跟场地深度走，而且沿 z 按「一格一块」切开：
            // 整条拉通的话侧面上那张贴图会被拉成一条长条，砖缝全糊了；
            // 按格切开之后每一块都是 1×1×1 的整方块，和场上的方块一个规格。
            float zFront = -0.5f - FrontLip;
            int depth = Mathf.Max(1, sizeZ);

            // 只铺一层地砖，而且只铺「能放方块」的那些列：左右不多铺那一格，
            // 地台边缘就是场地边界，一眼能看出方块最多能推到哪儿
            for (int x = 0; x < sizeX; x++)
                AddRow(verts, uvs, tris, x, -1, zFront, depth, side, top);

            CreateMeshObject(verts, uvs, tris, BlockMesh.AtlasMaterial, "FrameFloor");

            BuildGlassWalls(sizeX, sizeY, sizeZ);
        }

        /// <summary>
        /// 四周的玻璃围挡：左、右、后、顶各一层极淡的透明面，前脸敞着。
        ///
        /// 第一版照方块表里 glass 那格贴图、按格拼了一圈，结果每一格都自带一圈白边，
        /// 四面叠起来就是一张白网，比原来的发光线框还闹。这版换成「无痕」的画法：
        /// 不用贴图的格子图案，只用一层 alpha 很低的纯色，另外沿四个竖角描一条细边
        /// 提示边界。远看就是一块干净的玻璃罩子，近看才有一点点反光。
        /// </summary>
        void BuildGlassWalls(int sizeX, int sizeY, int sizeZ)
        {
            float xL = -0.5f - GlassLip, xR = sizeX - 0.5f + GlassLip;
            float yB = -0.5f, yT = sizeY - 0.5f;
            float zF = -0.5f, zB = sizeZ - 0.5f + GlassLip;

            // 面板本身：几乎看不见的一层色
            Material paneMat = MakeGlassMaterial(new Color(0.74f, 0.90f, 1f, 0.030f));
            if (paneMat != null)
            {
                var v = new List<Vector3>();
                var uv = new List<Vector2>();
                var t = new List<int>();

                AddPane(v, uv, t, new Vector3(xL, yB, zF), new Vector3(xL, yB, zB),
                    new Vector3(xL, yT, zB), new Vector3(xL, yT, zF));      // 左
                AddPane(v, uv, t, new Vector3(xR, yB, zB), new Vector3(xR, yB, zF),
                    new Vector3(xR, yT, zF), new Vector3(xR, yT, zB));      // 右
                AddPane(v, uv, t, new Vector3(xL, yB, zB), new Vector3(xR, yB, zB),
                    new Vector3(xR, yT, zB), new Vector3(xL, yT, zB));      // 后
                AddPane(v, uv, t, new Vector3(xL, yT, zF), new Vector3(xR, yT, zF),
                    new Vector3(xR, yT, zB), new Vector3(xL, yT, zB));      // 顶

                CreateMeshObject(v, uv, t, paneMat, "FrameGlass");
            }

            // 原来这里还沿四个竖角描了一圈细边（alpha 0.30、宽 0.022 格），
            // 想交代出「这里有个罩子」。实际效果是场地四周浮着一个白色大方框，
            // 网页版上尤其明显（canvas 分辨率低，不到一像素的边被抗锯齿摊成均匀亮线），
            // 看着像页面边框而不像玻璃。无痕玻璃本来就不该有框线，整段删掉：
            // 现在只剩一层 alpha 0.03 的面，远处完全看不出来，近处才有一点反光。
            //
            // AddEdge 暂时留着不用，以后想加回装饰边可以直接调。
        }

        /// <summary>
        /// 一块透明面。UV 全给同一个点：贴图是一张 1×1 的纯色，取哪儿都一样，
        /// 这样就不会出现方块贴图那种一格一格的边框。
        /// </summary>
        static void AddPane(List<Vector3> v, List<Vector2> uv, List<int> t,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            int i = v.Count;
            v.Add(p0);
            v.Add(p1);
            v.Add(p2);
            v.Add(p3);
            uv.Add(Vector2.zero);
            uv.Add(Vector2.zero);
            uv.Add(Vector2.zero);
            uv.Add(Vector2.zero);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
            t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        /// <summary>竖向的细边：一条朝前、一条朝侧，凑出有厚度的棱。</summary>
        static void AddEdge(List<Vector3> v, List<Vector2> uv, List<int> t,
            float x, float z, float y0, float y1, float w)
        {
            AddPane(v, uv, t,
                new Vector3(x - w, y0, z - w), new Vector3(x + w, y0, z - w),
                new Vector3(x + w, y1, z - w), new Vector3(x - w, y1, z - w));
            AddPane(v, uv, t,
                new Vector3(x - w, y0, z + w), new Vector3(x, y0, z + w),
                new Vector3(x, y1, z + w), new Vector3(x - w, y1, z + w));
        }

        /// <summary>
        /// 造一块纯色透明材质：克隆现有的透明方块材质（shader 不透明会盖住场景），
        /// 把贴图换成 1×1 纯色。颜色里的 alpha 就是这层玻璃的浓度。
        /// </summary>
        static Material MakeGlassMaterial(Color color)
        {
            int gid = BlockCatalog.IdOf(GlassBlockName);
            if (gid <= 0)
                return null;

            Material src = BlockMesh.GetMaterial(BlockCatalog.Of(gid));
            if (src == null)
                return null;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;

            var mat = new Material(src);
            mat.mainTexture = tex;
            return mat;
        }

        /// <summary>沿 z 方向摆一串方块（一个边框格子 = depth 块）。</summary>
        void AddRow(List<Vector3> v, List<Vector2> uv, List<int> t,
            float cx, float cy, float zFront, int depth, Tile side, Tile top)
        {
            for (int z = 0; z < depth; z++)
            {
                float z0 = zFront + z;
                AddBox(v, uv, t, cx, cy, z0, z0 + 1f, side, top);
            }
        }

        /// <summary>一塊 1×1 的方块：中心在 (cx, cy)，z 从 z0 到 z1。</summary>
        void AddBox(List<Vector3> v, List<Vector2> uv, List<int> t,
            float cx, float cy, float z0, float z1, Tile side, Tile top)
        {
            float x0 = cx - 0.5f, x1 = cx + 0.5f;
            float y0 = cy - 0.5f, y1 = cy + 0.5f;

            // 前后面（朝向镜头 / 背面）
            AddQuad(v, uv, t, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0),
                new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), side);
            AddQuad(v, uv, t, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1),
                new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), side);

            // 左右两侧：转视角的时候看得到，不画就是一张纸片
            AddQuad(v, uv, t, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0),
                new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), side);
            AddQuad(v, uv, t, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1),
                new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), side);

            // 顶面用方块自己的顶面贴图：石砖顶面和侧面花纹不一样，混用一眼能看出来
            AddQuad(v, uv, t, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0),
                new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), top);
            AddQuad(v, uv, t, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1),
                new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), top);
        }

        /// <summary>
        /// 四个点按「左下、右下、右上、左上」给，UV 对应图集里那一格。
        /// shader 是 Cull Off，绕序不用管，只关心贴图别上下颠倒。
        /// </summary>
        void AddQuad(List<Vector3> v, List<Vector2> uv, List<int> t,
            Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Tile tile)
        {
            int i = v.Count;
            v.Add(p0);
            v.Add(p1);
            v.Add(p2);
            v.Add(p3);

            float u0, u1, v0, v1;
            TileUV(tile, out u0, out u1, out v0, out v1);
            uv.Add(new Vector2(u0, v0));
            uv.Add(new Vector2(u1, v0));
            uv.Add(new Vector2(u1, v1));
            uv.Add(new Vector2(u0, v1));

            t.Add(i);
            t.Add(i + 1);
            t.Add(i + 2);
            t.Add(i);
            t.Add(i + 2);
            t.Add(i + 3);
        }

        /// <summary>
        /// 图集格子外面有 1 像素 padding（复制边缘像素），UV 只取里面那 16×16，
        /// 和 BlockMesh.Quad / LayerMap.TileUV 保持一致。
        /// </summary>
        static void TileUV(Tile tile, out float u0, out float u1, out float v0, out float v1)
        {
            int cell = BlockCatalog.CellPixels;
            int pad = BlockCatalog.TilePadding;
            float w = (float)BlockCatalog.AtlasColumns * cell;
            float h = (float)BlockCatalog.AtlasRows * cell;

            float x0 = tile.Col * cell + pad;
            float yTop = tile.Row * cell + pad;
            u0 = x0 / w;
            u1 = (x0 + BlockCatalog.TilePixels) / w;
            v1 = 1f - yTop / h;
            v0 = 1f - (yTop + BlockCatalog.TilePixels) / h;
        }

        void CreateMeshObject(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Material mat, string name)
        {
            if (verts.Count == 0 || mat == null)
                return;

            var mesh = new Mesh();
            mesh.name = "GridFrame_" + name;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
