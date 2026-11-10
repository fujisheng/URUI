using System;
using UnityEngine;
using URUI.Documents;
using URUI.Internal.Interop;

namespace URUI.Elements
{
    /// <summary>
    /// 文档拥有的 URUI 节点身份。名字变化、移动与脱离层级不改变身份；销毁或卸载后失效。
    /// 所有访问限 Unity 主线程，不暴露原生指针，节点类型和资源由文档工厂管理，不内置数据绑定。
    /// </summary>
    public partial class Element
    {
        readonly Document document;
        readonly uint generation;
        readonly ulong token;
        internal ulong NativeHandle { get; }
        bool invalid;

        internal Element(Document document, uint generation, ulong token, ulong nativeHandle)
        {
            this.document = document;
            this.generation = generation;
            this.token = token;
            NativeHandle = nativeHandle;
            Style = new ElementStyle(this);
        }

        /// <summary>对应 HTML id；允许空名和同名，局部 Q 返回首项，Query 返回全部。</summary>
        public string Name
        {
            get
            {
                RequireValid();
                return ReadString(0);
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
                return ReadString(1);
            }
        }

        /// <summary>普通文本。读取结构化子节点会抛异常；写入会销毁原来的子节点。</summary>
        /// <remarks>文本不会被解释成标签、实体或数据表达式，保留 UTF-8 内容与空白。</remarks>
        public virtual string Text
        {
            get
            {
                RequireValid();
                return ReadString(2);
            }

            set
            {
                RequireText(value, nameof(value));
                Write(1, null, value);
                document.SweepInvalidElements();
            }
        }

        /// <summary>行内 CSS 写入入口；布局和样式计算仍由 URUI 完成。</summary>
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

