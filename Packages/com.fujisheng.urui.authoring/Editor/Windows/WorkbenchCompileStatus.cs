using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using URUI.Authoring.Preview;
using URUI.Authoring.Sessions;
using URUI.Editor.Compilation;
using URUI.Resources;
using L = URUI.Editor.Localization.EditorText;

namespace URUI.Authoring.Windows
{
    internal sealed class WorkbenchCompileStatus : Label
    {
        string page;
        bool needsScan = true;
        CompiledDocumentAsset compiled;
        bool compiledCurrent;
        string validationError;
        IVisualElementScheduledItem recheck;

        internal WorkbenchCompileStatus()
        {
            name = "urui-compile-status";
            style.marginLeft = 6;
            style.whiteSpace = WhiteSpace.Normal;
        }

        internal void Invalidate()
        {
            needsScan = true;
            Refresh();
            recheck?.Pause();
            recheck = schedule.Execute(() =>
            {
                needsScan = true;
                Refresh();
            }).StartingIn(500);
        }

        internal void Refresh()
        {
            var author = AuthoringEnvironment.Author;
            if (page != author.File?.Path)
            {
                page = author.File?.Path;
                needsScan = true;
            }
            if (page == null || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                style.display = DisplayStyle.None;
                return;
            }
            if (needsScan && !EditorApplication.isCompiling && !EditorApplication.isUpdating)
            {
                needsScan = false;
                string uri = AuthoringAssetPaths.DocumentUri(author.Workspace.ProjectRoot, page);
                compiled = AssetDatabase.FindAssets("t:CompiledDocumentAsset", new[] { "Assets" })
                    .Select(guid => AssetDatabase.LoadAssetAtPath<CompiledDocumentAsset>(AssetDatabase.GUIDToAssetPath(guid)))
                    .FirstOrDefault(asset => asset && string.Equals(asset.SourceUrl, uri, StringComparison.Ordinal));
                compiledCurrent = false;
                validationError = null;
                if (compiled)
                    try { CompiledDocumentCompiler.ValidateCurrent(compiled); compiledCurrent = true; }
                    catch (InvalidOperationException error) { validationError = error.Message; }
            }
            if (!compiled)
            {
                style.display = DisplayStyle.None;
                return;
            }
            style.display = DisplayStyle.Flex;
            bool dirty = author.Workspace.Files.Any(file => file.Dirty && (file.Path == page ||
                Path.GetExtension(file.Path).Equals(".css", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file.Path).Equals(".html", StringComparison.OrdinalIgnoreCase)));
            text = dirty ? L.Tr("编译资产基于已保存的源码。") : compiledCurrent ? L.Tr("编译资产已更新。") : L.Tr("编译资产需要更新。");
            tooltip = AssetDatabase.GetAssetPath(compiled) + (validationError == null ? "" : "\n" + validationError);
        }
    }
}
