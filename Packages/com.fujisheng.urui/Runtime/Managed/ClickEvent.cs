namespace URUI
{
    /// <summary>
    /// 当前桥提供的点击通知。Target 是最近的带 id 元素，不提供 UITK 的冒泡/捕获语义。
    /// </summary>
    public readonly struct ClickEvent
    {
        /// <summary>
        /// 接收此通知的元素，即实际点击目标向上查找到的最近带 ID 节点。
        /// </summary>
        /// <remarks>仅表示当前分发目标，不包含冒泡阶段或指针捕获信息。</remarks>
        public Element Target
        {
            get;
        }

        internal ClickEvent(Element target)
        {
            Target = target;
        }
    }
}
