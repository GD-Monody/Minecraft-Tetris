using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地边框：铺在场地底下的一排 MC 石砖，只当地台，两侧不竖墙。
    ///
    /// 上一版是蓝色发光线框（两面网格墙 + 加色光晕），读起来像科幻界面，和满场的方块
    /// 不在一个世界里。换成方块表里的石砖之后，左右两堵墙在透视里会被摊成两条斜边
    /// （相机是俯视的），看着像个漏斗 —— 干脆只留地台：方块就像摆在一层砖上，
    /// 场地边界由「能摆到第几格」自己说明，不用墙去画。
    ///
    /// 地台进深跟场地深度一致，而且沿 z 按「一格一块」切开：整条拉通的话贴图会被
    /// 拉成一条长条，砖缝全糊了。
    /// </summary>
    public class GridFrame : MonoBehaviour
    {
        /// <summary>围场地用哪种方块。想换材质改这一行就行（方块表里的英文名）。</summary>
        const string FrameBlockName = "stone_bricks";

        /// <summary>前脸比场地前沿再出来一点，别和场上最前排方块的贴面共面打架。</summary>
        const float FrontLip = 0.14f;


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

            CreateMeshObject(verts, uvs, tris, BlockMesh.AtlasMaterial);
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

        void CreateMeshObject(List<Vector3> verts, List<Vector2> uvs, List<int> tris, Material mat)
        {
            if (verts.Count == 0 || mat == null)
                return;

            var mesh = new Mesh();
            mesh.name = "GridFrame_Blocks";
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var go = new GameObject("FrameBlocks");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
    }
}
