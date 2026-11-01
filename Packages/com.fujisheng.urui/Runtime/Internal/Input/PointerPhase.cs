namespace URUI.Internal.Input
{
    /// <summary>宿主转发给原生输入层的指针阶段。</summary>
    internal enum PointerPhase
    {
        Down,
        Move,
        Up,
        Cancel
    }
}
