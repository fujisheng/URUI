using System;
using System.Collections.Generic;
namespace URUI.Editor.Inspection
{
    /// <summary>
    /// 可选资产工作副本的只读状态。Id/Revision 只在所属扩展会话内有效，不是运行时资产标识。
    /// </summary>
    [Serializable]
    public sealed class AuthoringAssetChange
    {
        public string Provider;
        public string Id;
        public string Path;
        public string Kind;
        public long Revision;
    }
    /// <summary>
    /// Editor 主线程的可选资产保存能力；枚举不落盘，Apply 必须带用户选定的当前版本。
    /// </summary>
    public interface IAuthoringAssetWorkspace
    {
        IReadOnlyList<AuthoringAssetChange> PendingAssets();
        void ApplyAsset(string id, long expectedRevision);
    }
}