                if (document.IsCurrent(generation) && NativeMethods.urui_element_ref_valid(NativeHandle, token) == 1)
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
                return NativeMethods.urui_element_attached(NativeHandle, token) == 1;
            }
        }

        /// <summary>读取真实属性；不存在时返回 null，存在但为空时返回空字符串。属性名区分大小写。</summary>
        public string GetAttribute(string name)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            return ReadString(3, name);
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
            Document.FinishNativeCall();
            OnAttributeChanged(name);
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
            RequireSuccess(NativeMethods.urui_element_erase(NativeHandle, token, 0, name), "移除属性");
            Document.FinishNativeCall();
            OnAttributeChanged(name);
        }

        /// <summary>属性成功写入或移除后，同步控件的作者呈现。</summary>
        protected virtual void OnAttributeChanged(string name) { }

        /// <summary>严格解析 HTML 片段并原子替换内容；解析、建树或控件初始化失败时保留原子树。</summary>
        /// <param name="html">按当前元素上下文解析的 HTML。</param>
        /// <param name="sourceUrl">片段的真实来源，用于诊断；节点资源相对所属文档解析。</param>
        public void SetInnerHtml(string html, string sourceUrl)
        {
            RequireValid(); RequireText(html, nameof(html)); Document.RequireName(sourceUrl, nameof(sourceUrl));
            if (document.Defer(() => SetInnerHtml(html, sourceUrl))) return;
            document.ReplaceInnerHtml(this, html, sourceUrl);
        }

        /// <summary>序列化当前元素的 HTML 内容，保留标准空元素与显式结束标签。</summary>
        public string GetInnerHtml() { RequireValid(); return NativeMethods.ReadElement(NativeHandle, token, 4); }

        /// <summary>按节点身份设置二维位移与缩放，分量必须为有限数值。</summary>
        public void SetTransform(Vector2 translation, Vector2 scale)
        {
            RequireValid();
            ElementStyle.RequireFinite(translation.x);
            ElementStyle.RequireFinite(translation.y);
            ElementStyle.RequireFinite(scale.x);
            ElementStyle.RequireFinite(scale.y);
            RequireSuccess(NativeMethods.urui_element_ref_set_translate_scale_2d(NativeHandle, token, translation.x, translation.y, scale.x, scale.y), "设置变换");
        }

        /// <summary>更新运行时TRS，保留作者变换原点的单位、数值与样式表来源。</summary>
        public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale)
        {
            var originX=Style.Get("--urui-transform-origin-x");var originY=Style.Get("--urui-transform-origin-y");
            SetTransform(translation,rotation,scale,new Vector2(50,50));
            if(originX==null){Style.Remove("--urui-transform-origin-x");}else{Style.Set("--urui-transform-origin-x",originX);}
            if(originY==null){Style.Remove("--urui-transform-origin-y");}else{Style.Set("--urui-transform-origin-y",originY);}
        }

        /// <summary>更新运行时 TRS 与百分比变换原点；使用原生变换原语，不生成样式文本。</summary>
        public void SetTransform(Vector3 translation, Quaternion rotation, Vector3 scale, Vector2 originPercent)
        {
            RequireValid();
            var norm = (double)rotation.x * rotation.x + (double)rotation.y * rotation.y +
                (double)rotation.z * rotation.z + (double)rotation.w * rotation.w;
            if (double.IsNaN(norm) || double.IsInfinity(norm) || norm <= 0) throw new ArgumentOutOfRangeException(nameof(rotation));
            rotation = rotation.normalized;
            rotation.ToAngleAxis(out var angle, out var axis);
            var values = new[] { translation.x, translation.y, translation.z, axis.x, axis.y, axis.z, angle,
                scale.x, scale.y, scale.z, originPercent.x, originPercent.y };
            foreach (var value in values) ElementStyle.RequireFinite(value);
            RequireSuccess(NativeMethods.urui_element_trs(NativeHandle, token, values, values.Length), "设置 TRS");
        }

        internal void SetColor(string property, Color value)
        {
            RequireValid();
            Document.RequireName(property, nameof(property));
            ElementStyle.RequireFinite(value.r); ElementStyle.RequireFinite(value.g);
            ElementStyle.RequireFinite(value.b); ElementStyle.RequireFinite(value.a);
            Color32 bytes = value;
            RequireSuccess(NativeMethods.urui_element_color(NativeHandle, token, property, bytes.r, bytes.g, bytes.b, bytes.a), "设置颜色");
        }

        /// <summary>查询是否具有单个 CSS 类名，区分大小写。</summary>
        public bool ClassListContains(string className)
        {
            RequireValid();
            RequireClassName(className);
            int result = NativeMethods.urui_element_class(NativeHandle, token, className, -1);
            if (result < 0)
            {
                throw new InvalidOperationException("URUI 无法读取类名。");
            }

            return result == 1;
        }

        /// <summary>添加单个 CSS 类名，重复添加安全。</summary>
        public void AddToClassList(string className) => EnableInClassList(className, true);

        /// <summary>移除单个 CSS 类名，重复移除安全。</summary>
        public void RemoveFromClassList(string className) => EnableInClassList(className, false);

        /// <summary>启用或移除单个类名；不接受用空白分隔的多个类名。</summary>
        public void EnableInClassList(string className, bool enabled)
        {
            RequireValid();
            RequireClassName(className);
            RequireSuccess(NativeMethods.urui_element_class(NativeHandle, token, className, enabled ? 1 : 0), "更新类名");
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
            RequireSuccess(NativeMethods.urui_element_erase(NativeHandle, token, 1, name), "移除行内样式");
        }

        internal void SetPixels(string name, float value)
        {
            ElementStyle.RequireFinite(value);
            SetNumeric(name, value, 1);
        }

        internal void SetNumeric(string name, float value, int unit)
        {
            RequireValid();
            Document.RequireName(name, nameof(name));
            ElementStyle.RequireFinite(value);
            RequireSuccess(NativeMethods.urui_element_number(NativeHandle, token, name, value, unit), "更新动态数值");
        }

        void Write(int field, string name, string value)
        {
            RequireValid();
            RequireSuccess(NativeMethods.urui_element_write(NativeHandle, token, field, name, value), "更新元素");
        }

        internal void RequireValid()
        {
            document.RequireEffectDomAccess();
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

        internal static void RequireSuccess(int result, string operation)
        {
            if (result != 1)
            {
                throw new InvalidOperationException("URUI 无法" + operation + "：" + NativeMethods.LastError());
            }
        }
    }
}
