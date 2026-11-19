using System;
using URUI.Elements;
using URUI.Rendering;
using URUI.Styles;
using UnityEngine;

namespace URUI.Effect
{
    /// <summary>
    /// 声明作者节点的解析羽化矩形；组遮罩的绘制方式由通用原生计划决定。
    /// </summary>
    public sealed class FeatherMaskPainter : IDisposable
    {
        static readonly RenderParameterLayout layout = new(1, new RenderParameterField[]{
            new(new StylePropertyDefinition("--urui-mask-feather-left",StyleValueType.Pixels),0,optional:true),
            new(new StylePropertyDefinition("--urui-mask-feather-top",StyleValueType.Pixels),1,optional:true),
            new(new StylePropertyDefinition("--urui-mask-feather-right",StyleValueType.Pixels),2,optional:true),
            new(new StylePropertyDefinition("--urui-mask-feather-bottom",StyleValueType.Pixels),3,optional:true)});
        IDisposable registration;
        public FeatherMaskPainter(Element element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }
            registration = element.Document.AttachGeometryProvider(element, "feather-mask", Prepare, parameters: layout);
        }
        static void Prepare(ExternalGeometryContext context)
        {
            context.DrawFeatheredRectangle(new Rect(Vector2.zero, context.Size), context.Parameters[0]);
        }
        /// <summary>
        /// 注销准备器；宿主持有仍在录制或 GPU 使用中的资源版本。
        /// </summary>
        public void Dispose()
        {
            registration?.Dispose();
            registration = null;
        }
    }
}
