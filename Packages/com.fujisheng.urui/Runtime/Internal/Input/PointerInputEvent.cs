namespace URUI.Internal.Input
{
    /// <summary>指针阶段、位置与命中结果的内部样本，不持有原生或 Unity 资源。</summary>
    internal struct PointerInputEvent
    {
        public PointerPhase phase;
        public int pointerId;
        public float x;
        public float y;
        public string hitId;
    }
}
