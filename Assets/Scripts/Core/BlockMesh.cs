using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 把 BlockDef 做成一个 1×1×1 的立方体网格，UV 从图集取。
    /// 全场方块共用一份材质，靠图集区分外观。
    /// </summary>
    public static class BlockMesh
    {
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
                        _atlasMaterial = BuildFallback("Unlit/Texture");
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
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f));

            // 下面（-y）
            Quad(verts, uvs, tris, def.Bottom,
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f));

            // 北（+z）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f));

            // 南（-z）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f));

            // 东（+x）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(0.5f, -0.5f, 0.5f),
                new Vector3(0.5f, -0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, -0.5f),
                new Vector3(0.5f, 0.5f, 0.5f));

            // 西（-x）
            Quad(verts, uvs, tris, def.Side,
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, 0.5f),
                new Vector3(-0.5f, 0.5f, -0.5f));

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

            float du = 1f / BlockCatalog.AtlasColumns;
            float dv = 1f / BlockCatalog.AtlasRows;
            float u0 = tile.Col * du;
            float v0 = 1f - (tile.Row + 1) * dv;

            uv.Add(new Vector2(u0, v0));
            uv.Add(new Vector2(u0 + du, v0));
            uv.Add(new Vector2(u0 + du, v0 + dv));
            uv.Add(new Vector2(u0, v0 + dv));

            t.Add(i);
            t.Add(i + 1);
            t.Add(i + 2);
            t.Add(i);
            t.Add(i + 2);
            t.Add(i + 3);
        }
    }
}
