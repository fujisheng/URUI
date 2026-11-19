using System;
using UnityEngine;
using URUI.Effect.Rendering;
using URUI.Rendering.Effects;
namespace URUI.Effect
{
    /// <summary>
    /// 编辑器与测试共用的生产效果预览；时间显式传入，参数取独立快照。
    /// </summary>
    public sealed class EffectPreview : IDisposable
    {
        EffectParameters parameters;
        readonly EffectPreviewRenderer renderer;
        public EffectPreview(EffectParameters parameters)
        {
            SetParameters(parameters);
            renderer = new EffectPreviewRenderer(new EffectDefinition("preview", EffectKind.Image, new UIEffectFactory(null, _ => this.parameters), "ui:v1", "UIEffect production shader", renderProgram: EffectProgramDescriptors.UI, renderParameters: UIEffectParameters.Layout));
        }
        public void SetParameters(EffectParameters value)
        {
            parameters = (value ?? throw new ArgumentNullException(nameof(value))).Clone();
        }
        public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background) => renderer.Render(source, content, time, destination, background);
        public void Dispose() => renderer.Dispose();
    }
}
