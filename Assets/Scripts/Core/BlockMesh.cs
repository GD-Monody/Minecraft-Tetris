using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 把 BlockDef 做成一个立方体网格，UV 从图集取。
    /// 全场方块共用一份材质，靠图集区分外观。
    /// </summary>
    public static class BlockMesh
    {
        // 顶点比 1 单位稍微大一点，让相邻方块重叠，避免交界处出现 1 像素的缝
        const float Half = 0.5005f;

        static Material _atlasMaterial;
        static Material _transparentMaterial;

        public static Material AtlasMaterial
        {
            get
            {
                if (_atlasMaterial == null)
                {
                    // 构建版本里 Shader.Find 找不到没有资源引用的内置 shader，
                    // 所以正常路径是加载工程里引用了该 shader 的材质资源。
                    _atlasMaterial = Resources.Load<Material>("BlockMat");
                    if (_atlasMaterial == null)
                    {
                        _atlasMaterial = BuildFallback("Unlit/Texture");
                    }
                    else
                    {
                        var tex = _atlasMaterial.mainTexture as Texture2D;
                        if (tex != null)
                            Debug.Log("[MinecraftTetris] atlas filter=" + tex.filterMode
                                      + " size=" + tex.width + "x" + tex.height
                                      + " mips=" + tex.mipmapCount);
                        else
                            Debug.LogWarning("[MinecraftTetris] BlockMat 没有主纹理");
                    }
                }
                return _atlasMaterial;
            }
        }

        public static Material GetMaterial(BlockDef def)
        {
            if (!def.Transparent)
                return AtlasMaterial;

            if (_transparentMaterial == null)
            {
                _transparentMaterial = Resources.Load<Material>("BlockTransparentMat");
                if (_transparentMaterial == null)
                    _transparentMaterial = BuildFallback("Unlit/Transparent");
            }
            return _transparentMaterial;
        }

        static Material BuildFallback(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader);
            Texture2D tex = Resources.Load<Texture2D>("atlas");
            if (tex != null)
                tex.filterMode = FilterMode.Point;
            mat.mainTexture = tex;
            return mat;
        }

        public static Mesh Build(BlockDef def)
        {
            var verts = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);

            // 上面（+y）
            Quad(verts, uvs, tris, def.Top,
                new Vector3(-Half, Half, Half),
                new Vector3(Half, Half, Half),
                new Vector3(Half, Half, -Half),
                new Vector3(-Half, Half, -Half));

            // 下面（-y）
            Quad(verts, uvs, tris, def.Bottom,
                new Vector3(-Half, -Half, -Half),
                new Vector3(Half, -Half, -Half),
                new Vector3(Half, -Half, Half),
                new Vector3(-Half, -Half, Half));

            // 北（+z）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(-Half, -Half, Half),
                new Vector3(Half, -Half, Half),
                new Vector3(Half, Half, Half),
                new Vector3(-Half, Half, Half));

            // 南（-z）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(Half, -Half, -Half),
                new Vector3(-Half, -Half, -Half),
                new Vector3(-Half, Half, -Half),
                new Vector3(Half, Half, -Half));

            // 东（+x）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(Half, -Half, Half),
                new Vector3(Half, -Half, -Half),
                new Vector3(Half, Half, -Half),
                new Vector3(Half, Half, Half));

            // 西（-x）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(-Half, -Half, -Half),
                new Vector3(-Half, -Half, Half),
                new Vector3(-Half, Half, Half),
                new Vector3(-Half, Half, -Half));

            Mesh mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            return mesh;
        }

        static void Quad(List<Vector3> v, List<Vector2> uv, List<int> t, Tile tile,
                         Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(c);
            v.Add(d);

            // 图集每个格子外面有 1 像素 padding（复制边缘像素），
            // 所以 UV 只取格子内部那 16×16；边缘附近就算采样越界，取到的也是同一块方块的边缘色。
            int cell = BlockCatalog.CellPixels;
            int pad = BlockCatalog.TilePadding;
            float w = (float)BlockCatalog.AtlasColumns * cell;
            float h = (float)BlockCatalog.AtlasRows * cell;

            float x0 = tile.Col * cell + pad;
            float yTop = tile.Row * cell + pad;

            float u0 = x0 / w;
            float u1 = (x0 + BlockCatalog.TilePixels) / w;
            float v1 = 1f - yTop / h;
            float v0 = 1f - (yTop + BlockCatalog.TilePixels) / h;

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
    }
}
