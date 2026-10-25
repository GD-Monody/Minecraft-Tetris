using System.Collections.Generic;
using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地边框：左右两侧各一面"网格墙"，由细亮线和外面套的加色光晕组成。
    /// 每层高度一条横线、前后两条竖线，从正面看是两条竖立的荧光网格带。
    /// </summary>
    public class GridFrame : MonoBehaviour
    {
        const float CoreHalfWidth = 0.012f;
        const float GlowHalfWidth = 0.08f;

        public Material CoreMaterial;
        public Material GlowMaterial;

        public void Build(int sizeX, int sizeY, int sizeZ, Color coreColor, Color glowColor)
        {
            if (CoreMaterial == null)
                CoreMaterial = Resources.Load<Material>("GridCoreMat");
            if (GlowMaterial == null)
                GlowMaterial = Resources.Load<Material>("GridGlowMat");

            if (CoreMaterial != null)
                CoreMaterial.color = coreColor;
            if (GlowMaterial != null)
                GlowMaterial.color = glowColor;

            for (int i = transform.childCount - 1; i >= 0; i--)
                Destroy(transform.GetChild(i).gameObject);

            var coreVerts = new List<Vector3>();
            var coreTris = new List<int>();
            var glowVerts = new List<Vector3>();
            var glowTris = new List<int>();

            float xLeft = -0.56f;
            float xRight = sizeX - 0.44f;
            float yBottom = -0.56f;
            float yTop = sizeY - 0.44f;
            float zFront = -0.56f;
            float zBack = sizeZ - 0.44f;

            // 前后两个矩形框 + 四角连线，围出一个完整的立方体框
            AddRect(coreVerts, coreTris, glowVerts, glowTris, xLeft, yBottom, xRight, yTop, zFront);
            if (sizeZ > 1)
                AddRect(coreVerts, coreTris, glowVerts, glowTris, xLeft, yBottom, xRight, yTop, zBack);

            AddLine(coreVerts, coreTris, glowVerts, glowTris, new Vector3(xLeft, yBottom, zFront), new Vector3(xLeft, yBottom, zBack));
            AddLine(coreVerts, coreTris, glowVerts, glowTris, new Vector3(xRight, yBottom, zFront), new Vector3(xRight, yBottom, zBack));
            AddLine(coreVerts, coreTris, glowVerts, glowTris, new Vector3(xLeft, yTop, zFront), new Vector3(xLeft, yTop, zBack));
            AddLine(coreVerts, coreTris, glowVerts, glowTris, new Vector3(xRight, yTop, zFront), new Vector3(xRight, yTop, zBack));

            // 左右两侧：每层一条横线 + 每格一段竖线，组成网格
            AddSideGrid(coreVerts, coreTris, glowVerts, glowTris, xLeft, yBottom, yTop, zFront, zBack, sizeY);
            AddSideGrid(coreVerts, coreTris, glowVerts, glowTris, xRight, yBottom, yTop, zFront, zBack, sizeY);

            CreateMeshObject("FrameCore", coreVerts, coreTris, CoreMaterial);
            CreateMeshObject("FrameGlow", glowVerts, glowTris, GlowMaterial);
        }

        /// <summary>一个竖直矩形框（四条边）。</summary>
        void AddRect(List<Vector3> cv, List<int> ct, List<Vector3> gv, List<int> gt,
            float xLeft, float yBottom, float xRight, float yTop, float z)
        {
            AddLine(cv, ct, gv, gt, new Vector3(xLeft, yBottom, z), new Vector3(xRight, yBottom, z));
            AddLine(cv, ct, gv, gt, new Vector3(xRight, yBottom, z), new Vector3(xRight, yTop, z));
            AddLine(cv, ct, gv, gt, new Vector3(xRight, yTop, z), new Vector3(xLeft, yTop, z));
            AddLine(cv, ct, gv, gt, new Vector3(xLeft, yTop, z), new Vector3(xLeft, yBottom, z));
        }

        /// <summary>一面竖直的网格墙：每层一条横线（沿 z 方向），前后各一条竖线。</summary>
        void AddSideGrid(List<Vector3> cv, List<int> ct, List<Vector3> gv, List<int> gt,
            float x, float yBottom, float yTop, float zFront, float zBack, int sizeY)
        {
            for (int i = 0; i <= sizeY; i++)
            {
                float y = yBottom + (yTop - yBottom) * i / sizeY;
                AddLine(cv, ct, gv, gt, new Vector3(x, y, zFront), new Vector3(x, y, zBack));
            }

            for (int i = 0; i < sizeY; i++)
            {
                float y0 = yBottom + (yTop - yBottom) * i / sizeY;
                float y1 = yBottom + (yTop - yBottom) * (i + 1) / sizeY;
                AddLine(cv, ct, gv, gt, new Vector3(x, y0, zFront), new Vector3(x, y1, zFront));
                AddLine(cv, ct, gv, gt, new Vector3(x, y0, zBack), new Vector3(x, y1, zBack));
            }
        }

        void AddLine(List<Vector3> cv, List<int> ct, List<Vector3> gv, List<int> gt, Vector3 a, Vector3 b)
        {
            AddQuad(cv, ct, a, b, CoreHalfWidth);
            AddQuad(gv, gt, a, b, GlowHalfWidth);
        }

        static void AddQuad(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, float halfWidth)
        {
            Vector3 dir = b - a;
            if (dir.sqrMagnitude < 0.000001f)
                return;
            dir.Normalize();

            Vector3 side;
            if (Mathf.Abs(dir.z) > 0.9f)
                side = Vector3.up;
            else
                side = new Vector3(-dir.y, dir.x, 0f);

            side *= halfWidth;

            int i0 = verts.Count;
            verts.Add(a - side);
            verts.Add(a + side);
            verts.Add(b + side);
            verts.Add(b - side);
            tris.Add(i0);
            tris.Add(i0 + 1);
            tris.Add(i0 + 2);
            tris.Add(i0);
            tris.Add(i0 + 2);
            tris.Add(i0 + 3);
        }

        void CreateMeshObject(string name, List<Vector3> verts, List<int> tris, Material mat)
        {
            if (verts.Count == 0 || mat == null)
                return;

            var mesh = new Mesh();
            mesh.name = "GridFrame_" + name;
            mesh.SetVertices(verts);
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
