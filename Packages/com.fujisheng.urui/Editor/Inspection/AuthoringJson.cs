using URUI.Internal.Serialization;

namespace URUI.Editor.Inspection
{
    /// <summary>Editor 数据文件/协议共用的严格 JSON 值读写入口，不访问 Unity 对象或原生句柄。</summary>
    public static class AuthoringJson
    {
        public static object Parse(string text) => ValueJson.Parse(text);
        public static string Stringify(object value) => ValueJson.Stringify(value);
    }
}
