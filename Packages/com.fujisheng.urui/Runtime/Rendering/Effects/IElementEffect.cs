using System;
namespace URUI.Rendering.Effects
{
    /// <summary>Prepare 可更新资源；Record 只录制现有资源，不访问或修改 DOM。</summary>
    /// <summary>可选的 Prepare 结果：节点本地坐标中的实际绘制范围，供子树滤镜/遮罩分配区域。</summary>
    public interface IEffectBounds {UnityEngine.Rect InkBounds {get;}}
    /// <summary>固定参数图片程序。宿主直接提交原始几何和冻结参数，不调用 Record。
    /// 材质在 Prepare 后不可变；替换材质须通过 EffectContext 退役。</summary>
    public interface IParameterImageEffect
    {
        UnityEngine.Material Program {get;}
        /// <summary>当前不可变程序实际消费的时间源；取值为 None 或 UiClock。</summary>
        RenderTimeInput TimeInput {get;}
        void PrepareProgram(in EffectProgramPreparation preparation);
        /// <summary>无 DOM 预览的数值输入；字段含义由程序拥有，Core 不解释属性名称。</summary>
        void GetPreviewParameters(Span<UnityEngine.Vector4> values);
    }
    /// <summary>在所有效果准备完成后上传共享资源；不能访问 DOM。</summary>
    public interface IParameterImageResources {void CommitResources();}
    /// <summary>程序拥有的额外运行时行，接在宿主的两行采样输入后。只复制已冻结的值。</summary>
    public interface IParameterImageRuntime
    {
        int RuntimeRowCount {get;}
        void CopyRuntimeParameters(Span<UnityEngine.Vector4> values);
    }
    /// <summary>在租用捕获图之前声明保守覆盖范围和最终混合；最多返回 64 个覆盖变换。</summary>
    public interface IParameterEffectLayout
    {
        void PrepareCoverage(in EffectProgramPreparation preparation,Span<UnityEngine.Matrix4x4> transforms,
            out int count,out UnityEngine.Vector4 expansion,out EffectComposite composite);
    }
    /// <summary>不可变的局部几何模板；内容变化递增版本，变换单独录入参数。</summary>
    public interface IParameterImageGeometry : IParameterImageEffect
    {
        ExternalGeometryInput Geometry {get;}
        UnityEngine.Matrix4x4 GeometryTransform {get;}
    }
    public interface IElementEffect : IDisposable
    {
        void Prepare(EffectFrame frame);
        void Record(EffectRenderContext context);
    }
}
