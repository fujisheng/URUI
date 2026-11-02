using System;
using UnityEngine;
using URUI.Documents;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>
    /// 文档拥有的 RmlUi 节点身份。名字变化、移动与脱离层级不改变身份；销毁或卸载后失效。
    /// 所有访问限 Unity 主线程，不暴露原生指针，节点类型和资源由文档工厂管理，不内置数据绑定。
    /// </summary>
    public partial class Element
    {
        readonly Document document;
        readonly uint generation;
        readonly ulong token;
        bool invalid;

        internal Element(Document document, uint generation, ulong token)
        {
            this.document = document;
            this.generation = generation;
            this.token = token;
            Style = new ElementStyle(this);
        }

        /// <summary>对应 RML id；允许空名和同名，局部 Q 返回首项，Query 返回全部。</summary>
        public string Name
        {
            get
            {
                RequireValid();
                return NativeMethods.ReadElement(token, 0);
            }

            set
            {
                RequireText(value, nameof(value));
                Write(0, null, value);
            }
        }

        /// <summary>原生标签名，例如 div、span；文本节点的标签为 #text。</summary>
        public string TagName
        {
            get
            {
                RequireValid();
                return NativeMethods.ReadElement(token, 1);
            }
        }

        /// <summary>普通文本。读取结构化子节点会抛异常；写入会销毁原来的子节点。</summary>
        /// <remarks>文本不会被解释成标签、实体或数据表达式，保留 UTF-8 内容与空白。</remarks>
        public virtual string Text
        {
            get
            {
                RequireValid();
                return NativeMethods.ReadElement(token, 2);
            }

            set
            {
                RequireText(value, nameof(value));
                Write(1, null, value);
                document.SweepInvalidElements();
            }
        }

        /// <summary>行内 RCSS 写入入口；布局和样式计算仍由 RmlUi 完成。</summary>
        public ElementStyle Style
        {
            get;
        }

        /// <summary>节点未销毁，且所属文档仍是当前加载世代；脱离层级的节点仍有效。</summary>
        public bool IsValid
        {
            get
            {
                document.CheckThread();
                if (invalid)
                {
                    return false;
                }

                if (document.IsCurrent(generation) && NativeMethods.urui_element_ref_valid(token) == 1)
                {
                    return true;
                }

                Invalidate();
                return false;
            }
        }

        /// <summary>节点当前连接到所属文档的根；有效但脱离层级的节点返回 false。</summary>
        public bool IsAttached
        {
            get
            {
                RequireValid();
                return NativeMethods.urui_element_attached(token) == 1;
            }
        }

        /// <summary>读取真实属性；不存在时返回 null，存在但为空时返回空字符串。属性名区分大小写。</summary>
        public string GetAttribute(string name)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            return NativeMethods.ReadElement(token, 3, name);
        }

        /// <summary>检查真实属性是否存在，包括值为空的属性。</summary>
        public bool HasAttribute(string name) => GetAttribute(name) != null;

        /// <summary>写入真实属性；小写 id 与 Name 操作相同节点，class 与类名方法保持一致。</summary>
        public void SetAttribute(string name, string value)
        {
            Document.RequireName(name, nameof(name));
            RequireText(value, nameof(value));
            if (((name == "type" && TagName == "input") || name == "data-urui-type") && GetAttribute(name) != value)
            {
                throw new InvalidOperationException("控件类型在创建时确定，不能通过属性更换。");
            }
            Write(2, name, value);
        }

        /// <summary>移除属性；重复移除安全，移除 id 后 Name 为空。</summary>
        public void RemoveAttribute(string name)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            if (((name == "type" && TagName == "input") || name == "data-urui-type") && HasAttribute(name))
            {
                throw new InvalidOperationException("控件类型在创建时确定，不能移除类型属性。");
            }
            RequireSuccess(NativeMethods.urui_element_erase(token, 0, name), "移除属性");
        }

        /// <summary>在已挂载的节点上使用 RmlUi 解析器替换内部 RML；原子节点会被销毁。</summary>
        /// <remarks>这不是纯文本操作，也不提供事务性校验或数据绑定 API。</remarks>
        public void SetInnerMarkup(string markup)
        {
            RequireText(markup, nameof(markup));
            if (!IsAttached)
            {
                throw new InvalidOperationException("请先挂载节点，再解析内部 RML；脱离的子树可用 CreateElement、Text 和 Add 构建。");
            }

            Write(3, null, markup);
            document.SweepInvalidElements();
        }

        /// <summary>按节点身份设置二维位移与缩放，分量必须为有限数值。</summary>
        public void SetTransform(Vector2 translation, Vector2 scale)
        {
            RequireValid();
            ElementStyle.RequireFinite(translation.x);
            ElementStyle.RequireFinite(translation.y);
            ElementStyle.RequireFinite(scale.x);
            ElementStyle.RequireFinite(scale.y);
            RequireSuccess(NativeMethods.urui_element_ref_set_translate_scale_2d(token, translation.x, translation.y, scale.x, scale.y), "设置变换");
        }

        /// <summary>查询是否具有单个 RCSS 类名，区分大小写。</summary>
        public bool ClassListContains(string className)
        {
            RequireValid();
            RequireClassName(className);
            int result = NativeMethods.urui_element_class(token, className, -1);
            if (result < 0)
            {
                throw new InvalidOperationException("URUI 无法读取类名。");
            }

            return result == 1;
        }

        /// <summary>添加单个 RCSS 类名，重复添加安全。</summary>
        public void AddToClassList(string className) => EnableInClassList(className, true);

        /// <summary>移除单个 RCSS 类名，重复移除安全。</summary>
        public void RemoveFromClassList(string className) => EnableInClassList(className, false);

        /// <summary>启用或移除单个类名；不接受用空白分隔的多个类名。</summary>
        public void EnableInClassList(string className, bool enabled)
        {
            RequireValid();
            RequireClassName(className);
            RequireSuccess(NativeMethods.urui_element_class(token, className, enabled ? 1 : 0), "更新类名");
        }

        internal void SetStyle(string name, string value)
        {
            Document.RequireName(name, nameof(name));
            RequireText(value, nameof(value));
            Write(4, name, value);
        }

        internal void RemoveStyle(string name)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            RequireSuccess(NativeMethods.urui_element_erase(token, 1, name), "移除行内样式");
        }

        internal void SetPixels(string name, float value)
        {
            ElementStyle.RequireFinite(value);
            SetStyle(name, value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px");
        }

        void Write(int field, string name, string value)
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_write(token, field, name, value), "更新元素");
        }

        internal void RequireValid()
        {
            if (!IsValid)
            {
                throw new InvalidOperationException("URUI 元素已销毁或文档已卸载，请重新获取节点。");
            }
        }

        internal void Invalidate()
        {
            if (invalid)
            {
                return;
            }

            invalid = true;
            UserData = null;
            try
            {
                OnInvalidated();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            foreach (var subscription in subscriptions)
            {
                subscription.Clear();
            }

            subscriptions.Clear();
        }

        internal static void RequireText(string value, string parameter)
        {
            if (value == null || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("值不能为 null 或包含 NUL。", parameter);
            }
        }

        internal static void RequireClassName(string value)
        {
            Document.RequireName(value, nameof(value));
            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c))
                {
                    throw new ArgumentException("请使用单个类名。", nameof(value));
                }
            }
        }

        static void RequireSuccess(int result, string operation)
        {
            if (result != 1)
            {
                throw new InvalidOperationException("URUI 无法" + operation + "：" + NativeMethods.LastError());
            }
        }
    }
}
