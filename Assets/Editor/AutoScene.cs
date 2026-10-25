using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    public static class AutoScene
    {
        [MenuItem("MinecraftTetris/生成游戏场景")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Game");
            go.AddComponent<Bootstrap>();

            if (!Directory.Exists("Assets/Scenes"))
                Directory.CreateDirectory("Assets/Scenes");

            EditorSceneManager.SaveScene(scene, "Assets/Scenes/Game.unity");
            AssetDatabase.Refresh();

            Debug.Log("[MinecraftTetris] 场景已生成: Assets/Scenes/Game.unity");
        }
    }
}
