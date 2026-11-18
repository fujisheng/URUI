using URUI.Editor.Inspection;

namespace URUI.Authoring.Agent
{
    /// <summary>复用 Core 的严格 JSON 契约；Editor facade 不暴露运行时内部句柄。</summary>
    public static class AgentJson
    {
        public static object Parse(string text) => AuthoringJson.Parse(text);
        public static string Stringify(object value) => AuthoringJson.Stringify(value);
    }
}
