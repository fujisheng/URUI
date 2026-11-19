using System;
using System.Collections.Generic;
using URUI.Styles;

namespace URUI.Rendering
{
    /// <summary>
    /// 把作者关键字映射为程序使用的有限数值；不依赖 CSS 枚举内部编号。
    /// </summary>
    public readonly struct RenderKeywordMapping
    {
        public string Keyword
        {
            get;
        }
        public float Value
        {
            get;
        }
        public RenderKeywordMapping(string keyword, float value)
        {
            if (string.IsNullOrWhiteSpace(keyword) || keyword.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("关键字不能为空。", nameof(keyword));
            }
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            Keyword = keyword;
            Value = value;
        }
    }
    /// <summary>
    /// 一个 CSS 计算属性到固定分量区间的映射；颜色占四个分量，带关键字映射的数值占两个分量（值、关键字标记）。
    /// </summary>
    public sealed class RenderParameterField
    {
        public StylePropertyDefinition Property
        {
            get;
        }
        public int ComponentOffset
        {
            get;
        }
        /// <summary>
        /// 可选的级联来源分量；-1 表示程序不读取来源。
        /// </summary>
        public int OriginOffset
        {
            get;
        }
        /// <summary>
        /// 缺失的属性输出零；存在但类型无效的作者值仍报错。
        /// </summary>
        public bool Optional
        {
            get;
        }
        /// <summary>
        /// 关键字的显式映射；数值可声明有限关键字，第二分量区分数值（0）和关键字（1）。图像只输出变化标识，不编码纹理。
        /// </summary>
        public IReadOnlyList<RenderKeywordMapping> Keywords
        {
            get;
        }
        internal int Width => Property.Type == StyleValueType.Color ? 4 : Property.Type == StyleValueType.Number && Keywords.Count != 0 ? 2 : 1;
        public RenderParameterField(StylePropertyDefinition property, int componentOffset, int originOffset = -1, IEnumerable<RenderKeywordMapping> keywords = null, bool optional = false)
        {
            Property = property ?? throw new ArgumentNullException(nameof(property));
            if (componentOffset < 0 || originOffset < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(componentOffset));
            }
            var mappings = keywords == null ? new List<RenderKeywordMapping>() : new List<RenderKeywordMapping>(keywords);
            if (mappings.Count > 128 || mappings.Count != 0 && property.Type != StyleValueType.Keyword && property.Type != StyleValueType.Number)
            {
                throw new ArgumentException("映射只用于关键字或数值属性，且最多 128 项。", nameof(keywords));
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var mapping in mappings)
            {
                if (string.IsNullOrEmpty(mapping.Keyword) || !names.Add(mapping.Keyword))
                {
                    throw new ArgumentException("关键字映射不能为空或重复。", nameof(keywords));
                }
            }
            ComponentOffset = componentOffset;
            OriginOffset = originOffset;
            Keywords = mappings.AsReadOnly();
            Optional = optional;
        }
    }
    /// <summary>
    /// 程序的不可变 float4 布局；注册时编译属性访问器，未注册自定义值保留 CSS 解析语义。
    /// </summary>
    public sealed class RenderParameterLayout
    {
        public int RowCount
        {
            get;
        }
        public IReadOnlyList<RenderParameterField> Fields
        {
            get;
        }
        public RenderParameterLayout(int rowCount, IEnumerable<RenderParameterField> fields)
        {
            if (rowCount < 1 || rowCount > 512)
            {
                throw new ArgumentOutOfRangeException(nameof(rowCount));
            }
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            var values = new List<RenderParameterField>(fields);
            if (values.Count > 2048)
            {
                throw new ArgumentException("固定字段超出协议容量。", nameof(fields));
            }
            var used = new bool[rowCount * 4];
            foreach (var field in values)
            {
                if (field == null)
                {
                    throw new ArgumentException("字段不能为空。", nameof(fields));
                }
                var width = field.Width;
                if (field.ComponentOffset > used.Length - width || field.OriginOffset >= used.Length)
                {
                    throw new ArgumentException("字段区间超出布局。", nameof(fields));
                }
                for (var i = 0; i < width; i++)
                {
                    Claim(field.ComponentOffset + i);
                }
                if (field.OriginOffset >= 0)
                {
                    Claim(field.OriginOffset);
                }
            }
            void Claim(int index)
            {
                if (used[index])
                {
                    throw new ArgumentException("固定字段和来源不能覆盖同一分量。", nameof(fields));
                }
                used[index] = true;
            }
            RowCount = rowCount;
            Fields = values.AsReadOnly();
        }
    }
}
