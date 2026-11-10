using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Documents;
using URUI.Elements;

namespace URUI.Controls
{
    /// <summary>按作者横向子项的实测外界更新内容宽度，保留节点、间距、内边距和边框的 CSS 定义。</summary>
    public sealed class RowContentSize : IDisposable
    {
        readonly Element content;
        readonly Document document;
        readonly string previousWidth;
        readonly List<(Element element, bool text)> children = new();
        bool disposed;
        /// <summary>用于子项具有固定或固有宽度的横向滚动内容；无需在业务代码计算条目数量和单元格宽度。</summary>
        public RowContentSize(Element content)
        {
            this.content=content??throw new ArgumentNullException(nameof(content));
            document=content.Document;previousWidth=content.Style.Get("width");
            document.LayoutUpdated+=Apply;document.Unloaded+=Unloaded;
        }
        void Apply(Document owner)
        {
            if(disposed||!content.IsValid||!content.HasLayout){return;}
            var inner=content.ContentRect;var box=content.Layout;
            var right=inner.x;
            var hierarchy=content.Hierarchy;var count=hierarchy.ChildCount;
            if(children.Count>count)children.RemoveRange(count,children.Count-count);
            for(var index=0;index<count;index++)
            {
                var child=hierarchy.ChildAt(index);
                if(index==children.Count)children.Add((child,child.TagName=="#text"));
                else if(!ReferenceEquals(children[index].element,child))children[index]=(child,child.TagName=="#text");
                if(children[index].text||!child.HasLayout){continue;}
                var style=document.Styles.GetComputed(child);
                if(style.GetKeyword("display")=="none"||style.GetKeyword("position") is "absolute" or "fixed"){continue;}
                var margin=style.TryGetPixels("margin-right",out var pixels)?pixels:0;
                right=Mathf.Max(right,child.Layout.xMax+margin);
            }
            var outerWidth=Mathf.Max(0,right+box.width-inner.xMax);
            if(Mathf.Abs(box.width-outerWidth)<=.01f){return;}
            var width=document.Styles.GetComputed(content).GetKeyword("box-sizing")=="border-box"?outerWidth:outerWidth-(box.width-inner.width);
            content.Style.SetWidth(Mathf.Max(0,width));
        }
        void Unloaded(Document owner)=>Dispose();
        /// <summary>停止测量并恢复原有尺寸覆盖。</summary>
        public void Dispose()
        {
            if(disposed){return;}disposed=true;document.LayoutUpdated-=Apply;document.Unloaded-=Unloaded;
            children.Clear();
            if(!content.IsValid){return;}
            if(previousWidth==null){content.Style.Remove("width");}else{content.Style.Set("width",previousWidth);}
        }
    }
}
