using UnityEngine;

namespace MinecraftTetris
{
    /// <summary>
    /// 场地边框：左右两根柱子和一根底梁，让方块有边界感。
    /// 边框放在方块后面一点，不和方块穿插。
    /// </summary>
    public class BorderRenderer : MonoBehaviour
    {
        public void Build(int sizeX, int sizeY, int sizeZ)
        {
            Material mat = Resources.Load<Material>("BorderMat");
            if (mat == null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    mat = new Material(shader);
                    mat.color = new Color(0.20f, 0.21f, 0.24f);
                }
            }

            float cx = (sizeX - 1) * 0.5f;
            float cy = (sizeY - 1) * 0.5f;
            float cz = (sizeZ - 1) * 0.5f;
            float zLen = sizeZ + 0.08f;
            float thick = 0.28f;

            CreateCube("BorderLeft",
                new Vector3(-0.5f - thick * 0.5f - 0.02f, cy, cz),
                new Vector3(thick, sizeY, zLen), mat);

            CreateCube("BorderRight",
                new Vector3(sizeX - 0.5f + thick * 0.5f + 0.02f, cy, cz),
                new Vector3(thick, sizeY, zLen), mat);

            CreateCube("BorderBottom",
                new Vector3(cx, -0.5f - thick * 0.5f - 0.02f, cz),
                new Vector3(sizeX + thick * 2f + 0.04f, thick, zLen), mat);
        }

        void CreateCube(string cubeName, Vector3 position, Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = cubeName;
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            go.transform.localScale = scale;

            Collider col = go.GetComponent<Collider>();
            if (col != null)
                Destroy(col);

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && mat != null)
                rend.sharedMaterial = mat;
        }
    }
}
