namespace URUI
{
    /// <summary>当前桥提供的点击通知。Target 是最近的带 id 元素，不提供 UITK 的冒泡/捕获语义。</summary>
    public readonly struct ClickEvent
    {
        /// <summary>接收此通知的元素。</summary>
        public Element Target { get; }

        internal ClickEvent(Element target) { Target = target; }
    }
}
