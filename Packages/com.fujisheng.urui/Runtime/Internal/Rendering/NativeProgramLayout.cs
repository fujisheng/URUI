using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;
using URUI.Internal.Interop;
using URUI.Rendering;
using URUI.Styles;

namespace URUI.Internal.Rendering
{
    // 注册时复制临时 UTF-8 描述；稳定帧只传 token 与连续数值，不创建托管属性对象。
    internal sealed unsafe class NativeProgramLayout : IDisposable
    {
        readonly Document document;
        readonly ulong owner;
        readonly RenderParameterLayout layout;
        ulong identity;
        readonly Dictionary<(int Offset,string Text),float> boundKeywords=new();
        readonly Dictionary<(int Offset,string Css,string Source),float> boundImages=new();
        internal ulong Identity=>identity;
        internal string Name {get;}
        internal int RowCount=>layout.RowCount;
        internal NativeProgramLayout(Document document,string name,RenderParameterLayout layout)
        {
            this.document=document??throw new ArgumentNullException(nameof(document));document.CheckThread();
            this.layout=layout??throw new ArgumentNullException(nameof(layout));
            if(string.IsNullOrWhiteSpace(name)||name.IndexOf('\0')>=0)throw new ArgumentException("程序布局名不能为空。",nameof(name));
            if(NativeMethods.urui_program_parameter_api_version()!=1)throw new NotSupportedException("需要原生程序参数协议 v1。");
            Name=name;owner=document.NativeHandle;
            var root=document.DocumentElement??throw new InvalidOperationException("程序布局需要已加载文档。");
            var keywordCount=0;foreach(var field in layout.Fields)keywordCount=checked(keywordCount+field.Keywords.Count);
            var strings=new List<IntPtr>(checked(keywordCount+layout.Fields.Count));var blocks=new List<IntPtr>(layout.Fields.Count);
            var fields=new ProgramParameterField[layout.Fields.Count];
            IntPtr Utf8(string value){var pointer=Marshal.StringToCoTaskMemUTF8(value);strings.Add(pointer);return pointer;}
            try
            {
                for(var i=0;i<fields.Length;i++)
                {
                    var source=layout.Fields[i];IntPtr keywords=IntPtr.Zero;
                    if(source.Keywords.Count!=0)
                    {
                        keywords=Marshal.AllocHGlobal(checked(source.Keywords.Count*sizeof(ProgramParameterKeyword)));blocks.Add(keywords);
                        var entries=(ProgramParameterKeyword*)keywords;
                        for(var k=0;k<source.Keywords.Count;k++)entries[k]=new ProgramParameterKeyword{Text=Utf8(source.Keywords[k].Keyword),Value=source.Keywords[k].Value};
                    }
                    fields[i]=new ProgramParameterField{Property=Utf8(source.Property.Name),Offset=(uint)source.ComponentOffset,OriginOffset=source.OriginOffset,
                        Kind=source.Property.Type switch{StyleValueType.Number=>source.Keywords.Count==0?1u:32u,StyleValueType.Pixels=>2u,StyleValueType.Color=>4u,StyleValueType.Keyword=>8u,StyleValueType.Image=>16u,_=>0},
                        KeywordCount=(uint)source.Keywords.Count,Keywords=keywords};
                    if(source.Optional)fields[i].Kind|=256;
                }
                fixed(ProgramParameterField* pointer=fields)
                {
                    var result=NativeMethods.urui_program_layout_register(owner,root.CheckedToken(),name,(IntPtr)pointer,(uint)fields.Length,(uint)layout.RowCount,out identity);
                    if(result!=1)throw new InvalidOperationException("程序固定字段注册失败："+name+" / "+result);
                }
            }
            finally{foreach(var pointer in blocks)Marshal.FreeHGlobal(pointer);foreach(var pointer in strings)Marshal.FreeCoTaskMem(pointer);}
        }
        internal void ReadValues(Element element,Span<Vector4> values)
        {
            document.CheckThread();
            if(identity==0||element==null||!ReferenceEquals(element.Document,document)||!element.IsValid||!element.HasLayout||element.NativeHandle!=owner)
                throw new InvalidOperationException("程序布局所属文档或节点已失效。");
            if(values.Length<layout.RowCount)throw new ArgumentException("程序参数缓冲区不足。",nameof(values));
            fixed(Vector4* pointer=values)
            {
                var result=NativeMethods.urui_program_layout_values(owner,element.CheckedToken(),identity,(IntPtr)pointer,(uint)layout.RowCount);
                if(result!=1)throw new InvalidOperationException("程序固定字段读取失败："+result);
            }
        }
        internal void ReadBoundValues(ComputedStyleSnapshot snapshot,Span<Vector4> values)
        {
            if(snapshot==null||values.Length<layout.RowCount)throw new ArgumentException("参数绑定需要有效样式与足够的连续空间。");
            values.Slice(0,layout.RowCount).Clear();
            foreach(var field in layout.Fields)
            {
                var name=field.Property.Name;Vector4 value;
                if(field.Optional&&!snapshot.HasValue(name))continue;
                switch(field.Property.Type)
                {
                    case StyleValueType.Number:
                        if(field.Keywords.Count==0||snapshot.Contains(field.Property))value=new Vector4(snapshot.GetNumber(name),0,0,0);
                        else {
                            var keyword=snapshot.GetKeyword(name);value=default;var matched=false;
                            foreach(var mapping in field.Keywords)if(mapping.Keyword==keyword){value=new Vector4(mapping.Value,1,0,0);matched=true;break;}
                            if(!matched)throw new InvalidOperationException("绑定的数值关键字缺少映射："+name);
                        }
                        break;
                    case StyleValueType.Image:
                        var image=snapshot.GetImage(name);var imageKey=(field.ComponentOffset,image.CssText,image.SourceUrl);
                        if(!boundImages.TryGetValue(imageKey,out var imageToken)) {
                            if(boundImages.Count==65536)throw new InvalidOperationException("绑定的图像超出文档容量。");
                            imageToken=-(boundImages.Count+1);boundImages.Add(imageKey,imageToken);
                        }
                        value=new Vector4(imageToken,0,0,0);break;
                    case StyleValueType.Pixels:value=new Vector4(snapshot.GetPixels(name),0,0,0);break;
                    case StyleValueType.Color:value=snapshot.GetColor(name);break;
                    case StyleValueType.Keyword:
                        var text=snapshot.GetKeyword(name);var found=false;value=default;
                        if(field.Keywords.Count==0)
                        {
                            // Resource keywords are opaque change identities. Negative
                            // values belong to this document's explicit bindings.
                            var key=(field.ComponentOffset,text);
                            if(!boundKeywords.TryGetValue(key,out var token))
                            {
                                if(boundKeywords.Count==65536)throw new InvalidOperationException("绑定的资源关键字超出文档容量。");
                                token=-(boundKeywords.Count+1);boundKeywords.Add(key,token);
                            }
                            value.x=token;found=true;
                        }
                        else foreach(var mapping in field.Keywords)if(mapping.Keyword==text){value.x=mapping.Value;found=true;break;}
                        if(!found)throw new InvalidOperationException("绑定的关键字缺少程序映射："+name);break;
                    default:throw new InvalidOperationException("固定参数不支持该字段类型。");
                }
                var width=field.Width;
                for(var n=0;n<width;n++)Set(values,field.ComponentOffset+n,value[n]);
                if(field.OriginOffset>=0)Set(values,field.OriginOffset,(float)snapshot.GetOrigin(name));
            }
        }
        static void Set(Span<Vector4> values,int offset,float number){var value=values[offset/4];value[offset%4]=number;values[offset/4]=value;}
        public void Dispose()
        {
            document.CheckThread();var previous=identity;identity=0;if(previous==0)return;
            var result=NativeMethods.urui_program_layout_release(owner,previous);
            if(result!=1&&result!=-2)throw new InvalidOperationException("程序布局释放失败："+result);
        }
    }
}
