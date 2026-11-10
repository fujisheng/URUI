using System;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI.Elements;
using URUI.Internal.Interop;
namespace URUI.Styles
{
    public enum StyleValueOrigin { Initial, UserAgent, Author, Animation, Important, Transition }
    /// <summary>一个稳定布局版本的独立值副本；重载或销毁后的代际不能继续使用。</summary>
    public sealed class ComputedStyleSnapshot
    {
        readonly DocumentStyles owner;
        readonly Element element;
        readonly Dictionary<string, int> indices;
        readonly byte[] textBuffer;
        readonly Dictionary<string,string> keywords;
        const int ChunkSize=32;
        readonly NativeMethods.StyleValue[][] chunks;
        readonly int entryCount;
        readonly bool[] writableChunks;
        Dictionary<string, ComputedImageValue> images;
        ComputedStyleSnapshot(ComputedStyleSnapshot source)
        {
            owner=source.owner;element=source.element;Generation=source.Generation;Revision=source.Revision;
            textBuffer=source.textBuffer;indices=source.indices;
            chunks=(NativeMethods.StyleValue[][])source.chunks.Clone();entryCount=source.entryCount;writableChunks=new bool[chunks.Length];
            keywords=new(source.keywords,StringComparer.Ordinal);
            if(source.images!=null)images=new(source.images,StringComparer.Ordinal);
        }
        /// <summary>创建独立的效果参数视图；不写入 DOM，也不改变 CSS 级联。仅覆盖指定来源以下的值。</summary>
        public ComputedStyleSnapshot WithOverrides(IReadOnlyDictionary<string,StyleOverride> overrides, StyleValueOrigin maximumOrigin)
        {
            if(!IsValid)throw new InvalidOperationException("计算样式快照的文档代际已失效。");
            if(overrides==null)throw new ArgumentNullException(nameof(overrides));
            var result=new ComputedStyleSnapshot(this);
            foreach(var pair in overrides)
            {
                var current=Get(pair.Key,255);if((StyleValueOrigin)(current.Kind>>8)>maximumOrigin)continue;
                var value=pair.Value;
                if((current.Kind&255)!=value.Kind)throw new ArgumentException("参数覆盖类型不匹配："+pair.Key);
                current.X=value.Value.x;current.Y=value.Value.y;current.Z=value.Value.z;current.W=value.Value.w;
                result.SetEntry(indices[pair.Key],current);
                if(value.Kind==8)result.keywords[pair.Key]=value.Keyword;
            }
            return result;
        }
        public uint Generation { get; }
        public ulong Revision { get; }
        internal bool IsValid => owner.Generation == Generation && element.IsValid;
        NativeMethods.StyleValue GetEntry(int index)=>chunks[index/ChunkSize][index%ChunkSize];
        void SetEntry(int index,NativeMethods.StyleValue value)
        {
            var chunk=index/ChunkSize;
            if(!writableChunks[chunk]){chunks[chunk]=(NativeMethods.StyleValue[])chunks[chunk].Clone();writableChunks[chunk]=true;}
            chunks[chunk][index%ChunkSize]=value;
        }
        internal ComputedStyleSnapshot(DocumentStyles owner, Element element, uint generation, ulong revision, NativeMethods.StyleValue[] entries,int count,byte[] names,int nameBytes,ComputedStyleSnapshot previous=null)
        {
            this.owner=owner;this.element=element;Generation=generation;Revision=revision;entryCount=count;
            // 快照保持不可变；数值更新只复制变化的固定大小块，其余数据与文本共享。
            var sameText=previous!=null&&previous.entryCount==count&&names.AsSpan(0,nameBytes).SequenceEqual(previous.textBuffer);
            if(sameText)for(var i=0;i<count;i++)
            {
                var a=entries[i];var b=previous.GetEntry(i);
                if(a.NameOffset!=b.NameOffset||a.TextOffset!=b.TextOffset||a.SourceOffset!=b.SourceOffset||(a.Kind&255)!=(b.Kind&255)){sameText=false;break;}
            }
            textBuffer=sameText?previous.textBuffer:names.AsSpan(0,nameBytes).ToArray();
            chunks=new NativeMethods.StyleValue[(count+ChunkSize-1)/ChunkSize][];
            for(var chunk=0;chunk<chunks.Length;chunk++)
            {
                var start=chunk*ChunkSize;var length=Math.Min(ChunkSize,count-start);var unchanged=sameText;
                if(unchanged) unchanged=MemoryMarshal.AsBytes(entries.AsSpan(start,length)).SequenceEqual(MemoryMarshal.AsBytes(previous.chunks[chunk].AsSpan()));
                if(unchanged)chunks[chunk]=previous.chunks[chunk];
                else{chunks[chunk]=new NativeMethods.StyleValue[length];Array.Copy(entries,start,chunks[chunk],0,length);}
            }
            if(sameText){keywords=previous.keywords;indices=previous.indices;return;}
            indices=new Dictionary<string,int>(count,StringComparer.Ordinal);keywords=new(StringComparer.Ordinal);
            for(var i=0;i<count;i++)
            {
                var entry=entries[i];var start=entry.NameOffset;
                if(start<0||start>=textBuffer.Length)throw new InvalidOperationException("原生样式名称偏移越界。");
                var end=Array.IndexOf(textBuffer,(byte)0,start);
                if(end<0)throw new InvalidOperationException("原生样式名称缺少结束符。");
                var name=Encoding.UTF8.GetString(textBuffer,start,end-start);indices.Add(name,i);
                if((entry.Kind&255)==8)keywords.Add(name,ReadText(textBuffer,entry.TextOffset));
            }
        }
        NativeMethods.StyleValue Get(string name, int kind)
        {
            if (!IsValid) { throw new InvalidOperationException("计算样式快照的文档代际已失效。"); }
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("样式参数名不能为空。", nameof(name)); }
            if (!indices.TryGetValue(name, out var index) || (GetEntry(index).Kind & kind) == 0)
            { throw new InvalidOperationException("计算样式不存在或类型不匹配：" + name); }
            return GetEntry(index);
        }
        public string GetKeyword(string name){Get(name,8);return keywords[name];}
        /// <summary>计算值的级联来源；注册初始值不算作者覆盖，动画、important 与 transition 保留各自优先级。</summary>
        public StyleValueOrigin GetOrigin(string name) => (StyleValueOrigin)(Get(name,255).Kind >> 8);
        public ComputedImageValue GetImage(string name)
        {
            var entry = Get(name, 16);
            images ??= new Dictionary<string, ComputedImageValue>(StringComparer.Ordinal);
            if (!images.TryGetValue(name, out var image))
            {
                image = new ComputedImageValue((int)entry.X, ReadText(textBuffer, entry.TextOffset), ReadText(textBuffer, entry.SourceOffset));
                images.Add(name, image);
            }
            return image;
        }
        public float GetNumber(string name) => Get(name, 1).X;
        public float GetPixels(string name) => Get(name, 2).X;
        internal bool TryGetPixels(string name, out float pixels)
        {
            if (!IsValid) throw new InvalidOperationException("计算样式快照的文档代际已失效。");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("样式参数名不能为空。", nameof(name));
            if (indices.TryGetValue(name, out var index) && (GetEntry(index).Kind & 2) != 0)
            { pixels = GetEntry(index).X; return true; }
            pixels = 0; return false;
        }
        public Color GetColor(string name) { var value = Get(name, 4); return new Color(value.X, value.Y, value.Z, value.W); }
        static string ReadText(byte[] names, int offset)
        {
            if (offset < 0 || offset >= names.Length) { throw new InvalidOperationException("原生图像文本偏移越界。"); }
            var end = Array.IndexOf(names, (byte)0, offset);
            if (end < 0) { throw new InvalidOperationException("原生图像文本缺少结束符。"); }
            return Encoding.UTF8.GetString(names, offset, end - offset);
        }
        public bool Contains(StylePropertyDefinition property)
        {
            if (!IsValid) { throw new InvalidOperationException("计算样式快照的文档代际已失效。"); }
            if (property == null) { throw new ArgumentNullException(nameof(property)); }
            return indices.TryGetValue(property.Name, out var index) && (GetEntry(index).Kind & (1 << (int)property.Type)) != 0;
        }
    }
}
