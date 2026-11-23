using System;
using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    /// <summary>
    /// 直接导入标准 .css，保留原始 UTF-8 字节供浏览器与 URUI 共用，并校验有效 UTF-8 编码。
    /// </summary>
    [ScriptedImporter(1, "css")]
    public sealed class CssImporter : ScriptedImporter
    {
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public override void OnImportAsset(AssetImportContext context)
        {
            try
            {
                var bytes = File.ReadAllBytes(context.assetPath);
                var text = StrictUtf8.GetString(bytes);
                var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(context.assetPath) };
                context.AddObjectToAsset("css", asset);
                context.SetMainObject(asset);
            }
            catch (Exception error) when (error is DecoderFallbackException || error is IOException || error is UnauthorizedAccessException)
            {
                context.LogImportError("URUI CSS 导入失败（需要有效的 UTF-8 编码）：" + error.Message);
            }
        }
    }
}
