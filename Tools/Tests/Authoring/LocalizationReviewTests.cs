using System;
using URUI.Editor.Localization;
static partial class Program
{
    static partial void LocalizationReviewTests(string name)
    {
        if (name == "symbols")
        {
            foreach (string key in new[] { "S", "s", "R", "T", "I", "O" })
                Equal(key, EditorTextCatalog.Translate(key, true));
            Equal("共", EditorTextCatalog.Translate("共", true));
            Equal("S", EditorTextCatalog.Translate("共", false));
            Equal("秒", EditorTextCatalog.Translate("秒", true));
        }
        else if (name == "conflict")
        {
            Equal("source_conflict: 外部文件与未保存修改存在冲突: Assets/保存.html", EditorTextCatalog.Message("source_conflict: external change and unsaved edits: Assets/保存.html", true));
            Equal("source_conflict: external change and unsaved edits: Assets/保存.html", EditorTextCatalog.Message("source_conflict: 外部文件与未保存修改存在冲突: Assets/保存.html", false));
        }
        else if (name == "status-prefixes")
        {
            Equal("本机 Agent API 已启用 · Assets/保存/endpoint.json", EditorTextCatalog.Message("Local Agent API enabled · Assets/保存/endpoint.json", true));
            Equal("已应用资产: Assets/保存.asset", EditorTextCatalog.Message("Applied asset: Assets/保存.asset", true));
        }
        else
            throw new Exception("Unknown localization review case");
        Console.WriteLine("PASS: localization review " + name + " " + assertions);
    }
}
