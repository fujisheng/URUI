using System;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Resources;
namespace URUI.Rendering.Effects
{
    /// <summary>
    /// 无 DOM 的矩形图像预览；复用生产效果实例、网格和 Record 路径，不创建第二个 Surface。
    /// </summary>
    public sealed class EffectPreviewRenderer : IDisposable
    {
        readonly EffectContext context;
        readonly IElementEffect instance;
        readonly CommandBuffer commands = new() { name = "URUI Effect Preview" };
        readonly PaintContextPool paintContexts = new();
        readonly EffectDefinition definition;
        Texture2D parameterTexture;
        Color[] parameterData;
        MaterialPropertyBlock parameterProperties;
        sealed class PreviewInputs : IPreparedEffectInputs
        {
            internal EffectFrame Frame;
            internal ulong Version;
            internal bool Active;
            public bool IsActive(ulong version) => Active && Version == version;
            public Vector2 Size => Frame.Size;
            public Vector2 ContentOffset => Frame.ContentOffset;
            public double Time => Frame.Time;
            public EffectProgramCoordinates Coordinates => new(Frame.Layout.ContentRect, Frame.Layout.RootContentRect, Frame.ElementToDocument, Frame.Layout.RootToDocument);
            public EffectProgramSource Source => new(Frame.Source, Frame.SourceGeometry, Frame.SourceIsCaptured, Frame.SourceIsBottomLeft, Frame.SourceCaptureBounds, Frame.SourceUvBounds);
            public EffectFrame Materialize() => Frame;
        }
        readonly PreviewInputs previewInputs = new();
        Vector4[] previewValues;
        Mesh mesh;
        EffectGeometry geometry;
        Vector2 size;
        ulong geometryIdentity;
        bool disposed;
        static void DestroyObject(UnityEngine.Object value)
        {
            if (!value)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(value);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }
        public EffectPreviewRenderer(EffectDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }
            this.definition = definition;
            context = new EffectContext(definition, null, 0, value => GpuRetirement.Retire(value.Dispose));
            try
            {
                instance = definition.Factory.Create(context) ?? throw new InvalidOperationException("预览工厂返回 null。");
            }
            catch
            {
                context.Dispose();
                commands.Dispose();
                throw;
            }
        }
        /// <summary>
        /// source 是普通 Unity 直 Alpha 纹理。content 为目标中的像素矩形，时间由调用方明确给定。
        /// </summary>
        public void Render(Texture source, Rect content, double time, RenderTexture destination, Color background)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EffectPreviewRenderer));
            }
            if (!source || !destination || !double.IsFinite(time) || !float.IsFinite(content.x) || !float.IsFinite(content.y) || !float.IsFinite(content.width) || !float.IsFinite(content.height) || content.width <= 0 || content.height <= 0)
            {
                throw new ArgumentException("预览输入、尺寸、时间及输出必须有效。");
            }
            if (!mesh || size != content.size)
            {
                if (mesh)
                {
                    var old = mesh;
                    GpuRetirement.Retire(() => DestroyObject(old));
                }
                size = content.size;
                var vertices = new[] { Vector3.zero, new Vector3(size.x, 0), new Vector3(size.x, size.y), new Vector3(0, size.y) };
                var uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                var colors = new Color32[] { Color.white, Color.white, Color.white, Color.white };
                var indices = new[] { 0, 1, 2, 0, 2, 3 };
                geometry = new EffectGeometry(++geometryIdentity, vertices, uv, colors, indices);
                mesh = new Mesh
                {
                    name = "URUI Effect Preview Quad",
                    vertices = vertices,
                    uv = uv,
                    colors32 = colors,
                    triangles = indices,
                    uv2 = new Vector2[4]
                };
            }
            var transform = Matrix4x4.Translate(content.position);
            var layout = new EffectLayout(null, new Rect(Vector2.zero, size), transform, 0, new Rect(Vector2.zero, size), transform);
            var frame = new EffectFrame(0, layout, source, true, time, geometry: geometry);
            if (instance is IParameterImageEffect parameters)
            {
                var count = definition.RenderParameters?.RowCount ?? throw new InvalidOperationException("预览缺少固定参数布局。");
                if (previewValues == null || previewValues.Length != count)
                {
                    previewValues = new Vector4[count];
                }
                previewValues.AsSpan().Clear();
                parameters.GetPreviewParameters(previewValues);
                previewInputs.Frame = frame;
                previewInputs.Version = checked(previewInputs.Version + 1);
                previewInputs.Active = true;
                try
                {
                    var preparation = new EffectProgramPreparation(previewInputs, previewInputs.Version, previewValues);
                    parameters.PrepareProgram(preparation);
                }
                finally
                {
                    previewInputs.Active = false;
                }
            }
            else
            {
                instance.Prepare(frame);
            }
            if (instance is IParameterImageResources resources)
            {
                resources.CommitResources();
            }
            commands.Clear();
            commands.SetRenderTarget(destination);
            commands.SetViewport(new Rect(0, 0, destination.width, destination.height));
            commands.ClearRenderTarget(false, true, QualitySettings.activeColorSpace == ColorSpace.Linear ? background.linear : background);
            commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.Ortho(0, destination.width, destination.height, 0, -1, 1));
            var paint = paintContexts.Open(null, commands, transform, default, false);
            var render = new EffectRenderContext(paint, mesh, frame, _ => throw new NotSupportedException("预览效果不能订阅文档提交。"));
            context.Recording = true;
            try
            {
                if (instance is IParameterImageEffect program)
                {
                    RecordProgram(program, frame, transform);
                }
                else
                {
                    instance.Record(render);
                }
                Graphics.ExecuteCommandBuffer(commands);
            }
            finally
            {
                context.Recording = false;
                render.Close();
            }
        }
        void RecordProgram(IParameterImageEffect program, EffectFrame frame, Matrix4x4 transform)
        {
            var layout = definition.RenderParameters ?? throw new InvalidOperationException("预览程序缺少固定参数布局。");
            var runtimeCount = 2 + (program is IParameterImageRuntime runtime ? runtime.RuntimeRowCount : 0);
            if (runtimeCount < 2 || runtimeCount > 32)
            {
                throw new InvalidOperationException("程序运行时参数超出容量。 ");
            }
            if (!parameterTexture)
            {
                parameterTexture = new Texture2D(256, Mathf.Max(1, (9 + runtimeCount + layout.RowCount + 255) / 256), TextureFormat.RGBAFloat, false, true)
                {
                    name = "URUI Preview Parameters",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                parameterData = new Color[parameterTexture.width * parameterTexture.height];
                parameterProperties = new MaterialPropertyBlock();
            }
            var drawMesh = mesh;
            if (program is IParameterImageGeometry template)
            {
                transform = transform * template.GeometryTransform;
                drawMesh = template.Geometry.Mesh;
            }
            for (var column = 0; column < 4; column++)
            {
                parameterData[column] = transform.GetColumn(column);
            }
            parameterData[5] = Color.white;
            parameterData[6] = new Color(1, 1, 0, 0);
            parameterData[7] = new Color(0, 2, 8, 0);
            parameterData[8] = new Color(0, 0, 1 / size.x, 1 / size.y);
            parameterData[9] = new Color(size.x, size.y, 8 + runtimeCount + layout.RowCount, 1);
            if (runtimeCount > 2)
            {
                Span<Vector4> extra = stackalloc Vector4[runtimeCount - 2];
                ((IParameterImageRuntime)program).CopyRuntimeParameters(extra);
                for (var index = 0; index < extra.Length; index++)
                {
                    parameterData[10 + index] = extra[index];
                }
            }
            for (var index = 0; index < previewValues.Length; index++)
            {
                parameterData[8 + runtimeCount + index] = previewValues[index];
            }
            parameterData[8 + runtimeCount + layout.RowCount] = new Color((float)frame.Time, 0, 0, 0);
            parameterTexture.SetPixels(parameterData);
            parameterTexture.Apply(false, false);
            parameterProperties.Clear();
            parameterProperties.SetTexture("_MainTex", frame.Source);
            parameterProperties.SetTexture("_UruiParameters", parameterTexture);
            parameterProperties.SetFloat("_UruiBaseParameter", 0f);
            commands.DrawMesh(drawMesh, Matrix4x4.identity, program.Program, 0, 0, parameterProperties);
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            commands.Dispose();
            var oldParameters = parameterTexture;
            var oldMesh = mesh;
            var oldContext = context;
            var oldInstance = instance;
            GpuRetirement.Retire(() =>
            {
                try
                {
                    oldInstance.Dispose();
                }
                finally
                {
                    oldContext.Dispose();
                    DestroyObject(oldMesh);
                    DestroyObject(oldParameters);
                }
            });
        }
    }
}
