using System.IO;
using System.Text;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace URUI.Editor.Authoring
{
    /// <summary>直接导入标准 .css，保留原始 UTF-8 内容供浏览器与 URUI 共用。</summary>
    [ScriptedImporter(1, "css")]
    public sealed class CssImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext context)
        {
            var bytes = File.ReadAllBytes(context.assetPath);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            var asset = new TextAsset(text) { name = Path.GetFileNameWithoutExtension(context.assetPath) };
            context.AddObjectToAsset("css", asset);
            context.SetMainObject(asset);
        }
    }
}
