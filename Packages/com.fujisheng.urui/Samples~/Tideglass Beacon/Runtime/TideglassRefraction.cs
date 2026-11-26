using UnityEngine;
using URUI.Rendering.Effects;

namespace URUI.Samples.Tideglass
{
    // The native backdrop filter supplies the already-composited pixels behind this pane.
    internal sealed class TideglassRefraction : IEffectFactory
    {
        readonly Material material;

        internal TideglassRefraction(Material material) => this.material = material;

        public IElementEffect Create(EffectContext context) => new Lens(material);

        sealed class Lens : IElementEffect
        {
            static readonly int DocumentToGlass = Shader.PropertyToID("_DocumentToGlass");
            static readonly int GlassRect = Shader.PropertyToID("_GlassRect");
            static readonly int SampleStep = Shader.PropertyToID("_SampleStep");
            readonly Material material;
            readonly MaterialPropertyBlock properties = new();

            internal Lens(Material material) => this.material = material;

            public void Prepare(EffectFrame frame)
            {
                // Invert only the local z=0 plane. The deck also has a flattened z axis.
                var plane = frame.ElementToDocument;
                plane.m02 = plane.m12 = plane.m20 = plane.m21 = plane.m23 = plane.m32 = 0;
                plane.m22 = 1;
                var rect = frame.Layout.ContentRect;
                properties.SetMatrix(DocumentToGlass, plane.inverse);
                properties.SetVector(GlassRect, new Vector4(rect.x, rect.y, rect.width, rect.height));
                properties.SetVector(SampleStep, new Vector4(1f / frame.SourcePixelSize.x, 1f / frame.SourcePixelSize.y, 0, 0));
            }

            public void Record(EffectRenderContext context) => context.Draw(material, properties);
            public void Dispose() { }
        }
    }
}
