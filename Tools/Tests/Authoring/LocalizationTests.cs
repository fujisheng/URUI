using System;
using System.Reflection;
static partial class Program
{
    static partial void LocalizationTests()
    {
        var type = typeof(Program).Assembly.GetType("URUI.Editor.Localization.EditorTextCatalog");
        Equal(true, type != null);
        string Text(string key, bool chinese) => (string)type.GetMethod("Translate").Invoke(null, new object[] { key, chinese });
        bool Chinese(string language) => (bool)type.GetMethod("IsChineseLanguage").Invoke(null, new object[] { language });
        Equal("Save", Text("保存", false));
        Equal("保存", Text("Save", true));
        Equal("Layout", Text("布局 Layout", false));
        Equal("布局", Text("布局 Layout", true));
        Equal(true, Chinese("ChineseSimplified"));
        Equal(true, Chinese("ChineseTraditional"));
        Equal(true, Chinese("Chinese"));
        Equal(false, Chinese("English"));
        Equal(false, Chinese("Japanese"));
        Equal(false, Chinese(null));
        Equal("style.width", Text("style.width", false));
        Equal("style.width", Text("style.width", true));
        string Message(string value, bool chinese) => (string)type.GetMethod("Message").Invoke(null, new object[] { value, chinese });
        Equal("Animation target is missing or duplicated: 领取按钮", Message("动画目标缺失或重名：领取按钮", false));
        Equal("动画目标缺失或重名：ClaimButton", Message("Animation target is missing or duplicated: ClaimButton", true));
        Equal("HTML_DEPENDENCY: Missing authoring dependency: 资源.css", Message("HTML_DEPENDENCY: 缺少作者依赖：资源.css", false));
        Equal("2 unsaved files · 3 unapplied assets", string.Format(Text("{0} unsaved files · {1} unapplied assets", false), 2, 3));
        Equal("2 个文件未保存 · 3 个资产未应用", string.Format(Text("{0} unsaved files · {1} unapplied assets", true), 2, 3));
        LocalizationReviewTests("symbols");
        LocalizationReviewTests("conflict");
        LocalizationReviewTests("status-prefixes");
        foreach (var pair in (System.Collections.IEnumerable)type.GetProperty("Entries").GetValue(null))
        {
            string key = (string)pair.GetType().GetProperty("Key").GetValue(pair);
            Equal(false, System.Text.RegularExpressions.Regex.IsMatch(Text(key, false), @"[\p{IsCJKUnifiedIdeographs}]"));
        }

    }
}
