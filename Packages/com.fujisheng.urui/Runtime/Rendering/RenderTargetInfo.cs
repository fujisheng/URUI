using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace URUI.Rendering
{
    /// <summary>
    /// 实际提交目标的像素格式与采样信息；默认值表示调用者未提供可验证的目标信息。
    /// </summary>
    public readonly struct RenderTargetInfo : IEquatable<RenderTargetInfo>
    {
        internal readonly GraphicsFormat Format;
        internal readonly int Samples;
        internal readonly int Width;
        internal readonly int Height;
        internal readonly int Display;
        internal readonly bool IsScreen;
        internal bool IsKnown => Format != GraphicsFormat.None;
        internal bool IsHighPrecision => !IsScreen && (Format == GraphicsFormat.R16G16B16A16_SFloat || Format == GraphicsFormat.R32G32B32A32_SFloat);
        RenderTargetInfo(GraphicsFormat format, int samples, int width, int height, bool screen, int display)
        {
            Format = format;
            Samples = samples;
            Width = width;
            Height = height;
            IsScreen = screen;
            Display = display;
        }
        /// <summary>
        /// 从实际颜色纹理读取信息，不从相机的其他输出推断。
        /// </summary>
        public static RenderTargetInfo FromTexture(RenderTexture texture)
        {
            if (!texture)
            {
                throw new ArgumentNullException(nameof(texture));
            }
            return new RenderTargetInfo(texture.graphicsFormat, texture.antiAliasing, texture.width, texture.height, false, 0);
        }
        /// <summary>
        /// 描述管线已解析的最终屏幕颜色目标；采样数必须来自实际输出。
        /// </summary>
        public static RenderTargetInfo FromScreen(int display = 0, int samples = 1)
        {
            if (display < 0 || display >= UnityEngine.Display.displays.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(display));
            }
            if (samples < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(samples));
            }
            var size = display == 0 ? new Vector2Int(Screen.width, Screen.height) : new Vector2Int(UnityEngine.Display.displays[display].renderingWidth, UnityEngine.Display.displays[display].renderingHeight);
            return new RenderTargetInfo(GraphicsFormatUtility.GetGraphicsFormat(RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default), samples, size.x, size.y, true, display);
        }
        public bool Equals(RenderTargetInfo other) => Format == other.Format && Samples == other.Samples && Width == other.Width && Height == other.Height && Display == other.Display && IsScreen == other.IsScreen;
        public override bool Equals(object other) => other is RenderTargetInfo value && Equals(value);
        public override int GetHashCode() => HashCode.Combine(Format, Samples, Width, Height, Display, IsScreen);
    }
}
