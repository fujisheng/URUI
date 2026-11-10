using System;
using System.Globalization;
using URUI.Elements;
using URUI.Internal.Interop;

namespace URUI.Controls
{
    /// <summary>图像在作者内容框内的适配方式，不改变节点布局或命中范围。</summary>
    public enum ImageFit { Fill, Contain, Cover }
    /// <summary>作者声明的基础填充几何；方向由 FillOrigin 与 FillClockwise 决定。</summary>
    public enum ImageFillMode { None, Horizontal, Vertical, Radial90, Radial180, Radial360 }
    /// <summary>映射到 Document 图片资源的图像元素。</summary>
    public class Image : Element
    {
        /// <summary>文档工厂构造入口。</summary>
        protected internal Image(ElementContext context) : base(context) { }
        /// <summary>ImageAsset.Source 对应的 HTML 图片路径。</summary>
        public string Source { get => GetAttribute("src") ?? ""; set => SetAttribute("src", value); }
        /// <summary>选择文档图片清单中的命名图集区域；空值恢复 src 来源。</summary>
        public string SpriteName
        {
            get => GetAttribute("data-urui-sprite") ?? "";
            set
            {
                if (string.IsNullOrEmpty(value)) RemoveAttribute("data-urui-sprite");
                else SetAttribute("data-urui-sprite", value);
            }
        }
        /// <summary>让图像固有尺寸跟随 Surface 密度；HTML width/height 仍表示 CSS 像素。</summary>
        public bool DensityScaledNaturalSize
        {
            get => Property("--urui-image-density") == "surface";
            set => Style.SetKeyword("--urui-image-density", value ? 1 : 0);
        }
        // Non-geometric values expose the current cascade snapshot before layout.
        string Property(string name) => ReadStyle(name,false);
        /// <summary>普通 CSS object-fit；默认拉伸，Contain 留边，Cover 居中裁剪纹理。</summary>
        public ImageFit Fit
        {
            get => Property("object-fit") switch { "contain" => ImageFit.Contain, "cover" => ImageFit.Cover, _ => ImageFit.Fill };
            set
            {
                if (value < ImageFit.Fill || value > ImageFit.Cover) { throw new ArgumentOutOfRangeException(nameof(value)); }
                Style.SetKeyword("object-fit",(int)value);
            }
        }
        /// <summary>基础填充方式；可在外部 CSS 声明 --urui-image-fill-mode。</summary>
        public ImageFillMode FillMode
        {
            get => Property("--urui-image-fill-mode") switch { "horizontal"=>ImageFillMode.Horizontal,"vertical"=>ImageFillMode.Vertical,
                "radial90"=>ImageFillMode.Radial90,"radial180"=>ImageFillMode.Radial180,"radial360"=>ImageFillMode.Radial360,_=>ImageFillMode.None };
            set
            {
                if (value<ImageFillMode.None || value>ImageFillMode.Radial360) { throw new ArgumentOutOfRangeException(nameof(value)); }
                Style.SetKeyword("--urui-image-fill-mode",(int)value);
            }
        }
        float Number(string name,float fallback) => float.TryParse(Property(name),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:fallback;
        /// <summary>九宫格是否绘制中心；普通图像不受此属性影响。</summary>
        public bool FillCenter { get=>Number("--urui-image-fill-center",1)>=.5f; set=>Style.SetNumber("--urui-image-fill-center",value?1:0); }
        /// <summary>线性填充起点为0或1，径向为0到3；超出范围拒绝写入。</summary>
        public int FillOrigin
        {
            get => (int)UnityEngine.Mathf.Clamp(Number("--urui-image-fill-origin",0),0,3);
            set
            {
                if (value<0 || value>3) { throw new ArgumentOutOfRangeException(nameof(value)); }
                Style.SetNumber("--urui-image-fill-origin",value);
            }
        }
        /// <summary>径向填充的顺时针方向。</summary>
        public bool FillClockwise { get=>Number("--urui-image-fill-clockwise",1)>=.5f; set=>Style.SetNumber("--urui-image-fill-clockwise",value?1:0); }
        /// <summary>填充比例，有限值夹取到0到1；0时不提交图像几何。</summary>
        public float FillAmount
        {
            get=>Number("--urui-image-fill-amount",1);
            set
            {
                if (!float.IsFinite(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
                Style.SetNumber("--urui-image-fill-amount",UnityEngine.Mathf.Clamp01(value));
            }
        }
    }
}
