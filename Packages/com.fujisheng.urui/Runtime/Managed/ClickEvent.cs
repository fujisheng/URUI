namespace URUI
{
    /// <summary>
    /// 普通点击通知，沿分发时快照的父级路径由目标向根分发。
    /// 不提供 UITK 的捕获、传播中止、指针捕获或事件池协议。
    /// </summary>
    public readonly struct ClickEvent
    {
        /// <summary>实际被点击的节点，包括没有 Name 的节点；身份不会按名字重新查找。</summary>
        public Element Target
        {
            get;
        }

        /// <summary>当前正在执行此回调的注册节点。</summary>
        public Element CurrentTarget
        {
            get;
        }

        internal ClickEvent(Element target, Element currentTarget)
        {
            Target = target;
            CurrentTarget = currentTarget;
        }
    }
}
