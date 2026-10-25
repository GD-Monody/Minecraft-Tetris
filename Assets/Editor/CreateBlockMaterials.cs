using UnityEditor;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    /// <summary>生成引用内置 shader 的材质资源，保证构建里带上 shader。</summary>
    public static class CreateBlockMaterials
    {
        public static void Run()
        {
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/atlas.png");
            if (atlas == null)
            {
                Debug.LogError("[MinecraftTetris] 找不到 Assets/Resources/atlas.png");
                EditorApplication.Exit(1);
                return;
            }

            atlas.filterMode = FilterMode.Point;
            EditorUtility.SetDirty(atlas);

            CreateMaterial("Assets/Resources/BlockMat.mat", "MinecraftTetris/Atlas", atlas);
            CreateMaterial("Assets/Resources/BlockTransparentMat.mat", "MinecraftTetris/AtlasTransparent", atlas);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[MinecraftTetris] 方块材质已生成");
            EditorApplication.Exit(0);
        }

        static void CreateMaterial(string path, string shaderName, Texture2D texture)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError("[MinecraftTetris] 找不到 shader: " + shaderName);
                return;
            }

            var mat = new Material(shader);
            mat.mainTexture = texture;
            AssetDatabase.CreateAsset(mat, path);
        }
    }
}
