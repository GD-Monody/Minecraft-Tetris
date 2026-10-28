using System;
using UnityEditor;
using UnityEngine;

namespace MinecraftTetris.EditorTools
{
    /// <summary>
    /// 强制 Assets/Resources/Fonts 下的 TTF 按「动态字体」导入。
    ///
    /// 动态字体运行时按需光栅化，不需要导入时预生成字符集，中文（包括方块名里的生僻字）
    /// 用到哪个渲染哪个，任意字号都清晰。
    /// includeFontData 必须为 true：否则字体文件不跟着打包走，运行时字体是空的、满屏方框。
    /// </summary>
    public class FontImportSettings : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
                return;
            if (!path.Contains("/Resources/Fonts/"))
                return;

            var importer = assetImporter as TrueTypeFontImporter;
            if (importer == null)
                return;

            importer.fontTextureCase = FontTextureCase.Dynamic;
            importer.includeFontData = true;
            importer.characterSpacing = 0;
            importer.characterPadding = 1;
        }
    }
}
