using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>验证资源迁移时真实 Unity 序列化引用，不进入运行时分发。</summary>
    public sealed class TextReferenceFixture : ScriptableObject
    {
        public TextAsset Source;
        public string Literal;
    }
}
