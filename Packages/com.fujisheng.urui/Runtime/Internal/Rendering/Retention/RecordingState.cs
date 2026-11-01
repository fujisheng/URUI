using URUI.Internal.Interop;

namespace URUI.Internal.Rendering.Retention
{
    /// <summary>
    /// 仅在一整帧记录成功后提交复用依据；异常或外部资源变化须先使其失效。
    /// </summary>
    internal sealed class RecordingState
    {
        bool valid;
        UruiFrameStampNative stamp;
        RecordingKey key;

        public bool CanReuse(UruiFrameStampNative current, RecordingKey state) => valid
            && current.flags == 1
            && current.count <= int.MaxValue
            && current.epoch != 0
            && current.commands != 0
            && current.resources != 0
            && stamp.epoch == current.epoch
            && stamp.commands == current.commands
            && stamp.resources == current.resources
            && stamp.count == current.count
            && key.Equals(state);

        public void Commit(UruiFrameStampNative current, RecordingKey state)
        {
            stamp = current;
            key = state;
            valid = current.flags == 1
                && current.count <= int.MaxValue
                && current.epoch != 0
                && current.commands != 0
                && current.resources != 0;
        }

        public void Invalidate()
        {
            valid = false;
        }
    }
}
