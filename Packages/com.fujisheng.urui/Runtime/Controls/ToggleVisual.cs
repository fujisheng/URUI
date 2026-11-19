using System;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>
    /// 将原生开关状态组合到普通作者节点，动画与图片由外部样式控制。
    /// </summary>
    public sealed class ToggleVisual : INotifyValueChanged<bool>, IDisposable
    {
        readonly Element host;
        readonly IDisposable subscription;
        /// <summary>
        /// 负责值、焦点及键盘操作的原生开关。
        /// </summary>
        public Toggle Control
        {
            get;
        }
        /// <summary>
        /// 选中时在外观容器启用 checked 类，不依赖业务命名。
        /// </summary>
        public ToggleVisual(Toggle control, Element host)
        {
            Control = control ?? throw new ArgumentNullException(nameof(control));
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            if (!ReferenceEquals(control.Document, host.Document))
            {
                throw new ArgumentException("外观节点必须与开关属于同一文档。", nameof(host));
            }
            subscription = control.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
        }
        /// <summary>
        /// 更新值和作者状态，并保留原生通知。
        /// </summary>
        public bool Value
        {
            get => Control.Value;
            set
            {
                Control.Value = value;
                Refresh();
            }
        }
        /// <summary>
        /// 静默更新状态和外观。
        /// </summary>
        public void SetValueWithoutNotify(bool value)
        {
            Control.SetValueWithoutNotify(value);
            Refresh();
        }
        void Refresh()
        {
            if (Control.IsValid && host.IsValid)
            {
                host.EnableInClassList("checked", Control.Value);
            }
        }
        /// <summary>
        /// 解绑输入；不销毁作者节点。
        /// </summary>
        public void Dispose() => subscription.Dispose();
    }
}
